using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

/// <summary>Koldun Thaumaturgy (VtR p.203; house rulings 2026-11-04).</summary>
public class KoldunRulesTests
{
    private const int Tzimisce = 1, Tremere = 2;
    private const int Auspex = 10, Vishap = 11, Hekate = 12;
    private const int Decay = 100, Koldun = 101, Banishing = 102;

    private static readonly CreationCatalog Catalog = new(
        [
            new(Tzimisce, "Tzimisce", "MagiHouse", null, null, null, null, null),
            new(Tremere, "Tremere", "MagiHouse", null, null, null, null, null),
        ],
        [
            new(Auspex, "Auspex", PowerGroupKind.CaitiffDiscipline, null, null, null, null),
            new(Vishap, "Vishap", PowerGroupKind.BloodSorcery, Tzimisce, "Tzimisce", null, null),
            new(Hekate, "Hekate", PowerGroupKind.BloodSorcery, Tremere, "Tremere", null, null),
        ],
        new Dictionary<int, int> { [Decay] = Vishap, [Koldun] = Vishap, [Banishing] = Hekate },
        [],
        new Dictionary<int, int?> { [Decay] = null, [Koldun] = null, [Banishing] = null },
        new Dictionary<int, string> { [Decay] = "Decay", [Koldun] = KoldunRules.RitualName, [Banishing] = "Banishing Sign" });

    private static XpPurchase BuyKoldun(bool choices = true) => new()
    {
        Kind = PurchaseKind.Ritual, PowerId = Koldun,
        KoldunDomain = choices ? KoldunDomain.Regeneration : null,
        KoldunArts = choices ? [KoldunArt.AffectSelf, KoldunArt.AffectOtherLiving, KoldunArt.AffectArea] : null,
        KoldunMethod = choices ? "Old Church Slavonic chants over running water" : null,
    };

    [Fact]
    public void The_first_dot_is_its_ritual_for_ten_xp_with_its_choices()
    {
        var sheet = Magus(Tzimisce);
        var quote = AdvancementRules.Quote(sheet, BuyKoldun(), Catalog, 4);
        Assert.Equal((10, true), (quote.Cost, quote.IsAllowed));

        AdvancementRules.Apply(sheet, BuyKoldun(), quote, Catalog);
        Assert.Contains(Koldun, sheet.LearnedPowerIds);
        Assert.Equal((1, KoldunDomain.Regeneration, 3, 0), (sheet.Koldun!.Dots, sheet.Koldun.Domain, sheet.Koldun.Arts.Count, sheet.Koldun.Antipathia));
    }

    [Fact]
    public void Its_choices_are_made_when_it_is_bought()
    {
        var violations = AdvancementRules.Quote(Magus(Tzimisce), BuyKoldun(choices: false), Catalog, 4).Violations;
        Assert.Equal(3, violations.Count(v => v.Code == "koldun.choices"));
    }

    [Fact]
    public void Only_house_tzimisce_learns_it()
    {
        Assert.Contains(AdvancementRules.Quote(Magus(Tremere), BuyKoldun(), Catalog, 4).Violations, v => v.Code == "ritual.other-house");
    }

    [Fact]
    public void Each_further_dot_costs_ten_up_to_five_and_undoes()
    {
        var sheet = Magus(Tzimisce, xp: 100);
        var thaumaturgy = new XpPurchase { Kind = PurchaseKind.Thaumaturgy };
        Assert.Contains(AdvancementRules.Quote(sheet, thaumaturgy, Catalog, 4).Violations, v => v.Code == "koldun.not-known");

        AdvancementRules.Apply(sheet, BuyKoldun(), AdvancementRules.Quote(sheet, BuyKoldun(), Catalog, 4), Catalog);
        var quote = AdvancementRules.Quote(sheet, thaumaturgy, Catalog, 4);
        Assert.Equal((1, 2, 10, true), (quote.FromDots, quote.ToDots, quote.Cost, quote.IsAllowed));
        AdvancementRules.Apply(sheet, thaumaturgy, quote, Catalog);
        Assert.Equal(2, sheet.Koldun!.Dots);

        // The Ritual can't be undone under a later dot; the dot can.
        Assert.False(AdvancementRules.Revert(sheet, BuyKoldun(), AdvancementRules.Quote(Magus(Tzimisce), BuyKoldun(), Catalog, 4), Catalog));
        Assert.True(AdvancementRules.Revert(sheet, thaumaturgy, quote, Catalog));
        Assert.Equal(1, sheet.Koldun.Dots);

        sheet.Koldun.Dots = KoldunRules.MaxDots;
        Assert.Contains(AdvancementRules.Quote(sheet, thaumaturgy, Catalog, 4).Violations, v => v.Code == "xp.exceeds-max");
    }

