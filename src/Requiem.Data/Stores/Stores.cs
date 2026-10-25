using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;

namespace Requiem.Data.Stores;

// Storage behind the API (SERVERLESS.md). Endpoints talk to these, never to a database directly, so the
// implementation can move from EF Core to DynamoDB without touching them. Every write also commits the
// request's pending AuditBatch, so a change and its audit entries succeed or fail together.

/// <summary>The rules catalog: read-only for everyone but the site admin, and cached.</summary>
public interface ICatalogStore
{
    /// <summary>The whole catalog, with navigation properties (a power's group, a group's bloodline) filled in.</summary>
    Task<CatalogSnapshot> GetAsync(CancellationToken ct);

    /// <summary>A power to edit: a copy of its own, not the cached one, with its group and bonus options.</summary>
    Task<Power?> FindPowerAsync(int id, CancellationToken ct);

    /// <summary>Saves an admin's edit to a power from <see cref="FindPowerAsync"/>.</summary>
    Task SavePowerAsync(Power power, CancellationToken ct);
}

public sealed record CatalogSnapshot(
    IReadOnlyList<Bloodline> Bloodlines,
    IReadOnlyList<PredatorType> PredatorTypes,
    IReadOnlyList<MeritDefinition> Merits,
    IReadOnlyList<CompelDefinition> Compels,
    IReadOnlyList<EdgeDefinition> Edges,
    IReadOnlyList<ConceptDefinition> Concepts,
    IReadOnlyList<PowerGroup> PowerGroups,
    IReadOnlyList<Power> Powers)
{
    private Dictionary<int, Power>? _powers;
    private Dictionary<int, PowerGroup>? _groups;
    private Dictionary<int, Bloodline>? _bloodlines;

    public Power? Power(int id) => (_powers ??= Powers.ToDictionary(p => p.Id)).GetValueOrDefault(id);
    public PowerGroup? PowerGroup(int id) => (_groups ??= PowerGroups.ToDictionary(g => g.Id)).GetValueOrDefault(id);
    public Bloodline? Bloodline(int id) => (_bloodlines ??= Bloodlines.ToDictionary(b => b.Id)).GetValueOrDefault(id);
}

/// <summary>
/// Characters, each loaded and saved whole: the sheet with every list on it. Loaded characters have
/// <see cref="Character.Bloodline"/> and each discipline's <see cref="CharacterDiscipline.PowerGroup"/> set.
/// </summary>
public interface ICharacterStore
{
    Task<Character?> GetAsync(Guid id, CancellationToken ct);

    Task<List<Character>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<List<Character>> ListByCampaignAsync(Guid campaignId, CancellationToken ct);

    Task<int> CountByCampaignAsync(Guid campaignId, CancellationToken ct);

    /// <summary>Adds a new character, giving it an id (unless set), timestamps and a version.</summary>
    Task AddAsync(Character character, CancellationToken ct);

    /// <summary>
    /// Saves a loaded character if nobody else saved it since <paramref name="expectedVersion"/>
    /// (by default, the version it was loaded at). Gives it a new version.
    /// </summary>
    /// <returns>False when someone else saved it first; nothing is written.</returns>
    Task<bool> TrySaveAsync(Character character, Guid? expectedVersion, CancellationToken ct);

    /// <summary>Moves a loaded character to another campaign. Sire and domitor links don't cross campaigns, so they're cut both ways.</summary>
    Task MoveAsync(Character character, Guid campaignId, CancellationToken ct);

    /// <summary>Deletes a character; its childer lose their sire and its ghouls their domitor.</summary>
    /// <returns>False if there was no such character.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}

public static class CharacterStoreExtensions
{
    /// <summary>Saves a character that nobody else should be editing at the same moment (table play, XP).</summary>
    public static async Task SaveAsync(this ICharacterStore store, Character character, CancellationToken ct)
    {
        if (!await store.TrySaveAsync(character, null, ct))
            throw new InvalidOperationException($"Character {character.Id} was changed by someone else while it was being saved.");
    }
}

/// <summary>Campaigns with their members and invite links.</summary>
public interface ICampaignStore
{
    /// <summary>Every campaign the user belongs to, and their role in it.</summary>
    Task<IReadOnlyDictionary<Guid, CampaignRole>> RolesOfAsync(string userId, CancellationToken ct);

