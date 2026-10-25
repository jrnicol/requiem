using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Accounts;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

/// <summary>
/// Campaigns, their members and invite links (PHASE2.md milestone 2.1). Anyone signed in can start a
/// campaign and becomes its Storyteller; players join through invite links. A campaign you don't belong
/// to reads as "not found".
/// </summary>
public static class CampaignEndpoints
{
    public static RouteGroupBuilder MapCampaignEndpoints(this RouteGroupBuilder api)
    {
        var campaigns = api.MapGroup("/campaigns").WithTags("Campaigns");
        campaigns.MapGet("/", ListAsync);
        campaigns.MapPost("/", CreateAsync);
        campaigns.MapGet("/{id:guid}", GetAsync);
        campaigns.MapPut("/{id:guid}", UpdateAsync);
        campaigns.MapPut("/{id:guid}/coterie-script", UpdateCoterieScriptAsync);
        campaigns.MapPost("/{id:guid}/archive", (Guid id, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct) =>
            SetArchivedAsync(id, true, store, users, access, audit, time, ct));
        campaigns.MapPost("/{id:guid}/unarchive", (Guid id, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct) =>
            SetArchivedAsync(id, false, store, users, access, audit, time, ct));
        campaigns.MapDelete("/{id:guid}", DeleteAsync);
        campaigns.MapDelete("/{id:guid}/members/{userId}", RemoveMemberAsync);
        campaigns.MapPost("/{id:guid}/invites", CreateInviteAsync);
        campaigns.MapDelete("/{id:guid}/invites/{inviteId:guid}", RevokeInviteAsync);

        // The join page needs the preview before anyone has an account.
        var invites = api.MapGroup("/invites").WithTags("Invites");
        invites.MapGet("/{token}", PreviewInviteAsync).AllowAnonymous();
        invites.MapPost("/{token}/accept", AcceptInviteAsync);
        invites.MapPost("/{token}/join", JoinAsync).AllowAnonymous().RequireRateLimiting(AccountEndpoints.SignUpLimit);

        return api;
    }

    // ---------- Campaigns ----------

