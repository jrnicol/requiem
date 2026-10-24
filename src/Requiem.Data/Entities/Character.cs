using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data.Entities;

public class Character
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public CharacterTier Tier { get; set; }
    public CharacterKind Kind { get; set; }

    // Identity
    public string? Concept { get; set; }
    public string? Virtue { get; set; }
    public string? VirtueDetail { get; set; }
    public string? Vice { get; set; }
    public string? ViceDetail { get; set; }
    /// <summary>Supporting Cast: drives how the NPC approaches situations.</summary>
    public string? Ambition { get; set; }
    public string? Faction { get; set; }
    public string? PortraitUrl { get; set; }
    public string? Notes { get; set; }
    /// <summary>Never shown to players once campaigns exist (Phase 2).</summary>
    public string? GmNotes { get; set; }

    // Supporting Cast stat block (null on full sheets)
    public int? Physical { get; set; }
    public int? Mental { get; set; }
    public int? Social { get; set; }
    public NpcEdge Edges { get; set; }

    // Full sheet scalars
    public int Size { get; set; } = DerivedStats.DefaultSize;
    /// <summary>Stored, not derived: set from Wits + Resolve at creation only.</summary>
    public int? WillpowerDots { get; set; }
    public int? WillpowerCurrent { get; set; }
    public int? Humanity { get; set; }
    public int? Morality { get; set; }

    // Vampire
    public int? BloodlineId { get; set; }
    public Bloodline? Bloodline { get; set; }
    public int? PredatorTypeId { get; set; }
    public PredatorType? PredatorType { get; set; }
    public int? Generation { get; set; }
    public int? BloodPotency { get; set; }
    public int? VitaeCurrent { get; set; }
    public bool IsThinblood { get; set; }
    public string? BeastDescription { get; set; }
    public Guid? SireId { get; set; }
    public Character? Sire { get; set; }
    public List<Character> Childer { get; set; } = [];

    // Scene state (cleared or reset by the app)
    public int PotenceBoost { get; set; }
    public int CelerityBoost { get; set; }
    public int FortitudeBoost { get; set; }
    public bool IsInTorpor { get; set; }
    public bool IsStaked { get; set; }
    public bool IsMasquerading { get; set; }
    public bool IsUnderDominion { get; set; }

    // Ownership and lifecycle. Owner and campaign stay null until Phase 2.
    public string? OwnerUserId { get; set; }
    public Guid? CampaignId { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Optimistic concurrency token, regenerated on every save.</summary>
    public Guid Version { get; set; }

    public List<CharacterAttributeRating> Attributes { get; set; } = [];
    public List<CharacterSkillRating> Skills { get; set; } = [];
    public List<CharacterSpecialty> Specialties { get; set; } = [];
    public List<CharacterDiscipline> Disciplines { get; set; } = [];
    public List<CharacterPower> LearnedPowers { get; set; } = [];
    public List<CharacterMerit> Merits { get; set; } = [];
    public List<CharacterWound> Wounds { get; set; } = [];
    public List<CharacterStain> Stains { get; set; } = [];
    public List<CharacterCompel> Compels { get; set; } = [];
    public List<CharacterCondition> Conditions { get; set; } = [];
    public List<CharacterScript> Scripts { get; set; } = [];
    public List<CharacterMilestone> Milestones { get; set; } = [];
    public List<CorruptionMark> CorruptionMarks { get; set; } = [];
    public List<CharacterItem> Items { get; set; } = [];
    public List<XpEntry> XpLedger { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
}