    [Fact]
    public void Creation_takes_it_only_through_starting_xp()
    {
        var picked = Magus(Tzimisce);
        picked.LearnedPowerIds = [Decay, Koldun];
        Assert.Contains(CreationRules.Evaluate(picked, Catalog).Issues, i => i.Message.Contains("bought only with XP"));

        // Bought with starting XP instead: the free Ritual is Decay, and Koldun replays on top.
        var draft = Magus(Tzimisce, xp: 0);
        draft.LearnedPowerIds = [Decay];
        Assert.DoesNotContain(CreationRules.Evaluate(draft, Catalog).Issues, i => i.Message.Contains(KoldunRules.RitualName));
        var bought = StartingXpRules.Replay(draft, [], Catalog, 20).Sheet;
        var quote = AdvancementRules.Quote(bought, BuyKoldun(), Catalog, ExperienceRules.ProgressPointsToUnlockXp);
        var replay = StartingXpRules.Replay(draft, [StartingXpPurchase.From(BuyKoldun(), quote)], Catalog, 20);
        Assert.Null(replay.Problem);
        Assert.Equal((10, 1), (replay.Spent, replay.Sheet.Koldun!.Dots));
    }

    [Theory]
    [InlineData(0, 4, false, 4, false)]
    [InlineData(7, 2, false, 9, false)]
    [InlineData(7, 3, false, 0, true)]   // reaches 10: clears, Dark Retribution
    [InlineData(2, 12, false, 0, true)]
    [InlineData(5, 0, true, 0, true)]    // a botch clears it at once
    public void Antipathia_builds_from_successes_and_clears_at_ten(int before, int successes, bool botched, int after, bool retribution) =>
        Assert.Equal((after, retribution), KoldunRules.Gain(before, successes, botched));

    [Theory]
    [InlineData(5, 2)]
    [InlineData(2, 0)]
    public void An_oblation_takes_three_off(int before, int after) => Assert.Equal(after, KoldunRules.Oblation(before));

    // House ruling 2026-11-04: the book's four results spread over the d10.
    [Theory]
    [InlineData(1, "Manic")]
    [InlineData(3, "Manic")]
    [InlineData(4, "Backlash")]
    [InlineData(5, "Backlash")]
    [InlineData(6, "Madness")]
    [InlineData(7, "Madness")]
    [InlineData(8, "Haunted")]
    [InlineData(10, "Haunted")]
    public void Dark_retribution_spreads_four_results_over_the_d10(int roll, string result) =>
        Assert.Equal(result, KoldunRules.Retribution(roll).Name);

    [Fact]
    public void Invoking_rolls_thaumaturgy_plus_extra_vitae_and_eruditions()
    {
        // Thaumaturgy 2 + 3 Vitae + 1 Erudition (2) − target's Blood Potency 1.
        Assert.Equal(6, KoldunRules.InvokeDice(2, 3, 1, 1));
        Assert.Equal(4, KoldunRules.MaxExtraVitae(4));
    }

    private static FullSheetForm Magus(int house, int xp = 30)
    {
        var sheet = new FullSheetForm
        {
            Name = "Ilya", Kind = CharacterKind.Vampire, BloodlineId = house, BloodPotency = 1, Generation = 12, Humanity = 6,
            Disciplines = [new() { PowerGroupId = Auspex, Dots = 1 }],
            Experience = [new() { Amount = xp, Reason = "Sessions" }],
        };
        return sheet;
    }
}
