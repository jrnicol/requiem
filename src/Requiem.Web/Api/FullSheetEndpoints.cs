using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Api;

public static class FullSheetEndpoints
{
    public static RouteGroupBuilder MapFullSheetEndpoints(this RouteGroupBuilder api)
    {
        var characters = api.MapGroup("/characters").WithTags("Characters");

        characters.MapGet("/{id:guid}/full-sheet", GetAsync);
        characters.MapPost("/full-sheet", CreateAsync);
        characters.MapPut("/{id:guid}/full-sheet", UpdateAsync);
        characters.MapPost("/{id:guid}/promote", PromoteAsync);

        return api;
    }

    private static async Task<Results<Ok<FullSheetForm>, NotFound>> GetAsync(Guid id, RequiemDbContext db, CancellationToken ct)
    {
        var character = await LoadAsync(db, id, CharacterTier.FullSheet, tracked: false, ct);
        return character is null ? TypedResults.NotFound() : TypedResults.Ok(FullSheetMapper.ToForm(character));
    }

    private static async Task<Results<Created<FullSheetForm>, ValidationProblem>> CreateAsync(
        FullSheetForm form, RequiemDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (await ValidateAsync(form, null, db, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        var character = new Character { Name = form.Name };
        FullSheetMapper.Apply(form, character, time.GetUtcNow());
        db.Characters.Add(character);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/characters/{character.Id}/full-sheet", FullSheetMapper.ToForm(character));
    }

    private static async Task<Results<Ok<FullSheetForm>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>> UpdateAsync(
        Guid id, FullSheetForm form, RequiemDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (await ValidateAsync(form, id, db, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        var character = await LoadAsync(db, id, CharacterTier.FullSheet, tracked: true, ct);
        if (character is null) return TypedResults.NotFound();

        db.Entry(character).Property(c => c.Version).OriginalValue = form.Version;
        FullSheetMapper.Apply(form, character, time.GetUtcNow());
        db.Entry(character).Property(c => c.UpdatedAt).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This character was changed somewhere else after you opened it. Reload to see the latest version."));
        }

        return TypedResults.Ok(FullSheetMapper.ToForm(character));
    }

    private static async Task<Results<Ok<FullSheetForm>, NotFound, Conflict<ProblemDetailsBody>>> PromoteAsync(
        Guid id, PromoteRequest request, RequiemDbContext db, CancellationToken ct)
    {
        var character = await LoadAsync(db, id, CharacterTier.SupportingCast, tracked: true, ct);
        if (character is null) return TypedResults.NotFound();

        db.Entry(character).Property(c => c.Version).OriginalValue = request.Version;
        FullSheetMapper.Promote(character);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This NPC was changed somewhere else. Reload it before promoting."));
        }

        return TypedResults.Ok(FullSheetMapper.ToForm(character));
    }

    private static Task<Character?> LoadAsync(RequiemDbContext db, Guid id, CharacterTier tier, bool tracked, CancellationToken ct)
    {
        var query = db.Characters
            .Include(c => c.Attributes).Include(c => c.Skills).Include(c => c.Specialties)
            .Include(c => c.Disciplines).Include(c => c.LearnedPowers).Include(c => c.Merits).Include(c => c.Wounds)
            .Include(c => c.Stains).Include(c => c.Compels).Include(c => c.Conditions).Include(c => c.Scripts)
            .Include(c => c.Milestones).Include(c => c.CorruptionMarks).Include(c => c.Items)
            .Include(c => c.XpLedger)
            .AsSplitQuery();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(c => c.Id == id && c.Tier == tier, ct);
    }

    /// <summary>
    /// Learned powers are rituals, ritae, or picks in a pick-per-dot Discipline (Investiture) the
    /// character has, no more picks than its dots.
    /// </summary>
    private static async Task<List<ValidationResult>> ValidateLearnedPowersAsync(FullSheetForm form, RequiemDbContext db, CancellationToken ct)
    {
        var errors = new List<ValidationResult>();
        var powerIds = form.LearnedPowerIds.Distinct().ToList();
        if (powerIds.Count == 0) return errors;

        var powers = await db.Powers.AsNoTracking()
            .Where(p => powerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PowerGroupId, GroupName = p.PowerGroup.Name, p.PowerGroup.Kind })
            .ToListAsync(ct);
        var groupIds = powers.Select(p => p.PowerGroupId).Distinct().ToList();
        var levels = (await db.Powers.AsNoTracking().Where(p => groupIds.Contains(p.PowerGroupId))
                .Select(p => new { p.PowerGroupId, p.Level }).ToListAsync(ct))
            .GroupBy(p => p.PowerGroupId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<int?>)g.Select(p => p.Level).ToList());

        var unknown = powerIds.Count != powers.Count;
        foreach (var group in powers.GroupBy(p => (p.PowerGroupId, p.GroupName, p.Kind)))
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
    private static async Task<Dictionary<string, string[]>?> ValidateAsync(
        FullSheetForm form, Guid? id, RequiemDbContext db, CancellationToken ct)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);

        if (form.IsVampire)
        {
            if (form.BloodlineId is { } bloodlineId && !await db.Bloodlines.AnyAsync(b => b.Id == bloodlineId, ct))
                results.Add(new ValidationResult("Unknown Bloodline.", [nameof(form.BloodlineId)]));
            if (form.PredatorTypeId is { } predatorId && !await db.PredatorTypes.AnyAsync(p => p.Id == predatorId, ct))
                results.Add(new ValidationResult("Unknown Predator type.", [nameof(form.PredatorTypeId)]));

            var groupIds = form.Disciplines.Select(d => d.PowerGroupId).Distinct().ToList();
            var ratable = await db.PowerGroups
                .Where(g => groupIds.Contains(g.Id)
                    && (g.Kind == Shared.Rules.PowerGroupKind.CaitiffDiscipline || g.Kind == Shared.Rules.PowerGroupKind.BloodlineDiscipline))
                .CountAsync(ct);
            if (ratable != groupIds.Count)
                results.Add(new ValidationResult("Only Caitiff and Bloodline Disciplines take dots.", [nameof(form.Disciplines)]));

            results.AddRange(await ValidateLearnedPowersAsync(form, db, ct));
        }

        var meritIds = form.Merits.Where(m => m.MeritDefinitionId is not null).Select(m => m.MeritDefinitionId!.Value).Distinct().ToList();
        if (meritIds.Count > 0 && await db.MeritDefinitions.CountAsync(m => meritIds.Contains(m.Id), ct) != meritIds.Count)
            results.Add(new ValidationResult("Unknown merit or flaw.", [nameof(form.Merits)]));

        if (form.SireId is { } sireId)
        {
            if (sireId == id)
                results.Add(new ValidationResult("A character can't be their own sire.", [nameof(form.SireId)]));
            else if (!await db.Characters.AnyAsync(c => c.Id == sireId, ct))
                results.Add(new ValidationResult("Unknown sire.", [nameof(form.SireId)]));
        }

        if (results.Count == 0) return null;
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(e => e.Member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").Distinct().ToArray());
    }
}
