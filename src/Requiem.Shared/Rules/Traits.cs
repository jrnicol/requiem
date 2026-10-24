namespace Requiem.Shared.Rules;

public enum TraitCategory { Mental, Physical, Social }

/// <summary>House-rules attributes (WoD Core p.76). Note Perception replaces Composure.</summary>
public enum CharacterAttribute
{
    Intelligence, Wits, Perception,
    Strength, Dexterity, Stamina,
    Charisma, Manipulation, Resolve,
}

/// <summary>House-rules skills (WoD Core p.80): 15 skills, five per category.</summary>
public enum Skill
{
    Academics, Crafts, Intrigue, Investigation, Technology,
    Athletics, Brawl, Ballistics, Stealth, Weaponry,
    Empathy, Expression, Persuasion, Streetwise, Subterfuge,
}

public static class Traits
{
    public const int MinAttribute = 1;
    public const int MaxAttribute = 5;
    public const int MinSkill = 0;
    public const int MaxSkill = 5;

    public static TraitCategory CategoryOf(CharacterAttribute attribute) => attribute switch
    {
        CharacterAttribute.Intelligence or CharacterAttribute.Wits or CharacterAttribute.Perception => TraitCategory.Mental,
        CharacterAttribute.Strength or CharacterAttribute.Dexterity or CharacterAttribute.Stamina => TraitCategory.Physical,
        _ => TraitCategory.Social,
    };

    public static TraitCategory CategoryOf(Skill skill) => skill switch
    {
        <= Skill.Technology => TraitCategory.Mental,
        <= Skill.Weaponry => TraitCategory.Physical,
        _ => TraitCategory.Social,
    };

    /// <summary>
    /// The physical disciplines boosted by Fortify Form, keyed to the attribute that caps them
    /// (VtR p.19): Potence/Strength, Celerity/Dexterity, Fortitude/Stamina.
    /// </summary>
    public static CharacterAttribute FortifyCap(FortifyDiscipline discipline) => discipline switch
    {
        FortifyDiscipline.Potence => CharacterAttribute.Strength,
        FortifyDiscipline.Celerity => CharacterAttribute.Dexterity,
        _ => CharacterAttribute.Stamina,
    };
}

public enum FortifyDiscipline { Potence, Celerity, Fortitude }
