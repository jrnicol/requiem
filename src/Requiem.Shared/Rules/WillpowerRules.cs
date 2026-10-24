namespace Requiem.Shared.Rules;

/// <summary>What a character did to regain Willpower (WoD Core p.91).</summary>
public enum WillpowerSource
{
    /// <summary>Accomplished something toward their Concept: 1 point.</summary>
    Concept,
    /// <summary>Acted on their Virtue, giving more than they had to offer: all spent points back.</summary>
    Virtue,
    /// <summary>Indulged their Vice, hurting someone (themselves included): 1 point, once per scene.</summary>
    Vice,
}

/// <summary>Regaining Willpower (WoD Core p.91). Points never exceed Willpower dots.</summary>
public static class WillpowerRules
{
    /// <summary>Willpower points after regaining from <paramref name="source"/>.</summary>
    public static int Regain(WillpowerSource source, int current, int dots) => source switch
    {
        WillpowerSource.Virtue => dots,
        _ => Math.Min(dots, current + 1),
    };

    /// <summary>The rule as shown at the table.</summary>
    public static string Describe(WillpowerSource source) => source switch
    {
        WillpowerSource.Virtue => "Regain all spent Willpower when they give more than they had to offer.",
        WillpowerSource.Vice => "Regain 1 Willpower, once per scene, when indulging it hurts someone (themselves included).",
        _ => "Regain 1 Willpower when they accomplish something that fits their Concept.",
    };

    /// <summary>Out of Willpower, the character's Vice applies as a Condition until they regain a point (WoD Core p.91).</summary>
    public static bool ViceApplies(int current) => current <= 0;
}
