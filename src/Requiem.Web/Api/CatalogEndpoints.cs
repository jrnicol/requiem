using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
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

        catalog.MapGet("/bloodlines", async (ICatalogStore store, CancellationToken ct) => Bloodlines(await store.GetAsync(ct)));

        catalog.MapGet("/predator-types", async (ICatalogStore store, CancellationToken ct) =>
            (await store.GetAsync(ct)).PredatorTypes
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new PredatorTypeOption(p.Id, p.Name, p.Description, p.Restrictions, p.SourcePage))
                .ToList());

        catalog.MapGet("/edges", async (ICatalogStore store, CancellationToken ct) =>
            (await store.GetAsync(ct)).Edges
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new EdgeOption(e.Id, e.Name, e.Edge, e.Description, e.SourcePage))
                .ToList());

        catalog.MapGet("/disciplines", async (ICatalogStore store, CancellationToken ct) => Disciplines(await store.GetAsync(ct)));

        catalog.MapGet("/concepts", async (ICatalogStore store, CancellationToken ct) =>
            (await store.GetAsync(ct)).Concepts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new ConceptOption(c.Id, c.Name, c.Description, c.WillpowerTrigger, c.SourcePage))
                .ToList());

        // The whole powers catalog (a few hundred KB): the client caches it and searches locally.
        catalog.MapGet("/powers", async (ICatalogStore store, CancellationToken ct) =>
            (await store.GetAsync(ct)).Powers
                .OrderBy(p => p.PowerGroupId).ThenBy(p => p.Level ?? 0).ThenBy(p => p.Id)
                .Select(ToOption)
                .ToList());

        // Players read the catalog to build characters; only site GMs correct it.
        catalog.MapPut("/powers/{id:int}", UpdatePowerAsync).RequireAuthorization(Policies.Admin);

        catalog.MapGet("/merits", async (ICatalogStore store, CancellationToken ct) => Merits(await store.GetAsync(ct)));

        catalog.MapGet("/compels", async (ICatalogStore store, CancellationToken ct) =>
            (await store.GetAsync(ct)).Compels
                .OrderBy(c => c.Kind).ThenBy(c => c.Roll).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CompelOption(c.Id, c.Name, c.Kind, c.Roll, c.Rating, c.Trigger, c.EndsWhen,
                    c.Description, c.SourcePage))
                .ToList());

        return api;
    }

    internal static List<BloodlineOption> Bloodlines(CatalogSnapshot catalog) =>
        catalog.Bloodlines
            .OrderBy(b => KindOrder(b.Kind)).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BloodlineOption(b.Id, b.Name, b.Kind.ToString(), b.WeaknessName, b.WeaknessText,
                b.CompelName, b.CompelText, b.SourcePage))
            .ToList();

    internal static List<DisciplineOption> Disciplines(CatalogSnapshot catalog) =>
        catalog.PowerGroups
            .OrderBy(g => g.Kind).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DisciplineOption(g.Id, g.Name, g.Kind, g.BloodlineId, g.Bloodline?.Name, g.Description, g.SourcePage))
            .ToList();

    internal static List<MeritOption> Merits(CatalogSnapshot catalog) =>
        catalog.Merits
            .OrderBy(m => m.Kind).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Source)
            .Select(m => new MeritOption(m.Id, m.Name, m.Kind, m.Source, m.Cost, m.MinDots, m.MaxDots,
                m.Description, m.Restrictions, m.SourcePage))
            .ToList();

    /// <summary>What <see cref="CreationRules"/> needs from the catalog.</summary>
    internal static async Task<CreationCatalog> CreationCatalogAsync(ICatalogStore store, CancellationToken ct)
    {
        var catalog = await store.GetAsync(ct);
        return new(
            Bloodlines(catalog),
            Disciplines(catalog),
            catalog.Powers.ToDictionary(p => p.Id, p => p.PowerGroupId),
            Merits(catalog),
            catalog.Powers.ToDictionary(p => p.Id, p => p.Level));
    }

    private static async Task<Results<Ok<PowerOption>, NotFound, ValidationProblem>> UpdatePowerAsync(
        int id, PowerEditForm form, ICatalogStore store, CancellationToken ct)
    {
        var power = await store.FindPowerAsync(id, ct);
        if (power is null) return TypedResults.NotFound();
        var catalog = await store.GetAsync(ct);

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        foreach (var option in form.BonusOptions)
            Validator.TryValidateObject(option, new ValidationContext(option), results, validateAllProperties: true);
        var name = form.Name.Trim();
        if (catalog.Powers.Any(p => p.PowerGroupId == power.PowerGroupId && p.Id != id && p.Name == name))
            results.Add(new ValidationResult($"{power.PowerGroup.Name} already has a power called {name}.", [nameof(form.Name)]));
        if (form.DicePoolPowerGroupId is { } groupId && catalog.PowerGroup(groupId) is null)
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

        await store.SavePowerAsync(power, ct);
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
