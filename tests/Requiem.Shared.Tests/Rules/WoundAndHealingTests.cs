using Requiem.Shared.Rules;

namespace Requiem.Shared.Tests.Rules;

public class WoundAndHealingTests
{
    [Fact]
    public void No_wounds_means_no_penalty()
    {
        var status = WoundRules.Evaluate([], health: 7);
        Assert.Equal(0, status.DicePenalty);
        Assert.False(status.IsTakenOut);
        Assert.Equal(7, status.Remaining);
    }

    [Fact]
    public void Penalty_is_highest_single_wound_not_sum()
    {
        Wound[] wounds = [new("gunshot", 2, DamageType.Lethal), new("baseball bat", 1, DamageType.Bashing)];
        var status = WoundRules.Evaluate(wounds, health: 7);
        Assert.Equal(2, status.DicePenalty);
        Assert.Equal(3, status.TotalDamage);
    }

    [Fact]
    public void Aggravated_penalty_is_five() =>
        Assert.Equal(5, WoundRules.Evaluate([new("fire", 1, DamageType.Aggravated)], 7).DicePenalty);

    [Fact]
    public void Taken_out_when_total_damage_reaches_health()
    {
        Wound[] wounds = [new("knife", 5, DamageType.Lethal), new("knife", 2, DamageType.Lethal)];
        Assert.True(WoundRules.Evaluate(wounds, health: 7).IsTakenOut);
        Assert.False(WoundRules.Evaluate(wounds[..1], health: 7).IsTakenOut);
    }

    [Fact]
    public void Single_wound_over_double_health_flags_possible_death()
    {
        Assert.True(WoundRules.Evaluate([new("bus", 15, DamageType.Lethal)], 7).HasPotentiallyLethalWound);
        Assert.False(WoundRules.Evaluate([new("bus", 14, DamageType.Lethal)], 7).HasPotentiallyLethalWound);
    }

    [Fact]
    public void Healing_costs_by_type()
    {
        Assert.Equal(new HealingCost(1, 0), HealingRules.Cost(DamageType.Bashing, 6));
        Assert.Equal(new HealingCost(3, 0), HealingRules.Cost(DamageType.Lethal, 3));
        Assert.Equal(new HealingCost(6, 2), HealingRules.Cost(DamageType.Aggravated, 2));
    }

    [Fact]
    public void Healing_per_round_is_capped_by_efficiency()
    {
        Assert.Equal(5, HealingRules.MaxPointsPerRound(DamageType.Lethal, bloodPotency: 7));
        Assert.Equal(1, HealingRules.MaxPointsPerRound(DamageType.Aggravated, bloodPotency: 7));
        Assert.Equal(0, HealingRules.MaxPointsPerRound(DamageType.Aggravated, bloodPotency: 1));
    }
}