    private static async Task<List<CampaignSummary>> ListAsync(
        ICampaignStore store, ICharacterStore characters, CampaignAccess access, CancellationToken ct)
    {
        var summaries = new List<CampaignSummary>();
        foreach (var (id, role) in await access.RolesAsync(ct))
        {
            if (await store.GetAsync(id, ct) is not { } campaign) continue;
            var players = (await store.MembersAsync(id, ct)).Count(m => m.Role == CampaignRole.Player);
            summaries.Add(new CampaignSummary(campaign.Id, campaign.Name, campaign.Description, role, players,
                await characters.CountByCampaignAsync(id, ct), campaign.ArchivedAt));
        }
        return summaries
            .OrderBy(s => s.ArchivedAt is not null)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<Results<Created<CampaignView>, ValidationProblem>> CreateAsync(
        CampaignForm form, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var now = time.GetUtcNow();
        var campaign = new Campaign { Id = Guid.CreateVersion7(now), Name = form.Name.Trim(), CreatedAt = now };
        Apply(form, campaign);
        campaign.Members.Add(new CampaignMember { CampaignId = campaign.Id, UserId = access.UserId, Role = CampaignRole.Storyteller, JoinedAt = now });
        audit.Record(campaign.Id, null, "campaign.create", $"Created the campaign {campaign.Name}.");
        await store.AddAsync(campaign, ct);

        return TypedResults.Created($"/api/campaigns/{campaign.Id}", await ViewAsync(store, users, access.UserId, campaign.Id, CampaignRole.Storyteller, ct));
    }

    private static async Task<Results<Ok<CampaignView>, NotFound>> GetAsync(
        Guid id, ICampaignStore store, IUserDirectory users, CampaignAccess access, CancellationToken ct) =>
        await access.RoleInAsync(id, ct) is { } role ? TypedResults.Ok(await ViewAsync(store, users, access.UserId, id, role, ct)) : TypedResults.NotFound();

    private static async Task<Results<Ok<CampaignView>, NotFound, ForbidHttpResult, ValidationProblem>> UpdateAsync(
        Guid id, CampaignForm form, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var campaign = (await store.GetAsync(id, ct))!;
        Apply(form, campaign);
        audit.Record(id, null, "campaign.update", $"Updated the settings of {campaign.Name}.");
        await store.SaveAsync(campaign, ct);
        return TypedResults.Ok(await ViewAsync(store, users, access.UserId, id, role, ct));
    }

    /// <summary>Any member may edit the shared Coterie Script (PHASE2.md decision 7).</summary>
    private static async Task<Results<Ok<CampaignView>, NotFound, ValidationProblem>> UpdateCoterieScriptAsync(
        Guid id, CoterieScriptForm form, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var campaign = (await store.GetAsync(id, ct))!;
        var text = Clean(form.Text);
        if (text != campaign.CoterieScript)
        {
            audit.Record(id, null, "campaign.coterie-script",
                $"Changed the Coterie Script from \"{campaign.CoterieScript}\" to \"{text}\".");
            campaign.CoterieScript = text;
            await store.SaveAsync(campaign, ct);
        }
        return TypedResults.Ok(await ViewAsync(store, users, access.UserId, id, role, ct));
    }

    private static async Task<Results<Ok<CampaignView>, NotFound, ForbidHttpResult>> SetArchivedAsync(
        Guid id, bool archived, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();

        var campaign = (await store.GetAsync(id, ct))!;
        campaign.ArchivedAt = archived ? campaign.ArchivedAt ?? time.GetUtcNow() : null;
        audit.Record(id, null, archived ? "campaign.archive" : "campaign.unarchive",
            $"{(archived ? "Archived" : "Restored")} {campaign.Name}.");
        await store.SaveAsync(campaign, ct);
        return TypedResults.Ok(await ViewAsync(store, users, access.UserId, id, role, ct));
    }

    /// <summary>Only an empty campaign can be deleted; one with characters or scenes is archived instead.</summary>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<ProblemDetailsBody>>> DeleteAsync(
        Guid id, ICampaignStore store, ICharacterStore characters, ISceneStore scenes, CampaignAccess access, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();

        if (await characters.CountByCampaignAsync(id, ct) > 0 || await scenes.AnyInCampaignAsync(id, ct))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This campaign still has characters or scenes. Move them out, or archive the campaign instead."));

        await store.DeleteAsync(id, ct);
        return TypedResults.NoContent();
    }

    // ---------- Members ----------

    /// <summary>Storytellers remove players; any member can remove themselves (leave). A campaign keeps at least one Storyteller.</summary>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<ProblemDetailsBody>>> RemoveMemberAsync(
        Guid id, string userId, ICampaignStore store, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        var leaving = userId == access.UserId;
        if (!leaving && role != CampaignRole.Storyteller) return TypedResults.Forbid();

        var members = await store.MembersAsync(id, ct);
        var member = members.SingleOrDefault(m => m.UserId == userId);
        if (member is null) return TypedResults.NotFound();
        if (member.Role == CampaignRole.Storyteller && members.Count(m => m.Role == CampaignRole.Storyteller) == 1)
            return TypedResults.Conflict(new ProblemDetailsBody("A campaign needs a Storyteller. Archive it instead."));

