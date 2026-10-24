namespace Requiem.Data.Entities;

/// <summary>A set of NPCs the GM runs together at the table, e.g. "Elysium at the Orpheum".</summary>
public class Scene
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>The GM who made a personal scene; null on scenes made before Phase 2 (shared by site GMs).</summary>
    public string? OwnerUserId { get; set; }
    /// <summary>Set for a campaign's scenes, which its Storytellers share.</summary>
    public Guid? CampaignId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<SceneMember> Members { get; set; } = [];
}

public class SceneMember
{
    public Guid SceneId { get; set; }
    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = null!;
    public int SortOrder { get; set; }
}
