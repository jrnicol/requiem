using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

/// <summary>Spending XP on a character in play (WoD Core p.75, VtR p.137).</summary>
public class AdvancementRulesTests
{
    private const int Ventrue = 1, Tremere = 2;
    private const int Auspex = 10, Investiture = 11, Dominate = 12, Majesty = 13, Hekate = 14, Auctoritas = 15;
    private const int Ritual1 = 100, Ritual2 = 101, Rite = 102, Pick1 = 200, Pick2 = 201;
    private const int Allies = 50, Library = 51, Nightmares = 52;

    private static readonly CreationCatalog Catalog = new(
        [
            new(Ventrue, "Ventrue", "Bloodline", null, null, null, null, null),
            new(Tremere, "Tremere", "MagiHouse", null, null, null, null, null),
        ],
        [
            new(Auspex, "Auspex", PowerGroupKind.CaitiffDiscipline, null, null, null, null),
            new(Investiture, "Investiture", PowerGroupKind.CaitiffDiscipline, null, null, null, null),
            new(Dominate, "Dominate", PowerGroupKind.BloodlineDiscipline, Ventrue, "Ventrue", null, null),
            new(Majesty, "Majesty", PowerGroupKind.BloodlineDiscipline, 99, "Toreador", null, null),
            new(Hekate, "Hekate", PowerGroupKind.BloodSorcery, Tremere, "Tremere", null, null),
            new(Auctoritas, "Auctoritas", PowerGroupKind.SabbatRitae, null, null, null, null),
        ],
        new Dictionary<int, int> { [Ritual1] = Hekate, [Ritual2] = Hekate, [Rite] = Auctoritas, [Pick1] = Investiture, [Pick2] = Investiture },
        [
            new(Allies, "Allies", MeritKind.Merit, RuleSource.WodCore, "1–3", 1, 3, null, null, 96),
            new(Library, "Library", MeritKind.Merit, RuleSource.WodCore, "Awarded through plot only", null, null, null, null, 98),
            new(Nightmares, "Nightmares", MeritKind.Flaw, RuleSource.WodCore, "1–3", 1, 3, null, null, 104),
        ],
        new Dictionary<int, int?> { [Ritual1] = null, [Ritual2] = null, [Rite] = null, [Pick1] = 1, [Pick2] = 1 });

    // ---------- Locks and XP ----------

