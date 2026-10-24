namespace Requiem.Shared.Rules;

/// <summary>Blood Potency, Vitae, Bloodlust and Hunger dice (VtR p.5, p.15, p.16, p.19).</summary>
public static class BloodRules
{
    public const int MinBloodPotency = 1;
    public const int MaxBloodPotency = 10;
    public const int ThinbloodPoolPenalty = 3;

    private static readonly int[] Efficiency = [1, 1, 1, 2, 2, 3, 5, 7, 10, 15];

    public static int MaxVitae(int bloodPotency, bool isThinblood = false)
    {
        EnsureBloodPotency(bloodPotency);
        var pool = bloodPotency == MaxBloodPotency ? 50 : 10 + 2 * (bloodPotency - 1);
        return isThinblood ? Math.Max(0, pool - ThinbloodPoolPenalty) : pool;
    }

    /// <summary>Most Vitae that can be spent in a single action round.</summary>
    public static int EfficiencyFor(int bloodPotency)
    {
        EnsureBloodPotency(bloodPotency);
        return Efficiency[bloodPotency - 1];
    }

    /// <summary>Remaining Vitae at which Bloodlust level 1 begins: 5, or Blood Potency above 5.</summary>
    public static int BloodlustThreshold(int bloodPotency)
    {
        EnsureBloodPotency(bloodPotency);
        return Math.Max(5, bloodPotency);
    }

    /// <summary>
    /// Bloodlust level from current Vitae. Equals the number of Hunger dice.
    /// At 0 Vitae the vampire falls to Dominion; the level stays at its maximum.
    /// </summary>
    public static int Bloodlust(int currentVitae, int bloodPotency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentVitae);
        var threshold = BloodlustThreshold(bloodPotency);
        if (currentVitae > threshold) return 0;
        return Math.Min(threshold, threshold - currentVitae + 1);
    }

    /// <summary>
    /// Hunger dice in a pool: the Bloodlust level, but never below <paramref name="minimum"/>
    /// (e.g. <see cref="BloodlineRules.BrujahMinimumHungerDice"/> for Brujah).
    /// </summary>
    public static int HungerDice(int currentVitae, int bloodPotency, int minimum = 0) =>
        Math.Max(Bloodlust(currentVitae, bloodPotency), Math.Max(0, minimum));

    /// <summary>Vitae spent on rising each night: 1, plus 1 per Blood Potency dot over 5.</summary>
    public static int NightlyRiseCost(int bloodPotency)
    {
        EnsureBloodPotency(bloodPotency);
        return 1 + Math.Max(0, bloodPotency - 5);
    }

    /// <summary>Masquerading (Blush of Life): 3 Vitae at Humanity 7, ±1 per dot below/above.</summary>
    public static int MasqueradeCost(int humanity)
    {
        HumanityRules.EnsureHumanity(humanity);
        return Math.Max(0, 3 + (7 - humanity));
    }

    internal static void EnsureBloodPotency(int bloodPotency)
    {
        if (bloodPotency is < MinBloodPotency or > MaxBloodPotency)
            throw new ArgumentOutOfRangeException(nameof(bloodPotency), bloodPotency,
                $"Blood Potency must be {MinBloodPotency}–{MaxBloodPotency}.");
    }
}

public sealed record DicePoolSplit(int RegularDice, int HungerDice)
{
    public int Total => RegularDice + HungerDice;
}

public static class DicePools
{
    /// <summary>
    /// Swaps regular dice for Hunger dice one-for-one, up to the Bloodlust level.
    /// Humanity pools never include Hunger dice (VtR p.5).
    /// </summary>
    public static DicePoolSplit Split(int pool, int bloodlust, bool isHumanityRoll = false)
    {
        pool = Math.Max(0, pool);
        var hunger = isHumanityRoll ? 0 : Math.Min(Math.Max(0, bloodlust), pool);
        return new DicePoolSplit(pool - hunger, hunger);
    }
}
