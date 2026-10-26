using System.ComponentModel.DataAnnotations;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Contracts;

/// <summary>
/// Create/edit model for a Supporting Cast NPC. The same validation runs in the browser
/// (EditForm + DataAnnotationsValidator) and on the server.
/// </summary>
public sealed class SupportingCastForm : IValidatableObject, ICharacterProfile
{
    public const int MaxListItemLength = 200;

    /// <summary>Null when creating.</summary>
    public Guid? Id { get; set; }

    /// <summary>The campaign it belongs to (required on create). Moving uses its own endpoint.</summary>
    public Guid? CampaignId { get; set; }

    [Required(ErrorMessage = "Every NPC needs a name."), StringLength(200)]
    public string Name { get; set; } = "";

    public CharacterKind Kind { get; set; } = CharacterKind.Mortal;

    [StringLength(200)] public string? Concept { get; set; }
    [StringLength(200)] public string? Faction { get; set; }
    [StringLength(500)] public string? Ambition { get; set; }

    [Range(SupportingCastRules.MinRating, SupportingCastRules.MaxRating)] public int Physical { get; set; } = 2;
    [Range(SupportingCastRules.MinRating, SupportingCastRules.MaxRating)] public int Mental { get; set; } = 2;
    [Range(SupportingCastRules.MinRating, SupportingCastRules.MaxRating)] public int Social { get; set; } = 2;

    public NpcEdge Edges { get; set; }

    // Vampire only; ignored (and cleared on save) for ghouls and mortals.
    public int? BloodlineId { get; set; }
    public int? PredatorTypeId { get; set; }
    [Range(BloodRules.MinBloodPotency, BloodRules.MaxBloodPotency)] public int? BloodPotency { get; set; }
    [Range(1, GenerationRules.ThinbloodGeneration)] public int? Generation { get; set; }
    [Range(0, 50)] public int? VitaeCurrent { get; set; }

    /// <summary>Free-standing Specialties (+1 die each when relevant).</summary>
    public List<string> Specialties { get; set; } = [];

    /// <summary>Places, organizations and tools the NPC can call on (+2 dice when used).</summary>
    public List<string> Assets { get; set; } = [];

    [StringLength(4000)] public string? Notes { get; set; }
    [StringLength(4000)] public string? GmNotes { get; set; }

    [StringLength(CharacterProfile.MaxDescriptionLength)] public string? Appearance { get; set; }
    [StringLength(CharacterProfile.MaxDescriptionLength)] public string? Reputation { get; set; }
    [StringLength(CharacterProfile.MaxBackstoryLength)] public string? Backstory { get; set; }
    public string? PortraitUrl { get; set; }
    public bool IsShared { get; set; }
    public NpcReveal Reveals { get; set; }


    /// <summary>Concurrency token from the last load; the save is rejected if someone else saved since.</summary>
    public Guid Version { get; set; }

    public bool IsVampire => Kind == CharacterKind.Vampire;

    public bool IsGhoul => Kind == CharacterKind.Ghoul;

    /// <summary>Size of the Vitae track: by Blood Potency for vampires, 8 for ghouls, null for mortals.</summary>
    public int? VitaeCapacity => GhoulRules.VitaeCapacity(Kind, BloodPotency);

    /// <summary>
    /// Fills unset values with the book's starting values: BP 1, Generation 12 and full Vitae for vampires;
    /// a full 8 Vitae for ghouls.
    /// </summary>
    public void ApplyVampireDefaults()
    {
        if (IsGhoul) VitaeCurrent ??= GhoulRules.MaxVitae;
        if (!IsVampire) return;
        BloodPotency ??= BloodRules.MinBloodPotency;
        Generation ??= GenerationRules.StartingGeneration;
        VitaeCurrent ??= BloodRules.MaxVitae(BloodPotency.Value);
    }

    /// <summary>Drops vampire-only values when the NPC isn't a vampire. Ghouls keep their Vitae.</summary>
    public void ClearNonVampireFields()
    {
        if (IsVampire) return;
        BloodlineId = null;
        PredatorTypeId = null;
        BloodPotency = null;
        Generation = null;
        if (!IsGhoul) VitaeCurrent = null;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ValidateList(Specialties, nameof(Specialties), "Specialty")) yield return result;
        foreach (var result in ValidateList(Assets, nameof(Assets), "Asset")) yield return result;

        if (IsVampire && BloodPotency is { } bp && VitaeCurrent is { } vitae && vitae > BloodRules.MaxVitae(bp))
            yield return new ValidationResult(
                $"Vitae can't exceed {BloodRules.MaxVitae(bp)} at Blood Potency {bp}.", [nameof(VitaeCurrent)]);
        if (IsGhoul && VitaeCurrent > GhoulRules.MaxVitae)
            yield return new ValidationResult($"A ghoul holds at most {GhoulRules.MaxVitae} Vitae.", [nameof(VitaeCurrent)]);
    }

    private static IEnumerable<ValidationResult> ValidateList(List<string> items, string member, string label)
    {
        if (items.Any(string.IsNullOrWhiteSpace))
            yield return new ValidationResult($"{label} entries can't be blank.", [member]);
        if (items.Any(i => i.Length > MaxListItemLength))
            yield return new ValidationResult($"{label} entries are limited to {MaxListItemLength} characters.", [member]);
        if (items.GroupBy(i => i.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            yield return new ValidationResult($"Each {label.ToLowerInvariant()} can only be listed once.", [member]);
    }
}
