using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Requiem.Data.Entities;
using Requiem.Data.Seeding;
using Requiem.Shared.Campaigns;

namespace Requiem.Data.Stores.Ef;

// The EF Core (SQLite / SQL Server) implementation of the stores, kept while the API moves onto the store
// interfaces; DynamoDB replaces it (SERVERLESS.md). Entities come back tracked, so saving is SaveChanges.

/// <summary>Commits the request's changes together with its pending audit entries, in one transaction.</summary>
internal sealed class EfUnitOfWork(RequiemDbContext db, AuditBatch audit)
{
    public RequiemDbContext Db => db;
    public AuditBatch Audit => audit;

    public Task CommitAsync(CancellationToken ct) =>
        // The execution strategy retries the whole transaction on transient Azure SQL failures.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // First save assigns ids that audit undo data refers to (an XP entry, a Link).
            await db.SaveChangesAsync(ct);
            if (!audit.IsEmpty)
            {
                var (added, _) = audit.Take(); // undone entries are tracked: their UndoneAt saves below
                db.AuditEntries.AddRange(added);
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        });
}

internal sealed class EfCatalogStore(EfUnitOfWork work) : ICatalogStore
{
    private RequiemDbContext Db => work.Db;

    public async Task<CatalogSnapshot> GetAsync(CancellationToken ct) => new(
        await Db.Bloodlines.AsNoTracking().ToListAsync(ct),
        await Db.PredatorTypes.AsNoTracking().ToListAsync(ct),
        await Db.MeritDefinitions.AsNoTracking().ToListAsync(ct),
        await Db.CompelDefinitions.AsNoTracking().ToListAsync(ct),
        await Db.EdgeDefinitions.AsNoTracking().ToListAsync(ct),
        await Db.ConceptDefinitions.AsNoTracking().ToListAsync(ct),
        await Db.PowerGroups.AsNoTracking().Include(g => g.Bloodline).ToListAsync(ct),
        await Db.Powers.AsNoTracking().Include(p => p.PowerGroup).Include(p => p.BonusOptions).AsSplitQuery().ToListAsync(ct));

    public Task<Power?> FindPowerAsync(int id, CancellationToken ct) =>
        Db.Powers.Include(p => p.BonusOptions).Include(p => p.PowerGroup).SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task SavePowerAsync(Power power, CancellationToken ct) => work.CommitAsync(ct);
}

internal sealed class EfCharacterStore(EfUnitOfWork work) : ICharacterStore
{
    private RequiemDbContext Db => work.Db;

    /// <summary>Every part of a character, plus the catalog entries the mappers read.</summary>
    private IQueryable<Character> Whole => Db.Characters
        .Include(c => c.Bloodline)
        .Include(c => c.Attributes).Include(c => c.Skills).Include(c => c.Specialties)
        .Include(c => c.Disciplines).ThenInclude(d => d.PowerGroup)
        .Include(c => c.LearnedPowers).Include(c => c.Merits).Include(c => c.Wounds)
        .Include(c => c.Stains).Include(c => c.Compels).Include(c => c.Conditions).Include(c => c.Scripts)
        .Include(c => c.Milestones).Include(c => c.CorruptionMarks).Include(c => c.Items)
        .Include(c => c.XpLedger).Include(c => c.Links).Include(c => c.Relationships)
        .AsSplitQuery();

    public Task<Character?> GetAsync(Guid id, CancellationToken ct) => Whole.SingleOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Character>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? Task.FromResult(new List<Character>()) : Whole.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

    public Task<List<Character>> ListByCampaignAsync(Guid campaignId, CancellationToken ct) =>
        Whole.Where(c => c.CampaignId == campaignId).ToListAsync(ct);

    public Task<int> CountByCampaignAsync(Guid campaignId, CancellationToken ct) =>
        Db.Characters.CountAsync(c => c.CampaignId == campaignId, ct);

    public Task AddAsync(Character character, CancellationToken ct)
    {
        Db.Characters.Add(character);
        return work.CommitAsync(ct);
    }

