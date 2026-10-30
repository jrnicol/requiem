using System.Text.Json;
using Requiem.Shared.Contracts;

namespace Requiem.Shared.Rules;

/// <summary>
/// Something a player buys with the campaign's starting XP while building a character. The purchase is kept with what it
/// cost on the sheet it was bought on, so a later change to the dots underneath shows up instead of silently repricing.
/// </summary>
public sealed class StartingXpPurchase
{
    public XpPurchase Purchase { get; set; } = new();
    public string Trait { get; set; } = "";
    public int FromDots { get; set; }
    public int ToDots { get; set; }
    public int Cost { get; set; }

    public static StartingXpPurchase From(XpPurchase purchase, XpPurchaseQuote quote) => new()
    {
        Purchase = purchase, Trait = quote.Trait, FromDots = quote.FromDots, ToDots = quote.ToDots, Cost = quote.Cost,
    };

    public string Label => XpPurchaseQuote.Label(Purchase.Kind, Trait, FromDots, ToDots);
}

/// <param name="Sheet">The draft with every purchase that still fits applied, in order.</param>
/// <param name="Spent">What those purchases cost.</param>
/// <param name="Valid">The purchases that still fit, up to the first that doesn't.</param>
/// <param name="Broken">The first purchase that no longer fits, if any; it and everything after it are left off.</param>
/// <param name="Problem">Why <paramref name="Broken"/> no longer fits.</param>
public sealed record StartingXpReplay(
    FullSheetForm Sheet, int Spent, IReadOnlyList<StartingXpPurchase> Valid, StartingXpPurchase? Broken, string? Problem)
{
    /// <summary>The purchases left off: the broken one and those bought after it.</summary>
    public int Dropped(IReadOnlyList<StartingXpPurchase> all) => all.Count - Valid.Count;
}

/// <param name="Cost">What the group's XP-bought dots cost.</param>
/// <param name="Saved">What buying the same dots on top of the creation dots would have cost more.</param>
/// <param name="Bought">The dots bought with XP, e.g. "Occult 0 → 1".</param>
/// <param name="HighCreation">Creation dots on high ratings in the same group, e.g. "Brawl 3".</param>
/// <param name="CheapDots">XP-bought dots to a rating of 1 or 2, in a group whose creation dots reach 3 somewhere.</param>
public sealed record SplitGroup(string Name, int Cost, int Saved, IReadOnlyList<string> Bought, IReadOnlyList<string> HighCreation, int CheapDots);

public sealed record SplitAnalysis(IReadOnlyList<SplitGroup> Groups)
{
    public int Saved => Groups.Sum(g => g.Saved);
    public int Cost => Groups.Sum(g => g.Cost);
    public int CheapDots => Groups.Sum(g => g.CheapDots);

    /// <summary>
    /// A pattern rather than one purchase: several cheap dots bought with XP while creation dots went high, saving at least
    /// <see cref="StartingXpRules.SplitFlagMinimum"/> XP and at least what the dots cost. A single new Skill dot never flags.
    /// </summary>
    public bool LooksGamed => CheapDots >= StartingXpRules.SplitFlagCheapDots && Saved >= StartingXpRules.SplitFlagMinimum && Saved >= Cost;
}

/// <summary>
/// Starting XP is spent during creation, at the book's XP prices on top of the creation dots, before the Storyteller
/// approves the character (house ruling 2026-10-28). It needs no Corruption or Links; what's left after approval waits for
/// them like any other XP.
/// </summary>
public static class StartingXpRules
{
    public const string Reason = "Starting XP";

    /// <summary>Applies the purchases to a copy of the draft, in the order they were made, within the campaign's starting XP.</summary>
    public static StartingXpReplay Replay(FullSheetForm sheet, IReadOnlyList<StartingXpPurchase> purchases, CreationCatalog catalog, int budget)
    {
        var copy = Copy(sheet);
        copy.Experience = [new XpLedgerEntry { Amount = budget, Reason = Reason }];
        copy.StartingXpPurchases = [];
        var valid = new List<StartingXpPurchase>();
        var spent = 0;
        foreach (var bought in purchases)
        {
            // Starting XP doesn't wait for the unlock, so price it as if spending were open.
            var quote = AdvancementRules.Quote(copy, bought.Purchase, catalog, ExperienceRules.ProgressPointsToUnlockXp);
            if (!quote.IsAllowed)
                return new(copy, spent, valid, bought, quote.Violations[0].Message);
            if (quote.FromDots != bought.FromDots || quote.ToDots != bought.ToDots || quote.Cost != bought.Cost)
                return new(copy, spent, valid, bought, $"{bought.Trait} has changed since it was bought.");
            AdvancementRules.Apply(copy, bought.Purchase, quote, catalog);
            copy.Experience.Add(new XpLedgerEntry { Amount = -quote.Cost, Reason = bought.Label });
            spent += quote.Cost;
            valid.Add(bought);
        }
        return new(copy, spent, valid, null, null);
    }

