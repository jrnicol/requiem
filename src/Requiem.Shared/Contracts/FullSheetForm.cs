using System.ComponentModel.DataAnnotations;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Shared.Contracts;

public sealed class SpecialtyEntry
{
    public Skill Skill { get; set; }
    public string Name { get; set; } = "";
}

public sealed class DisciplineEntry
{
    public int PowerGroupId { get; set; }
    public int Dots { get; set; } = 1;
}

public sealed class MeritEntry
{
    /// <summary>Null for a custom merit or flaw that isn't in the catalog.</summary>
    public int? MeritDefinitionId { get; set; }
    public string Name { get; set; } = "";
    public MeritKind Kind { get; set; }
    public int Dots { get; set; } = 1;
    /// <summary>e.g. "Allies (Harbor Police)" or "Madness (Paranoia)".</summary>
    public string? Qualifier { get; set; }
    public string? Notes { get; set; }
}

public sealed class WoundEntry
{
    public string Description { get; set; } = "";
    public int Amount { get; set; } = 1;
    public DamageType Type { get; set; } = DamageType.Lethal;
}

public sealed class CompelEntry
{
    public string Name { get; set; } = "";
    public int Rating { get; set; } = 1;
    public string? EndsWhen { get; set; }
}

public sealed class ScriptEntry
{
    public int Slot { get; set; }
    public ScriptKind Kind { get; set; }
    public string Text { get; set; } = "";
    public bool IsMarked { get; set; }
}

public sealed class MilestoneEntry
{
    public string Title { get; set; } = "";
    public string? Opportunity { get; set; }
    public string? Risk { get; set; }
    public string? Choice { get; set; }
    public string? Benefit { get; set; }
    public string? Notes { get; set; }
    public MilestoneStage Stage { get; set; } = MilestoneStage.Opportunity;
}

