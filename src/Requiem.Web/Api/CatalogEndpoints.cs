using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Api;

/// <summary>Catalog lists for pickers, plus the GM's review edits to imported powers.</summary>
public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var catalog = api.MapGroup("/catalog").WithTags("Catalog");

        catalog.MapGet("/bloodlines", BloodlinesAsync);

        catalog.MapGet("/predator-types", async (RequiemDbContext db, CancellationToken ct) =>
        {
            var rows = await db.PredatorTypes.AsNoTracking().ToListAsync(ct);
            return rows
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new PredatorTypeOption(p.Id, p.Name, p.Description, p.Restrictions, p.SourcePage))
                .ToList();
        });

        catalog.MapGet("/edges", async (RequiemDbContext db, CancellationToken ct) =>
        {
            var rows = await db.EdgeDefinitions.AsNoTracking().ToListAsync(ct);
            return rows
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new EdgeOption(e.Id, e.Name, e.Edge, e.Description, e.SourcePage))
                .ToList();
        });

        catalog.MapGet("/disciplines", DisciplinesAsync);

        catalog.MapGet("/concepts", async (RequiemDbContext db, CancellationToken ct) =>
        {
            var rows = await db.ConceptDefinitions.AsNoTracking().ToListAsync(ct);
            return rows
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new ConceptOption(c.Id, c.Name, c.Description, c.WillpowerTrigger, c.SourcePage))
                .ToList();
        });

        // The whole powers catalog (a few hundred KB): the client caches it and searches locally.
        catalog.MapGet("/powers", async (RequiemDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Powers.AsNoTracking().Include(p => p.BonusOptions).AsSplitQuery().ToListAsync(ct);
            return rows
                .OrderBy(p => p.PowerGroupId).ThenBy(p => p.Level ?? 0).ThenBy(p => p.Id)
                .Select(ToOption)
                .ToList();
        });

        // Players read the catalog to build characters; only site GMs correct it.
        catalog.MapPut("/powers/{id:int}", UpdatePowerAsync).RequireAuthorization(Policies.Admin);

        catalog.MapGet("/merits", MeritsAsync);

        catalog.MapGet("/compels", async (RequiemDbContext db, CancellationToken ct) =>
        {
            var rows = await db.CompelDefinitions.AsNoTracking().ToListAsync(ct);
            return rows
                .OrderBy(c => c.Kind).ThenBy(c => c.Roll).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CompelOption(c.Id, c.Name, c.Kind, c.Roll, c.Rating, c.Trigger, c.EndsWhen,
                    c.Description, c.SourcePage))
                .ToList();
        });

        return api;
    }

    internal static async Task<List<BloodlineOption>> BloodlinesAsync(RequiemDbContext db, CancellationToken ct)
    {
        var rows = await db.Bloodlines.AsNoTracking().ToListAsync(ct);
        return rows
            .OrderBy(b => KindOrder(b.Kind)).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BloodlineOption(b.Id, b.Name, b.Kind.ToString(), b.WeaknessName, b.WeaknessText,
                b.CompelName, b.CompelText, b.SourcePage))
            .ToList();
    }

    internal static async Task<List<DisciplineOption>> DisciplinesAsync(RequiemDbContext db, CancellationToken ct)
    {
        var rows = await db.PowerGroups.AsNoTracking().Include(g => g.Bloodline).ToListAsync(ct);
        return rows
            .OrderBy(g => g.Kind).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DisciplineOption(g.Id, g.Name, g.Kind, g.BloodlineId, g.Bloodline?.Name, g.Description, g.SourcePage))
            .ToList();
    }

    internal static async Task<List<MeritOption>> MeritsAsync(RequiemDbContext db, CancellationToken ct)
    {
        var rows = await db.MeritDefinitions.AsNoTracking().ToListAsync(ct);
        return rows
            .OrderBy(m => m.Kind).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Source)
            .Select(m => new MeritOption(m.Id, m.Name, m.Kind, m.Source, m.Cost, m.MinDots, m.MaxDots,
                m.Description, m.Restrictions, m.SourcePage))
            .ToList();
    }

    /// <summary>What <see cref="CreationRules"/> needs from the catalog.</summary>
    internal static async Task<CreationCatalog> CreationCatalogAsync(RequiemDbContext db, CancellationToken ct) => new(
        await BloodlinesAsync(db, ct),
        await DisciplinesAsync(db, ct),
        await db.Powers.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.PowerGroupId, ct),
        await MeritsAsync(db, ct));

    private static async Task<Results<Ok<PowerOption>, NotFound, ValidationProblem>> UpdatePowerAsync(
        int id, PowerEditForm form, RequiemDbContext db, CancellationToken ct)
    {
        var power = await db.Powers.Include(p => p.BonusOptions).Include(p => p.PowerGroup).SingleOrDefaultAsync(p => p.Id == id, ct);
        if (power is null) return TypedResults.NotFound();

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        foreach (var option in form.BonusOptions)
            Validator.TryValidateObject(option, new ValidationContext(option), results, validateAllProperties: true);
        var name = form.Name.Trim();
        if (await db.Powers.AnyAsync(p => p.PowerGroupId == power.PowerGroupId && p.Id != id && p.Name == name, ct))
            results.Add(new ValidationResult($"{power.PowerGroup.Name} already has a power called {name}.", [nameof(form.Name)]));
        if (form.DicePoolPowerGroupId is { } groupId && !await db.PowerGroups.AnyAsync(g => g.Id == groupId, ct))
            results.Add(new ValidationResult("Unknown Discipline.", [nameof(form.DicePoolPowerGroupId)]));
        if (results.Count > 0)
            return TypedResults.ValidationProblem(results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
                .GroupBy(e => e.Member)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").Distinct().ToArray()));

        power.Level = form.Level;
        power.Name = name;
        power.Category = Clean(form.Category);
        power.Flavor = Clean(form.Flavor);
        power.VitaeCost = form.VitaeCost;
        power.WillpowerCost = form.WillpowerCost;
        power.CostText = Clean(form.CostText);
        power.DicePoolText = Clean(form.DicePoolText);
        power.DicePoolAttribute = form.DicePoolAttribute;
        power.DicePoolSkill = form.DicePoolSkill;
        power.DicePoolPowerGroupId = form.DicePoolPowerGroupId;
        power.OpposedBy = Clean(form.OpposedBy);
        power.ActionText = Clean(form.ActionText);
        power.Duration = Clean(form.Duration);
        power.CastingDuration = Clean(form.CastingDuration);
        power.HowToLearn = Clean(form.HowToLearn);
        power.DramaticFailureText = Clean(form.DramaticFailureText);
        power.FailureText = Clean(form.FailureText);
        power.SuccessText = Clean(form.SuccessText);
        power.ExceptionalSuccessText = Clean(form.ExceptionalSuccessText);
        power.Notes = Clean(form.Notes);
        power.IsReviewed = form.IsReviewed;

        var wanted = form.BonusOptions.Select(b => (b.SuccessCost, Effect: b.Effect.Trim())).ToList();
        if (!power.BonusOptions.OrderBy(b => b.SortOrder).Select(b => (b.SuccessCost, b.Effect)).SequenceEqual(wanted))
        {
            power.BonusOptions.Clear();
            power.BonusOptions.AddRange(wanted.Select((b, i) => new PowerBonusOption { SuccessCost = b.SuccessCost, Effect = b.Effect, SortOrder = i }));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToOption(power));
    }

    private static PowerOption ToOption(Power p) => new(
        p.Id, p.PowerGroupId, p.Level, p.Name, p.Category, p.Flavor, p.VitaeCost, p.WillpowerCost, p.CostText,
        p.DicePoolText, p.DicePoolAttribute, p.DicePoolSkill, p.DicePoolPowerGroupId, p.OpposedBy, p.ActionText,
        p.Duration, p.CastingDuration, p.HowToLearn, p.DramaticFailureText, p.FailureText, p.SuccessText,
        p.ExceptionalSuccessText, p.Notes, p.SourcePage, p.IsReviewed,
        p.BonusOptions.OrderBy(b => b.SortOrder).Select(b => new PowerBonusOptionDto(b.SuccessCost, b.Effect)).ToList());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Bloodlines first, then Magi Houses, then Caitiff, as in the weakness chart.
    private static int KindOrder(BloodlineKind kind) => kind switch
    {
        BloodlineKind.Bloodline => 0,
        BloodlineKind.MagiHouse => 1,
        _ => 2,
    };
}
