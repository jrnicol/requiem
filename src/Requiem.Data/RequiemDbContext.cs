using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data;

public class RequiemDbContext(DbContextOptions<RequiemDbContext> options, TimeProvider? timeProvider = null)
    : IdentityDbContext<RequiemUser>(options), IDataProtectionKeyContext
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Catalog
    public DbSet<Bloodline> Bloodlines => Set<Bloodline>();
    public DbSet<PredatorType> PredatorTypes => Set<PredatorType>();
    public DbSet<MeritDefinition> MeritDefinitions => Set<MeritDefinition>();
    public DbSet<CompelDefinition> CompelDefinitions => Set<CompelDefinition>();
    public DbSet<EdgeDefinition> EdgeDefinitions => Set<EdgeDefinition>();
    public DbSet<PowerGroup> PowerGroups => Set<PowerGroup>();
    public DbSet<Power> Powers => Set<Power>();

    // Characters
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<CharacterRelationship> Relationships => Set<CharacterRelationship>();
    public DbSet<BloodBond> BloodBonds => Set<BloodBond>();
    public DbSet<Boon> Boons => Set<Boon>();

    // Table play
    public DbSet<Scene> Scenes => Set<Scene>();

    // Campaigns (Phase 2)
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignMember> CampaignMembers => Set<CampaignMember>();
    public DbSet<CampaignInvite> CampaignInvites => Set<CampaignInvite>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>
    /// ASP.NET Core's key ring (it signs the sign-in cookie). Kept in the database because the hosted app
    /// scales to zero and its disk doesn't survive, which would otherwise sign everyone out.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's users, roles, claims and logins (AspNet* tables).
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RequiemDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store enums as text so the database reads clearly and enum members can be reordered safely.
        // NpcEdge is a [Flags] enum and stays numeric.
        builder.Properties<BloodlineKind>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<MeritKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<RuleSource>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<CompelKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<PowerGroupKind>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<CharacterAttribute>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<Skill>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<CharacterTier>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<CharacterKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<DamageType>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<ScriptKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<MilestoneStage>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<ItemKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<PurchaseKind>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<RelationshipKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<CampaignRole>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<CharacterStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<NpcVisibility>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<LinkKind>().HaveConversion<string>().HaveMaxLength(16);
        // CharacterTemplates is a [Flags] enum and stays numeric, like NpcEdge.

        // SQLite can't compare or sort DateTimeOffset values; store them as sortable integers there.
        if (Database.IsSqlite())
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCharacters();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCharacters();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Deletes a character and everything that points at it: its childer lose their Sire link (and ghouls their domitor),
    /// and relationships, blood bonds and boons involving it are removed.
    /// Prefer archiving (<see cref="Character.IsArchived"/>) for NPCs you might bring back.
    /// </summary>
    /// <returns>False if no character has that id.</returns>
    public Task<bool> DeleteCharacterAsync(Guid id, CancellationToken cancellationToken = default) =>
        // The execution strategy retries the whole transaction on transient Azure SQL failures.
        Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);

            await Characters.Where(c => c.SireId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SireId, (Guid?)null), ct);
            await Characters.Where(c => c.DomitorId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DomitorId, (Guid?)null), ct);
            await Relationships.Where(r => r.FromCharacterId == id || r.ToCharacterId == id)
                .ExecuteDeleteAsync(ct);
            await BloodBonds.Where(b => b.ThrallId == id || b.RegnantId == id)
                .ExecuteDeleteAsync(ct);
            await Boons.Where(b => b.DebtorId == id || b.CreditorId == id)
                .ExecuteDeleteAsync(ct);
            var deleted = await Characters.Where(c => c.Id == id).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
            return deleted > 0;
        }, cancellationToken);

    /// <summary>
    /// Moves a tracked character to another campaign and saves pending changes (e.g. audit entries) with it.
    /// Sire and domitor links don't cross campaigns, so the move cuts them both ways.
    /// </summary>
    public Task MoveCharacterAsync(Character character, Guid campaignId, CancellationToken cancellationToken = default) =>
        Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);

            character.CampaignId = campaignId;
            character.SireId = null;
            character.DomitorId = null;
            Entry(character).Property(c => c.UpdatedAt).IsModified = true;
            await SaveChangesAsync(ct);
            await Characters.Where(c => c.SireId == character.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SireId, (Guid?)null), ct);
            await Characters.Where(c => c.DomitorId == character.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DomitorId, (Guid?)null), ct);

            await transaction.CommitAsync(ct);
        }, cancellationToken);

    private void StampCharacters()
    {
        var now = _time.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Character>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty) entry.Entity.Id = Guid.CreateVersion7(now);
                entry.Entity.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version = Guid.NewGuid();
            }
        }
    }
}
