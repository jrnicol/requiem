using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

/// <summary>
/// Advancement for player characters in play (PHASE2.md §6, milestone 2.3). Players log Corruption (vampires)
/// or Links (mortals and ghouls), mark their Scripts and spend XP; it all counts at once and goes into the
/// audit log, where the Storyteller sees it and can undo it. Storytellers also award XP.
/// <list type="bullet">
/// <item>4 Corruption, from 4 different vampiric acts, or 4 Links unlock spending. The count resets when the
/// player is done spending, or at the Storyteller's next award once they've spent (house ruling 2026-10-24).</item>
/// <item>The 4th Link earns 1 XP; Corruption only unlocks spending. All four Scripts marked earn 4 XP for
/// a vampire or 1 for anyone else, and clear.</item>
/// </list>
/// </summary>
public static class AdvancementEndpoints
{
    public static RouteGroupBuilder MapAdvancementEndpoints(this RouteGroupBuilder api)
    {
        var characters = api.MapGroup("/player-characters").WithTags("Advancement");
        characters.MapGet("/{id:guid}/advancement", GetAsync);
        characters.MapPost("/{id:guid}/corruption", AddCorruptionAsync);
        characters.MapPost("/{id:guid}/links", AddLinkAsync);
        characters.MapPost("/{id:guid}/scripts/{slot:int}/mark", MarkScriptAsync);
        characters.MapPost("/{id:guid}/spend", SpendAsync);
        characters.MapPost("/{id:guid}/done-spending", DoneSpendingAsync);

        api.MapPost("/campaigns/{campaignId:guid}/xp-awards", AwardAsync).WithTags("Advancement");
        api.MapPost("/audit/{auditId}/undo", UndoAsync).WithTags("Advancement");
        return api;
    }

    /// <summary>How to reverse an audited change, stored as JSON on its audit entry.</summary>
    private sealed record Undo(string Kind, long? Id = null, XpPurchase? Purchase = null, XpPurchaseQuote? Quote = null, int? HumanityBefore = null);

    private static readonly JsonSerializerOptions Json = RequiemJson.Options;

    // ---------- Reading ----------