    /// <summary>XP saved before the split looks deliberate: at least this many, and at least what the dots actually cost.</summary>
    public const int SplitFlagMinimum = 10;

    /// <summary>Cheap XP-bought dots (to a rating of 1 or 2, beside creation dots at 3 or more) before the split looks like a pattern.</summary>
    public const int SplitFlagCheapDots = 3;

    /// <summary>
    /// How much a draft saves by splitting its Attribute and Skill dots between creation and starting XP. Creation dots cost the
    /// same at any rating, but XP costs more for each higher dot, so putting the creation dots on high ratings and the XP on cheap
    /// low ones elsewhere buys the same sheet for less. Measured per group (Physical Attributes, Social Skills and so on), against
    /// the same creation dots filling every trait from the bottom and the XP buying the dots on top. The Storyteller decides
    /// whether it matters; the report only flags a pattern (<see cref="SplitAnalysis.LooksGamed"/>).
    /// </summary>
    /// <param name="before">The draft, with its creation dots.</param>
    /// <param name="after">The same draft with its starting XP purchases applied.</param>
    public static SplitAnalysis AnalyseSplit(FullSheetForm before, FullSheetForm after)
    {
        var groups = new List<SplitGroup>();
        foreach (var category in new[] { TraitCategory.Physical, TraitCategory.Social, TraitCategory.Mental })
        {
            var attributes = Enum.GetValues<CharacterAttribute>().Where(a => Traits.CategoryOf(a) == category)
                .Select(a => new TraitDots(a.ToString(), before.Attribute(a), after.Attribute(a), Traits.MinAttribute)).ToList();
            groups.Add(Group($"{category} Attributes", attributes, ExperienceRules.Quote(PurchaseKind.Attribute, 2, 3).Cost / 3));
            var skills = Enum.GetValues<Skill>().Where(s => Traits.CategoryOf(s) == category)
                .Select(s => new TraitDots(s.ToString(), before.SkillDots(s), after.SkillDots(s), Traits.MinSkill)).ToList();
            groups.Add(Group($"{category} Skills", skills, ExperienceRules.Quote(PurchaseKind.Skill, 2, 3).Cost / 3));
        }
        return new SplitAnalysis(groups.Where(g => g.Bought.Count > 0).ToList());
    }

    private sealed record TraitDots(string Name, int Creation, int Final, int Floor);

    /// <param name="perDot">XP for a dot is its new rating times this.</param>
    private static SplitGroup Group(string name, List<TraitDots> traits, int perDot)
    {
        var bought = traits.Where(t => t.Final > t.Creation).ToList();
        int Cost(IEnumerable<(int From, int To)> ranges) => ranges.Sum(r => Enumerable.Range(r.From + 1, r.To - r.From).Sum(level => level * perDot));
        var actual = Cost(bought.Select(t => (t.Creation, t.Final)));

        // The same creation dots, each placed on the lowest rating still open in the group; the XP then buys the rest on top.
        var filled = traits.ToDictionary(t => t, t => t.Floor);
        var creationDots = traits.Sum(t => Math.Max(0, Math.Min(t.Creation, t.Final) - t.Floor));
        for (var i = 0; i < creationDots; i++)
        {
            var next = traits.Where(t => filled[t] < t.Final).MinBy(t => filled[t]);
            if (next is null) break;
            filled[next]++;
        }
        var onTop = Cost(traits.Select(t => (filled[t], t.Final)));

        // What a Storyteller would look at: the XP-bought dots, and the creation dots sitting on ratings above them.
        var lowBought = bought.Select(t => $"{t.Name} {t.Creation} → {t.Final}").ToList();
        var highCreation = traits.Where(t => bought.Count > 0 && t.Creation > bought.Min(b => b.Creation) + 1 && t.Creation >= 3)
            .OrderByDescending(t => t.Creation).Select(t => $"{t.Name} {t.Creation}").ToList();
        var cheap = traits.Any(t => t.Creation >= 3)
            ? bought.Sum(t => Enumerable.Range(t.Creation + 1, t.Final - t.Creation).Count(level => level <= 2))
            : 0;
        return new SplitGroup(name, actual, Math.Max(0, onTop - actual), lowBought, highCreation, cheap);
    }

    /// <summary>A deep copy, so replaying never touches the draft itself.</summary>
    public static FullSheetForm Copy(FullSheetForm sheet) =>
        JsonSerializer.Deserialize<FullSheetForm>(JsonSerializer.Serialize(sheet, RequiemJson.Options), RequiemJson.Options)!;
}
