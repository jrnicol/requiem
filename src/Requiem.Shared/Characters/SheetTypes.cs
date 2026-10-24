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

/// <summary>
/// VtR Scripts: Humanity, Beast, Coterie tie, Chronicle Tenet (p.136). Mortals and ghouls use the WoD Core
/// kinds (p.66): a Quirk (personality or habit), a Personal Script from their first drama, then the shared
/// Coterie Script and the Chronicle Tenet (house ruling 2026-10-24). PlayerCharacter is the book's per-player tie.
/// </summary>
public enum ScriptKind { Humanity, Beast, Coterie, ChronicleTenet, Personal, PlayerCharacter, Quirk }

/// <summary>The three people every new character starts with (WoD Core p.65).</summary>
public enum RelationshipRole
{
    /// <summary>Knows you for who you are and means a lot to you.</summary>
    Anchor,
    /// <summary>Complicates your life, and you can't escape them.</summary>
    Complication,
    /// <summary>Connects you to the chronicle's plot; picked by you from the Storyteller's list.</summary>
    Connection,
    /// <summary>Anyone added later.</summary>
    Other,
}

public enum MilestoneStage { NotStarted, Opportunity, Risk, Choice, Completed }
