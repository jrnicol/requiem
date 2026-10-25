using Requiem.Shared.Characters;

namespace Requiem.Data.Entities;

// Links between characters. Deleting a character removes links to it (DynamoCharacterStore.DeleteAsync).

public enum RelationshipKind { Ally, Rival, Enemy, Lover, Family, Friend, Mentor, Contact, Retainer, Other }

/// <summary>
/// A directed link from one character to another character, or to someone not in the app
/// (ToCharacterId null, ToName set).
/// </summary>
public class CharacterRelationship
{
    public int Id { get; set; }
    public Guid FromCharacterId { get; set; }
    public Character FromCharacter { get; set; } = null!;
    public Guid? ToCharacterId { get; set; }
    public Character? ToCharacter { get; set; }
    public string? ToName { get; set; }
    public RelationshipKind Kind { get; set; }
    /// <summary>Which of the three starting relationships this is (WoD Core p.65), or Other.</summary>
    public RelationshipRole Role { get; set; } = RelationshipRole.Other;
    public string? Description { get; set; }
}