        var name = (await users.GetAsync([userId], ct)).GetValueOrDefault(userId)?.DisplayName ?? "Someone";
        audit.Record(id, null, leaving ? "member.leave" : "member.remove",
            leaving ? $"{name} left the campaign." : $"Removed {name} from the campaign.");
        await store.RemoveMemberAsync(id, userId, ct);
        return TypedResults.NoContent();
    }

    // ---------- Invites ----------

    private static async Task<Results<Created<CreatedInvite>, NotFound, ForbidHttpResult, ValidationProblem>> CreateInviteAsync(
        Guid id, InviteForm form, ICampaignStore store, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var token = NewToken();
        var now = time.GetUtcNow();
        var invite = new CampaignInvite
        {
            Id = Guid.CreateVersion7(now),
            CampaignId = id,
            TokenHash = Hash(token),
            // Player invites only for now; co-Storyteller invites aren't offered yet.
            Role = CampaignRole.Player,
            ExpiresAt = now.AddDays(form.ExpiresInDays),
            UsesLeft = form.MaxUses,
            CreatedByUserId = access.UserId,
            CreatedAt = now,
        };
        audit.Record(id, null, "invite.create",
            $"Made an invite link for {(form.MaxUses is { } uses ? $"{uses} player{(uses == 1 ? "" : "s")}" : "any number of players")}, " +
            $"valid {form.ExpiresInDays} day{(form.ExpiresInDays == 1 ? "" : "s")}.");
        await store.AddInviteAsync(invite, ct);

        return TypedResults.Created($"/api/invites/{token}", new CreatedInvite(ToView(invite), token));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> RevokeInviteAsync(
        Guid id, Guid inviteId, ICampaignStore store, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();

        var invite = (await store.InvitesAsync(id, ct)).SingleOrDefault(i => i.Id == inviteId);
        if (invite is null) return TypedResults.NotFound();
        if (invite.RevokedAt is null)
        {
            invite.RevokedAt = time.GetUtcNow();
            audit.Record(id, null, "invite.revoke", "Revoked an invite link.");
            await store.SaveInviteAsync(invite, ct);
        }
        return TypedResults.NoContent();
    }

    private static async Task<InvitePreview> PreviewInviteAsync(
        string token, ICampaignStore store, IUserDirectory users, TimeProvider time, CancellationToken ct)
    {
        var found = await FindInviteAsync(store, token, ct);
        if (found is null) return new InvitePreview(null, null, CampaignRole.Player, "This invite link isn't valid. Ask your Storyteller for a new one.");

        var (invite, campaign) = found.Value;
        var storytellerId = (await store.MembersAsync(campaign.Id, ct))
            .Where(m => m.Role == CampaignRole.Storyteller).OrderBy(m => m.JoinedAt).Select(m => m.UserId).FirstOrDefault();
        var storyteller = storytellerId is null ? null : (await users.GetAsync([storytellerId], ct)).GetValueOrDefault(storytellerId)?.DisplayName;
        return new InvitePreview(campaign.Name, storyteller, invite.Role, Problem(invite, campaign, time.GetUtcNow()));
    }

    private static async Task<Results<Ok<JoinedCampaign>, ProblemHttpResult>> AcceptInviteAsync(
        string token, ICampaignStore store, CampaignAccess access, AuditLog audit, UserManager<RequiemUser> users,
        HttpContext http, TimeProvider time, CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null) return TypedResults.Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);

        var found = await FindInviteAsync(store, token, ct);
        if (found is null) return Gone("This invite link isn't valid. Ask your Storyteller for a new one.");
        var (invite, campaign) = found.Value;

        // Already in: nothing to do, and no use of the link is spent.
        if (await access.RoleInAsync(campaign.Id, ct) is null)
        {
            var now = time.GetUtcNow();
            audit.Record(campaign.Id, null, "member.join", $"{user.DisplayName} joined through an invite link.");
            var member = new CampaignMember { CampaignId = campaign.Id, UserId = user.Id, Role = invite.Role, JoinedAt = now };
            if (!await store.JoinWithInviteAsync(invite, member, now, ct)) return Gone(UsedUp);
        }
        return TypedResults.Ok(new JoinedCampaign(campaign.Id, await ToCurrentUserAsync(user, users)));
    }

    /// <summary>Creates an account through an invite link, joins the campaign and signs in.</summary>
    private static async Task<Results<Ok<JoinedCampaign>, ValidationProblem, ProblemHttpResult>> JoinAsync(
        string token, RegisterRequest request, ICampaignStore store, AuditLog audit, UserManager<RequiemUser> users,
        SignInManager<RequiemUser> signIn, TimeProvider time, CancellationToken ct)
    {
        if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);

        var found = await FindInviteAsync(store, token, ct);
        if (found is null) return Gone("This invite link isn't valid. Ask your Storyteller for a new one.");
        var (invite, campaign) = found.Value;
        if (Problem(invite, campaign, time.GetUtcNow()) is { } unusable) return Gone(unusable);

        var email = request.Email.Trim();
        var user = new RequiemUser { UserName = email, Email = email, EmailConfirmed = false, DisplayName = request.DisplayName.Trim() };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return TypedResults.ValidationProblem(AccountEndpoints.CreationErrors(created));

        // The new account isn't signed in yet, so the entry is in its name rather than the request's user.
        var now = time.GetUtcNow();
        audit.RecordFor(user.Id, campaign.Id, "member.join", $"{user.DisplayName} made an account and joined through an invite link.");
        var member = new CampaignMember { CampaignId = campaign.Id, UserId = user.Id, Role = invite.Role, JoinedAt = now };
        if (!await store.JoinWithInviteAsync(invite, member, now, ct))
        {
            // Someone took the last use meanwhile: the account and the membership go together, so neither stays.
            await users.DeleteAsync(user);
            return Gone(UsedUp);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(new JoinedCampaign(campaign.Id, await ToCurrentUserAsync(user, users)));
    }

    private const string UsedUp = "This invite link has expired or been used up. Ask your Storyteller for a new one.";

    private static async Task<(CampaignInvite Invite, Campaign Campaign)?> FindInviteAsync(ICampaignStore store, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var invite = await store.FindInviteAsync(Hash(token), ct);
        if (invite is null || await store.GetAsync(invite.CampaignId, ct) is not { } campaign) return null;
        return (invite, campaign);
    }

    private static string? Problem(CampaignInvite invite, Campaign campaign, DateTimeOffset now) =>
        invite.RevokedAt is not null ? "This invite link was withdrawn. Ask your Storyteller for a new one."
        : invite.ExpiresAt <= now ? "This invite link has expired. Ask your Storyteller for a new one."
        : invite.UsesLeft == 0 ? "This invite link has been used up. Ask your Storyteller for a new one."
        : campaign.ArchivedAt is not null ? "This campaign has been archived."
        : null;

    private static ProblemHttpResult Gone(string message) =>
        TypedResults.Problem(message, statusCode: StatusCodes.Status410Gone);

    /// <summary>256 random bits, URL-safe.</summary>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    // ---------- Mapping ----------

    private static void Apply(CampaignForm form, Campaign campaign)
    {
        campaign.Name = form.Name.Trim();
        campaign.Description = Clean(form.Description);
        campaign.ChronicleTenet = Clean(form.ChronicleTenet);
        campaign.CoterieScript = Clean(form.CoterieScript);
        campaign.AllowedTemplates = form.AllowedTemplates & CharacterTemplates.All;
    }

    private static async Task<CampaignView> ViewAsync(
        ICampaignStore store, IUserDirectory users, string me, Guid id, CampaignRole role, CancellationToken ct)
    {
        var campaign = (await store.GetAsync(id, ct))!;
        var storyteller = role == CampaignRole.Storyteller;
        var members = await store.MembersAsync(id, ct);
        var people = await users.GetAsync(members.Select(m => m.UserId), ct);
        var invites = storyteller
            ? (await store.InvitesAsync(id, ct)).OrderByDescending(i => i.CreatedAt).Select(ToView).ToList()
            : [];

        return new CampaignView(
            campaign.Id, campaign.Name, campaign.Description, campaign.ChronicleTenet, campaign.CoterieScript,
            campaign.AllowedTemplates, campaign.ArchivedAt, role,
            members
                .Select(m => (Member: m, Person: people.GetValueOrDefault(m.UserId)))
                .OrderBy(m => m.Member.Role).ThenBy(m => m.Person?.DisplayName, StringComparer.OrdinalIgnoreCase)
                // Players see each other's names, not their email addresses.
                .Select(m => new CampaignMemberView(m.Member.UserId, m.Person?.DisplayName ?? "Someone", storyteller ? m.Person?.Email : null,
                    m.Member.Role, m.Member.JoinedAt, m.Member.UserId == me))
                .ToList(),
            invites);
    }

    private static CampaignInviteView ToView(CampaignInvite i) => new(i.Id, i.Role, i.CreatedAt, i.ExpiresAt, i.UsesLeft, i.RevokedAt);

    private static async Task<CurrentUser> ToCurrentUserAsync(RequiemUser user, UserManager<RequiemUser> users) =>
        new(true, user.Email, user.DisplayName, [.. await users.GetRolesAsync(user)]);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string[]>? Validate(object form)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true)) return null;
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(e => e.Member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").ToArray());
    }
}
