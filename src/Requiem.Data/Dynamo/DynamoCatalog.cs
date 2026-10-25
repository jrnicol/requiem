using System.Security.Cryptography;
using Amazon.DynamoDBv2.Model;
using Requiem.Data.Entities;
using Requiem.Data.Seeding;
using Requiem.Data.Stores;
using static Requiem.Data.Dynamo.DynamoExtensions;

namespace Requiem.Data.Dynamo;

/// <summary>
/// The catalog, loaded once and kept for the life of the process (a Lambda instance). Every
/// <see cref="CheckInterval"/> it reads the catalog's version stamp and reloads when an admin edit (on any
/// instance) has bumped it.
/// </summary>
public sealed class CatalogCache
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CatalogSnapshot? _snapshot;
    private long _version = -1;
    private DateTimeOffset _checkedAt;

    public async Task<CatalogSnapshot> GetAsync(DynamoWriter table, CancellationToken ct)
    {
        var now = table.Time.GetUtcNow();
        if (_snapshot is { } cached && now - _checkedAt < CheckInterval) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot is { } fresh && now - _checkedAt < CheckInterval) return fresh;
            var version = await DynamoCatalog.VersionAsync(table, ct);
            if (_snapshot is null || version != _version)
            {
                _snapshot = await DynamoCatalog.LoadAsync(table, ct);
                _version = version;
            }
            _checkedAt = now;
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the cached copy, after this instance changed the catalog.</summary>
    public void Invalidate() => _snapshot = null;
}

internal sealed class DynamoCatalogStore(DynamoWriter table, CatalogCache cache) : ICatalogStore
{
    public Task<CatalogSnapshot> GetAsync(CancellationToken ct) => cache.GetAsync(table, ct);

    public async Task<Power?> FindPowerAsync(int id, CancellationToken ct)
    {
        var catalog = await GetAsync(ct);
        if (catalog.Power(id) is not { } cached) return null;
        var copy = Documents.Copy(cached);
        copy.PowerGroup = Documents.Copy(cached.PowerGroup);
        return copy;
    }

    public async Task SavePowerAsync(Power power, CancellationToken ct)
    {
        await table.CommitAsync([table.Put(DynamoCatalog.Item(DynamoCatalog.Kinds.Power, power.Id, power)), DynamoCatalog.BumpVersion(table)], ct);
        cache.Invalidate();
    }
}

/// <summary>Catalog items: <c>CATALOG</c> / <c>POWER#00042</c> and so on, plus the <c>VERSION</c> stamp.</summary>
internal static class DynamoCatalog
{
    public const string PK = "CATALOG", VersionSK = "VERSION";

    public static class Kinds
    {
        public const string Bloodline = "BLOODLINE", PredatorType = "PREDATOR", Merit = "MERIT", Compel = "COMPEL",
            Edge = "EDGE", Concept = "CONCEPT", PowerGroup = "GROUP", Power = "POWER";
    }

    public static Dictionary<string, AttributeValue> Item<T>(string kind, int id, T value)
    {
        var item = Key(PK, $"{kind}#{id:D5}");
        item[Keys.Doc] = Documents.Write(value);
        return item;
    }

    public static TransactWriteItem BumpVersion(DynamoWriter table) => new()
    {
        Update = new Update
        {
            TableName = table.Table,
            Key = Key(PK, VersionSK),
            UpdateExpression = "ADD #v :one",
            ExpressionAttributeNames = new() { ["#v"] = "Version" },
            ExpressionAttributeValues = new() { [":one"] = N(1) },
        },
    };

    public static async Task<long> VersionAsync(DynamoWriter table, CancellationToken ct) =>
        (await table.GetItemAsync(PK, VersionSK, ct))?.Number("Version") ?? 0;

    public static async Task<CatalogSnapshot> LoadAsync(DynamoWriter table, CancellationToken ct)
    {
        var items = await table.Client.PartitionAsync(table.Table, PK, null, ct);
        List<T> Of<T>(string kind) => items.Where(i => i[Keys.SK].S!.StartsWith(kind + "#", StringComparison.Ordinal)).Select(Documents.Read<T>).ToList();

        var bloodlines = Of<Bloodline>(Kinds.Bloodline);
        var groups = Of<PowerGroup>(Kinds.PowerGroup);
        var powers = Of<Power>(Kinds.Power);
        var bloodlineById = bloodlines.ToDictionary(b => b.Id);
        var groupById = groups.ToDictionary(g => g.Id);
        foreach (var group in groups) group.Bloodline = group.BloodlineId is { } b ? bloodlineById.GetValueOrDefault(b) : null;
        foreach (var power in powers) power.PowerGroup = groupById[power.PowerGroupId];

        return new CatalogSnapshot(bloodlines, Of<PredatorType>(Kinds.PredatorType), Of<MeritDefinition>(Kinds.Merit),
            Of<CompelDefinition>(Kinds.Compel), Of<EdgeDefinition>(Kinds.Edge), Of<ConceptDefinition>(Kinds.Concept), groups, powers);
    }