    [Fact]
    public void Spending_waits_for_four_corruption_or_links()
    {
        var quote = Quote(Vampire(), new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl }, progress: 3);
        Assert.Contains(quote.Violations, v => v.Code == "xp.locked");
        Assert.True(Quote(Vampire(), new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl }).IsAllowed);
    }

    [Fact]
    public void Spending_needs_the_xp()
    {
        var sheet = Vampire(xp: 2);
        var quote = Quote(sheet, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl });
        Assert.Equal(6, quote.Cost); // Brawl 1 → 2
        Assert.Contains(quote.Violations, v => v.Code == "xp.not-enough");
    }

    // ---------- Attributes, Skills, Specialties ----------

    [Theory]
    [InlineData(CharacterAttribute.Strength, 2, 15)] // new dot 3 × 5
    [InlineData(CharacterAttribute.Dexterity, 4, 25)]
    public void Attributes_cost_new_dots_times_five(CharacterAttribute attribute, int from, int cost)
    {
        var sheet = Vampire();
        sheet.Attributes[attribute] = from;
        var quote = Quote(sheet, new XpPurchase { Kind = PurchaseKind.Attribute, Attribute = attribute });
        Assert.Equal((from, from + 1, cost, true), (quote.FromDots, quote.ToDots, quote.Cost, quote.IsAllowed));
    }

    [Fact]
    public void An_attribute_at_one_needs_a_plot_event()
    {
        var sheet = Vampire();
        sheet.Attributes[CharacterAttribute.Charisma] = 1;
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Attribute, Attribute = CharacterAttribute.Charisma }).Violations,
            v => v.Code == "xp.attribute-at-one");
    }

    [Fact]
    public void Skills_cost_new_dots_times_three_up_to_five()
    {
        var sheet = Vampire();
        Assert.Equal(3, Quote(sheet, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Academics }).Cost); // 0 → 1
        sheet.Skills[Skill.Academics] = 5;
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Academics }).Violations, v => v.Code == "xp.exceeds-max");
    }

    [Fact]
    public void A_specialty_costs_five_and_each_skill_takes_one()
    {
        var sheet = Vampire();
        var quote = Quote(sheet, new XpPurchase { Kind = PurchaseKind.Specialty, Skill = Skill.Streetwise, SpecialtyName = "Gangs" });
        Assert.Equal((5, true), (quote.Cost, quote.IsAllowed));

        sheet.Specialties.Add(new SpecialtyEntry { Skill = Skill.Streetwise, Name = "Rumors" });
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Specialty, Skill = Skill.Streetwise, SpecialtyName = "Gangs" }).Violations,
            v => v.Code == "specialty.one-per-skill");
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Specialty, Skill = Skill.Academics, SpecialtyName = "History" }).Violations,
            v => v.Code == "specialty.no-skill");
    }

    // ---------- Merits ----------

    [Fact]
    public void A_new_merit_costs_each_new_dot_times_five()
    {
        var sheet = Vampire();
        var quote = Quote(sheet, new XpPurchase { Kind = PurchaseKind.Merit, MeritDefinitionId = Allies, MeritQualifier = "Harbor Police", MeritDots = 2 });
        Assert.Equal(("Allies (Harbor Police)", 0, 2, 15), (quote.Trait, quote.FromDots, quote.ToDots, quote.Cost));

        AdvancementRules.Apply(sheet, new XpPurchase { Kind = PurchaseKind.Merit, MeritDefinitionId = Allies, MeritQualifier = "Harbor Police", MeritDots = 2 }, quote, Catalog);
        var raise = Quote(sheet, new XpPurchase { Kind = PurchaseKind.Merit, MeritDefinitionId = Allies, MeritQualifier = "Harbor Police" });
        Assert.Equal((2, 3, 15, true), (raise.FromDots, raise.ToDots, raise.Cost, raise.IsAllowed));

        sheet.Merits.Single(m => m.MeritDefinitionId == Allies).Dots = 3;
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Merit, MeritDefinitionId = Allies, MeritQualifier = "Harbor Police" }).Violations,
            v => v.Code == "merit.max");
    }

    [Fact]
    public void Plot_merits_and_flaws_are_not_for_sale()
    {
        Assert.Contains(Quote(Vampire(), new XpPurchase { Kind = PurchaseKind.Merit, MeritDefinitionId = Library }).Violations, v => v.Code == "merit.plot-only");
        Assert.Contains(Quote(Vampire(), new XpPurchase { Kind = PurchaseKind.Merit, MeritDefinitionId = Nightmares }).Violations, v => v.Code == "merit.flaw");
    }

    // ---------- Vampires ----------

    [Fact]
    public void Blood_potency_costs_five_per_dot_and_humanity_gives_way()
    {
        var sheet = Vampire();
        sheet.Humanity = 9; // Generation 12 is strength 10
        var purchase = new XpPurchase { Kind = PurchaseKind.BloodPotency };
        var quote = Quote(sheet, purchase);
        Assert.Equal((1, 2, 10), (quote.FromDots, quote.ToDots, quote.Cost));

        AdvancementRules.Apply(sheet, purchase, quote, Catalog);
        Assert.Equal((2, 8), (sheet.BloodPotency, sheet.Humanity));
    }

    [Fact]
    public void Own_bloodline_disciplines_cost_less_than_caitiff_ones_and_stay_within_blood_potency()
    {
        var sheet = Vampire(bloodPotency: 2);
        Assert.Equal(10, Quote(sheet, new XpPurchase { Kind = PurchaseKind.BloodlineDiscipline, PowerGroupId = Dominate }).Cost); // 2 × 5
        Assert.Equal(12, Quote(sheet, new XpPurchase { Kind = PurchaseKind.CaitiffDiscipline, PowerGroupId = Auspex }).Cost); // 2 × 6

        sheet.BloodPotency = 1;
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.BloodlineDiscipline, PowerGroupId = Dominate }).Violations,
            v => v.Code == "discipline.dots-exceed-bp");
    }

    [Fact]
    public void New_disciplines_come_only_through_diablerie()
    {
        Assert.Contains(Quote(Vampire(bloodPotency: 2), new XpPurchase { Kind = PurchaseKind.BloodlineDiscipline, PowerGroupId = Majesty }).Violations,
            v => v.Code == "discipline.not-owned");
    }

    [Fact]
    public void Each_investiture_dot_buys_a_chosen_power()
    {
        var sheet = Vampire(bloodPotency: 2);
        sheet.Disciplines = [new() { PowerGroupId = Investiture, Dots = 1 }, new() { PowerGroupId = Dominate, Dots = 1 }];
        sheet.LearnedPowerIds = [Pick1];

        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.CaitiffDiscipline, PowerGroupId = Investiture }).Violations, v => v.Code == "discipline.pick");
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.CaitiffDiscipline, PowerGroupId = Investiture, PowerId = Pick1 }).Violations, v => v.Code == "discipline.pick-owned");

        var purchase = new XpPurchase { Kind = PurchaseKind.CaitiffDiscipline, PowerGroupId = Investiture, PowerId = Pick2 };
        var quote = Quote(sheet, purchase);
        Assert.True(quote.IsAllowed, string.Join("; ", quote.Violations.Select(v => v.Message)));
        AdvancementRules.Apply(sheet, purchase, quote, Catalog);
        Assert.Equal([Pick1, Pick2], sheet.LearnedPowerIds);
    }

    [Fact]
    public void Rituals_cost_ten_from_your_own_house_only()
    {
        var sheet = Vampire();
        sheet.BloodlineId = Tremere;
        sheet.Disciplines = [new() { PowerGroupId = Auspex, Dots = 1 }];
        sheet.LearnedPowerIds = [Ritual1];

        var quote = Quote(sheet, new XpPurchase { Kind = PurchaseKind.Ritual, PowerId = Ritual2 });
        Assert.Equal((10, true), (quote.Cost, quote.IsAllowed));
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Ritual, PowerId = Rite }).Violations, v => v.Code == "ritual.ritae");

        sheet.BloodlineId = Ventrue;
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.Ritual, PowerId = Ritual2 }).Violations, v => v.Code == "ritual.other-house");
    }

    [Fact]
    public void Mortals_buy_no_vampire_traits()
    {
        var sheet = Vampire();
        sheet.Kind = CharacterKind.Mortal;
        Assert.Contains(Quote(sheet, new XpPurchase { Kind = PurchaseKind.BloodPotency }).Violations, v => v.Code == "xp.not-for-sale");
    }

    // ---------- Scripts ----------

    [Fact]
    public void Scripts_are_complete_when_all_four_are_marked()
    {
        var scripts = FullSheetForm.DefaultScripts();
        Assert.False(AdvancementRules.ScriptsComplete(scripts));
        foreach (var script in scripts) script.IsMarked = true;
        Assert.True(AdvancementRules.ScriptsComplete(scripts));
    }

    // ---------- Helpers ----------

    private static XpPurchaseQuote Quote(FullSheetForm sheet, XpPurchase purchase, int progress = 4) =>
        AdvancementRules.Quote(sheet, purchase, Catalog, progress);

    private static FullSheetForm Vampire(int xp = 100, int bloodPotency = 1)
    {
        var sheet = new FullSheetForm
        {
            Name = "Vera", Kind = CharacterKind.Vampire, BloodlineId = Ventrue, BloodPotency = bloodPotency, Generation = 12, Humanity = 6,
            Disciplines = [new() { PowerGroupId = Auspex, Dots = 1 }, new() { PowerGroupId = Dominate, Dots = 1 }],
            Experience = [new() { Amount = xp, Reason = "Sessions" }],
        };
        foreach (var attribute in Enum.GetValues<CharacterAttribute>()) sheet.Attributes[attribute] = 2;
        sheet.Skills[Skill.Brawl] = 1;
        sheet.Skills[Skill.Streetwise] = 2;
        return sheet;
    }
}
