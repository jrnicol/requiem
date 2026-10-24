namespace Requiem.Shared.Rules;

public enum PowerGroupKind
{
    BasePower,
    CaitiffDiscipline,
    BloodlineDiscipline,
    BloodSorcery,
    SabbatRitae,
    GhoulPower,
}

public enum PowerAcquisition
{
    /// <summary>N dots grants every power at levels 1 through N.</summary>
    Cumulative,

    /// <summary>Each power is learned and recorded separately.</summary>
    LearnedIndividually,
}

public sealed record DisciplineRating(string Name, int Dots);

/// <summary>How characters gain powers and the limits on Disciplines (house ruling 2026-10-23; VtR p.15).</summary>
public static class PowerRules
{
    public const int MaxDisciplineDots = 5;

    public static PowerAcquisition AcquisitionFor(PowerGroupKind kind) => kind switch
    {
        PowerGroupKind.BloodSorcery or PowerGroupKind.SabbatRitae => PowerAcquisition.LearnedIndividually,
        _ => PowerAcquisition.Cumulative,
    };

    /// <summary>
    /// True for a dotted Discipline whose powers all sit at the first dot, so each dot buys one of
    /// them in any order (Investiture, VtR p.158). Its picks are recorded like learned rituals.
    /// </summary>
    public static bool IsPickPerDot(PowerGroupKind kind, IReadOnlyCollection<int?> powerLevels) =>
        kind is PowerGroupKind.CaitiffDiscipline or PowerGroupKind.BloodlineDiscipline
        && powerLevels.Count > 1 && powerLevels.All(l => l == 1);

    /// <summary>Power levels a cumulative discipline grants at the given dots.</summary>
    public static IReadOnlyList<int> GrantedLevels(int dots) =>
        Enumerable.Range(1, Math.Clamp(dots, 0, MaxDisciplineDots)).ToArray();

    /// <summary>
    /// No single Discipline may exceed Blood Potency. The limit is on dots, not on how many Disciplines
    /// a vampire has: they always have their Caitiff Discipline and, in a Bloodline, its Discipline, and
    /// gain others only through diablerie (house ruling 2026-10-24).
    /// </summary>
    public static IReadOnlyList<RuleViolation> Validate(int bloodPotency, IEnumerable<DisciplineRating> disciplines)
    {
        BloodRules.EnsureBloodPotency(bloodPotency);
        var owned = disciplines.Where(d => d.Dots > 0).ToList();
        var violations = new List<RuleViolation>();

        foreach (var d in owned.Where(d => d.Dots > bloodPotency))
            violations.Add(new("discipline.dots-exceed-bp",
                $"{d.Name} has {d.Dots} dots but Blood Potency is {bloodPotency}."));

        foreach (var d in owned.Where(d => d.Dots > MaxDisciplineDots))
            violations.Add(new("discipline.dots-exceed-max",
                $"{d.Name} has {d.Dots} dots; the maximum is {MaxDisciplineDots}."));

        return violations;
    }
}
