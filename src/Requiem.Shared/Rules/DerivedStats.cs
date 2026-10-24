namespace Requiem.Shared.Rules;

/// <summary>Values computed from a full character sheet (WoD Core p.74).</summary>
public static class DerivedStats
{
    public const int DefaultSize = 5;
    public const int StartingMorality = 7;

    public static int Health(int stamina, int size = DefaultSize) => stamina + size;

    public static int Defense(int wits, int dexterity) => Math.Min(wits, dexterity);

    /// <summary>
    /// Willpower dots are set from Wits + Resolve at creation only; they do not
    /// track later changes to either attribute, so store the result.
    /// </summary>
    public static int StartingWillpower(int wits, int resolve) => Math.Clamp(wits + resolve, 1, 10);

    /// <summary>Starting Humanity is starting Morality minus the dot burned by the Embrace (VtR p.136).</summary>
    public static int StartingHumanity(int morality = StartingMorality) => morality - 1;

    /// <summary>Temporary Willpower points that may be spent on one roll (WoD Core p.93).</summary>
    public static int MaxWillpowerSpendPerRoll(int resolve) => resolve;

    public const int DicePerWillpowerPoint = 2;
}
