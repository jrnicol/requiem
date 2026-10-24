using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Contracts;

/// <param name="Kind">Bloodline, MagiHouse or Caitiff.</param>
public sealed record BloodlineOption(
    int Id,
    string Name,
    string Kind,
    string? WeaknessName,
    string? WeaknessText,
    string? CompelName,
    string? CompelText,
    int? SourcePage);

/// <summary>A character Concept and how acting it out regains Willpower (WoD Core p.55).</summary>
public sealed record ConceptOption(int Id, string Name, string? Description, string WillpowerTrigger, int? SourcePage);

public sealed record PredatorTypeOption(int Id, string Name, string? Description, string? Restrictions, int? SourcePage);

public sealed record EdgeOption(int Id, string Name, NpcEdge? Edge, string? Description, int? SourcePage);

public sealed record DisciplineOption(
    int Id,
    string Name,
    PowerGroupKind Kind,
    int? BloodlineId,
    string? BloodlineName,
    string? Description,
    int? SourcePage)
{
    /// <summary>Rated in dots (Caitiff and Bloodline Disciplines); the rest are learned power by power.</summary>
    public bool HasDots => Kind is PowerGroupKind.CaitiffDiscipline or PowerGroupKind.BloodlineDiscipline;
}

public sealed record PowerBonusOptionDto(int SuccessCost, string Effect);

/// <summary>A catalog power with everything the book prints about it.</summary>
public sealed record PowerOption(
    int Id,
    int PowerGroupId,
    int? Level,
    string Name,
    string? Category,
    string? Flavor,
    int? VitaeCost,
    int? WillpowerCost,
    string? CostText,
    string? DicePoolText,
    CharacterAttribute? DicePoolAttribute,
    Skill? DicePoolSkill,
    int? DicePoolPowerGroupId,
    string? OpposedBy,
    string? ActionText,
    string? Duration,
    string? CastingDuration,
    string? HowToLearn,
    string? DramaticFailureText,
    string? FailureText,
    string? SuccessText,
    string? ExceptionalSuccessText,
    string? Notes,
    int? SourcePage,
    bool IsReviewed,
    IReadOnlyList<PowerBonusOptionDto> BonusOptions)
{
    /// <summary>Text the search box matches: name, category and every printed field.</summary>
    [JsonIgnore]
    public string SearchText => string.Join('\n', new[]
    {
        Name, Category, Flavor, CostText, DicePoolText, OpposedBy, ActionText, Duration, CastingDuration, HowToLearn,
        DramaticFailureText, FailureText, SuccessText, ExceptionalSuccessText, Notes,
    }.Concat(BonusOptions.Select(b => b.Effect)).Where(s => s is not null));
}

/// <summary>The GM's corrections to an imported power on the review screen.</summary>
public sealed class PowerEditForm
{
    public const int MaxLongText = 8000;

    [Range(1, 5)] public int? Level { get; set; }
    [Required(ErrorMessage = "A power needs a name."), StringLength(200)] public string Name { get; set; } = "";
    [StringLength(200)] public string? Category { get; set; }
    [StringLength(MaxLongText)] public string? Flavor { get; set; }
    [Range(0, 20)] public int? VitaeCost { get; set; }
    [Range(0, 10)] public int? WillpowerCost { get; set; }
    [StringLength(500)] public string? CostText { get; set; }
    [StringLength(500)] public string? DicePoolText { get; set; }
    public CharacterAttribute? DicePoolAttribute { get; set; }
    public Skill? DicePoolSkill { get; set; }
    public int? DicePoolPowerGroupId { get; set; }
    [StringLength(200)] public string? OpposedBy { get; set; }
    [StringLength(MaxLongText)] public string? ActionText { get; set; }
    [StringLength(500)] public string? Duration { get; set; }
    [StringLength(500)] public string? CastingDuration { get; set; }
    [StringLength(MaxLongText)] public string? HowToLearn { get; set; }
    [StringLength(MaxLongText)] public string? DramaticFailureText { get; set; }
    [StringLength(MaxLongText)] public string? FailureText { get; set; }
    [StringLength(MaxLongText)] public string? SuccessText { get; set; }
    [StringLength(MaxLongText)] public string? ExceptionalSuccessText { get; set; }
    [StringLength(MaxLongText)] public string? Notes { get; set; }
    public bool IsReviewed { get; set; }
    public List<PowerBonusEntry> BonusOptions { get; set; } = [];

    public static PowerEditForm From(PowerOption p) => new()
    {
        Level = p.Level, Name = p.Name, Category = p.Category, Flavor = p.Flavor,
        VitaeCost = p.VitaeCost, WillpowerCost = p.WillpowerCost, CostText = p.CostText,
        DicePoolText = p.DicePoolText, DicePoolAttribute = p.DicePoolAttribute, DicePoolSkill = p.DicePoolSkill,
        DicePoolPowerGroupId = p.DicePoolPowerGroupId, OpposedBy = p.OpposedBy, ActionText = p.ActionText,
        Duration = p.Duration, CastingDuration = p.CastingDuration, HowToLearn = p.HowToLearn,
        DramaticFailureText = p.DramaticFailureText, FailureText = p.FailureText, SuccessText = p.SuccessText,
        ExceptionalSuccessText = p.ExceptionalSuccessText, Notes = p.Notes, IsReviewed = p.IsReviewed,
        BonusOptions = p.BonusOptions.Select(b => new PowerBonusEntry { SuccessCost = b.SuccessCost, Effect = b.Effect }).ToList(),
    };
}

public sealed class PowerBonusEntry
{
    [Range(1, 10)] public int SuccessCost { get; set; } = 1;
    [Required, StringLength(1000)] public string Effect { get; set; } = "";
}

public sealed record MeritOption(
    int Id,
    string Name,
    MeritKind Kind,
    RuleSource Source,
    string? Cost,
    int? MinDots,
    int? MaxDots,
    string? Description,
    string? Restrictions,
    int? SourcePage);

public sealed record CompelOption(
    int Id,
    string Name,
    CompelKind Kind,
    int? Roll,
    string? Rating,
    string? Trigger,
    string? EndsWhen,
    string? Description,
    int? SourcePage);