    public async Task<bool> TrySaveAsync(Character character, Guid? expectedVersion, CancellationToken ct)
    {
        var entry = Db.Entry(character);
        if (expectedVersion is { } version) entry.Property(c => c.Version).OriginalValue = version;
        // Always stamp the character, so changes only to its lists are concurrency-checked too.
        entry.Property(c => c.UpdatedAt).IsModified = true;
        try
        {
            await work.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task MoveAsync(Character character, Guid campaignId, CancellationToken ct)
    {
        await work.CommitAsync(ct); // the move's audit entries
        await Db.MoveCharacterAsync(character, campaignId, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await work.CommitAsync(ct); // the deletion's audit entries
        return await Db.DeleteCharacterAsync(id, ct);
    }
}

internal sealed class EfCampaignStore(EfUnitOfWork work) : ICampaignStore
{
    private RequiemDbContext Db => work.Db;

    public async Task<IReadOnlyDictionary<Guid, CampaignRole>> RolesOfAsync(string userId, CancellationToken ct) =>
        await Db.CampaignMembers.AsNoTracking().Where(m => m.UserId == userId).ToDictionaryAsync(m => m.CampaignId, m => m.Role, ct);

    public Task<Campaign?> GetAsync(Guid id, CancellationToken ct) => Db.Campaigns.SingleOrDefaultAsync(c => c.Id == id, ct);

    public Task AddAsync(Campaign campaign, CancellationToken ct)
    {
        Db.Campaigns.Add(campaign);
        return work.CommitAsync(ct);
    }

    public Task SaveAsync(Campaign campaign, CancellationToken ct) => work.CommitAsync(ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await work.CommitAsync(ct);
        await Db.Campaigns.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
    }

    public Task<List<CampaignMember>> MembersAsync(Guid campaignId, CancellationToken ct) =>
        Db.CampaignMembers.Where(m => m.CampaignId == campaignId).ToListAsync(ct);

    public Task AddMemberAsync(CampaignMember member, CancellationToken ct)
    {
        Db.CampaignMembers.Add(member);
        return work.CommitAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid campaignId, string userId, CancellationToken ct)
    {
        var member = await Db.CampaignMembers.SingleOrDefaultAsync(m => m.CampaignId == campaignId && m.UserId == userId, ct);
        if (member is not null) Db.CampaignMembers.Remove(member);
        await work.CommitAsync(ct);
    }

    public Task<List<CampaignInvite>> InvitesAsync(Guid campaignId, CancellationToken ct) =>
        Db.CampaignInvites.Where(i => i.CampaignId == campaignId).ToListAsync(ct);

    public Task<CampaignInvite?> FindInviteAsync(string tokenHash, CancellationToken ct) =>
        Db.CampaignInvites.SingleOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

    public Task AddInviteAsync(CampaignInvite invite, CancellationToken ct)
    {
        Db.CampaignInvites.Add(invite);
        return work.CommitAsync(ct);
    }

    public Task SaveInviteAsync(CampaignInvite invite, CancellationToken ct) => work.CommitAsync(ct);

    public Task<bool> JoinWithInviteAsync(CampaignInvite invite, CampaignMember member, DateTimeOffset now, CancellationToken ct) =>
        Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await Db.Database.BeginTransactionAsync(ct);
            var spent = await Db.CampaignInvites
                .Where(i => i.Id == invite.Id && i.RevokedAt == null && i.ExpiresAt > now && (i.UsesLeft == null || i.UsesLeft > 0))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsesLeft, i => i.UsesLeft == null ? null : i.UsesLeft - 1), ct);
            if (spent != 1) return false;

            Db.CampaignMembers.Add(member);
            await Db.SaveChangesAsync(ct);
            var (added, _) = work.Audit.Take();
            Db.AuditEntries.AddRange(added);
            await Db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
}

internal sealed class EfSceneStore(EfUnitOfWork work) : ISceneStore
{
    private RequiemDbContext Db => work.Db;

    public Task<Scene?> GetAsync(Guid id, CancellationToken ct) =>
        Db.Scenes.Include(s => s.Members).SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<Scene>> ListByCampaignAsync(Guid campaignId, CancellationToken ct) =>
        Db.Scenes.Include(s => s.Members).Where(s => s.CampaignId == campaignId).ToListAsync(ct);

    public Task<bool> AnyInCampaignAsync(Guid campaignId, CancellationToken ct) =>
        Db.Scenes.AnyAsync(s => s.CampaignId == campaignId, ct);

    public Task AddAsync(Scene scene, CancellationToken ct)
    {
        Db.Scenes.Add(scene);
        return work.CommitAsync(ct);
    }

    public Task SaveAsync(Scene scene, CancellationToken ct) => work.CommitAsync(ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await work.CommitAsync(ct);
        await Db.Scenes.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    }
}

internal sealed class EfAuditStore(EfUnitOfWork work) : IAuditStore
{
    private RequiemDbContext Db => work.Db;

    public async Task<AuditEntry?> GetAsync(string id, CancellationToken ct) =>
        long.TryParse(id, out var key) ? await Db.AuditEntries.SingleOrDefaultAsync(e => e.Id == key, ct) : null;

    public Task<List<AuditEntry>> ForCharacterAsync(Guid characterId, int take, CancellationToken ct) =>
        Db.AuditEntries.AsNoTracking().Where(e => e.CharacterId == characterId).OrderByDescending(e => e.Id).Take(take).ToListAsync(ct);

    public Task FlushAsync(CancellationToken ct) => work.CommitAsync(ct);
}

internal sealed class EfUserDirectory(RequiemDbContext db) : IUserDirectory
{
    public Task<bool> AnyUsersAsync(CancellationToken ct) => db.Users.AnyAsync(ct);

    public async Task<IReadOnlyDictionary<string, UserInfo>> GetAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserInfo(u.Id, u.DisplayName, u.Email), ct);
    }
}

internal sealed class EfDatabaseSetup(RequiemDbContext db, ILogger<EfDatabaseSetup> logger) : IDatabaseSetup
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
        var seeded = await CatalogSeeder.SeedAsync(db, ct);
        if (seeded.Total > 0) logger.LogInformation("Seeded catalog: {Result}", seeded);
        var upgraded = await AccountsUpgrade.RunAsync(db, ct: ct);
        if (upgraded.Changed) logger.LogInformation("Upgraded accounts and loose characters: {Result}", upgraded);
    }

    public Task AfterFirstAccountAsync(CancellationToken ct) => AccountsUpgrade.RunAsync(db, ct: ct);
}
