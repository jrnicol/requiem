using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Campaigns;

/// <summary>
/// A campaign's character creation budgets, set by its Storytellers. The defaults are the book's (WoD Core p.74,
/// VtR p.136); the creation wizard and the approval screen measure every player character against them.
/// </summary>
public sealed class CreationSettings : IValidatableObject
{
    public const int MinStartingGeneration = 4;

    [Range(0, 15, ErrorMessage = "Primary attributes take 0 to 15 dots.")] public int PrimaryAttributes { get; set; } = 5;
    [Range(0, 15, ErrorMessage = "Secondary attributes take 0 to 15 dots.")] public int SecondaryAttributes { get; set; } = 4;
    [Range(0, 15, ErrorMessage = "Tertiary attributes take 0 to 15 dots.")] public int TertiaryAttributes { get; set; } = 3;

    [Range(0, 40, ErrorMessage = "Primary skills take 0 to 40 dots.")] public int PrimarySkills { get; set; } = 11;
    [Range(0, 40, ErrorMessage = "Secondary skills take 0 to 40 dots.")] public int SecondarySkills { get; set; } = 7;
    [Range(0, 40, ErrorMessage = "Tertiary skills take 0 to 40 dots.")] public int TertiarySkills { get; set; } = 5;

    [Range(0, 50, ErrorMessage = "Merit points run from 0 to 50.")] public int MeritPoints { get; set; } = 10;
    [Range(0, 20, ErrorMessage = "Flaw points run from 0 to 20.")] public int FlawPoints { get; set; } = 7;

    /// <summary>XP each player character starts with, credited when the Storyteller approves it. The book gives none.</summary>
    [Range(0, 200, ErrorMessage = "Starting XP runs from 0 to 200.")] public int StartingXp { get; set; }

    /// <summary>Where vampires start, before any Generation bought with Merit points. Thin-bloods are always 13th.</summary>
    [Range(MinStartingGeneration, GenerationRules.StartingGeneration, ErrorMessage = "Vampires start between the 4th and 12th Generation.")]
    public int StartingGeneration { get; set; } = GenerationRules.StartingGeneration;

    /// <summary>
    /// Whether players can spend Merit points on a lower Generation than <see cref="StartingGeneration"/>. On by default; the
    /// settings screen turns it off when the Storyteller lowers the starting Generation, and they can turn it back on.
    /// </summary>
    public bool CanBuyGeneration { get; set; } = true;

    [JsonIgnore] public IReadOnlyList<int> AttributeBudgets => [PrimaryAttributes, SecondaryAttributes, TertiaryAttributes];
    [JsonIgnore] public IReadOnlyList<int> SkillBudgets => [PrimarySkills, SecondarySkills, TertiarySkills];

    public CreationSettings Copy() => (CreationSettings)MemberwiseClone();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PrimaryAttributes < SecondaryAttributes || SecondaryAttributes < TertiaryAttributes)
            yield return new ValidationResult("Attributes go from most dots to fewest: primary, secondary, then tertiary.", [nameof(PrimaryAttributes)]);
        if (PrimarySkills < SecondarySkills || SecondarySkills < TertiarySkills)
            yield return new ValidationResult("Skills go from most dots to fewest: primary, secondary, then tertiary.", [nameof(PrimarySkills)]);
    }
}
