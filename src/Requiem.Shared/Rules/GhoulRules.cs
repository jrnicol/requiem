using Requiem.Shared.Characters;

namespace Requiem.Shared.Rules;

/// <summary>
/// Ghouls (VtR p.62): mortals holding a vampire's Vitae. They stop aging while any remains, lose 1 Vitae
/// a day, heal with Vitae as a vampire does, and may spend 1 Vitae for a temporary dot of Potence,
/// Celerity or Fortitude (at most 1 in each, lasting as long as a vampire's Fortify Form).
/// </summary>
public static class GhoulRules
{
    /// <summary>Most Vitae a ghoul can hold (house ruling 2026-10-23: the 8 they can drink in a night).</summary>
    public const int MaxVitae = 8;

    public const int DailyVitaeLoss = 1;

    public const int MaxFortifyBoost = 1;

    /// <summary>Shown wherever a ghoul's Vitae is tracked; the GM marks the daily loss by hand.</summary>
    public const string VitaeReminder =
        "Loses 1 Vitae each day. While any remains they don't age; at 0 they age to their true age at once.";

    /// <summary>Vampires and ghouls track Vitae; mortals don't.</summary>
    public static bool HoldsVitae(CharacterKind kind) => kind is CharacterKind.Vampire or CharacterKind.Ghoul;

    /// <summary>The size of the Vitae track, or null for mortals.</summary>
    public static int? VitaeCapacity(CharacterKind kind, int? bloodPotency, bool isThinblood = false) => kind switch
    {
        CharacterKind.Vampire => BloodRules.MaxVitae(bloodPotency ?? BloodRules.MinBloodPotency, isThinblood),
        CharacterKind.Ghoul => MaxVitae,
        _ => null,
    };

    /// <summary>
    /// Most Fortify Form points in one physical Discipline: up to the matching attribute for vampires,
    /// and 1 for ghouls (never more than the attribute either way). Mortals get none.
    /// </summary>
    public static int FortifyCap(CharacterKind kind, int attribute) => kind switch
    {
        CharacterKind.Vampire => Math.Max(0, attribute),
        CharacterKind.Ghoul => Math.Clamp(attribute, 0, MaxFortifyBoost),
        _ => 0,
    };
}
