using Amazon.DynamoDBv2.Model;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using static Requiem.Data.Dynamo.DynamoExtensions;

namespace Requiem.Data.Dynamo;

/// <summary>
/// One item per character, <c>CHARACTER#id</c> / <c>CHARACTER</c>, holding the whole sheet as a document. GSI2
/// (<c>CAMPAIGN#id</c> / <c>CHARACTER#id</c>) lists a campaign's characters. Every write checks
/// <see cref="Character.Version"/>, so two saves of the same loaded sheet can't both land.
/// </summary>
internal sealed class DynamoCharacterStore(DynamoWriter table, ICatalogStore catalog) : ICharacterStore
{
    private const string SK = "CHARACTER";

    public async Task<Character?> GetAsync(Guid id, CancellationToken ct) =>
        await table.GetItemAsync(Keys.Character(id), SK, ct) is { } item ? await HydrateAsync(Documents.Read<Character>(item), ct) : null;

    public async Task<List<Character>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var items = await table.Client.GetManyAsync(table.Table, ids.Distinct().Select(id => Key(Keys.Character(id), SK)), ct);
        return await HydrateAsync(items.Select(Documents.Read<Character>), ct);
    }

    public async Task<List<Character>> ListByCampaignAsync(Guid campaignId, CancellationToken ct)
    {
        var items = await table.Client.PartitionAsync(table.Table, Keys.Campaign(campaignId), "CHARACTER#", ct, Keys.Gsi2);
        return await HydrateAsync(items.Select(Documents.Read<Character>), ct);
    }

    public async Task<int> CountByCampaignAsync(Guid campaignId, CancellationToken ct)
    {
        var request = new QueryRequest
        {
            TableName = table.Table,
            IndexName = Keys.Gsi2,
            KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :prefix)",
            ExpressionAttributeNames = new() { ["#pk"] = Keys.Gsi2PK, ["#sk"] = Keys.Gsi2SK },
            ExpressionAttributeValues = new() { [":pk"] = S(Keys.Campaign(campaignId)), [":prefix"] = S("CHARACTER#") },
            Select = Amazon.DynamoDBv2.Select.COUNT,
        };
        var count = 0;
        do
        {
            var response = await table.Client.QueryAsync(request, ct);
            count += response.Count ?? 0;
            request.ExclusiveStartKey = response.LastEvaluatedKey is { Count: > 0 } last ? last : null;
        } while (request.ExclusiveStartKey is not null);
        return count;
    }

    public async Task AddAsync(Character character, CancellationToken ct)
    {
        var now = table.Time.GetUtcNow();
        if (character.Id == Guid.Empty) character.Id = Guid.CreateVersion7(now);
        character.CreatedAt = now;
        Stamp(character, now);
        if (!await table.CommitAsync(table.Put(Item(character), "attribute_not_exists(PK)"), ct))
            throw new InvalidOperationException($"Character {character.Id} already exists.");
        await HydrateAsync(character, ct);
    }

    public async Task<bool> TrySaveAsync(Character character, Guid? expectedVersion, CancellationToken ct) =>
        await TrySaveAllAsync([(character, expectedVersion ?? character.Version)], ct);

    public async Task MoveAsync(Character character, Guid campaignId, CancellationToken ct)
    {
        var from = character.CampaignId;
        var expected = character.Version;
        character.CampaignId = campaignId;
        character.SireId = null;
        character.DomitorId = null;
        // Childer and ghouls left behind lose their link to it.
        var others = from is { } old ? Unlink(await ListByCampaignAsync(old, ct), character.Id) : [];
        if (!await TrySaveAllAsync([(character, expected), .. others.Select(o => (o, o.Version))], ct))
            throw new InvalidOperationException($"Character {character.Id} was changed by someone else while it was being moved.");
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var character = await GetAsync(id, ct);
        if (character is null) return false;
        var others = character.CampaignId is { } campaignId ? Unlink(await ListByCampaignAsync(campaignId, ct), id) : [];
        var now = table.Time.GetUtcNow();
        var writes = new List<TransactWriteItem> { table.Delete(Keys.Character(id), SK) };
        foreach (var other in others)
        {
            var expected = other.Version;
            Stamp(other, now);
            writes.Add(Conditional(other, expected));
        }
        if (!await table.CommitAsync(writes, ct))
            throw new InvalidOperationException($"A character linked to {id} changed while it was being deleted; try again.");
        return true;
    }

    /// <summary>Clears every link to <paramref name="id"/> (sire, domitor, relationships) and returns the characters it changed.</summary>
    private static List<Character> Unlink(List<Character> characters, Guid id)
    {
        var changed = new List<Character>();
        foreach (var c in characters.Where(c => c.Id != id))
        {
            var touched = false;
            if (c.SireId == id) { c.SireId = null; touched = true; }
            if (c.DomitorId == id) { c.DomitorId = null; touched = true; }
            if (c.Relationships.RemoveAll(r => r.ToCharacterId == id) > 0) touched = true;
            if (touched) changed.Add(c);
        }
        return changed;
    }

    private async Task<bool> TrySaveAllAsync(IReadOnlyList<(Character Character, Guid Expected)> saves, CancellationToken ct)
    {
        var now = table.Time.GetUtcNow();
        var previous = saves.Select(s => (s.Character.Version, s.Character.UpdatedAt)).ToList();
        foreach (var (character, _) in saves) Stamp(character, now);
        if (await table.CommitAsync(saves.Select(s => Conditional(s.Character, s.Expected)).ToList(), ct))
        {
            foreach (var (character, _) in saves) await HydrateAsync(character, ct);
            return true;
        }
        // Nothing was written: leave the characters as they were loaded.
        for (var i = 0; i < saves.Count; i++) (saves[i].Character.Version, saves[i].Character.UpdatedAt) = previous[i];
        return false;
    }

    private TransactWriteItem Conditional(Character character, Guid expected) =>
        table.Put(Item(character), "#v = :expected",
            new() { ["#v"] = "Version" },
            new() { [":expected"] = S(expected.ToString()) });

    /// <summary>New list items get ids (audit undo data refers to them), and the character a new version.</summary>
    private static void Stamp(Character character, DateTimeOffset now)
    {
        ChildIds.Assign(character);
        character.UpdatedAt = now;
        character.Version = Guid.NewGuid();
    }

    private static Dictionary<string, AttributeValue> Item(Character character)
    {
        var item = Key(Keys.Character(character.Id), SK);
        item[Keys.Doc] = Documents.Write(character);
        item["Version"] = S(character.Version.ToString());
        if (character.CampaignId is { } campaignId)
        {
            item[Keys.Gsi2PK] = S(Keys.Campaign(campaignId));
            item[Keys.Gsi2SK] = S(Keys.Character(character.Id));
        }
        return item;
    }

    private async Task<Character> HydrateAsync(Character character, CancellationToken ct) => (await HydrateAsync([character], ct))[0];

    /// <summary>Fills in the catalog entries the mappers read (EF loaded these as navigations).</summary>
    private async Task<List<Character>> HydrateAsync(IEnumerable<Character> characters, CancellationToken ct)
    {
        var snapshot = await catalog.GetAsync(ct);
        var list = characters.ToList();
        foreach (var c in list)
        {
            c.Bloodline = c.BloodlineId is { } b ? snapshot.Bloodline(b) : null;
            c.PredatorType = c.PredatorTypeId is { } p ? snapshot.PredatorTypes.FirstOrDefault(t => t.Id == p) : null;
            foreach (var d in c.Disciplines) d.PowerGroup = snapshot.PowerGroup(d.PowerGroupId)!;
        }
        return list;
    }
}
