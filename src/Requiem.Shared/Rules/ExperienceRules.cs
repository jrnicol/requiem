namespace Requiem.Shared.Rules;

public enum PurchaseKind
{
    Attribute,
    Skill,
    Specialty,
    Merit,
    BloodPotency,
    BloodlineDiscipline,
    /// <summary>Caitiff Disciplines and Disciplines gained through Diablerie.</summary>
    CaitiffDiscipline,
    Ritual,
}

public sealed record XpQuote(int Cost, IReadOnlyList<RuleViolation> Violations)
{
    public bool IsAllowed => Violations.Count == 0;
}

/// <summary>Advancement costs (WoD Core p.75, VtR p.137).</summary>
public static class ExperienceRules
{
    /// <summary>Vampires spend XP after 4 Corruption points; mortals after 4 Links.</summary>
    public const int ProgressPointsToUnlockXp = 4;

    public const int SpecialtyCost = 5;
    public const int RitualCost = 10;

    public static bool IsXpUnlocked(int corruptionOrLinks) => corruptionOrLinks >= ProgressPointsToUnlockXp;

    /// <summary>XP for marking all four Scripts: 4 for vampires, 1 for mortals and ghouls.</summary>
    public static int ScriptsCompletionXp(bool isVampire) => isVampire ? 4 : 1;

    /// <summary>
    /// Price to raise a rated trait from <paramref name="currentDots"/> to <paramref name="targetDots"/>.
    /// "New dots × N": each step costs the new rating times the multiplier.
    /// For <see cref="PurchaseKind.Specialty"/> and <see cref="PurchaseKind.Ritual"/> the dots are a count of items bought.
    /// </summary>
    /// <param name="bloodPotency">Required for Discipline purchases (dots may not exceed Blood Potency).</param>
    public static XpQuote Quote(PurchaseKind kind, int currentDots, int targetDots, int? bloodPotency = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentDots);
        var violations = new List<RuleViolation>();

        if (targetDots <= currentDots)
            violations.Add(new("xp.no-increase", "Target must be higher than the current rating."));

        var max = kind switch
        {
            PurchaseKind.Attribute => Traits.MaxAttribute,
            PurchaseKind.Skill => Traits.MaxSkill,
            PurchaseKind.Merit => 5,
            PurchaseKind.BloodPotency => BloodRules.MaxBloodPotency,
            PurchaseKind.BloodlineDiscipline or PurchaseKind.CaitiffDiscipline => PowerRules.MaxDisciplineDots,
            _ => int.MaxValue,
        };
        if (targetDots > max)
            violations.Add(new("xp.exceeds-max", $"Maximum rating is {max}."));

        if (kind == PurchaseKind.Attribute && currentDots <= 1)
            violations.Add(new("xp.attribute-at-one",
                "An Attribute at 1 dot can't be raised with XP without a plot event."));

        if (kind is PurchaseKind.BloodlineDiscipline or PurchaseKind.CaitiffDiscipline)
        {
            if (bloodPotency is null)
                throw new ArgumentNullException(nameof(bloodPotency), "Discipline purchases need Blood Potency.");
            if (targetDots > bloodPotency)
                violations.Add(new("discipline.dots-exceed-bp",
                    $"Discipline dots may not exceed Blood Potency ({bloodPotency})."));
        }

        var cost = targetDots <= currentDots ? 0 : kind switch
        {
            PurchaseKind.Specialty => (targetDots - currentDots) * SpecialtyCost,
            PurchaseKind.Ritual => (targetDots - currentDots) * RitualCost,
            _ => SumOfNewDots(currentDots, targetDots) * Multiplier(kind),
        };

        return new XpQuote(cost, violations);
    }

    private static int Multiplier(PurchaseKind kind) => kind switch
    {
        PurchaseKind.Attribute => 5,
        PurchaseKind.Skill => 3,
        PurchaseKind.Merit => 5,
        PurchaseKind.BloodPotency => 5,
        PurchaseKind.BloodlineDiscipline => 5,
        PurchaseKind.CaitiffDiscipline => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int SumOfNewDots(int from, int to)
    {
        var sum = 0;
        for (var dot = from + 1; dot <= to; dot++) sum += dot;
        return sum;
    }
}
