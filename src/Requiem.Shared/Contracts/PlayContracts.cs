using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Contracts;

public sealed record PlayWound(int Id, string Description, int Amount, DamageType Type);

public sealed record PlayCompel(int Id, string Name, int Rating, string? EndsWhen);

public sealed record PlayCondition(int Id, string Name);

/// <summary>A Specialty: tied to a skill on full sheets, free-standing for Supporting Cast.</summary>
public sealed record PlaySpecialty(Skill? Skill, string Name)
{
    public override string ToString() => Skill is { } skill ? $"{skill}: {Name}" : Name;
}

/// <summary>
/// A ready-made dice pool before the wound penalty and Hunger split, which the client applies live.
/// </summary>
/// <param name="Detail">How it adds up, e.g. "Manipulation 3 + Expression 2 + Dominate 2".</param>
/// <param name="Group">"Powers", "Attributes" and so on, for grouping on screen.</param>
public sealed record PlayPool(string Label, int Dice, string Detail, string Group);

/// <summary>
/// Who the character is and what restores their Willpower (WoD Core p.55–62): their Concept (with the
/// book's trigger, when it's one of the book's), Virtue and Vice, each with what it means to them.
/// </summary>
public sealed record PlayMotives(string? Concept, string? ConceptTrigger, string? Virtue, string? VirtueDetail, string? Vice, string? ViceDetail)
{
    [JsonIgnore] public bool Any => Concept is not null || Virtue is not null || Vice is not null;
}

/// <summary>
/// Everything the GM needs to run one NPC at the table, full sheet or Supporting Cast alike.
/// Changes go through <see cref="PlayAction"/> and save straight onto the character.
/// </summary>
public sealed record PlayCharacter(
    Guid Id,
    string Name,
    CharacterTier Tier,
    CharacterKind Kind,
    string? Concept,
    string? BloodlineName,
    int Health,
    IReadOnlyList<PlayWound> Wounds,
    int WillpowerDots,
    int WillpowerCurrent,
    bool CanSpendWillpower,
    int? BloodPotency,
    int? VitaeCurrent,
    int? MaxVitae,
    int MinimumHungerDice,
    int? Humanity,
    int? Morality,
    int? Defense,
    IReadOnlyDictionary<FortifyDiscipline, int> Boosts,
    IReadOnlyDictionary<FortifyDiscipline, int> BoostCaps,
    IReadOnlyList<PlayCompel> Compels,
    IReadOnlyList<PlayCondition> Conditions,
    IReadOnlyList<PlayPool> Pools,
    IReadOnlyList<int> PowerIds,
    IReadOnlyDictionary<CharacterAttribute, int> Attributes,
    IReadOnlyDictionary<Skill, int> Skills,
    IReadOnlyList<PlaySpecialty> Specialties,
    IReadOnlyList<string> Assets,
    string? Notes,
    string? GmNotes,
    bool IsPlayerCharacter = false,
    PlayMotives? Motives = null,
    IReadOnlyList<TitleOption>? Titles = null)
{
    [JsonIgnore] public bool IsVampire => Kind == CharacterKind.Vampire;
    [JsonIgnore] public bool IsFullSheet => Tier == CharacterTier.FullSheet;
    [JsonIgnore] public bool IsGhoul => Kind == CharacterKind.Ghoul;
    /// <summary>Vampires and ghouls: they spend Vitae on healing and Fortify Form.</summary>
    [JsonIgnore] public bool HoldsVitae => VitaeCurrent is not null;

    [JsonIgnore]
    public HealthStatus Status => WoundRules.Evaluate(Wounds.Select(w => new Wound(w.Description, w.Amount, w.Type)), Health);

    /// <summary>Hunger dice in every pool (vampires only): the Bloodlust level, with the Brujah minimum.</summary>
    [JsonIgnore]
    public int HungerDice => IsVampire && BloodPotency is { } bp && VitaeCurrent is { } vitae
        ? BloodRules.HungerDice(vitae, bp, MinimumHungerDice)
        : 0;

    /// <summary>A pool as rolled right now: minus the wound penalty, with Hunger dice split out.</summary>
    public DicePoolSplit Roll(int dice) => DicePools.Split(Math.Max(0, dice - Status.DicePenalty), HungerDice);
}

public enum PlayActionKind
{
    /// <summary>Needs <see cref="PlayAction.Text"/>: every wound says what caused it.</summary>
    AddWound,
    /// <summary>Reduces a wound by <see cref="PlayAction.Amount"/>; it's removed at 0.</summary>
    HealWound,
    RemoveWound,
    AdjustWillpower,
    AdjustVitae,
    /// <summary>Fortify Form: +N points of a physical Discipline costs N Vitae (VtR p.15); lowering refunds nothing.</summary>
    AdjustBoost,
    /// <summary>End of scene: Fortify Form boosts lapse.</summary>
    ClearBoosts,
    AddCompel,
    RemoveCompel,
    AddCondition,
    RemoveCondition,
    /// <summary>
    /// Vampires and ghouls heal a whole wound with Vitae (VtR p.19, p.62): see <see cref="HealingRules.Cost"/>.
    /// </summary>
    HealWithVitae,
    /// <summary>Acting on their Concept, Virtue or Vice restores Willpower: see <see cref="WillpowerRules.Regain"/>.</summary>
    RegainWillpower,
}

/// <summary>One change made at the table. Only the fields its <see cref="Kind"/> needs are read.</summary>
public sealed class PlayAction
{
    public PlayActionKind Kind { get; set; }
    /// <summary>Damage, healing, or the signed change to a tracker.</summary>
    public int Amount { get; set; }
    [StringLength(200)] public string? Text { get; set; }
    public DamageType DamageType { get; set; } = DamageType.Lethal;
    /// <summary>The wound, compel or condition acted on.</summary>
    public int? TargetId { get; set; }
    [StringLength(200)] public string? EndsWhen { get; set; }
    public FortifyDiscipline? Boost { get; set; }
    /// <summary>For <see cref="PlayActionKind.RegainWillpower"/>: what they did.</summary>
    public WillpowerSource? Source { get; set; }
}

public sealed record SceneSummary(Guid Id, string Name, int MemberCount, DateTimeOffset UpdatedAt, Guid? CampaignId = null);

public sealed record SceneView(Guid Id, string Name, IReadOnlyList<PlayCharacter> Members, Guid? CampaignId = null);

public sealed class SceneForm
{
    [Required(ErrorMessage = "Give the scene a name."), StringLength(200)]
    public string Name { get; set; } = "";

    /// <summary>The campaign the scene belongs to; null for a personal scene. Set on create only.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>The NPCs in the scene, in display order.</summary>
    public List<Guid> CharacterIds { get; set; } = [];
}
