namespace Requiem.Shared.Rules;

public enum KoldunDomain
{
    /// <summary>Heal and repair.</summary>
    Regeneration,
    /// <summary>Destroy and burn.</summary>
    Destruction,
    /// <summary>Make tougher or harder to find.</summary>
    Protection,
    /// <summary>Change shape or function.</summary>
    Alteration,
}

public enum KoldunArt
{
    AffectSelf,
    AffectOtherUndead,
    AffectOtherLiving,
    AffectInanimate,
    AffectArea,
    AffectAtDistance,
}

/// <summary>One Dark Retribution result and the d10 rolls that give it.</summary>
public sealed record DarkRetribution(int From, int To, string Name, string Effect)
{
    public string Rolls => From == To ? $"{From}" : $"{From}–{To}";
}

/// <summary>
/// Koldun Thaumaturgy (VtR p.203), House Tzimisce's rated sorcery (house rulings 2026-11-04). Unlike other Rituals it
/// has a rating, 1 to 5, at 10 XP a dot, and is only ever bought with XP: never the free starting Ritual, nor with
/// Merit points. The first dot comes with a Domain, 3 Arts and a Method; later dots add nothing but the rating.
/// <list type="bullet">
/// <item>Invoke a Spell: 1 Vitae; roll Thaumaturgy + extra Vitae (up to Blood Potency) + Eruditions, no Willpower.</item>
/// <item>Each success cast adds a point of Antipathia. At 10, or on a botch, it clears and the Dark Retribution strikes.</item>
/// <item>An Oblation takes 3 Antipathia off.</item>
/// </list>
/// </summary>
public static class KoldunRules
{
    public const string RitualName = "Koldun Thaumaturgy";
    public const string Sorcery = "Vishap";
    public const int MinDots = 1, MaxDots = 5;
    public const int CostPerDot = 10;
    public const int ArtsChosen = 3;
    public const int MaxAntipathia = 10;
    public const int OblationRelief = 3;
    public const int InvokeVitae = 1;

    public static bool IsKoldun(string? powerName) => string.Equals(powerName, RitualName, StringComparison.OrdinalIgnoreCase);

    public static bool IsKoldun(this CreationCatalog catalog, int powerId) => IsKoldun(catalog.PowerNames?.GetValueOrDefault(powerId));

    public static string Label(KoldunArt art) => art switch
    {
        KoldunArt.AffectSelf => "Affect self",
        KoldunArt.AffectOtherUndead => "Affect other undead",
        KoldunArt.AffectOtherLiving => "Affect other living",
        KoldunArt.AffectInanimate => "Affect inanimate",
        KoldunArt.AffectArea => "Affect area",
        KoldunArt.AffectAtDistance => "Affect at distance",
        _ => art.ToString(),
    };

    public static string Describe(KoldunDomain domain) => domain switch
    {
        KoldunDomain.Regeneration => "heal and repair",
        KoldunDomain.Destruction => "destroy and burn",
        KoldunDomain.Protection => "make tougher or harder to find",
        KoldunDomain.Alteration => "change shape or function",
        _ => "",
    };

    /// <summary>Koldun adds two Eruditions of its own to Vishap's.</summary>
    public static readonly IReadOnlyList<Erudition> Eruditions =
    [
        new("Blood with a Passion related to the spell"),
        new("A mortal magi's blood (no Antipathia)"),
    ];

    /// <summary>Extra Vitae is a pool of bonus dice as large as Blood Potency.</summary>
    public static int MaxExtraVitae(int bloodPotency) => Math.Max(0, bloodPotency);

    /// <summary>The casting roll before the wound penalty.</summary>
    public static int InvokeDice(int thaumaturgy, int extraVitae, int eruditions, int targetBloodPotency) =>
        thaumaturgy + extraVitae + eruditions * SorceryRules.DicePerErudition - Math.Max(0, targetBloodPotency);

    /// <summary>
    /// Antipathia after a casting's <paramref name="successes"/> (or a botch); <c>Retribution</c> is true when it reached
    /// 10 or the roll botched, and it then clears to 0.
    /// </summary>
    public static (int Antipathia, bool Retribution) Gain(int antipathia, int successes, bool botched = false)
    {
        var total = Math.Max(0, antipathia) + Math.Max(0, successes);
        return botched || total >= MaxAntipathia ? (0, true) : (total, false);
    }

    public static int Oblation(int antipathia) => Math.Max(0, antipathia - OblationRelief);

    /// <summary>The book prints four results for a d10; house ruling 2026-11-04 spreads them over it.</summary>
    public static readonly IReadOnlyList<DarkRetribution> Retributions =
    [
        new(1, 3, "Manic", "Your Flaws affect you as badly as they can until the next night's sunrise, plus a night for every Vitae spent on the spell."),
        new(4, 5, "Backlash", "Lethal damage equal to the Antipathia just gained plus any extra Vitae used; Aggravated if you botched."),
        new(6, 7, "Madness", "You see the infinite permutations of reality. Burn a dot of Intelligence."),
        new(8, 10, "Haunted", "For a day per dot of Blood Potency spirits terrorize you: fail Wits + Resolve and take a penalty equal to the Vitae spent, until sunrise, when you may roll again."),
    ];

    public static DarkRetribution Retribution(int roll) =>
        Retributions.FirstOrDefault(r => roll >= r.From && roll <= r.To) ?? Retributions[^1];

    /// <summary>What a character who knows Koldun still needs to choose; empty when complete.</summary>
    public static IEnumerable<string> Missing(KoldunDomain? domain, IReadOnlyCollection<KoldunArt> arts, string? method)
    {
        if (domain is null) yield return "Choose a Koldun Domain.";
        if (arts.Distinct().Count() != ArtsChosen) yield return $"Choose {ArtsChosen} Koldun Arts.";
        if (string.IsNullOrWhiteSpace(method)) yield return "Describe the Method by which you practice Koldun.";
    }
}
