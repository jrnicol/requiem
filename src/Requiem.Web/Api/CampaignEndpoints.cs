using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
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
        campaigns.MapPost("/{id:guid}/archive", (Guid id, RequiemDbContext db, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct) =>
            SetArchivedAsync(id, true, db, access, audit, time, ct));
        campaigns.MapPost("/{id:guid}/unarchive", (Guid id, RequiemDbContext db, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct) =>
            SetArchivedAsync(id, false, db, access, audit, time, ct));
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

    private static async Task<List<CampaignSummary>> ListAsync(RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        var userId = access.UserId;
        var rows = await db.CampaignMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new
            {
                Campaign = db.Campaigns.Single(c => c.Id == m.CampaignId),
                m.Role,
                Players = db.CampaignMembers.Count(x => x.CampaignId == m.CampaignId && x.Role == CampaignRole.Player),
                Characters = db.Characters.Count(c => c.CampaignId == m.CampaignId),
            })
            .ToListAsync(ct);
        return rows
            .OrderBy(r => r.Campaign.ArchivedAt is not null)
            .ThenBy(r => r.Campaign.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new CampaignSummary(r.Campaign.Id, r.Campaign.Name, r.Campaign.Description, r.Role, r.Players, r.Characters, r.Campaign.ArchivedAt))
            .ToList();
    }

    private static async Task<Results<Created<CampaignView>, ValidationProblem>> CreateAsync(
        CampaignForm form, RequiemDbContext db, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var now = time.GetUtcNow();
        var campaign = new Campaign { Id = Guid.CreateVersion7(now), Name = form.Name.Trim(), CreatedAt = now };
        Apply(form, campaign);
        campaign.Members.Add(new CampaignMember { UserId = access.UserId, Role = CampaignRole.Storyteller, JoinedAt = now });
        db.Campaigns.Add(campaign);
        audit.Record(campaign.Id, null, "campaign.create", $"Created the campaign {campaign.Name}.");
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/campaigns/{campaign.Id}", await ViewAsync(db, access.UserId,campaign.Id, CampaignRole.Storyteller, ct));
    }

    private static async Task<Results<Ok<CampaignView>, NotFound>> GetAsync(
        Guid id, RequiemDbContext db, CampaignAccess access, CancellationToken ct) =>
        await access.RoleInAsync(id, ct) is { } role ? TypedResults.Ok(await ViewAsync(db, access.UserId,id, role, ct)) : TypedResults.NotFound();

    private static async Task<Results<Ok<CampaignView>, NotFound, ForbidHttpResult, ValidationProblem>> UpdateAsync(
        Guid id, CampaignForm form, RequiemDbContext db, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var campaign = await db.Campaigns.SingleAsync(c => c.Id == id, ct);
        Apply(form, campaign);
        audit.Record(id, null, "campaign.update", $"Updated the settings of {campaign.Name}.");
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ViewAsync(db, access.UserId,id, role, ct));
    }

    /// <summary>Any member may edit the shared Coterie Script (PHASE2.md decision 7).</summary>
    private static async Task<Results<Ok<CampaignView>, NotFound, ValidationProblem>> UpdateCoterieScriptAsync(
        Guid id, CoterieScriptForm form, RequiemDbContext db, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (Validate(form) is { } errors) return TypedResults.ValidationProblem(errors);

        var campaign = await db.Campaigns.SingleAsync(c => c.Id == id, ct);
        var text = Clean(form.Text);
        if (text != campaign.CoterieScript)
        {
            audit.Record(id, null, "campaign.coterie-script",
                $"Changed the Coterie Script from \"{campaign.CoterieScript}\" to \"{text}\".");
            campaign.CoterieScript = text;
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.Ok(await ViewAsync(db, access.UserId,id, role, ct));
    }

    private static async Task<Results<Ok<CampaignView>, NotFound, ForbidHttpResult>> SetArchivedAsync(
        Guid id, bool archived, RequiemDbContext db, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();

        var campaign = await db.Campaigns.SingleAsync(c => c.Id == id, ct);
        campaign.ArchivedAt = archived ? campaign.ArchivedAt ?? time.GetUtcNow() : null;
        audit.Record(id, null, archived ? "campaign.archive" : "campaign.unarchive",
            $"{(archived ? "Archived" : "Restored")} {campaign.Name}.");
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ViewAsync(db, access.UserId,id, role, ct));
    }

    /// <summary>Only an empty campaign can be deleted; one with characters or scenes is archived instead.</summary>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<ProblemDetailsBody>>> DeleteAsync(
        Guid id, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();

        // Check here rather than rely on the database: EF would quietly detach loaded characters (Campaign.cs).
        if (await db.Characters.AnyAsync(c => c.CampaignId == id, ct) || await db.Scenes.AnyAsync(s => s.CampaignId == id, ct))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This campaign still has characters or scenes. Move them out, or archive the campaign instead."));

        await db.Campaigns.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Members ----------

    /// <summary>Storytellers remove players; any member can remove themselves (leave). A campaign keeps at least one Storyteller.</summary>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<ProblemDetailsBody>>> RemoveMemberAsync(
        Guid id, string userId, RequiemDbContext db, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        var leaving = userId == access.UserId;
        if (!leaving && role != CampaignRole.Storyteller) return TypedResults.Forbid();

        var member = await db.CampaignMembers.Include(m => m.User).SingleOrDefaultAsync(m => m.CampaignId == id && m.UserId == userId, ct);
        if (member is null) return TypedResults.NotFound();
        if (member.Role == CampaignRole.Storyteller
            && await db.CampaignMembers.CountAsync(m => m.CampaignId == id && m.Role == CampaignRole.Storyteller, ct) == 1)
            return TypedResults.Conflict(new ProblemDetailsBody("A campaign needs a Storyteller. Archive it instead."));

        db.CampaignMembers.Remove(member);
        audit.Record(id, null, leaving ? "member.leave" : "member.remove",
            leaving ? $"{member.User.DisplayName} left the campaign." : $"Removed {member.User.DisplayName} from the campaign.");
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Invites ----------

    private static async Task<Results<Created<CreatedInvite>, NotFound, ForbidHttpResult, ValidationProblem>> CreateInviteAsync(
        Guid id, InviteForm form, RequiemDbContext db, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
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
        db.CampaignInvites.Add(invite);
        audit.Record(id, null, "invite.create",
            $"Made an invite link for {(form.MaxUses is { } uses ? $"{uses} player{(uses == 1 ? "" : "s")}" : "any number of players")}, " +
            $"valid {form.ExpiresInDays} day{(form.ExpiresInDays == 1 ? "" : "s")}.");
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/invites/{token}", new CreatedInvite(ToView(invite), token));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> RevokeInviteAsync(
        Guid id, Guid inviteId, RequiemDbContext db, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await access.RoleInAsync(id, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();

        var invite = await db.CampaignInvites.SingleOrDefaultAsync(i => i.Id == inviteId && i.CampaignId == id, ct);
        if (invite is null) return TypedResults.NotFound();
        if (invite.RevokedAt is null)
        {
            invite.RevokedAt = time.GetUtcNow();
            audit.Record(id, null, "invite.revoke", "Revoked an invite link.");
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    private static async Task<InvitePreview> PreviewInviteAsync(string token, RequiemDbContext db, TimeProvider time, CancellationToken ct)
    {
        var found = await FindInviteAsync(db, token, ct);
        if (found is null) return new InvitePreview(null, null, CampaignRole.Player, "This invite link isn't valid. Ask your Storyteller for a new one.");

        var (invite, campaign) = found.Value;
        var storyteller = await db.CampaignMembers.AsNoTracking()
            .Where(m => m.CampaignId == campaign.Id && m.Role == CampaignRole.Storyteller)
            .OrderBy(m => m.JoinedAt).Select(m => m.User.DisplayName).FirstOrDefaultAsync(ct);
        return new InvitePreview(campaign.Name, storyteller, invite.Role, Problem(invite, campaign, time.GetUtcNow()));
    }

    private static async Task<Results<Ok<JoinedCampaign>, ProblemHttpResult>> AcceptInviteAsync(
        string token, RequiemDbContext db, CampaignAccess access, AuditLog audit, UserManager<RequiemUser> users,
        HttpContext http, TimeProvider time, CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null) return TypedResults.Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);

        var found = await FindInviteAsync(db, token, ct);
        if (found is null) return Gone("This invite link isn't valid. Ask your Storyteller for a new one.");
        var (invite, campaign) = found.Value;

        // Already in: nothing to do, and no use of the link is spent.
        if (await access.RoleInAsync(campaign.Id, ct) is null)
        {
            if (await SpendUseAsync(db, invite, time.GetUtcNow(), ct) is { } problem) return Gone(problem);
            db.CampaignMembers.Add(new CampaignMember { CampaignId = campaign.Id, UserId = user.Id, Role = invite.Role, JoinedAt = time.GetUtcNow() });
            audit.Record(campaign.Id, null, "member.join", $"{user.DisplayName} joined through an invite link.");
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.Ok(new JoinedCampaign(campaign.Id, await ToCurrentUserAsync(user, users)));
    }

    /// <summary>Creates an account through an invite link, joins the campaign and signs in.</summary>
    private static async Task<Results<Ok<JoinedCampaign>, ValidationProblem, ProblemHttpResult>> JoinAsync(
        string token, RegisterRequest request, RequiemDbContext db, UserManager<RequiemUser> users,
        SignInManager<RequiemUser> signIn, TimeProvider time, CancellationToken ct)
    {
        if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);

        var found = await FindInviteAsync(db, token, ct);
        if (found is null) return Gone("This invite link isn't valid. Ask your Storyteller for a new one.");
        var (invite, campaign) = found.Value;
        if (Problem(invite, campaign, time.GetUtcNow()) is { } unusable) return Gone(unusable);

        // The account, the spent use and the membership succeed or fail together. On Azure SQL the
        // connection retries on failure, so the transaction has to run inside EF's execution strategy.
        RequiemUser? user = null;
        var failure = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var email = request.Email.Trim();
            user = new RequiemUser { UserName = email, Email = email, EmailConfirmed = false, DisplayName = request.DisplayName.Trim() };

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded) return (IResult)TypedResults.ValidationProblem(AccountEndpoints.CreationErrors(created));

            if (await SpendUseAsync(db, invite, time.GetUtcNow(), ct) is { } problem) return Gone(problem);
            db.CampaignMembers.Add(new CampaignMember { CampaignId = campaign.Id, UserId = user.Id, Role = invite.Role, JoinedAt = time.GetUtcNow() });
            // Written directly: AuditLog needs a signed-in user, and this one isn't signed in yet.
            db.AuditEntries.Add(new AuditEntry
            {
                CampaignId = campaign.Id, UserId = user.Id, Action = "member.join",
                Summary = $"{user.DisplayName} made an account and joined through an invite link.", At = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        });

        switch (failure)
        {
            case ValidationProblem validation: return validation;
            case ProblemHttpResult problem: return problem;
        }

        await signIn.SignInAsync(user!, isPersistent: true);
        return TypedResults.Ok(new JoinedCampaign(campaign.Id, await ToCurrentUserAsync(user!, users)));
    }

    /// <summary>Takes one use of the link atomically, so two players can't both take the last one.</summary>
    /// <returns>A problem message, or null when a use was spent.</returns>
    private static async Task<string?> SpendUseAsync(RequiemDbContext db, CampaignInvite invite, DateTimeOffset now, CancellationToken ct)
    {
        var spent = await db.CampaignInvites
            .Where(i => i.Id == invite.Id && i.RevokedAt == null && i.ExpiresAt > now && (i.UsesLeft == null || i.UsesLeft > 0))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsesLeft, i => i.UsesLeft == null ? null : i.UsesLeft - 1), ct);
        return spent == 1 ? null : "This invite link has expired or been used up. Ask your Storyteller for a new one.";
    }

    private static async Task<(CampaignInvite Invite, Campaign Campaign)?> FindInviteAsync(RequiemDbContext db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Hash(token);
        var invite = await db.CampaignInvites.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null) return null;
        var campaign = await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == invite.CampaignId, ct);
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

    private static async Task<CampaignView> ViewAsync(RequiemDbContext db, string me, Guid id, CampaignRole role, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        var storyteller = role == CampaignRole.Storyteller;
        var members = await db.CampaignMembers.AsNoTracking()
            .Where(m => m.CampaignId == id)
            .Select(m => new { m.UserId, m.User.DisplayName, m.User.Email, m.Role, m.JoinedAt })
            .ToListAsync(ct);
        var invites = storyteller
            ? (await db.CampaignInvites.AsNoTracking().Where(i => i.CampaignId == id).ToListAsync(ct))
                .OrderByDescending(i => i.CreatedAt).Select(ToView).ToList()
            : [];

        return new CampaignView(
            campaign.Id, campaign.Name, campaign.Description, campaign.ChronicleTenet, campaign.CoterieScript,
            campaign.AllowedTemplates, campaign.ArchivedAt, role,
            members.OrderBy(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                // Players see each other's names, not their email addresses.
                .Select(m => new CampaignMemberView(m.UserId, m.DisplayName, storyteller ? m.Email : null, m.Role, m.JoinedAt, m.UserId == me))
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
