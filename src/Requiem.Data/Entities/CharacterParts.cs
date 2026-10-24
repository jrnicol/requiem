using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data.Entities;

// Rows owned by one character. All cascade-delete with it.

public class CharacterAttributeRating
{
    public Guid CharacterId { get; set; }
    public CharacterAttribute Attribute { get; set; }
    public int Dots { get; set; } = Traits.MinAttribute;
}

public class CharacterSkillRating
{
    public Guid CharacterId { get; set; }
    public Skill Skill { get; set; }
    public int Dots { get; set; }
}

/// <summary>A Specialty on a skill, or a free-standing one on a Supporting Cast NPC (Skill null).</summary>
public class CharacterSpecialty
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public Skill? Skill { get; set; }
    public required string Name { get; set; }
}

/// <summary>Dots in a cumulative power group. Powers 1..Dots are implied, not stored.</summary>
public class CharacterDiscipline
{
    public Guid CharacterId { get; set; }
    public int PowerGroupId { get; set; }
    public PowerGroup PowerGroup { get; set; } = null!;
    public int Dots { get; set; }
}

/// <summary>An individually learned power: a Blood Sorcery ritual or Sabbat Ritae.</summary>
public class CharacterPower
{
    public Guid CharacterId { get; set; }
    public int PowerId { get; set; }
    public Power Power { get; set; } = null!;
    public string? Notes { get; set; }
}

/// <summary>A Merit or Flaw, from the catalog or custom (MeritDefinitionId null).</summary>
public class CharacterMerit
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public int? MeritDefinitionId { get; set; }
    public MeritDefinition? MeritDefinition { get; set; }
    public required string Name { get; set; }
    public MeritKind Kind { get; set; }
    public int Dots { get; set; }
    /// <summary>e.g. "Allies (Neighborhood Coalition)".</summary>
    public string? Qualifier { get; set; }
    public string? Notes { get; set; }
}

public class CharacterWound
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public required string Description { get; set; }
    public int Amount { get; set; }
    public DamageType Type { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>"No longer can…" (VtR p.14).</summary>
public class CharacterStain
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A short status noted during play, e.g. "Blinded" or "Shaken until sunrise".</summary>
public class CharacterCondition
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CharacterCompel
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public required string Name { get; set; }
    public int Rating { get; set; }
    /// <summary>When it ends, e.g. "until fed" or "until rests in a coffin".</summary>
    public string? EndsWhen { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}


public class CharacterScript
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    /// <summary>1–4.</summary>
    public int Slot { get; set; }
    public ScriptKind Kind { get; set; }
    public required string Text { get; set; }
    public bool IsMarked { get; set; }
}


public class CharacterMilestone
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public required string Title { get; set; }
    public string? Opportunity { get; set; }
    public string? Risk { get; set; }
    public string? Choice { get; set; }
    public string? Benefit { get; set; }
    public string? Notes { get; set; }
    public MilestoneStage Stage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>One Corruption point and the vampiric act that earned it (VtR p.137). Cleared when XP is spent.</summary>
public class CorruptionMark
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public required string Act { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ItemKind { Equipment, Asset }

/// <summary>Equipment on a full sheet, or an Asset (+2 dice) on a Supporting Cast NPC.</summary>
public class CharacterItem
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public ItemKind Kind { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

public class XpEntry
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    /// <summary>Positive when earned, negative when spent.</summary>
    public int Amount { get; set; }
    public required string Reason { get; set; }
    public PurchaseKind? PurchaseKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Character> Characters { get; set; } = [];
}
