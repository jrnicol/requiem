namespace Requiem.Shared.Rules;

/// <summary>One of a sorcery's Eruditions; <paramref name="Path"/> is Necromancy's Animus or Mortuus, whose lists differ.</summary>
public sealed record Erudition(string Name, string? Path = null);

/// <summary>
/// Casting Blood Sorcery (VtR p.195). Each Erudition used adds 2 dice, 1 hour of casting time and one simple
/// narrative element (the Storyteller's call). Rolls lose the target's Blood Potency. Magi cast by day, when
/// Disciplines don't work (VtR p.18).
/// </summary>
public static class SorceryRules
{
    public const int DicePerErudition = 2;
    public const int HoursPerErudition = 1;

    // Transcribed by hand from each sorcery's list: the OCR runs the items together.
    private static readonly Dictionary<string, Erudition[]> Lists = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hekate"] =
        [
            new("True names"),
            new("Blood ties or bonds"),
            new("Mortal sacrifice (kills the victim)"),
            new("Swallowing a burning coal (1 aggravated damage)"),
            new("A pendant carved from a tombstone"),
            new("Nightshade twine, sealed with wax"),
        ],
        ["Vishap"] =
        [
            new("Clay"),
            new("Earth and soil from the homelands"),
            new("Mortal sacrifice (kills the victim)"),
        ],
        ["Mayaparisatya"] =
        [
            new("Smoke"),
            new("Mirrors"),
            new("Music, song and dance"),
            new("Mortal sacrifice (kills the victim)"),
            new("Blood dripped on a dead man's eye"),
            new("A green candle's ash and wax, formed into a sphere on the target"),
            new("A black candle burning a hair from the target"),
            new("A name painted in a bird's blood on a consecrated stone"),
            new("The mummified hand of a hanged felon"),
        ],
        ["Noctis"] =
        [
            new("Candles of black wax or human tallow"),
            new("Objects placed to make shadow"),
            new("Items burnt and their ashes used"),
            new("Mortal sacrifice (kills the victim)"),
            new("Mirrors"),
        ],
        ["Necromancy"] =
        [
            new("An object the Shade touched in life", "Animus"),
            new("Pools of water", "Animus"),
            new("Rings of bells", "Animus"),
            new("Resonance", "Animus"),
            new("Mortal sacrifice (kills the victim)", "Animus"),
            new("A token buried in a grave six feet deep", "Animus"),
            new("A hanged man's hood on the target", "Animus"),
            new("Strings", "Mortuus"),
            new("Symbols carved on the floor around the corpse", "Mortuus"),
            new("Body parts soaked in blood before stitching", "Mortuus"),
            new("Dismembering the corpse and stitching it back together", "Mortuus"),
        ],
    };

    /// <summary>The Eruditions a sorcery lists, in the book's order; none for anything else.</summary>
    public static IReadOnlyList<Erudition> Eruditions(string? sorcery) =>
        sorcery is not null && Lists.TryGetValue(sorcery, out var list) ? list : [];

    /// <summary>The casting roll before the wound penalty: the Ritual's pool, +2 per Erudition, minus the target's Blood Potency.</summary>
    public static int CastingDice(int pool, int eruditions, int targetBloodPotency) =>
        pool + eruditions * DicePerErudition - Math.Max(0, targetBloodPotency);

    /// <summary>The Ritual's own casting time with an hour per Erudition after it.</summary>
    public static string CastingTime(string? baseTime, int eruditions)
    {
        var extra = eruditions <= 0 ? null : eruditions == 1 ? "1 hour" : $"{eruditions * HoursPerErudition} hours";
        if (string.IsNullOrWhiteSpace(baseTime)) return extra is null ? "As the Ritual says" : $"The Ritual's time + {extra}";
        return extra is null ? baseTime : $"{baseTime} + {extra}";
    }

    /// <summary>Disciplines stop at sunrise; Blood Sorcery, Ritae and the base powers (healing, Fortify Form) go on.</summary>
    public static bool WorksByDay(PowerGroupKind kind) => kind is not (PowerGroupKind.CaitiffDiscipline or PowerGroupKind.BloodlineDiscipline);
}
