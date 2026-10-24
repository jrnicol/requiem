namespace Requiem.Data.Entities;

/// <summary>A set of NPCs a Storyteller runs together at the table, e.g. "Elysium at the Orpheum".</summary>
public class Scene
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>The Storyteller who made it; null on scenes made before Phase 2.</summary>
    public string? OwnerUserId { get; set; }
    /// <summary>
    /// The campaign whose Storytellers share it. Null only on data from before campaigns, which
    /// <see cref="Seeding.AccountsUpgrade"/> moves into one at startup.
    /// </summary>
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
