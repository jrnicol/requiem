namespace Requiem.Data.Entities;

/// <summary>A set of NPCs the GM runs together at the table, e.g. "Elysium at the Orpheum".</summary>
public class Scene
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
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
