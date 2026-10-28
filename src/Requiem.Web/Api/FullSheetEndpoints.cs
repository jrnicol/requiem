using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

public static class FullSheetEndpoints
{
    public static RouteGroupBuilder MapFullSheetEndpoints(this RouteGroupBuilder api)
    {
        // Open to any member; CampaignAccess limits each request to the campaigns they tell.
        var characters = api.MapGroup("/characters").WithTags("Characters");

        characters.MapGet("/{id:guid}/full-sheet", GetAsync);
        characters.MapPost("/full-sheet", CreateAsync);
        characters.MapPut("/{id:guid}/full-sheet", UpdateAsync);
        characters.MapPost("/{id:guid}/promote", PromoteAsync);

        return api;
    }

    private static async Task<Results<Ok<FullSheetForm>, NotFound>> GetAsync(Guid id, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, CharacterTier.FullSheet, ct);
        return character is null ? TypedResults.NotFound() : TypedResults.Ok(FullSheetMapper.ToForm(character));
    }

    private static async Task<Results<Created<FullSheetForm>, ValidationProblem>> CreateAsync(
        FullSheetForm form, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access, TimeProvider time, CancellationToken ct)
    {
        if (await ValidateAsync(form, null, catalog, access, ct) is { } errors) return TypedResults.ValidationProblem(errors);
        if (await access.CampaignProblemAsync(form.CampaignId, ct) is { } problem)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(form.CampaignId)] = [problem] });

        var character = new Character { Name = form.Name, OwnerUserId = access.UserId, CampaignId = form.CampaignId };
        FullSheetMapper.Apply(form, character, time.GetUtcNow());
        await characters.AddAsync(character, ct);

        return TypedResults.Created($"/api/characters/{character.Id}/full-sheet", FullSheetMapper.ToForm(character));
    }

    private static async Task<Results<Ok<FullSheetForm>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>> UpdateAsync(
        Guid id, FullSheetForm form, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access, TimeProvider time, CancellationToken ct)
    {
        if (await ValidateAsync(form, id, catalog, access, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        var character = await LoadAsync(access, id, CharacterTier.FullSheet, ct);
        if (character is null) return TypedResults.NotFound();

        FullSheetMapper.Apply(form, character, time.GetUtcNow());
        if (!await characters.TrySaveAsync(character, form.Version, ct))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This character was changed somewhere else after you opened it. Reload to see the latest version."));

        return TypedResults.Ok(FullSheetMapper.ToForm(character));
    }

    private static async Task<Results<Ok<FullSheetForm>, NotFound, Conflict<ProblemDetailsBody>>> PromoteAsync(
        Guid id, PromoteRequest request, ICharacterStore characters, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadAsync(access, id, CharacterTier.SupportingCast, ct);
        if (character is null) return TypedResults.NotFound();

        FullSheetMapper.Promote(character);
        if (!await characters.TrySaveAsync(character, request.Version, ct))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This NPC was changed somewhere else. Reload it before promoting."));

        return TypedResults.Ok(FullSheetMapper.ToForm(character));
    }

    private static async Task<Character?> LoadAsync(CampaignAccess access, Guid id, CharacterTier tier, CancellationToken ct) =>
        await access.ManagedCharacterAsync(id, ct) is { } character && character.Tier == tier ? character : null;

    /// <summary>
    /// Learned powers are rituals, ritae, or picks in a pick-per-dot Discipline (Investiture) the
    /// character has, no more picks than its dots.
    /// </summary>
    private static List<ValidationResult> ValidateLearnedPowers(FullSheetForm form, CatalogSnapshot catalog)
    {
        var errors = new List<ValidationResult>();
        var powerIds = form.LearnedPowerIds.Distinct().ToList();
        if (powerIds.Count == 0) return errors;

        var powers = powerIds.Select(catalog.Power).OfType<Power>().ToList();
        var levels = catalog.Powers.GroupBy(p => p.PowerGroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int?>)g.Select(p => p.Level).ToList());

        var unknown = powerIds.Count != powers.Count;
        foreach (var group in powers.GroupBy(p => (p.PowerGroupId, GroupName: p.PowerGroup.Name, p.PowerGroup.Kind)))
        {
            if (PowerRules.AcquisitionFor(group.Key.Kind) == PowerAcquisition.LearnedIndividually) continue;
            if (!PowerRules.IsPickPerDot(group.Key.Kind, levels[group.Key.PowerGroupId]))
            {
                unknown = true;
                continue;
            }

            var dots = form.Disciplines.Where(d => d.PowerGroupId == group.Key.PowerGroupId).Sum(d => d.Dots);
            if (group.Count() > dots)
                errors.Add(new ValidationResult(
                    $"{group.Key.GroupName} has {dots} dots but {group.Count()} powers chosen.", [nameof(form.LearnedPowerIds)]));
        }

        if (unknown)
            errors.Add(new ValidationResult(
                "Only Blood Sorcery rituals, Sabbat Ritae and Investiture picks are learned one at a time.", [nameof(form.LearnedPowerIds)]));
        return errors;
    }

    /// <returns>Errors keyed by form property name, or null when valid.</returns>
    /// <param name="checkLinks">
    /// Check the sire and domitor against the campaign's characters. Off for players, who can't set them
    /// (the Storyteller links them).
    /// </param>
    internal static async Task<Dictionary<string, string[]>?> ValidateAsync(
        FullSheetForm form, Guid? id, ICatalogStore store, CampaignAccess access, CancellationToken ct, bool checkLinks = true)
    {
        // Tidy the form for its kind first (e.g. a ghoul keeps at most 8 Vitae), as saving would.
        form.NormalizeForKind();
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        var catalog = await store.GetAsync(ct);

        if (ProfileMapper.TitlesProblem(form.Titles, catalog) is { } titles) results.Add(titles);
        else form.Titles = ProfileMapper.CanonicalTitles(form.Titles, catalog);

        if (form.IsVampire)
        {
            if (form.BloodlineId is { } bloodlineId && catalog.Bloodline(bloodlineId) is null)
                results.Add(new ValidationResult("Unknown Bloodline.", [nameof(form.BloodlineId)]));
            if (form.PredatorTypeId is { } predatorId && catalog.PredatorTypes.All(p => p.Id != predatorId))
                results.Add(new ValidationResult("Unknown Predator type.", [nameof(form.PredatorTypeId)]));

            var ratable = form.Disciplines.Select(d => d.PowerGroupId).Distinct()
                .All(groupId => catalog.PowerGroup(groupId)?.Kind is PowerGroupKind.CaitiffDiscipline or PowerGroupKind.BloodlineDiscipline);
            if (!ratable)
                results.Add(new ValidationResult("Only Caitiff and Bloodline Disciplines take dots.", [nameof(form.Disciplines)]));

            results.AddRange(ValidateLearnedPowers(form, catalog));
        }

        var meritIds = form.Merits.Where(m => m.MeritDefinitionId is not null).Select(m => m.MeritDefinitionId!.Value).Distinct();
        if (meritIds.Any(meritId => catalog.Merits.All(m => m.Id != meritId)))
            results.Add(new ValidationResult("Unknown merit or flaw.", [nameof(form.Merits)]));

        if (checkLinks && (form.SireId ?? form.DomitorId) is not null)
        {
            // A sire or domitor must be in the same campaign: the form's for a new character, its own otherwise.
            var campaignId = id is { } existing ? (await access.ManagedCharacterAsync(existing, ct))?.CampaignId : form.CampaignId;
            foreach (var (linked, member, label) in new[] { (form.SireId, nameof(form.SireId), "sire"), (form.DomitorId, nameof(form.DomitorId), "domitor") })
            {
                if (linked is not { } linkedId) continue;
                if (linkedId == id)
                    results.Add(new ValidationResult($"A character can't be their own {label}.", [member]));
                else if (!await access.AllInCampaignAsync(campaignId, [linkedId], ct))
                    results.Add(new ValidationResult($"Unknown {label}.", [member]));
            }
        }

        if (results.Count == 0) return null;
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(e => e.Member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").Distinct().ToArray());
    }
}
