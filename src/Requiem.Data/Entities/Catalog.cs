using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data.Entities;

// Rulebook reference data. Seeded from the house rulebooks and editable by the GM.

public enum BloodlineKind { Bloodline, MagiHouse, Caitiff }

public class Bloodline
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public BloodlineKind Kind { get; set; }
    public string? WeaknessName { get; set; }
    public string? WeaknessText { get; set; }
    public string? CompelName { get; set; }
    public string? CompelText { get; set; }
    public int? SourcePage { get; set; }

    /// <summary>The Bloodline's own Discipline (Caitiff have none).</summary>
    public List<PowerGroup> PowerGroups { get; set; } = [];
}

public class PredatorType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Restrictions { get; set; }
    public int? SourcePage { get; set; }
}

public class MeritDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public MeritKind Kind { get; set; }
    public RuleSource Source { get; set; }

    /// <summary>Allowed ratings, e.g. "2 or 4" or "1–5"; free text because the books vary.</summary>
    public string? Cost { get; set; }
    public int? MinDots { get; set; }
    public int? MaxDots { get; set; }
    public string? Description { get; set; }
    /// <summary>Prerequisites and exclusions, e.g. "Generation 8 or lower and Blood Potency 6+".</summary>
    public string? Restrictions { get; set; }
    public int? SourcePage { get; set; }
}

public class CompelDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public CompelKind Kind { get; set; }
    /// <summary>d10 result for Base Compels (10 is the 0 face).</summary>
    public int? Roll { get; set; }
    /// <summary>Default rating, or how it's set (e.g. "Blood Potency", "Hunger dice").</summary>
    public string? Rating { get; set; }
    /// <summary>What imposes it, for Rule compels.</summary>
    public string? Trigger { get; set; }
    public string? EndsWhen { get; set; }
    public string? Description { get; set; }
    public RuleSource Source { get; set; }
    public int? SourcePage { get; set; }
}

/// <summary>Supporting Cast Edges (WoD Core p.117).</summary>
public class EdgeDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>The rules-engine flag when the Edge has a mechanical effect there.</summary>
    public NpcEdge? Edge { get; set; }
    public string? Description { get; set; }
    public int? SourcePage { get; set; }
}

/// <summary>A Discipline, Blood Sorcery house, Sabbat Ritae order, base powers, or ghoul powers.</summary>
public class PowerGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public PowerGroupKind Kind { get; set; }
    public int? BloodlineId { get; set; }
    public Bloodline? Bloodline { get; set; }
    public string? Description { get; set; }
    public int? SourcePage { get; set; }

    public List<Power> Powers { get; set; } = [];
}

public class Power
{
    public int Id { get; set; }
    public int PowerGroupId { get; set; }
    public PowerGroup PowerGroup { get; set; } = null!;

    /// <summary>Dot level for cumulative Disciplines; null for individually learned rituals and ritae.</summary>
    public int? Level { get; set; }
    public required string Name { get; set; }
    /// <summary>The sub-heading a ritual is listed under, e.g. "Chantry" or Necromancy's "Animus".</summary>
    public string? Category { get; set; }
    public string? Flavor { get; set; }

    public int? VitaeCost { get; set; }
    public int? WillpowerCost { get; set; }
    /// <summary>The cost line exactly as printed, for anything the numbers can't express.</summary>
    public string? CostText { get; set; }

    public string? DicePoolText { get; set; }
    public CharacterAttribute? DicePoolAttribute { get; set; }
    public Skill? DicePoolSkill { get; set; }
    /// <summary>The Discipline added to the pool, usually this power's own group.</summary>
    public int? DicePoolPowerGroupId { get; set; }
    public string? OpposedBy { get; set; }

    public string? ActionText { get; set; }
    public string? Duration { get; set; }
    public string? CastingDuration { get; set; }
    public string? HowToLearn { get; set; }

    public string? DramaticFailureText { get; set; }
    public string? FailureText { get; set; }
    public string? SuccessText { get; set; }
    public string? ExceptionalSuccessText { get; set; }
    /// <summary>Everything else the book says: sidebars, extended rolls, tables, clarifications.</summary>
    public string? Notes { get; set; }

    public int? SourcePage { get; set; }
    /// <summary>False until the GM checks the imported text against the book.</summary>
    public bool IsReviewed { get; set; }

    public List<PowerBonusOption> BonusOptions { get; set; } = [];
}

/// <summary>"Bonus Successes: +N → effect" rows on a power.</summary>
public class PowerBonusOption
{
    public int Id { get; set; }
    public int PowerId { get; set; }
    public int SuccessCost { get; set; }
    public required string Effect { get; set; }
    public int SortOrder { get; set; }
}
