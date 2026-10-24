using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requiem.Data.Entities;

namespace Requiem.Data.Configurations;

internal sealed class CampaignConfiguration :
    IEntityTypeConfiguration<Campaign>,
    IEntityTypeConfiguration<CampaignMember>,
    IEntityTypeConfiguration<CampaignInvite>,
    IEntityTypeConfiguration<AuditEntry>,
    IEntityTypeConfiguration<CharacterLink>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.Property(x => x.ChronicleTenet).HasMaxLength(Lengths.Short);
        b.Property(x => x.CoterieScript).HasMaxLength(Lengths.Short);
        b.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Invites).WithOne().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<CampaignMember> b)
    {
        b.ToTable("CampaignMembers");
        b.HasKey(x => new { x.CampaignId, x.UserId });
        b.Property(x => x.UserId).HasMaxLength(Lengths.UserId);
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<CampaignInvite> b)
    {
        b.ToTable("CampaignInvites");
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.CreatedByUserId).HasMaxLength(Lengths.UserId);
        b.ToTable(t => t.HasCheckConstraint("CK_CampaignInvite_UsesLeft", "UsesLeft IS NULL OR UsesLeft >= 0"));
    }

    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditEntries");
        b.Property(x => x.UserId).HasMaxLength(Lengths.UserId);
        b.Property(x => x.Action).HasMaxLength(64);
        b.Property(x => x.Summary).HasMaxLength(Lengths.Short);
        // No foreign keys on purpose: the log keeps its history after characters or campaigns are deleted.
        b.HasIndex(x => new { x.CampaignId, x.At });
        b.HasIndex(x => new { x.CharacterId, x.At });
    }

    public void Configure(EntityTypeBuilder<CharacterLink> b)
    {
        b.ToTable("CharacterLinks");
        b.Property(x => x.Note).HasMaxLength(Lengths.Short);
        b.HasIndex(x => new { x.CharacterId, x.At });
    }
}
