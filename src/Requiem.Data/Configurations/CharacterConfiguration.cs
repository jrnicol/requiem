using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requiem.Data.Entities;

namespace Requiem.Data.Configurations;

internal sealed class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> b)
    {
        b.Property(x => x.Name).HaveNameLength();
        foreach (var name in new[] { nameof(Character.Concept), nameof(Character.Virtue), nameof(Character.VirtueDetail),
                     nameof(Character.Vice), nameof(Character.ViceDetail), nameof(Character.Faction) })
            b.Property<string?>(name).HasMaxLength(Lengths.Name);
        b.Property(x => x.Ambition).HasMaxLength(Lengths.Short);
        b.Property(x => x.BeastDescription).HasMaxLength(Lengths.Short);
        b.Property(x => x.PortraitUrl).HasMaxLength(Lengths.Url);
        b.Property(x => x.OwnerUserId).HasMaxLength(Lengths.UserId);
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasIndex(x => x.Name);
        b.HasIndex(x => new { x.IsArchived, x.Tier });
        b.HasIndex(x => x.CampaignId);
        b.HasIndex(x => x.OwnerUserId);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Character_Physical", "Physical IS NULL OR Physical BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Character_Mental", "Mental IS NULL OR Mental BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Character_Social", "Social IS NULL OR Social BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Character_BloodPotency", "BloodPotency IS NULL OR BloodPotency BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_Character_Humanity", "Humanity IS NULL OR Humanity BETWEEN 0 AND 10");
            t.HasCheckConstraint("CK_Character_Generation", "Generation IS NULL OR Generation BETWEEN 1 AND 13");
            t.HasCheckConstraint("CK_Character_WillpowerDots", "WillpowerDots IS NULL OR WillpowerDots BETWEEN 0 AND 10");
        });

        b.HasOne(x => x.Bloodline).WithMany().HasForeignKey(x => x.BloodlineId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.PredatorType).WithMany().HasForeignKey(x => x.PredatorTypeId).OnDelete(DeleteBehavior.SetNull);
        // SQL Server rejects ON DELETE SET NULL on a self-reference; DeleteCharacterAsync clears it instead.
        b.HasOne(x => x.Sire).WithMany(x => x.Childer).HasForeignKey(x => x.SireId).OnDelete(DeleteBehavior.ClientSetNull);
        // Same for a ghoul's domitor.
        b.HasOne(x => x.Domitor).WithMany().HasForeignKey(x => x.DomitorId).OnDelete(DeleteBehavior.ClientSetNull);
        b.Property(x => x.DomitorName).HasMaxLength(Lengths.Name);
        b.Property(x => x.ReviewNote).HasMaxLength(2000);
        b.Property(x => x.PublicDescription).HasMaxLength(Lengths.Short);
        // A campaign with characters can't be deleted, only archived.
        b.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Links).WithOne().HasForeignKey(x => x.CharacterId);

        b.HasMany(x => x.Attributes).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Skills).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Specialties).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Disciplines).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.LearnedPowers).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Merits).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Wounds).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Stains).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Compels).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Conditions).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Scripts).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Milestones).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.CorruptionMarks).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.XpLedger).WithOne().HasForeignKey(x => x.CharacterId);
        b.HasMany(x => x.Tags).WithMany(x => x.Characters).UsingEntity("CharacterTags");
    }
}

