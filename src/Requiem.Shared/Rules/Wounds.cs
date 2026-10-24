namespace Requiem.Shared.Rules;

public enum DamageType { Bashing, Lethal, Aggravated }

public sealed record Wound(string Description, int Amount, DamageType Type);

public sealed record HealthStatus(
    int TotalDamage,
    int Health,
    int DicePenalty,
    bool IsTakenOut,
    bool HasPotentiallyLethalWound)
{
    public int Remaining => Math.Max(0, Health - TotalDamage);
}

/// <summary>
/// Wounds as a list (WoD Core p.32, house ruling 2026-10-23):
/// Taken Out when total damage reaches Health; the single worst wound sets the dice penalty;
/// otherwise type only matters for healing.
/// </summary>
public static class WoundRules
{
    public static int PenaltyFor(DamageType type) => type switch
    {
        DamageType.Bashing => 1,
        DamageType.Lethal => 2,
        _ => 5,
    };

    /// <param name="health">Health dots (Stamina + Size, or Physical for Supporting Cast).</param>
    public static HealthStatus Evaluate(IEnumerable<Wound> wounds, int health)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(health);

        var active = wounds.Where(w => w.Amount > 0).ToList();
        var total = active.Sum(w => w.Amount);
        var penalty = active.Count == 0 ? 0 : active.Max(w => PenaltyFor(w.Type));

        return new HealthStatus(
            TotalDamage: total,
            Health: health,
            DicePenalty: penalty,
            IsTakenOut: total >= health,
            // Over double Health in a single wound may kill outright (Storyteller may allow a Stamina roll).
            HasPotentiallyLethalWound: active.Any(w => w.Amount > health * 2));
    }
}
