namespace Requiem.Shared.Characters;

/// <summary>Which house rulebook an entry comes from.</summary>
public enum RuleSource
{
    /// <summary>World of Darkness Core (applies to everyone).</summary>
    WodCore,
    /// <summary>Vampire: The Requiem (vampires; overrides a same-named Core entry for them).</summary>
    Vampire,
}

public enum MeritKind { Merit, Flaw }

public enum CompelKind
{
    /// <summary>Rolled on the d10 Base Compels table (Bestial botch, Caitiff).</summary>
    Base,
    /// <summary>A fixed Compel a rule imposes, e.g. Sorrow 3 after a successful Stains roll.</summary>
    Rule,
}

/// <summary>VtR Scripts: Humanity, Beast, Coterie tie, Chronicle Tenet (p.136). Personal/PlayerCharacter are the WoD Core kinds.</summary>
public enum ScriptKind { Humanity, Beast, Coterie, ChronicleTenet, Personal, PlayerCharacter }

public enum MilestoneStage { NotStarted, Opportunity, Risk, Choice, Completed }