    private static async Task<Results<Ok<AdvancementView>, NotFound>> GetAsync(
        Guid id, ICharacterStore characters, IAuditStore audits, IUserDirectory users, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, ct);
        return character is null ? TypedResults.NotFound() : TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));
    }

    // ---------- Corruption, Links and Scripts ----------

    private static async Task<Results<Ok<AdvancementView>, NotFound, ValidationProblem>> AddCorruptionAsync(
        Guid id, CorruptionRequest request, ICharacterStore characters, IAuditStore audits, IUserDirectory users,
        CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var character = await LoadActableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();
        var act = request.Act.Trim();
        if (character.Kind != CharacterKind.Vampire) return Problem("Only vampires gain Corruption; others earn Links.");
        if (act.Length is 0 or > 200) return Problem("Say what they did, briefly.");
        if (character.CorruptionMarks.Count >= ExperienceRules.ProgressPointsToUnlockXp)
            return Problem("Corruption is already at 4: spend XP, then it starts again.");
        if (character.CorruptionMarks.Any(m => string.Equals(m.Act, act, StringComparison.OrdinalIgnoreCase)))
            return Problem("Corruption comes from 4 different vampiric acts; that one's already counted.");

        var mark = new CorruptionMark { Act = act, CreatedAt = time.GetUtcNow() };
        character.CorruptionMarks.Add(mark);
        Record(audit, character, "corruption.add", $"Corruption {character.CorruptionMarks.Count}/4: {act}.", () => new Undo("corruption", mark.Id));
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));
    }

    private static async Task<Results<Ok<AdvancementView>, NotFound, ValidationProblem>> AddLinkAsync(
        Guid id, LinkRequest request, ICharacterStore characters, IAuditStore audits, IUserDirectory users,
        CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var character = await LoadActableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();
        if (character.Kind == CharacterKind.Vampire) return Problem("Vampires gain Corruption instead of Links.");
        if (character.Links.Count >= ExperienceRules.ProgressPointsToUnlockXp)
            return Problem("Links are already at 4: spend XP, then they start again.");

        // A Link is a scene with other player characters in play in the same campaign.
        var others = request.WithCharacterIds.Distinct().Where(other => other != id).ToList();
        if (others.Count == 0) return Problem("Choose the other player characters in the scene.");
        var known = (await characters.GetManyAsync(others, ct))
            .Where(c => c.CampaignId == character.CampaignId && c.IsPlayerCharacter && c.Status == CharacterStatus.Active)
            .Select(c => c.Name).ToList();
        if (known.Count != others.Count) return Problem("Links are made with other player characters in play in this campaign.");

        var link = new CharacterLink { Kind = request.Kind, WithCharacterIds = others, Note = Clean(request.Note), At = time.GetUtcNow() };
        character.Links.Add(link);
        Record(audit, character, "link.add",
            $"Link {character.Links.Count}/4: {Label(request.Kind)} with {string.Join(", ", known)}.", () => new Undo("link", link.Id));
        await characters.SaveAsync(character, ct);

        // Completing 4 Links is worth 1 XP (WoD Core p.75).
        if (character.Links.Count == ExperienceRules.ProgressPointsToUnlockXp)
            await AwardOneAsync(characters, audit, access, character, AdvancementRules.LinksCompletionXp, "Completed 4 Links", time, ct);
        return TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));
    }

    private static async Task<Results<Ok<AdvancementView>, NotFound, ValidationProblem>> MarkScriptAsync(
        Guid id, int slot, ScriptMarkRequest request, ICharacterStore characters, IAuditStore audits, IUserDirectory users,
        CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var character = await LoadActableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();
        var script = character.Scripts.FirstOrDefault(s => s.Slot == slot);
        if (script is null) return Problem("That Script isn't on the sheet.");
        if (script.IsMarked == request.Marked) return TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));

        script.IsMarked = request.Marked;
        audit.Record(character.CampaignId, id, "script.mark",
            $"{(request.Marked ? "Marked" : "Unmarked")} the {CreationRules.ScriptName(script.Kind)} Script.");
        await characters.SaveAsync(character, ct);

        // All four played out: the XP, and the marks clear (house ruling 6).
        var form = FullSheetMapper.ToForm(character);
        if (AdvancementRules.ScriptsComplete(form.Scripts))
        {
            foreach (var s in character.Scripts) s.IsMarked = false;
            await AwardOneAsync(characters, audit, access, character, ExperienceRules.ScriptsCompletionXp(character.Kind == CharacterKind.Vampire),
                "All four Scripts played out", time, ct, action: "scripts.complete");
        }
        return TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));
    }

    // ---------- Spending ----------

    private static async Task<Results<Ok<AdvancementView>, NotFound, ValidationProblem>> SpendAsync(
        Guid id, XpPurchase purchase, ICharacterStore characters, ICatalogStore catalogStore, IAuditStore audits, IUserDirectory users,
        CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var character = await LoadActableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();

        var catalog = await CatalogEndpoints.CreationCatalogAsync(catalogStore, ct);
        var form = FullSheetMapper.ToForm(character);
        var quote = AdvancementRules.Quote(form, purchase, catalog, Progress(character));
        if (!quote.IsAllowed)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = quote.Violations.Select(v => v.Message).ToArray() });

        var humanityBefore = form.Humanity;
        AdvancementRules.Apply(form, purchase, quote, catalog);
        var now = time.GetUtcNow();
        FullSheetMapper.Apply(form, character, now);
        var entry = new XpEntry
        {
            Amount = -quote.Cost, Reason = $"{quote.Trait} {quote.FromDots} → {quote.ToDots}", PurchaseKind = purchase.Kind,
            Trait = quote.Trait, FromDots = quote.FromDots, ToDots = quote.ToDots, RecordedByUserId = access.UserId, CreatedAt = now,
        };
        character.XpLedger.Add(entry);

        var lostHumanity = humanityBefore is { } before && form.Humanity < before ? $" Humanity drops to {form.Humanity}." : "";
        Record(audit, character, "xp.spend",
            $"Spent {quote.Cost} XP: {quote.Trait} {quote.FromDots} → {quote.ToDots}.{lostHumanity}",
            () => new Undo("spend", entry.Id, purchase, quote, humanityBefore));
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));
    }

    /// <summary>Ends a spending session: Corruption or Links go back to 0, and need 4 more to spend again.</summary>
    private static async Task<Results<Ok<AdvancementView>, NotFound, ValidationProblem>> DoneSpendingAsync(
        Guid id, ICharacterStore characters, IAuditStore audits, IUserDirectory users, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        var character = await LoadActableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();
        if (!AdvancementRules.IsUnlocked(Progress(character))) return Problem("Spending isn't unlocked yet.");

        ResetProgress(character);
        audit.Record(character.CampaignId, id, "xp.session-end",
            $"Finished spending XP; {AdvancementRules.ProgressName(character.Kind)} starts again at 0.");
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ViewAsync(characters, audits, users, access, character, ct));
    }

    // ---------- Awards ----------

    private static async Task<Results<Ok<int>, NotFound, ForbidHttpResult, ValidationProblem>> AwardAsync(
        Guid campaignId, XpAwardRequest request, ICharacterStore store, CampaignAccess access, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await access.RoleInAsync(campaignId, ct) is not { } role) return TypedResults.NotFound();
        if (role != CampaignRole.Storyteller) return TypedResults.Forbid();
        if (request.Amount is < 1 or > 50 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
            return Problem("Award 1 to 50 XP, and say what it's for.");

        var ids = request.CharacterIds.Distinct().ToList();
        var characters = (await store.GetManyAsync(ids, ct))
            .Where(c => c.CampaignId == campaignId && c.IsPlayerCharacter && c.Status == CharacterStatus.Active)
            .ToList();
        if (characters.Count == 0 || characters.Count != ids.Count) return Problem("Choose player characters in play in this campaign.");

        foreach (var character in characters)
        {
            // A player who spent since unlocking has finished their session by now.
            if (SpentSinceUnlocking(character)) ResetProgress(character);
            await AwardOneAsync(store, audit, access, character, request.Amount, request.Reason.Trim(), time, ct);
        }
        return TypedResults.Ok(characters.Count);
    }

    private static Task AwardOneAsync(
        ICharacterStore characters, AuditLog audit, CampaignAccess access, Character character, int amount, string reason, TimeProvider time,
        CancellationToken ct, string action = "xp.award")
    {
        var entry = new XpEntry { Amount = amount, Reason = reason, RecordedByUserId = access.UserId, CreatedAt = time.GetUtcNow() };
        character.XpLedger.Add(entry);
        Record(audit, character, action, $"+{amount} XP: {reason}.", () => new Undo("xp", entry.Id));
        return characters.SaveAsync(character, ct);
    }

    // ---------- Undo ----------

    /// <summary>The Storyteller reverses an audited change: a spend, an award, a Corruption act or a Link.</summary>
    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetailsBody>>> UndoAsync(
        string auditId, ICharacterStore characters, ICatalogStore catalogStore, IAuditStore audits, CampaignAccess access,
        AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var entry = await audits.GetAsync(auditId, ct);
        if (entry?.CampaignId is not { } campaignId || !await access.IsStorytellerAsync(campaignId, ct)) return TypedResults.NotFound();
        if (entry.UndoData is null || entry.UndoneAt is not null)
            return TypedResults.Conflict(new ProblemDetailsBody(entry.UndoneAt is null ? "That can't be undone." : "That's already been undone."));
        var undo = JsonSerializer.Deserialize<Undo>(entry.UndoData, Json)!;

        var character = entry.CharacterId is { } characterId && await characters.GetAsync(characterId, ct) is { Tier: CharacterTier.FullSheet } found
            ? found
            : null;
        if (character is null) return TypedResults.Conflict(new ProblemDetailsBody("The character is gone."));

        switch (undo.Kind)
        {
            case "corruption":
                character.CorruptionMarks.RemoveAll(m => m.Id == undo.Id);
                break;
            case "link":
                character.Links.RemoveAll(l => l.Id == undo.Id);
                break;
            case "xp":
                character.XpLedger.RemoveAll(x => x.Id == undo.Id);
                break;
            case "spend":
            {
                var form = FullSheetMapper.ToForm(character);
                var catalog = await CatalogEndpoints.CreationCatalogAsync(catalogStore, ct);
                if (!AdvancementRules.Revert(form, undo.Purchase!, undo.Quote!, catalog))
                    return TypedResults.Conflict(new ProblemDetailsBody($"{undo.Quote!.Trait} has changed since; undo the later change first."));
                if (undo.Purchase!.Kind == PurchaseKind.BloodPotency) form.Humanity = undo.HumanityBefore;
                FullSheetMapper.Apply(form, character, time.GetUtcNow());
                character.XpLedger.RemoveAll(x => x.Id == undo.Id);
                break;
            }
            default:
                return TypedResults.Conflict(new ProblemDetailsBody("That can't be undone."));
        }

        audit.MarkUndone(entry);
        audit.Record(campaignId, character.Id, "undo", $"Undid: {entry.Summary}");
        await characters.SaveAsync(character, ct);
        return TypedResults.NoContent();
    }

    // ---------- Helpers ----------

    private static async Task<Character?> LoadAsync(CampaignAccess access, Guid id, CancellationToken ct) =>
        await access.VisiblePlayerCharacterAsync(id, ct) is { Tier: CharacterTier.FullSheet } character ? character : null;

    /// <summary>The character, when the viewer may act on it: its player or a Storyteller, while it's in play.</summary>
    private static async Task<Character?> LoadActableAsync(CampaignAccess access, Guid id, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, ct);
        if (character is not { Status: CharacterStatus.Active, CampaignId: { } campaignId }) return null;
        return character.OwnerUserId == access.UserId || await access.IsStorytellerAsync(campaignId, ct) ? character : null;
    }

    /// <summary>Records an undoable change; the undo data is worked out when it's saved, once new items have ids.</summary>
    private static void Record(AuditLog audit, Character character, string action, string summary, Func<Undo> undo) =>
        audit.Record(character.CampaignId, character.Id, action, summary, () => JsonSerializer.Serialize(undo(), Json));

    private static int Progress(Character c) => c.Kind == CharacterKind.Vampire ? c.CorruptionMarks.Count : c.Links.Count;

    private static void ResetProgress(Character c)
    {
        if (c.Kind == CharacterKind.Vampire) c.CorruptionMarks.Clear();
        else c.Links.Clear();
    }

    /// <summary>Unlocked, and XP has been spent since the count reached 4.</summary>
    private static bool SpentSinceUnlocking(Character c)
    {
        if (!AdvancementRules.IsUnlocked(Progress(c))) return false;
        var unlockedAt = c.Kind == CharacterKind.Vampire ? c.CorruptionMarks.Max(m => m.CreatedAt) : c.Links.Max(l => l.At);
        return c.XpLedger.Any(x => x.Amount < 0 && x.CreatedAt >= unlockedAt);
    }

    private static async Task<AdvancementView> ViewAsync(
        ICharacterStore characters, IAuditStore audits, IUserDirectory users, CampaignAccess access, Character character, CancellationToken ct)
    {
        var isStoryteller = character.CampaignId is { } campaignId && await access.IsStorytellerAsync(campaignId, ct);
        var form = FullSheetMapper.ToForm(character);
        var progress = Progress(character);
        var entries = character.Kind == CharacterKind.Vampire
            ? character.CorruptionMarks.OrderBy(m => m.CreatedAt).Select(m => new ProgressEntry(m.Id, m.Act, m.CreatedAt)).ToList()
            : await LinkEntriesAsync(characters, character, ct);

        var history = await audits.ForCharacterAsync(character.Id, 60, ct);
        var who = await users.GetAsync(history.Select(e => e.UserId), ct);

        return new AdvancementView(
            form.ExperienceEarned, form.ExperienceSpent, form.ExperienceAvailable,
            AdvancementRules.ProgressName(character.Kind), progress, AdvancementRules.IsUnlocked(progress), entries,
            form.Scripts,
            history.Select(e => new AdvancementHistoryEntry(e.Id.ToString(), e.At, who.GetValueOrDefault(e.UserId)?.DisplayName ?? "Someone",
                e.Summary, isStoryteller && e.UndoData is not null && e.UndoneAt is null, e.UndoneAt is not null)).ToList(),
            CanAct: character.Status == CharacterStatus.Active && (character.OwnerUserId == access.UserId || isStoryteller),
            isStoryteller);
    }

    private static async Task<List<ProgressEntry>> LinkEntriesAsync(ICharacterStore characters, Character character, CancellationToken ct)
    {
        var ids = character.Links.SelectMany(l => l.WithCharacterIds).Distinct().ToList();
        var names = (await characters.GetManyAsync(ids, ct)).ToDictionary(c => c.Id, c => c.Name);
        return character.Links.OrderBy(l => l.At)
            .Select(l => new ProgressEntry(l.Id,
                $"{Label(l.Kind)} with {string.Join(", ", l.WithCharacterIds.Select(i => names.GetValueOrDefault(i, "someone")))}{(l.Note is null ? "" : $": {l.Note}")}",
                l.At))
            .ToList();
    }

    private static string Label(LinkKind kind) => kind switch
    {
        LinkKind.CedeSpotlight => "Ceded the spotlight",
        LinkKind.Support => "Support",
        _ => "Camaraderie",
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 500)];

    private static ValidationProblem Problem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = [message] });
}