    /// <summary>
    /// Adds the embedded seed entries the table doesn't have yet, matched by natural key, as the EF seeder did:
    /// existing entries (and the admin's edits to them) are never changed. Skipped entirely when the seed
    /// files haven't changed since the last run, so a cold start costs one read.
    /// </summary>
    /// <param name="force">Compare entry by entry even if the seed files are unchanged (tests).</param>
    public static async Task<CatalogSeedResult> SeedAsync(DynamoWriter table, CancellationToken ct, bool force = false)
    {
        var seedHash = SeedHash();
        var stamp = await table.GetItemAsync(PK, VersionSK, ct);
        if (!force && stamp?.String("SeedHash") == seedHash) return new CatalogSeedResult(0, 0, 0, 0, 0, 0);

        var data = CatalogSeeder.Load();
        var current = await LoadAsync(table, ct);
        var writes = new List<WriteRequest>();

        int AddMissing<T, TKey>(string kind, IReadOnlyList<T> existing, IEnumerable<T> seed, Func<T, TKey> key,
            Func<T, int> getId, Action<T, int> setId)
        {
            var have = existing.Select(key).ToHashSet();
            var next = existing.Count == 0 ? 1 : existing.Max(getId) + 1;
            var added = 0;
            foreach (var entry in seed.Where(e => !have.Contains(key(e))))
            {
                setId(entry, next++);
                writes.Add(new WriteRequest { PutRequest = new PutRequest { Item = Item(kind, getId(entry), entry) } });
                added++;
            }
            return added;
        }

        var bloodlines = AddMissing(Kinds.Bloodline, current.Bloodlines, data.Bloodlines, b => b.Name, b => b.Id, (b, id) => b.Id = id);
        var predators = AddMissing(Kinds.PredatorType, current.PredatorTypes, data.PredatorTypes, p => p.Name, p => p.Id, (p, id) => p.Id = id);
        var merits = AddMissing(Kinds.Merit, current.Merits, data.Merits, m => (m.Name, m.Kind, m.Source), m => m.Id, (m, id) => m.Id = id);
        var compels = AddMissing(Kinds.Compel, current.Compels, data.Compels, c => c.Name, c => c.Id, (c, id) => c.Id = id);
        var edges = AddMissing(Kinds.Edge, current.Edges, data.Edges, e => e.Name, e => e.Id, (e, id) => e.Id = id);
        var concepts = AddMissing(Kinds.Concept, current.Concepts, data.Concepts, c => c.Name, c => c.Id, (c, id) => c.Id = id);

        // Power groups link to Bloodlines, and powers to groups, by id.
        var bloodlineIds = current.Bloodlines.Concat(data.Bloodlines.Where(b => b.Id != 0)).GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First().Id);
        var groups = AddMissing(Kinds.PowerGroup, current.PowerGroups, data.PowerGroups.Select(g => new PowerGroup
        {
            Name = g.Name,
            Kind = g.Kind,
            // A Bloodline the GM renamed or removed just leaves the group unlinked.
            BloodlineId = g.Bloodline is not null && bloodlineIds.TryGetValue(g.Bloodline, out var id) ? id : null,
            Description = g.Description,
            SourcePage = g.SourcePage,
        }).ToList(), g => g.Name, g => g.Id, (g, id) => g.Id = id);

        var groupIds = current.PowerGroups.Select(g => (g.Name, g.Id))
            .Concat(writes.Where(w => w.PutRequest.Item[Keys.SK].S!.StartsWith(Kinds.PowerGroup + "#", StringComparison.Ordinal))
                .Select(w => Documents.Read<PowerGroup>(w.PutRequest.Item)).Select(g => (g.Name, g.Id)))
            .GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.First().Id);
        var powers = AddMissing(Kinds.Power, current.Powers, data.Powers
            // A group the GM renamed or removed skips its powers rather than failing startup.
            .Where(p => groupIds.ContainsKey(p.Group))
            .Select(p => CatalogSeeder.ToEntity(p, groupIds)).ToList(), p => (p.PowerGroupId, p.Name), p => p.Id, (p, id) => p.Id = id);

        await table.Client.WriteManyAsync(table.Table, writes, ct);
        await table.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table.Table,
            Key = Key(PK, VersionSK),
            UpdateExpression = "ADD #v :one SET #h = :hash",
            ExpressionAttributeNames = new() { ["#v"] = "Version", ["#h"] = "SeedHash" },
            ExpressionAttributeValues = new() { [":one"] = N(1), [":hash"] = S(seedHash) },
        }, ct);
        return new CatalogSeedResult(bloodlines, predators, merits, compels, edges, groups, powers, concepts);
    }

    /// <summary>A fingerprint of the embedded seed files.</summary>
    private static string SeedHash()
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var assembly = typeof(CatalogSeeder).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Seed/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(name));
            sha.AppendData(buffer.ToArray());
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
