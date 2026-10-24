namespace Requiem.Shared.Rules;

[Flags]
public enum NpcEdge
{
    None = 0,
    Tough = 1,
    Unshakable = 2,
    QuickWitted = 4,
    WellConnected = 8,
    Assassin = 16,
}

/// <summary>
/// Light-weight Supporting Cast NPCs: Physical / Mental / Social 1–5 plus Edges (WoD Core p.114–117).
/// </summary>
public static class SupportingCastRules
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int EdgeBonus = 5;
    public const int SpecialtyBonus = 1;
    public const int AssetBonus = 2;

    /// <summary>Health = Physical (house ruling), +5 with Tough.</summary>
    public static int Health(int physical, NpcEdge edges = NpcEdge.None) =>
        Ensure(physical) + (edges.HasFlag(NpcEdge.Tough) ? EdgeBonus : 0);

    /// <summary>Willpower dots = Mental, +5 with Unshakable. Supporting Cast never spend Willpower.</summary>
    public static int Willpower(int mental, NpcEdge edges = NpcEdge.None) =>
        Ensure(mental) + (edges.HasFlag(NpcEdge.Unshakable) ? EdgeBonus : 0);

    public static int EffectiveSocial(int social, NpcEdge edges = NpcEdge.None) =>
        Ensure(social) + (edges.HasFlag(NpcEdge.QuickWitted) ? EdgeBonus : 0);

    /// <summary>Dice for a roll: the category rating plus +1 per relevant Specialty and +2 per Asset used.</summary>
    public static int DicePool(int rating, int specialties = 0, int assets = 0) =>
        Ensure(rating) + specialties * SpecialtyBonus + assets * AssetBonus;

    private static int Ensure(int rating)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rating, MinRating);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rating, MaxRating);
        return rating;
    }
}
