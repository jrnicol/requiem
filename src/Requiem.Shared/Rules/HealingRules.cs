namespace Requiem.Shared.Rules;

public sealed record HealingCost(int Vitae, int Willpower);

/// <summary>Vampire healing with Vitae (VtR p.19).</summary>
public static class HealingRules
{
    /// <summary>
    /// Cost to heal <paramref name="points"/> of one wound's damage.
    /// Bashing: all of it for 1 Vitae. Lethal: 1 Vitae per point. Aggravated: 3 Vitae + 1 Willpower per point.
    /// </summary>
    public static HealingCost Cost(DamageType type, int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        if (points == 0) return new HealingCost(0, 0);
        return type switch
        {
            DamageType.Bashing => new HealingCost(1, 0),
            DamageType.Lethal => new HealingCost(points, 0),
            _ => new HealingCost(points * 3, points),
        };
    }

    /// <summary>
    /// Most points of Lethal or Aggravated damage healable in one round, limited by
    /// Blood Potency Efficiency (Vitae per round). Bashing heals completely in one spend.
    /// </summary>
    public static int MaxPointsPerRound(DamageType type, int bloodPotency)
    {
        var efficiency = BloodRules.EfficiencyFor(bloodPotency);
        return type switch
        {
            DamageType.Bashing => int.MaxValue,
            DamageType.Lethal => efficiency,
            _ => efficiency / 3,
        };
    }
}
