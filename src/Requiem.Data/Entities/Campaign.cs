using Requiem.Shared.Campaigns;

namespace Requiem.Data.Entities;

/// <summary>
/// A chronicle the GM runs with players (Phase 2, PHASE2.md §3). The database refuses to delete a
/// campaign that still has characters or scenes, but EF would quietly detach any it has loaded, so
/// code that deletes a campaign must check for them first (or archive instead).
/// </summary>
public class Campaign
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    /// <summary>The fourth Script, set by the Storyteller for every character (VtR p.136).</summary>
    public string? ChronicleTenet { get; set; }
    /// <summary>The third Script, agreed by the players and stored once (PHASE2.md decision 7).</summary>
    public string? CoterieScript { get; set; }
    public CharacterTemplates AllowedTemplates { get; set; } = CharacterTemplates.Vampire;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public List<CampaignMember> Members { get; set; } = [];
    public List<CampaignInvite> Invites { get; set; } = [];
}

public class CampaignMember
{
    public Guid CampaignId { get; set; }
    public required string UserId { get; set; }
    public RequiemUser User { get; set; } = null!;
    public CampaignRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>A join link (<c>/join/{token}</c>). Only a hash of the token is stored.</summary>
public class CampaignInvite
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    /// <summary>SHA-256 of the token, hex-encoded.</summary>
    public required string TokenHash { get; set; }
    public CampaignRole Role { get; set; } = CampaignRole.Player;
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Null for unlimited uses until it expires.</summary>
    public int? UsesLeft { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Who changed what. Player actions take effect at once, and this log (with undo data) is how the
/// Storyteller reviews and reverses them (PHASE2.md §8). Rows outlive the characters they mention.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? CharacterId { get; set; }
    public required string UserId { get; set; }
    /// <summary>A short machine-readable name, e.g. "xp.spend" or "link.add".</summary>
    public required string Action { get; set; }
    public required string Summary { get; set; }
    /// <summary>JSON describing how to reverse the change, when it can be undone.</summary>
    public string? UndoData { get; set; }
    public DateTimeOffset At { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
}

/// <summary>A Link earned with other player characters; 4 unlock XP for mortals and ghouls (WoD Core p.75).</summary>
public class CharacterLink
{
    public int Id { get; set; }
    public Guid CharacterId { get; set; }
    public LinkKind Kind { get; set; }
    /// <summary>The other player characters in the scene.</summary>
    public List<Guid> WithCharacterIds { get; set; } = [];
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}
