using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requiem.Data.Entities;

namespace Requiem.Data.Configurations;

// Each link has two foreign keys to Character. SQL Server forbids two cascade paths to the same
// table, so these are NO ACTION and RequiemDbContext.DeleteCharacterAsync removes them explicitly.

internal sealed class CharacterRelationshipConfiguration : IEntityTypeConfiguration<CharacterRelationship>
{
    public void Configure(EntityTypeBuilder<CharacterRelationship> b)
    {
        b.Property(x => x.ToName).HaveNameLength();
        b.HasOne(x => x.FromCharacter).WithMany(x => x.Relationships).HasForeignKey(x => x.FromCharacterId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ToCharacter).WithMany().HasForeignKey(x => x.ToCharacterId).OnDelete(DeleteBehavior.NoAction);
        b.ToTable(t => t.HasCheckConstraint("CK_CharacterRelationship_Target",
            "ToCharacterId IS NOT NULL OR ToName IS NOT NULL"));
    }
}

internal sealed class BloodBondConfiguration : IEntityTypeConfiguration<BloodBond>
{
    public void Configure(EntityTypeBuilder<BloodBond> b)
    {
        b.HasOne(x => x.Thrall).WithMany().HasForeignKey(x => x.ThrallId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Regnant).WithMany().HasForeignKey(x => x.RegnantId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ThrallId, x.RegnantId }).IsUnique();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BloodBond_Stage", "Stage BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_BloodBond_NotSelf", "ThrallId <> RegnantId");
        });
    }
}

internal sealed class BoonConfiguration : IEntityTypeConfiguration<Boon>
{
    public void Configure(EntityTypeBuilder<Boon> b)
    {
        b.Property(x => x.Reason).HasMaxLength(Lengths.Short);
        b.Property(x => x.CashedInFor).HasMaxLength(Lengths.Short);
        b.HasOne(x => x.Debtor).WithMany().HasForeignKey(x => x.DebtorId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Creditor).WithMany().HasForeignKey(x => x.CreditorId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.DebtorId, x.CreditorId });
        b.ToTable(t => t.HasCheckConstraint("CK_Boon_NotSelf", "DebtorId <> CreditorId"));
    }
}
