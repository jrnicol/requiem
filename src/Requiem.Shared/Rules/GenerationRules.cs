namespace Requiem.Shared.Rules;

[Flags]
public enum FeedingSource
{
    None = 0,
    Animals = 1,
    Humans = 2,
    Vampires = 4,
    BloodBags = 8,
}

public sealed record FeedingRequirement(FeedingSource Allowed, string? Note);

/// <summary>Generation chart (VtR p.15).</summary>
public static class GenerationRules
{
    public const int StartingGeneration = 12;
    public const int ThinbloodGeneration = 13;

    /// <summary>
    /// Cap on Humanity + Blood Potency. Returns null below Generation 4
    /// (Methuselahs and Antediluvians), which the chart does not cover.
    /// </summary>
    public static int? Strength(int generation) => generation switch
    {
        13 => 9,
        12 => 10,
        10 or 11 => 11,
        >= 7 and <= 9 => 13,
        >= 4 and <= 6 => 15,
        < 4 and > 0 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(generation)),
    };

    public static FeedingRequirement Feeding(int generation, int bloodPotency) => generation switch
    {
        12 or 13 => new(FeedingSource.Animals | FeedingSource.Humans | FeedingSource.Vampires | FeedingSource.BloodBags, null),
        10 or 11 => new(FeedingSource.Humans | FeedingSource.Vampires | FeedingSource.BloodBags, null),
        >= 7 and <= 9 => new(FeedingSource.Humans | FeedingSource.Vampires,
            "Humans: 4 pints per Vitae, rounded down."),
        >= 1 and <= 6 => new(
            FeedingSource.Humans | (bloodPotency >= 6 ? FeedingSource.Vampires : FeedingSource.None),
            "Humans must be drained to death for 1 Vitae; vampire blood only at Blood Potency 6+."),
        _ => throw new ArgumentOutOfRangeException(nameof(generation)),
    };
}

/// <summary>Humanity limits and Stains (VtR p.13–15).</summary>
public static class HumanityRules
{
    public const int MinHumanity = 0;
    public const int MaxHumanity = 10;

    /// <summary>
    /// Humanity + Blood Potency may never exceed Generation Strength; Humanity is always
    /// the trait reduced (Blood Potency never goes down).
    /// </summary>
    public static int ClampToGeneration(int humanity, int bloodPotency, int generation)
    {
        EnsureHumanity(humanity);
        BloodRules.EnsureBloodPotency(bloodPotency);
        var strength = GenerationRules.Strength(generation);
        if (strength is null) return humanity;
        return Math.Clamp(strength.Value - bloodPotency, MinHumanity, humanity);
    }

    /// <summary>Dice for a Stains roll at the given Humanity; null at Humanity 1 or lower (the character is lost).</summary>
    public static int? StainsDicePool(int humanity)
    {
        EnsureHumanity(humanity);
        return humanity switch
        {
            10 => 5,
            8 or 9 => 4,
            6 or 7 => 3,
            >= 2 and <= 5 => 2,
            _ => null,
        };
    }

    internal static void EnsureHumanity(int humanity)
    {
        if (humanity is < MinHumanity or > MaxHumanity)
            throw new ArgumentOutOfRangeException(nameof(humanity), humanity,
                $"Humanity must be {MinHumanity}–{MaxHumanity}.");
    }
}
