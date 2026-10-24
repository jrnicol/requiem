using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requiem.Data.Entities;

namespace Requiem.Data.Configurations;

internal sealed class SceneConfiguration : IEntityTypeConfiguration<Scene>, IEntityTypeConfiguration<SceneMember>
{
    public void Configure(EntityTypeBuilder<Scene> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.HasIndex(x => x.UpdatedAt);
        b.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.SceneId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<SceneMember> b)
    {
        b.ToTable("SceneMembers");
        b.HasKey(x => new { x.SceneId, x.CharacterId });
        // Deleting a character takes it out of every scene.
        b.HasOne(x => x.Character).WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Cascade);
    }
}
