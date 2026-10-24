using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class PowerAndExperienceTests
{
    [Theory]
    [InlineData(PowerGroupKind.CaitiffDiscipline, PowerAcquisition.Cumulative)]
    [InlineData(PowerGroupKind.BloodlineDiscipline, PowerAcquisition.Cumulative)]
    [InlineData(PowerGroupKind.BloodSorcery, PowerAcquisition.LearnedIndividually)]
    [InlineData(PowerGroupKind.SabbatRitae, PowerAcquisition.LearnedIndividually)]
    public void Acquisition_by_group_kind(PowerGroupKind kind, PowerAcquisition expected) =>
        Assert.Equal(expected, PowerRules.AcquisitionFor(kind));

    [Fact]
    public void Pick_per_dot_means_a_dotted_discipline_with_every_power_at_one()
    {
        Assert.True(PowerRules.IsPickPerDot(PowerGroupKind.CaitiffDiscipline, [1, 1, 1, 1, 1]));
        Assert.False(PowerRules.IsPickPerDot(PowerGroupKind.CaitiffDiscipline, [1, 2, 3, 4, 5]));
        Assert.False(PowerRules.IsPickPerDot(PowerGroupKind.BloodlineDiscipline, [1]));
        Assert.False(PowerRules.IsPickPerDot(PowerGroupKind.BloodSorcery, [null, null]));
        Assert.False(PowerRules.IsPickPerDot(PowerGroupKind.BasePower, [1, 1]));
    }

    [Fact]
    public void Cumulative_dots_grant_levels_one_through_n()
    {
        Assert.Equal([1, 2, 3], PowerRules.GrantedLevels(3));
        Assert.Empty(PowerRules.GrantedLevels(0));
    }

    [Fact]
    public void Disciplines_within_limits_are_valid() =>
        Assert.Empty(PowerRules.Validate(2, [new("Auspex", 2), new("Furor", 1)]));

    [Fact]
    public void Discipline_over_blood_potency_is_flagged()
    {
        var violations = PowerRules.Validate(1, [new("Auspex", 2)]);
        Assert.Contains(violations, v => v.Code == "discipline.dots-exceed-bp");
    }

    [Fact]
    public void Starting_dots_are_exempt_from_the_blood_potency_limit()
    {
        // A Caitiff starts with 2 dots in their Caitiff Discipline at Blood Potency 1 (house ruling 2026-10-24)…
        Assert.Empty(PowerRules.Validate(1, [new("Furor", 2, StartingDots: 2)]));
        // …but a third dot still needs Blood Potency 3.
        Assert.Contains(PowerRules.Validate(1, [new("Furor", 3, StartingDots: 2)]), v => v.Code == "discipline.dots-exceed-bp");
    }

    [Fact]
    public void Blood_potency_limits_dots_not_how_many_disciplines()
    {
        // A new Bloodline vampire: a Caitiff dot and a Bloodline dot at Blood Potency 1 (house ruling 2026-10-24).
        Assert.Empty(PowerRules.Validate(1, [new("Auspex", 1), new("Dominate", 1)]));
    }

    [Theory]
    [InlineData(PurchaseKind.Attribute, 2, 3, 15)]
    [InlineData(PurchaseKind.Attribute, 2, 4, 35)]
    [InlineData(PurchaseKind.Skill, 0, 2, 9)]
    [InlineData(PurchaseKind.Merit, 1, 3, 25)]
    [InlineData(PurchaseKind.BloodPotency, 1, 2, 10)]
    [InlineData(PurchaseKind.Specialty, 0, 2, 10)]
    [InlineData(PurchaseKind.Ritual, 0, 1, 10)]
    public void Xp_costs_use_new_dots_times_multiplier(PurchaseKind kind, int from, int to, int expected)
    {
        var quote = ExperienceRules.Quote(kind, from, to);
        Assert.True(quote.IsAllowed);
        Assert.Equal(expected, quote.Cost);
    }

    [Fact]
    public void Caitiff_disciplines_cost_six_per_new_dot_bloodline_five()
    {
        Assert.Equal(12, ExperienceRules.Quote(PurchaseKind.CaitiffDiscipline, 1, 2, bloodPotency: 2).Cost);
        Assert.Equal(10, ExperienceRules.Quote(PurchaseKind.BloodlineDiscipline, 1, 2, bloodPotency: 2).Cost);
    }

    [Fact]
    public void Discipline_purchase_beyond_blood_potency_is_not_allowed()
    {
        var quote = ExperienceRules.Quote(PurchaseKind.BloodlineDiscipline, 2, 3, bloodPotency: 2);
        Assert.False(quote.IsAllowed);
        Assert.Contains(quote.Violations, v => v.Code == "discipline.dots-exceed-bp");
    }

    [Fact]
    public void Attribute_at_one_cannot_be_raised() =>
        Assert.Contains(ExperienceRules.Quote(PurchaseKind.Attribute, 1, 2).Violations,
            v => v.Code == "xp.attribute-at-one");

    [Fact]
    public void Ratings_cannot_exceed_maximum() =>
        Assert.Contains(ExperienceRules.Quote(PurchaseKind.Skill, 4, 6).Violations, v => v.Code == "xp.exceeds-max");

    [Fact]
    public void Xp_unlocks_at_four_corruption_and_scripts_pay_by_type()
    {
        Assert.False(ExperienceRules.IsXpUnlocked(3));
        Assert.True(ExperienceRules.IsXpUnlocked(4));
        Assert.Equal(4, ExperienceRules.ScriptsCompletionXp(isVampire: true));
        Assert.Equal(1, ExperienceRules.ScriptsCompletionXp(isVampire: false));
    }
}