    /// <summary>The user's role in one campaign, read consistently (it may have been granted a moment ago).</summary>
    Task<CampaignRole?> RoleAsync(Guid campaignId, string userId, CancellationToken ct);

    Task<Campaign?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Adds a campaign together with its members (the Storyteller who started it).</summary>
    Task AddAsync(Campaign campaign, CancellationToken ct);

    /// <summary>Saves the campaign's own fields (not members or invites).</summary>
    Task SaveAsync(Campaign campaign, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);

    Task<List<CampaignMember>> MembersAsync(Guid campaignId, CancellationToken ct);

    Task AddMemberAsync(CampaignMember member, CancellationToken ct);

    Task RemoveMemberAsync(Guid campaignId, string userId, CancellationToken ct);

    Task<List<CampaignInvite>> InvitesAsync(Guid campaignId, CancellationToken ct);

    Task<CampaignInvite?> FindInviteAsync(string tokenHash, CancellationToken ct);

    Task AddInviteAsync(CampaignInvite invite, CancellationToken ct);

    Task SaveInviteAsync(CampaignInvite invite, CancellationToken ct);

    /// <summary>
    /// Takes one use of an invite atomically, so two people can't both take the last one, and adds the
    /// member in the same write.
    /// </summary>
    /// <returns>False when the invite is revoked, expired or used up; nothing is written.</returns>
    Task<bool> JoinWithInviteAsync(CampaignInvite invite, CampaignMember member, DateTimeOffset now, CancellationToken ct);
}

public interface ISceneStore
{
    Task<Scene?> GetAsync(Guid id, CancellationToken ct);

    Task<List<Scene>> ListByCampaignAsync(Guid campaignId, CancellationToken ct);

    Task<bool> AnyInCampaignAsync(Guid campaignId, CancellationToken ct);

    Task AddAsync(Scene scene, CancellationToken ct);

    Task SaveAsync(Scene scene, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IAuditStore
{
    Task<AuditEntry?> GetAsync(string id, CancellationToken ct);

    /// <summary>The character's most recent entries, newest first.</summary>
    Task<List<AuditEntry>> ForCharacterAsync(Guid characterId, int take, CancellationToken ct);

    /// <summary>Commits the pending audit entries on their own, for changes that aren't a store write.</summary>
    Task FlushAsync(CancellationToken ct);
}

/// <summary>What the API shows about other users.</summary>
public sealed record UserInfo(string Id, string DisplayName, string? Email);

public interface IUserDirectory
{
    Task<bool> AnyUsersAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<string, UserInfo>> GetAsync(IEnumerable<string> userIds, CancellationToken ct);
}

/// <summary>Brings the database up to date when the app starts (and after the first account exists).</summary>
public interface IDatabaseSetup
{
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Runs once the site's first account has been created.</summary>
    Task AfterFirstAccountAsync(CancellationToken ct);
}

/// <summary>
/// The audit entries a request has recorded but not yet saved. Store writes commit them with their own
/// change. An entry's undo data is worked out at commit time, once new items (an XP entry, a Link) have ids.
/// </summary>
public sealed class AuditBatch
{
    private readonly List<(AuditEntry Entry, Func<string?>? Undo)> _pending = [];
    private readonly List<AuditEntry> _undone = [];

    public void Add(AuditEntry entry, Func<string?>? undo = null) => _pending.Add((entry, undo));

    /// <summary>Marks an existing entry undone, saved with the next write.</summary>
    public void MarkUndone(AuditEntry entry) => _undone.Add(entry);

    public bool IsEmpty => _pending.Count == 0 && _undone.Count == 0;

    /// <summary>Takes the pending work, filling in each entry's undo data.</summary>
    public (List<AuditEntry> Added, List<AuditEntry> Undone) Take()
    {
        var added = _pending.Select(p =>
        {
            if (p.Undo is not null) p.Entry.UndoData = p.Undo();
            return p.Entry;
        }).ToList();
        var undone = _undone.ToList();
        _pending.Clear();
        _undone.Clear();
        return (added, undone);
    }
}