internal sealed class CharacterPartsConfiguration :
    IEntityTypeConfiguration<CharacterAttributeRating>,
    IEntityTypeConfiguration<CharacterSkillRating>,
    IEntityTypeConfiguration<CharacterSpecialty>,
    IEntityTypeConfiguration<CharacterDiscipline>,
    IEntityTypeConfiguration<CharacterPower>,
    IEntityTypeConfiguration<CharacterMerit>,
    IEntityTypeConfiguration<CharacterWound>,
    IEntityTypeConfiguration<CharacterStain>,
    IEntityTypeConfiguration<CharacterCompel>,
    IEntityTypeConfiguration<CharacterCondition>,
    IEntityTypeConfiguration<CharacterScript>,
    IEntityTypeConfiguration<CharacterMilestone>,
    IEntityTypeConfiguration<CorruptionMark>,
    IEntityTypeConfiguration<CharacterItem>,
    IEntityTypeConfiguration<XpEntry>,
    IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<CharacterAttributeRating> b)
    {
        b.HasKey(x => new { x.CharacterId, x.Attribute });
        b.ToTable("CharacterAttributes", t => t.HasCheckConstraint("CK_CharacterAttributeRating_Dots", "Dots BETWEEN 0 AND 10"));
    }

    public void Configure(EntityTypeBuilder<CharacterSkillRating> b)
    {
        b.HasKey(x => new { x.CharacterId, x.Skill });
        b.ToTable("CharacterSkills", t => t.HasCheckConstraint("CK_CharacterSkillRating_Dots", "Dots BETWEEN 0 AND 10"));
    }

    public void Configure(EntityTypeBuilder<CharacterSpecialty> b) => b.ToTable("CharacterSpecialties").Property(x => x.Name).HaveNameLength();

    public void Configure(EntityTypeBuilder<CharacterDiscipline> b)
    {
        b.HasKey(x => new { x.CharacterId, x.PowerGroupId });
        // Catalog entries in use can't be deleted out from under a character.
        b.HasOne(x => x.PowerGroup).WithMany().HasForeignKey(x => x.PowerGroupId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("CharacterDisciplines", t => t.HasCheckConstraint("CK_CharacterDiscipline_Dots", "Dots BETWEEN 0 AND 10"));
    }

    public void Configure(EntityTypeBuilder<CharacterPower> b)
    {
        b.ToTable("CharacterPowers");
        b.HasKey(x => new { x.CharacterId, x.PowerId });
        b.HasOne(x => x.Power).WithMany().HasForeignKey(x => x.PowerId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<CharacterMerit> b)
    {
        b.ToTable("CharacterMerits");
        b.Property(x => x.Name).HaveNameLength();
        b.Property(x => x.Qualifier).HasMaxLength(Lengths.Name);
        // The merit keeps its own copy of the name, so a deleted definition just unlinks it.
        b.HasOne(x => x.MeritDefinition).WithMany().HasForeignKey(x => x.MeritDefinitionId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<CharacterWound> b)
    {
        b.Property(x => x.Description).HaveNameLength();
        b.Property(x => x.Location).HasMaxLength(64);
        b.ToTable("CharacterWounds", t => t.HasCheckConstraint("CK_CharacterWound_Amount", "Amount >= 0"));
    }

    public void Configure(EntityTypeBuilder<CharacterStain> b) =>
        b.ToTable("CharacterStains").Property(x => x.Text).HasMaxLength(Lengths.Short);

    public void Configure(EntityTypeBuilder<CharacterCompel> b)
    {
        b.ToTable("CharacterCompels");
        b.Property(x => x.Name).HaveNameLength();
        b.Property(x => x.EndsWhen).HasMaxLength(Lengths.Name);
        b.Property(x => x.Source).HasMaxLength(Lengths.Name);
    }

    public void Configure(EntityTypeBuilder<CharacterCondition> b) =>
        b.ToTable("CharacterConditions").Property(x => x.Name).HaveNameLength();

    public void Configure(EntityTypeBuilder<CharacterScript> b)
    {
        b.Property(x => x.Text).HasMaxLength(Lengths.Short);
        b.HasIndex(x => new { x.CharacterId, x.Slot }).IsUnique();
        b.ToTable("CharacterScripts", t => t.HasCheckConstraint("CK_CharacterScript_Slot", "Slot BETWEEN 1 AND 4"));
    }

    public void Configure(EntityTypeBuilder<CharacterMilestone> b) => b.ToTable("CharacterMilestones").Property(x => x.Title).HaveNameLength();

    public void Configure(EntityTypeBuilder<CorruptionMark> b) => b.ToTable("CorruptionMarks").Property(x => x.Act).HaveNameLength();

    public void Configure(EntityTypeBuilder<CharacterItem> b) => b.ToTable("CharacterItems").Property(x => x.Name).HaveNameLength();

    public void Configure(EntityTypeBuilder<XpEntry> b)
    {
        b.ToTable("XpEntries");
        b.Property(x => x.Reason).HasMaxLength(Lengths.Short);
        b.Property(x => x.Trait).HasMaxLength(64);
        b.Property(x => x.RecordedByUserId).HasMaxLength(Lengths.UserId);
        b.HasIndex(x => new { x.CharacterId, x.CreatedAt });
    }

    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.Property(x => x.Name).HasMaxLength(64);
        b.HasIndex(x => x.Name).IsUnique();
    }
}
