using Requiem.Shared.Characters;

namespace Requiem.Data.Entities;

// Links between characters. Deleting a character removes its links (see RequiemDbContext).

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

/// <summary>Vinculum stages 1–3; stage 3 makes the drinker a Thrall (VtR p.20).</summary>
public class BloodBond
{
    public int Id { get; set; }
    public Guid ThrallId { get; set; }
    public Character Thrall { get; set; } = null!;
    public Guid RegnantId { get; set; }
    public Character Regnant { get; set; } = null!;
    public int Stage { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One Boon owed by a debtor to a creditor (VtR p.129).</summary>
public class Boon
{
    public int Id { get; set; }
    public Guid DebtorId { get; set; }
    public Character Debtor { get; set; } = null!;
    public Guid CreditorId { get; set; }
    public Character Creditor { get; set; } = null!;
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CashedInAt { get; set; }
    public string? CashedInFor { get; set; }
}
