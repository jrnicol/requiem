namespace Requiem.Shared.Rules;

/// <param name="Label">What the number is, e.g. "Disgusted (Compel)".</param>
/// <param name="Detail">One line on when it applies.</param>
public sealed record BloodlineEffect(string Label, int Value, string Detail);

/// <summary>
/// Bloodline and Magi House weaknesses that scale with Blood Potency
/// (VtR: Brujah p.27, Nosferatu p.32, Gangrel p.43, Tremere p.51).
/// </summary>
public static class BloodlineRules
{
    /// <summary>Brujah gain Compel: Destroyer at this rating on entering Dominion.</summary>
    public const int BrujahDestroyerRating = 6;

    /// <summary>Nosferatu who don't spend 1 Willpower around exceptional beauty gain Compel: Spiteful at this rating.</summary>
    public const int NosferatuSpitefulRating = 3;

    /// <summary>
    /// The scale both Brujah Rage and Tremere Dread Countenance use:
    /// 1, plus 1 per 3 Blood Potency (1 at BP 1, 2 at BP 4, 3 at BP 7, 4 at BP 10).
    /// </summary>
    private static int OnePlusOnePerThree(int bloodPotency)
    {
        BloodRules.EnsureBloodPotency(bloodPotency);
        return 1 + (bloodPotency - 1) / 3;
    }

    /// <summary>Brujah Rage: a Brujah never has fewer Hunger dice than this.</summary>
    public static int BrujahMinimumHungerDice(int bloodPotency) => OnePlusOnePerThree(bloodPotency);

    /// <summary>
    /// Tremere Dread Countenance: unsoakable Bashing damage dealt at the end of each scene to every
    /// living thing able to sense the Tremere. Other Tremere are unaffected.
    /// </summary>
    public static int TremereDreadCountenanceDamage(int bloodPotency) => OnePlusOnePerThree(bloodPotency);

    /// <summary>Gangrel Predation: the permanent Compel's rating, half Blood Potency rounded up.</summary>
    public static int GangrelPredationRating(int bloodPotency)
    {
        BloodRules.EnsureBloodPotency(bloodPotency);
        return (bloodPotency + 1) / 2;
    }

    /// <summary>Nosferatu Hideous Visage: humans who see the Nosferatu gain Compel: Disgusted at Blood Potency.</summary>
    public static int NosferatuDisgustedRating(int bloodPotency)
    {
        BloodRules.EnsureBloodPotency(bloodPotency);
        return bloodPotency;
    }

    /// <summary>
    /// The Blood Potency-scaled numbers that apply to a Bloodline, keyed by the catalog name
    /// (case-insensitive). Empty for Bloodlines without one.
    /// </summary>
    public static IReadOnlyList<BloodlineEffect> EffectsFor(string? bloodlineName, int bloodPotency)
    {
        BloodRules.EnsureBloodPotency(bloodPotency);
        return bloodlineName?.Trim().ToUpperInvariant() switch
        {
            "BRUJAH" =>
            [
                new("Minimum Hunger dice", BrujahMinimumHungerDice(bloodPotency), "Rage: never fewer Hunger dice than this."),
                new("Destroyer (Compel)", BrujahDestroyerRating, "Gained on entering Dominion."),
            ],
            "NOSFERATU" =>
            [
                new("Disgusted (Compel)", NosferatuDisgustedRating(bloodPotency), "Imposed on any human who sees them."),
                new("Spiteful (Compel)", NosferatuSpitefulRating, "Around exceptional beauty, unless they spend 1 Willpower."),
            ],
            "GANGREL" =>
            [
                new("Predation (Compel)", GangrelPredationRating(bloodPotency), "Permanent; 1 Willpower ignores it for a scene."),
            ],
            "TREMERE" =>
            [
                new("Dread Countenance", TremereDreadCountenanceDamage(bloodPotency),
                    "Unsoakable Bashing to every living thing nearby at the end of each scene."),
            ],
            _ => [],
        };
    }

    /// <summary>The catalog kind of a Magi House, as opposed to "Bloodline" or "Caitiff".</summary>
    public const string MagiHouseKind = "MagiHouse";

    public static bool IsMagiHouse(string? kind) => kind == MagiHouseKind;

    /// <summary>
    /// What to call the field: Magi have a House, not a Bloodline, since they aren't descended from one (VtR p.45).
    /// </summary>
    public static string KindLabel(string? kind) => IsMagiHouse(kind) ? "House" : "Bloodline";

    /// <summary>A Magi House's weakness is a Lich Weakness (VtR p.45 onwards).</summary>
    public static string WeaknessLabel(string? kind) => IsMagiHouse(kind) ? "Lich Weakness" : "Weakness";

    /// <summary>"House Tremere", or the Bloodline's name as it is.</summary>
    public static string Named(string name, string? kind) => IsMagiHouse(kind) ? $"House {name}" : name;
}
