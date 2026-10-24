using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requiem.Data.Entities;

namespace Requiem.Data.Configurations;

internal sealed class BloodlineConfiguration : IEntityTypeConfiguration<Bloodline>
{
    public void Configure(EntityTypeBuilder<Bloodline> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.WeaknessName).HasMaxLength(Lengths.Name);
        b.Property(x => x.CompelName).HasMaxLength(Lengths.Name);
    }
}

internal sealed class PredatorTypeConfiguration : IEntityTypeConfiguration<PredatorType>
{
    public void Configure(EntityTypeBuilder<PredatorType> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class MeritDefinitionConfiguration : IEntityTypeConfiguration<MeritDefinition>
{
    public void Configure(EntityTypeBuilder<MeritDefinition> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.Property(x => x.Cost).HasMaxLength(64);
        // Child and Damned exist in both books with different rules.
        b.HasIndex(x => new { x.Name, x.Kind, x.Source }).IsUnique();
    }
}

internal sealed class CompelDefinitionConfiguration : IEntityTypeConfiguration<CompelDefinition>
{
    public void Configure(EntityTypeBuilder<CompelDefinition> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.Property(x => x.Rating).HasMaxLength(64);
        b.Property(x => x.Trigger).HasMaxLength(Lengths.Short);
        b.Property(x => x.EndsWhen).HasMaxLength(Lengths.Name);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Roll).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_CompelDefinition_Roll", "Roll IS NULL OR Roll BETWEEN 1 AND 10"));
    }
}

internal sealed class ConceptDefinitionConfiguration : IEntityTypeConfiguration<ConceptDefinition>
{
    public void Configure(EntityTypeBuilder<ConceptDefinition> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.WillpowerTrigger).HasMaxLength(Lengths.Short);
    }
}

internal sealed class EdgeDefinitionConfiguration : IEntityTypeConfiguration<EdgeDefinition>
{
    public void Configure(EntityTypeBuilder<EdgeDefinition> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class PowerGroupConfiguration : IEntityTypeConfiguration<PowerGroup>
{
    public void Configure(EntityTypeBuilder<PowerGroup> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.HasIndex(x => x.Name).IsUnique();
        b.HasOne(x => x.Bloodline).WithMany(x => x.PowerGroups)
            .HasForeignKey(x => x.BloodlineId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PowerConfiguration : IEntityTypeConfiguration<Power>
{
    public void Configure(EntityTypeBuilder<Power> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        b.Property(x => x.Category).HasMaxLength(Lengths.Name);
        b.Property(x => x.CostText).HasMaxLength(Lengths.Short);
        b.Property(x => x.DicePoolText).HasMaxLength(Lengths.Short);
        b.Property(x => x.OpposedBy).HasMaxLength(Lengths.Name);
        b.Property(x => x.Duration).HasMaxLength(Lengths.Short);
        b.Property(x => x.CastingDuration).HasMaxLength(Lengths.Short);
        b.HasIndex(x => new { x.PowerGroupId, x.Name }).IsUnique();
        b.HasIndex(x => new { x.PowerGroupId, x.Level });
        b.ToTable(t => t.HasCheckConstraint("CK_Power_Level", "Level IS NULL OR Level BETWEEN 1 AND 5"));

        b.HasOne(x => x.PowerGroup).WithMany(x => x.Powers)
            .HasForeignKey(x => x.PowerGroupId).OnDelete(DeleteBehavior.Cascade);
        // Second path to PowerGroup: SQL Server forbids a second cascade.
        b.HasOne<PowerGroup>().WithMany()
            .HasForeignKey(x => x.DicePoolPowerGroupId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x => x.BonusOptions).WithOne()
            .HasForeignKey(x => x.PowerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PowerBonusOptionConfiguration : IEntityTypeConfiguration<PowerBonusOption>
{
    public void Configure(EntityTypeBuilder<PowerBonusOption> b) =>
        b.ToTable("PowerBonusOptions", t => t.HasCheckConstraint("CK_PowerBonusOption_SuccessCost", "SuccessCost >= 1"));
}