/// <summary>Someone in the character's life (WoD Core p.65), usually not in the app.</summary>
public sealed class RelationshipEntry
{
    public RelationshipRole Role { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Who they are to the character, e.g. "my sister; she knows what I am".</summary>
    public string? Description { get; set; }
}

public sealed class XpLedgerEntry
{
    /// <summary>Positive when earned, negative when spent.</summary>
    public int Amount { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// Create/edit model for a full character sheet (the PDF sheet, both pages). Validation here covers
/// hard limits; the rules engine's softer checks (Discipline dots vs Blood Potency, etc.) are
/// warnings the editor shows without blocking a save.
/// </summary>
public sealed class FullSheetForm : IValidatableObject, ICharacterProfile
{
    public const int MaxTextLength = 200;
    public const int MaxStartingXpPurchases = 50;
    public const int MaxCorruption = ExperienceRules.ProgressPointsToUnlockXp;

    public Guid? Id { get; set; }

    /// <summary>The campaign it belongs to (required on create). Moving uses its own endpoint.</summary>
    public Guid? CampaignId { get; set; }

    [Required(ErrorMessage = "Every character needs a name."), StringLength(200)]
    public string Name { get; set; } = "";

    public CharacterKind Kind { get; set; } = CharacterKind.Vampire;

    [StringLength(200)] public string? Concept { get; set; }
    [StringLength(200)] public string? Faction { get; set; }
    /// <summary>Faction titles held, by name (TitleOption). Set by the Storyteller.</summary>
    [MaxLength(20, ErrorMessage = "At most 20 titles.")] public List<string> Titles { get; set; } = [];
    [StringLength(200)] public string? Virtue { get; set; }
    [StringLength(200)] public string? VirtueDetail { get; set; }
    [StringLength(200)] public string? Vice { get; set; }
    [StringLength(200)] public string? ViceDetail { get; set; }
    [StringLength(500)] public string? BeastDescription { get; set; }
    public Guid? SireId { get; set; }

    /// <summary>Ghouls: the vampire whose Vitae they drink, a character in the campaign or just a name (VtR p.62).</summary>
    public Guid? DomitorId { get; set; }
    [StringLength(200)] public string? DomitorName { get; set; }

    public List<RelationshipEntry> Relationships { get; set; } = [];

    public Dictionary<CharacterAttribute, int> Attributes { get; set; } =
        Enum.GetValues<CharacterAttribute>().ToDictionary(a => a, _ => Traits.MinAttribute);

    public Dictionary<Skill, int> Skills { get; set; } =
        Enum.GetValues<Skill>().ToDictionary(s => s, _ => Traits.MinSkill);

    public List<SpecialtyEntry> Specialties { get; set; } = [];

    [Range(1, 10)] public int Size { get; set; } = DerivedStats.DefaultSize;
    [Range(0, 10)] public int WillpowerDots { get; set; } = DerivedStats.StartingWillpower(1, 1);
    [Range(0, 10)] public int WillpowerCurrent { get; set; } = DerivedStats.StartingWillpower(1, 1);
    /// <summary>Vampires track Humanity; mortals and ghouls track Morality.</summary>
    [Range(0, 10)] public int? Humanity { get; set; } = DerivedStats.StartingHumanity();
    [Range(0, 10)] public int? Morality { get; set; }

    // Vampire only; cleared on save for ghouls and mortals.
    public int? BloodlineId { get; set; }
    public int? PredatorTypeId { get; set; }
    [Range(1, GenerationRules.ThinbloodGeneration)] public int? Generation { get; set; } = GenerationRules.StartingGeneration;
    [Range(BloodRules.MinBloodPotency, BloodRules.MaxBloodPotency)] public int? BloodPotency { get; set; } = BloodRules.MinBloodPotency;
    [Range(0, 50)] public int? VitaeCurrent { get; set; } = BloodRules.MaxVitae(BloodRules.MinBloodPotency);
    public bool IsThinblood { get; set; }
    [Range(0, 5)] public int PotenceBoost { get; set; }
    [Range(0, 5)] public int CelerityBoost { get; set; }
    [Range(0, 5)] public int FortitudeBoost { get; set; }

    public List<DisciplineEntry> Disciplines { get; set; } = [];
    /// <summary>
    /// Powers learned one at a time (catalog power ids): Blood Sorcery rituals, Sabbat Ritae, and the
    /// powers each Investiture dot bought.
    /// </summary>
    public List<int> LearnedPowerIds { get; set; } = [];
    public List<MeritEntry> Merits { get; set; } = [];
    public List<WoundEntry> Wounds { get; set; } = [];
    public List<string> Stains { get; set; } = [];
    public List<CompelEntry> Compels { get; set; } = [];
    /// <summary>Short statuses noted during play, e.g. "Blinded".</summary>
    public List<string> Conditions { get; set; } = [];
    public List<ScriptEntry> Scripts { get; set; } = DefaultScripts();
    public List<MilestoneEntry> Milestones { get; set; } = [];
    /// <summary>The vampiric acts that earned each Corruption point (max 4; XP unlocks at 4).</summary>
    public List<string> CorruptionMarks { get; set; } = [];
    public List<string> Equipment { get; set; } = [];
    public List<XpLedgerEntry> Experience { get; set; } = [];
    /// <summary>What a draft player character buys with the campaign's starting XP, in order. Applied when it's approved.</summary>
    public List<StartingXpPurchase> StartingXpPurchases { get; set; } = [];

    [StringLength(4000)] public string? Notes { get; set; }
    [StringLength(4000)] public string? GmNotes { get; set; }

    [StringLength(CharacterProfile.MaxDescriptionLength)] public string? Appearance { get; set; }
    [StringLength(CharacterProfile.MaxDescriptionLength)] public string? Reputation { get; set; }
    [StringLength(CharacterProfile.MaxBackstoryLength)] public string? Backstory { get; set; }
    public string? PortraitUrl { get; set; }
    public bool IsShared { get; set; }
    public NpcReveal Reveals { get; set; }

    /// <summary>
    /// Set by the server. Scripts, Milestones, Corruption and XP belong to player characters; a full-sheet
    /// NPC has none, and they're cleared when one is saved.
    /// </summary>
    public bool IsPlayerCharacter { get; set; }

    public Guid Version { get; set; }

    public bool IsVampire => Kind == CharacterKind.Vampire;

    public int Attribute(CharacterAttribute attribute) => Attributes.GetValueOrDefault(attribute, Traits.MinAttribute);
    public int SkillDots(Skill skill) => Skills.GetValueOrDefault(skill, Traits.MinSkill);

    public int Health => DerivedStats.Health(Attribute(CharacterAttribute.Stamina), Size);
    public int Defense => DerivedStats.Defense(Attribute(CharacterAttribute.Wits), Attribute(CharacterAttribute.Dexterity));
    public int ExperienceEarned => Experience.Where(e => e.Amount > 0).Sum(e => e.Amount);
    public int ExperienceSpent => -Experience.Where(e => e.Amount < 0).Sum(e => e.Amount);
    public int ExperienceAvailable => ExperienceEarned - ExperienceSpent;

    /// <summary>
    /// The four Scripts in order: VtR's for vampires (p.136); for mortals and ghouls a Quirk and a Personal
    /// Script (WoD Core p.66). Slots 3 and 4 are the campaign's Coterie Script and Chronicle Tenet either way.
    /// </summary>
    public static List<ScriptEntry> DefaultScripts(CharacterKind kind = CharacterKind.Vampire) =>
    [
        new() { Slot = 1, Kind = kind == CharacterKind.Vampire ? ScriptKind.Humanity : ScriptKind.Quirk },
        new() { Slot = 2, Kind = kind == CharacterKind.Vampire ? ScriptKind.Beast : ScriptKind.Personal },
        new() { Slot = 3, Kind = ScriptKind.Coterie },
        new() { Slot = 4, Kind = ScriptKind.ChronicleTenet },
    ];

    public bool IsGhoul => Kind == CharacterKind.Ghoul;

    /// <summary>Size of the Vitae track: by Blood Potency for vampires, 8 for ghouls, null for mortals.</summary>
    public int? VitaeCapacity => GhoulRules.VitaeCapacity(Kind, BloodPotency, IsThinblood);

    /// <summary>Most Fortify Form points in one physical Discipline (by its capping attribute).</summary>
    public int FortifyCap(FortifyDiscipline discipline) => GhoulRules.FortifyCap(Kind, Attribute(Traits.FortifyCap(discipline)));

    /// <summary>
    /// Switches between Humanity (vampires) and Morality (everyone else) and clears values the kind
    /// doesn't have. Ghouls keep Vitae and Fortify Form (VtR p.62).
    /// </summary>
    public void NormalizeForKind()
    {
        if (!IsGhoul) DomitorId = null;
        if (!IsGhoul) DomitorName = null;
        if (IsVampire)
        {
            Humanity ??= DerivedStats.StartingHumanity(Morality ?? DerivedStats.StartingMorality);
            Morality = null;
            Generation ??= GenerationRules.StartingGeneration;
            BloodPotency ??= BloodRules.MinBloodPotency;
            VitaeCurrent ??= BloodRules.MaxVitae(BloodPotency.Value, IsThinblood);
            return;
        }

        Morality ??= Humanity is { } humanity ? Math.Min(10, humanity + 1) : DerivedStats.StartingMorality;
        Humanity = null;
        SireId = null;
        BloodlineId = null;
        PredatorTypeId = null;
        Generation = null;
        BloodPotency = null;
        IsThinblood = false;
        if (IsGhoul)
        {
            VitaeCurrent = Math.Clamp(VitaeCurrent ?? GhoulRules.MaxVitae, 0, GhoulRules.MaxVitae);
            PotenceBoost = Math.Min(PotenceBoost, FortifyCap(FortifyDiscipline.Potence));
            CelerityBoost = Math.Min(CelerityBoost, FortifyCap(FortifyDiscipline.Celerity));
            FortitudeBoost = Math.Min(FortitudeBoost, FortifyCap(FortifyDiscipline.Fortitude));
        }
        else
        {
            VitaeCurrent = null;
            PotenceBoost = CelerityBoost = FortitudeBoost = 0;
        }
        Disciplines.Clear();
        LearnedPowerIds.Clear();
        CorruptionMarks.Clear();
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartingXpPurchases.Count > MaxStartingXpPurchases)
            yield return Error($"At most {MaxStartingXpPurchases} starting XP purchases.", nameof(StartingXpPurchases));
        foreach (var attribute in Enum.GetValues<CharacterAttribute>())
            if (Attribute(attribute) is < Traits.MinAttribute or > Traits.MaxAttribute)
                yield return Error($"{attribute} must be {Traits.MinAttribute}–{Traits.MaxAttribute}.", nameof(Attributes));
        foreach (var skill in Enum.GetValues<Skill>())
            if (SkillDots(skill) is < Traits.MinSkill or > Traits.MaxSkill)
                yield return Error($"{skill} must be {Traits.MinSkill}–{Traits.MaxSkill}.", nameof(Skills));

        foreach (var specialty in Specialties)
        {
            if (string.IsNullOrWhiteSpace(specialty.Name) || specialty.Name.Length > MaxTextLength)
                yield return Error("Each specialty needs a short name.", nameof(Specialties));
            else if (SkillDots(specialty.Skill) < 1)
                yield return Error($"{specialty.Name}: specialties need at least 1 dot in {specialty.Skill}.", nameof(Specialties));
        }
        if (Specialties.GroupBy(s => (s.Skill, s.Name.Trim().ToUpperInvariant())).Any(g => g.Count() > 1))
            yield return Error("Each specialty can only be listed once per skill.", nameof(Specialties));

        if (WillpowerCurrent > WillpowerDots)
            yield return Error("Willpower points can't exceed Willpower dots.", nameof(WillpowerCurrent));

        if (IsVampire && BloodPotency is { } bp && VitaeCurrent is { } vitae && vitae > BloodRules.MaxVitae(bp, IsThinblood))
            yield return Error($"Vitae can't exceed {BloodRules.MaxVitae(bp, IsThinblood)} at Blood Potency {bp}.", nameof(VitaeCurrent));
        if (IsGhoul && VitaeCurrent > GhoulRules.MaxVitae)
            yield return Error($"A ghoul holds at most {GhoulRules.MaxVitae} Vitae.", nameof(VitaeCurrent));

        foreach (var (discipline, boost, member) in new[]
                 {
                     (FortifyDiscipline.Potence, PotenceBoost, nameof(PotenceBoost)),
                     (FortifyDiscipline.Celerity, CelerityBoost, nameof(CelerityBoost)),
                     (FortifyDiscipline.Fortitude, FortitudeBoost, nameof(FortitudeBoost)),
                 })
        {
            // Mortals' boosts are simply cleared on save.
            if (boost <= FortifyCap(discipline) || !GhoulRules.HoldsVitae(Kind)) continue;
            yield return Error(IsGhoul && boost > GhoulRules.MaxFortifyBoost
                ? $"A ghoul gains at most {GhoulRules.MaxFortifyBoost} {discipline} from Vitae."
                : $"{discipline} from Fortify Form can't exceed {Traits.FortifyCap(discipline)}.", member);
        }

        if (Disciplines.Any(d => d.Dots is < 0 or > PowerRules.MaxDisciplineDots))
            yield return Error($"Discipline dots must be 0–{PowerRules.MaxDisciplineDots}.", nameof(Disciplines));
        if (Disciplines.GroupBy(d => d.PowerGroupId).Any(g => g.Count() > 1))
            yield return Error("Each Discipline can only be listed once.", nameof(Disciplines));

        if (Merits.Any(m => string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > MaxTextLength))
            yield return Error("Each merit and flaw needs a name.", nameof(Merits));
        if (Merits.Any(m => m.Dots is < 0 or > 10))
            yield return Error("Merit and flaw ratings must be 0–10.", nameof(Merits));

        if (Wounds.Any(w => string.IsNullOrWhiteSpace(w.Description) || w.Description.Length > MaxTextLength))
            yield return Error("Describe each wound briefly (e.g. 'gunshot').", nameof(Wounds));
        if (Wounds.Any(w => w.Amount is < 0 or > 50))
            yield return Error("Wound damage must be 0–50.", nameof(Wounds));

        if (Compels.Any(c => string.IsNullOrWhiteSpace(c.Name) || c.Rating is < 0 or > 10))
            yield return Error("Each Compel needs a name and a rating of 0–10.", nameof(Compels));

        if (Scripts.Count > 4 || Scripts.Select(s => s.Slot).Distinct().Count() != Scripts.Count || Scripts.Any(s => s.Slot is < 1 or > 4))
            yield return Error("Scripts use slots 1–4, once each.", nameof(Scripts));

        if (Relationships.Any(r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > MaxTextLength || r.Description?.Length > 500))
            yield return Error("Each relationship needs a name (and at most 500 characters about them).", nameof(Relationships));

        if (Milestones.Any(m => string.IsNullOrWhiteSpace(m.Title)))
            yield return Error("Each Milestone needs a title.", nameof(Milestones));

        if (CorruptionMarks.Count > MaxCorruption)
            yield return Error($"Corruption tops out at {MaxCorruption}; spend XP to clear it.", nameof(CorruptionMarks));

        if (Experience.Any(e => string.IsNullOrWhiteSpace(e.Reason)))
            yield return Error("Each XP entry needs a reason.", nameof(Experience));

        foreach (var (list, member) in new[] { (Stains, nameof(Stains)), (CorruptionMarks, nameof(CorruptionMarks)), (Equipment, nameof(Equipment)), (Conditions, nameof(Conditions)) })
            if (list.Any(i => string.IsNullOrWhiteSpace(i) || i.Length > MaxTextLength))
                yield return Error("List entries can't be blank or longer than 200 characters.", member);
    }

    private static ValidationResult Error(string message, string member) => new(message, [member]);
}

/// <summary>Sent to promote a Supporting Cast NPC; carries the version the GM last saw.</summary>
public sealed record PromoteRequest(Guid Version);
