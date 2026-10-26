using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Media;
using Requiem.Data.Stores;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

/// <summary>
/// Player characters (PHASE2.md §5, milestone 2.2). A player builds one with the creation wizard as a Draft,
/// then submits it; the Storyteller approves it (Active) or returns it with a note. A player has one character
/// in play at a time per campaign; retired and dead ones stay on the list.
/// <list type="bullet">
/// <item>Players edit their own Draft freely, except links to other characters (sire, domitor), the XP ledger
/// and the Storyteller's notes. Once Active, they edit its story; stats change through XP (2.3).</item>
/// <item>Storytellers open every PC in their campaigns, edit them with the full-sheet editor, and decide.</item>
/// </list>
/// Anything else reads as "not found".
/// </summary>
public static class PlayerCharacterEndpoints
{
    private static readonly CharacterStatus[] InPlay = [CharacterStatus.Draft, CharacterStatus.Submitted, CharacterStatus.Active];

    public static RouteGroupBuilder MapPlayerCharacterEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/campaigns/{campaignId:guid}/player-characters", ListAsync).WithTags("Player characters");
        api.MapPost("/campaigns/{campaignId:guid}/player-characters", CreateAsync).WithTags("Player characters");

        var characters = api.MapGroup("/player-characters").WithTags("Player characters");
        characters.MapGet("/{id:guid}", GetAsync);
        characters.MapPut("/{id:guid}", UpdateAsync);
        characters.MapDelete("/{id:guid}", DeleteAsync);
        characters.MapPost("/{id:guid}/submit", SubmitAsync);
        characters.MapPost("/{id:guid}/withdraw", WithdrawAsync);
        characters.MapPost("/{id:guid}/approve", ApproveAsync);
        characters.MapPost("/{id:guid}/return", ReturnAsync);
        characters.MapPost("/{id:guid}/status", SetStatusAsync);
        return api;
    }

    // ---------- Listing and creating ----------

    /// <summary>
    /// A Storyteller sees every PC in the campaign. A player sees their own, plus the others in play by name
    /// (for Links, and to know who's at the table); never their sheets.
    /// </summary>
    private static async Task<Results<Ok<List<PlayerCharacterSummary>>, NotFound>> ListAsync(
        Guid campaignId, ICharacterStore characters, IUserDirectory users, CampaignAccess access, CancellationToken ct)
    {
        if (await access.RoleInAsync(campaignId, ct) is not { } role) return TypedResults.NotFound();
        var me = access.UserId;
        var rows = (await characters.ListByCampaignAsync(campaignId, ct))
            .Where(c => c.IsPlayerCharacter && (role == CampaignRole.Storyteller || c.OwnerUserId == me || c.Status == CharacterStatus.Active))
            .ToList();
        var owners = await users.GetAsync(rows.Select(r => r.OwnerUserId).OfType<string>(), ct);
        return TypedResults.Ok(rows
            .OrderBy(r => Array.IndexOf(InPlay, r.Status) is var i && i < 0 ? InPlay.Length : i)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new PlayerCharacterSummary(r.Id, campaignId, r.Name, r.Kind, r.Concept, r.Status,
                (r.OwnerUserId is { } owner ? owners.GetValueOrDefault(owner)?.DisplayName : null) ?? "Unknown player",
                r.OwnerUserId == me, r.UpdatedAt))
            .ToList());
    }

    private static async Task<Results<Created<PlayerCharacterView>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>> CreateAsync(
        Guid campaignId, FullSheetForm form, ICharacterStore characters, ICampaignStore campaigns, ICatalogStore catalog, IUserDirectory users,
        CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var role = await access.RoleInAsync(campaignId, ct);
        if (role is null) return TypedResults.NotFound();
        if (role != CampaignRole.Player)
            return Problem(nameof(form.CampaignId), "Storytellers make NPCs in their own campaign; player characters belong to players.");

        var campaign = (await campaigns.GetAsync(campaignId, ct))!;
        if (campaign.ArchivedAt is not null) return Problem(nameof(form.CampaignId), "This campaign has been archived.");
        if (!Allows(campaign.AllowedTemplates, form.Kind))
            return Problem(nameof(form.Kind), $"This campaign doesn't allow {form.Kind.ToString().ToLowerInvariant()} characters.");

        var me = access.UserId;
        if ((await characters.ListByCampaignAsync(campaignId, ct)).Any(c => c.IsPlayerCharacter && c.OwnerUserId == me && InPlay.Contains(c.Status)))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "You already have a character in this campaign. Players have one at a time; a new one can follow when it retires or dies."));

        form.CampaignId = campaignId;
        KeepPlayerOutOf(form, current: null, campaign);
        if (await FullSheetEndpoints.ValidateAsync(form, null, catalog, access, ct, checkLinks: false) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var character = new Character
        {
            // Set now so the audit entry can point at it.
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            Name = form.Name, OwnerUserId = me, CampaignId = campaignId,
            IsPlayerCharacter = true, Status = CharacterStatus.Draft,
        };
        FullSheetMapper.Apply(form, character, time.GetUtcNow());
        audit.Record(campaignId, character.Id, "character.create", $"Started a {character.Kind.ToString().ToLowerInvariant()} character, {character.Name}.");
        await characters.AddAsync(character, ct);

        return TypedResults.Created($"/api/player-characters/{character.Id}", await ViewAsync(users, access, character, campaign, ct));
    }

    // ---------- Reading and editing ----------

    private static async Task<Results<Ok<PlayerCharacterView>, NotFound>> GetAsync(
        Guid id, ICampaignStore campaigns, IUserDirectory users, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();
        return TypedResults.Ok(await ViewAsync(users, access, character, await CampaignAsync(campaigns, character, ct), ct));
    }

    /// <summary>The player saves their character: everything while it's a Draft, its story once it's Active.</summary>
    private static async Task<Results<Ok<PlayerCharacterView>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>> UpdateAsync(
        Guid id, FullSheetForm form, ICharacterStore characters, ICampaignStore campaigns, ICatalogStore catalog, IUserDirectory users,
        CampaignAccess access, TimeProvider time, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, ct);
        if (character is null || character.OwnerUserId != access.UserId) return TypedResults.NotFound();
        var campaign = await CampaignAsync(campaigns, character, ct);
        var current = FullSheetMapper.ToForm(character);

        switch (character.Status)
        {
            case CharacterStatus.Draft:
                if (!Allows(campaign.AllowedTemplates, form.Kind))
                    return Problem(nameof(form.Kind), $"This campaign doesn't allow {form.Kind.ToString().ToLowerInvariant()} characters.");
                form.CampaignId = character.CampaignId;
                KeepPlayerOutOf(form, current, campaign);
                break;
            case CharacterStatus.Active:
                form = StoryOnly(form, current, campaign);
                break;
            default:
                return TypedResults.Conflict(new ProblemDetailsBody(character.Status == CharacterStatus.Submitted
                    ? "This character is waiting for the Storyteller. Withdraw it to make changes."
                    : $"This character is {character.Status.ToString().ToLowerInvariant()}; only the Storyteller can change it now."));
        }

        if (await FullSheetEndpoints.ValidateAsync(form, id, catalog, access, ct, checkLinks: false) is { } errors)
            return TypedResults.ValidationProblem(errors);

        FullSheetMapper.Apply(form, character, time.GetUtcNow());
        if (!await characters.TrySaveAsync(character, form.Version, ct))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This character was changed somewhere else after you opened it. Reload to see the latest version."));
        return TypedResults.Ok(await ViewAsync(users, access, character, campaign, ct));
    }

    /// <summary>A player can throw away a Draft; anything further along is the Storyteller's to retire.</summary>
    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetailsBody>>> DeleteAsync(
        Guid id, ICharacterStore characters, IMediaStore media, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        var character = await access.OwnCharacterAsync(id, ct);
        if (character is null) return TypedResults.NotFound();
        if (character.Status != CharacterStatus.Draft)
            return TypedResults.Conflict(new ProblemDetailsBody("Only a draft can be deleted."));

        audit.Record(character.CampaignId, null, "character.delete", $"Deleted the draft character {character.Name}.");
        await characters.DeleteAsync(id, ct);
        await media.DeleteAsync(character.PortraitUrl, ct);
        return TypedResults.NoContent();
    }

    // ---------- Submitting and approving ----------

    private static async Task<Results<Ok<PlayerCharacterView>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>> SubmitAsync(
        Guid id, ICharacterStore characters, ICampaignStore campaigns, ICatalogStore catalog, IUserDirectory users,
        CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, ct);
        if (character is null || character.OwnerUserId != access.UserId) return TypedResults.NotFound();
        if (character.Status != CharacterStatus.Draft)
            return TypedResults.Conflict(new ProblemDetailsBody("Only a draft can be submitted."));

        var campaign = await CampaignAsync(campaigns, character, ct);
        var report = CreationRules.Evaluate(WithCampaignScripts(FullSheetMapper.ToForm(character), campaign),
            await CatalogEndpoints.CreationCatalogAsync(catalog, ct), campaign.AllowedTemplates);
        if (!report.IsComplete)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = report.Issues.Where(i => i.Level == CreationIssueLevel.Missing).Select(i => i.Message).ToArray(),
            });

        character.Status = CharacterStatus.Submitted;
        character.ReviewNote = null;
        audit.Record(campaign.Id, id, "character.submit", report.IsWithinBudget
            ? $"Submitted {character.Name} for approval."
            : $"Submitted {character.Name} for approval, over budget: {string.Join("; ", report.Budget.Where(b => b.IsOver).Select(b => $"{b.Name} {b.Spent}/{b.Allowed}"))}.");
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ViewAsync(users, access, character, campaign, ct));
    }

    private static Task<Results<Ok<PlayerCharacterView>, NotFound, Conflict<ProblemDetailsBody>>> WithdrawAsync(
        Guid id, ICharacterStore characters, ICampaignStore campaigns, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct) =>
        ChangeStatusAsync(id, characters, campaigns, users, access, audit, ct, byStoryteller: false, CharacterStatus.Submitted, CharacterStatus.Draft,
            "character.withdraw", c => $"Withdrew {c.Name} to make changes.");

    private static Task<Results<Ok<PlayerCharacterView>, NotFound, Conflict<ProblemDetailsBody>>> ApproveAsync(
        Guid id, ICharacterStore characters, ICampaignStore campaigns, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct) =>
        ChangeStatusAsync(id, characters, campaigns, users, access, audit, ct, byStoryteller: true, CharacterStatus.Submitted, CharacterStatus.Active,
            "character.approve", c => $"Approved {c.Name}; they're in play.");

    private static Task<Results<Ok<PlayerCharacterView>, NotFound, Conflict<ProblemDetailsBody>>> ReturnAsync(
        Guid id, ReturnCharacterRequest request, ICharacterStore characters, ICampaignStore campaigns, IUserDirectory users,
        CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()[..Math.Min(request.Note.Trim().Length, 2000)];
        return ChangeStatusAsync(id, characters, campaigns, users, access, audit, ct, byStoryteller: true, CharacterStatus.Submitted, CharacterStatus.Draft,
            "character.return", c => note is null ? $"Returned {c.Name} to its player." : $"Returned {c.Name} to its player: {note}",
            c => c.ReviewNote = note);
    }

    /// <summary>The Storyteller retires a character, records its death, or brings it back into play.</summary>
    private static async Task<Results<Ok<PlayerCharacterView>, NotFound, Conflict<ProblemDetailsBody>, ValidationProblem>> SetStatusAsync(
        Guid id, CharacterStatusRequest request, ICharacterStore characters, ICampaignStore campaigns, IUserDirectory users,
        CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        if (request.Status is not (CharacterStatus.Active or CharacterStatus.Retired or CharacterStatus.Dead))
            return Problem(nameof(request.Status), "A character can be put back in play, retired or marked dead.");

        var character = await LoadAsync(access, id, ct);
        if (character?.CampaignId is not { } campaignId || !await access.IsStorytellerAsync(campaignId, ct)) return TypedResults.NotFound();
        if (character.Status is CharacterStatus.Draft or CharacterStatus.Submitted)
            return TypedResults.Conflict(new ProblemDetailsBody("Approve or return the character first."));
        var campaign = await CampaignAsync(campaigns, character, ct);
        if (character.Status == request.Status) return TypedResults.Ok(await ViewAsync(users, access, character, campaign, ct));

        if (request.Status == CharacterStatus.Active
            && (await characters.ListByCampaignAsync(campaignId, ct)).Any(c => c.Id != id && c.IsPlayerCharacter
                && c.OwnerUserId == character.OwnerUserId && InPlay.Contains(c.Status)))
            return TypedResults.Conflict(new ProblemDetailsBody("Their player already has another character in play."));

        audit.Record(campaignId, id, "character.status", $"{character.Name}: {character.Status} → {request.Status}.");
        character.Status = request.Status;
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ViewAsync(users, access, character, campaign, ct));
    }

    private static async Task<Results<Ok<PlayerCharacterView>, NotFound, Conflict<ProblemDetailsBody>>> ChangeStatusAsync(
        Guid id, ICharacterStore characters, ICampaignStore campaigns, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct,
        bool byStoryteller, CharacterStatus from, CharacterStatus to, string action, Func<Character, string> summary,
        Action<Character>? also = null)
    {
        var character = await LoadAsync(access, id, ct);
        if (character?.CampaignId is not { } campaignId) return TypedResults.NotFound();
        var allowed = byStoryteller ? await access.IsStorytellerAsync(campaignId, ct) : character.OwnerUserId == access.UserId;
        if (!allowed) return TypedResults.NotFound();
        if (character.Status != from)
            return TypedResults.Conflict(new ProblemDetailsBody($"This character is {character.Status.ToString().ToLowerInvariant()}, not {from.ToString().ToLowerInvariant()}."));

        character.Status = to;
        also?.Invoke(character);
        audit.Record(campaignId, id, action, summary(character));
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ViewAsync(users, access, character, await CampaignAsync(campaigns, character, ct), ct));
    }

    // ---------- Helpers ----------

    private static async Task<Character?> LoadAsync(CampaignAccess access, Guid id, CancellationToken ct) =>
        await access.VisiblePlayerCharacterAsync(id, ct) is { Tier: CharacterTier.FullSheet } character ? character : null;

    private static async Task<Campaign> CampaignAsync(ICampaignStore campaigns, Character character, CancellationToken ct) =>
        (await campaigns.GetAsync(character.CampaignId!.Value, ct))!;

    private static async Task<PlayerCharacterView> ViewAsync(
        IUserDirectory users, CampaignAccess access, Character character, Campaign campaign, CancellationToken ct)
    {
        var isStoryteller = await access.IsStorytellerAsync(campaign.Id, ct);
        var isMine = character.OwnerUserId == access.UserId;
        var sheet = WithCampaignScripts(FullSheetMapper.ToForm(character), campaign);
        if (!isStoryteller) sheet.GmNotes = null;
        var owner = character.OwnerUserId is { } ownerId ? (await users.GetAsync([ownerId], ct)).GetValueOrDefault(ownerId)?.DisplayName : null;
        return new PlayerCharacterView(
            sheet, character.Status, owner ?? "Unknown player", isMine, isStoryteller,
            CanEdit: isMine && character.Status is CharacterStatus.Draft or CharacterStatus.Active && campaign.ArchivedAt is null,
            character.ReviewNote, campaign.Name, campaign.AllowedTemplates);
    }

    /// <summary>The Coterie Script and Chronicle Tenet are the campaign's, shared by every character in it.</summary>
    private static FullSheetForm WithCampaignScripts(FullSheetForm sheet, Campaign campaign)
    {
        foreach (var script in sheet.Scripts)
        {
            if (script.Kind == ScriptKind.Coterie) script.Text = campaign.CoterieScript ?? "";
            else if (script.Kind == ScriptKind.ChronicleTenet) script.Text = campaign.ChronicleTenet ?? "";
        }
        return sheet;
    }

    /// <summary>
    /// What a player can't set on their own Draft: links to other characters (the Storyteller makes them),
    /// the XP ledger, the Storyteller's notes and play state. Those keep their current values.
    /// </summary>
    private static void KeepPlayerOutOf(FullSheetForm form, FullSheetForm? current, Campaign campaign)
    {
        form.SireId = current?.SireId;
        form.DomitorId = current?.DomitorId;
        form.GmNotes = current?.GmNotes;
        form.Experience = current?.Experience ?? [];
        form.Wounds = current?.Wounds ?? [];
        form.Stains = current?.Stains ?? [];
        form.CorruptionMarks = current?.CorruptionMarks ?? [];
        form.Compels = current?.Compels ?? [];
        form.Conditions = current?.Conditions ?? [];
        // A draft starts with full trackers.
        form.WillpowerCurrent = form.WillpowerDots;
        if (form.VitaeCapacity is { } vitae) form.VitaeCurrent = vitae;
        form.PotenceBoost = form.CelerityBoost = form.FortitudeBoost = 0;
        WithCampaignScripts(form, campaign);
    }

    /// <summary>
    /// Once a character is in play its stats change through XP and its trackers through the play view, so a
    /// player's save only touches its story: Virtue and Vice details, the Beast, relationships, appearance,
    /// reputation and backstory, their own Scripts' wording, notes and equipment.
    /// </summary>
    private static FullSheetForm StoryOnly(FullSheetForm form, FullSheetForm current, Campaign campaign)
    {
        current.VirtueDetail = form.VirtueDetail;
        current.ViceDetail = form.ViceDetail;
        current.BeastDescription = form.BeastDescription;
        current.Relationships = form.Relationships;
        current.Appearance = form.Appearance;
        current.Reputation = form.Reputation;
        current.Backstory = form.Backstory;
        current.Notes = form.Notes;
        current.Equipment = form.Equipment;
        foreach (var script in current.Scripts.Where(s => s.Kind is not (ScriptKind.Coterie or ScriptKind.ChronicleTenet)))
            script.Text = form.Scripts.FirstOrDefault(s => s.Slot == script.Slot)?.Text ?? script.Text;
        current.Version = form.Version;
        return WithCampaignScripts(current, campaign);
    }

    private static bool Allows(CharacterTemplates allowed, CharacterKind kind) => allowed.HasFlag(kind switch
    {
        CharacterKind.Vampire => CharacterTemplates.Vampire,
        CharacterKind.Ghoul => CharacterTemplates.Ghoul,
        _ => CharacterTemplates.Mortal,
    });

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
