using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class BloodRulesTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(3, 14)] // book example
    [InlineData(9, 26)]
    [InlineData(10, 50)] // BP 10 sets the pool to 50
    public void MaxVitae_follows_blood_pool_chart(int bp, int expected) =>
        Assert.Equal(expected, BloodRules.MaxVitae(bp));

    [Fact]
    public void Thinblood_pool_is_three_lower() =>
        Assert.Equal(7, BloodRules.MaxVitae(1, isThinblood: true));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 3)]
    [InlineData(7, 5)]
    [InlineData(8, 7)]
    [InlineData(9, 10)]
    [InlineData(10, 15)]
    public void Efficiency_matches_chart(int bp, int expected) =>
        Assert.Equal(expected, BloodRules.EfficiencyFor(bp));

    [Theory]
    [InlineData(6, 1, 0)]
    [InlineData(5, 1, 1)]
    [InlineData(4, 1, 2)]
    [InlineData(1, 1, 5)]
    [InlineData(0, 1, 5)]
    [InlineData(7, 6, 0)]
    [InlineData(6, 6, 1)]
    [InlineData(1, 6, 6)]
    [InlineData(10, 10, 1)]
    [InlineData(1, 10, 10)]
    public void Bloodlust_starts_at_threshold_and_rises_as_vitae_drops(int vitae, int bp, int expected) =>
        Assert.Equal(expected, BloodRules.Bloodlust(vitae, bp));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(7, 3)]
    [InlineData(10, 6)]
    public void Nightly_rise_costs_one_plus_bp_over_five(int bp, int expected) =>
        Assert.Equal(expected, BloodRules.NightlyRiseCost(bp));

    [Theory]
    [InlineData(7, 3)]
    [InlineData(8, 2)]
    [InlineData(10, 0)]
    [InlineData(4, 6)] // house ruling 2026-10-23: formula wins over the book's worked example of 8
    public void Masquerade_cost_is_three_at_humanity_seven(int humanity, int expected) =>
        Assert.Equal(expected, BloodRules.MasqueradeCost(humanity));

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Blood_potency_out_of_range_throws(int bp) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BloodRules.MaxVitae(bp));

    [Fact]
    public void Hunger_dice_replace_regular_dice()
    {
        Assert.Equal(new DicePoolSplit(3, 2), DicePools.Split(5, bloodlust: 2));
        Assert.Equal(new DicePoolSplit(0, 3), DicePools.Split(3, bloodlust: 5));
        Assert.Equal(new DicePoolSplit(5, 0), DicePools.Split(5, bloodlust: 2, isHumanityRoll: true));
    }
}
