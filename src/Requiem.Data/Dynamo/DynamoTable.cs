using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Requiem.Data.Entities;

namespace Requiem.Data.Dynamo;

/// <summary>Where the table is. Locally and in tests, <see cref="ServiceUrl"/> points at DynamoDB Local.</summary>
public sealed class DynamoOptions
{
    public const string Section = "DynamoDB";

    public string TableName { get; set; } = "requiem";

    /// <summary>DynamoDB Local's address (e.g. http://localhost:8000); unset on AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Create the table at startup if it's missing. On AWS, CloudFormation creates it instead.</summary>
    public bool CreateTable { get; set; }

    public string Region { get; set; } = "us-east-2";
}

/// <summary>
/// The single table's layout (SERVERLESS.md). Every item has <c>PK</c> and <c>SK</c>; some also fill a
/// global secondary index. Most entities are stored whole as JSON in <c>Doc</c>.
/// </summary>
public static class Keys
{
    public const string PK = "PK", SK = "SK";
    public const string Gsi1 = "GSI1", Gsi1PK = "GSI1PK", Gsi1SK = "GSI1SK";
    public const string Gsi2 = "GSI2", Gsi2PK = "GSI2PK", Gsi2SK = "GSI2SK";
    public const string Doc = "Doc";

    public static string Character(Guid id) => $"CHARACTER#{id}";
    public static string Campaign(Guid id) => $"CAMPAIGN#{id}";
    public static string Scene(Guid id) => $"SCENE#{id}";
    public static string User(string id) => $"USER#{id}";
    public static string Member(string userId) => $"MEMBER#{userId}";
    public static string Invite(Guid id) => $"INVITE#{id}";
    public static string Audit(string id) => $"AUDIT#{id}";
}

/// <summary>Entities as JSON documents: their own fields and lists, never the navigation properties EF used.</summary>
public static class Documents
{
    // Navigations are filled from the catalog (or other items) after loading, so they're never stored.
    private static readonly Dictionary<Type, string[]> Skipped = new()
    {
        [typeof(Character)] = [nameof(Character.Bloodline), nameof(Character.PredatorType), nameof(Character.Sire), nameof(Character.Childer),
            nameof(Character.Campaign), nameof(Character.Domitor), nameof(Character.Tags)],
        [typeof(CharacterDiscipline)] = [nameof(CharacterDiscipline.PowerGroup)],
        [typeof(CharacterPower)] = [nameof(CharacterPower.Power)],
        [typeof(CharacterMerit)] = [nameof(CharacterMerit.MeritDefinition)],
        [typeof(CharacterRelationship)] = [nameof(CharacterRelationship.FromCharacter), nameof(CharacterRelationship.ToCharacter)],
        [typeof(Bloodline)] = [nameof(Bloodline.PowerGroups)],
        [typeof(PowerGroup)] = [nameof(PowerGroup.Bloodline), nameof(PowerGroup.Powers)],
        [typeof(Power)] = [nameof(Power.PowerGroup)],
        [typeof(Campaign)] = [nameof(Campaign.Members), nameof(Campaign.Invites)],
        [typeof(CampaignMember)] = [nameof(CampaignMember.User)],
        [typeof(SceneMember)] = [nameof(SceneMember.Character)],
    };

    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    if (info.Kind != JsonTypeInfoKind.Object || !Skipped.TryGetValue(info.Type, out var skipped)) return;
                    foreach (var property in info.Properties.Where(p => skipped.Contains(p.Name)).ToList())
                        info.Properties.Remove(property);
                },
            },
        },
    };

    public static AttributeValue Write<T>(T value) => new() { S = JsonSerializer.Serialize(value, Options) };

    public static T Read<T>(Dictionary<string, AttributeValue> item) =>
        JsonSerializer.Deserialize<T>(item[Keys.Doc].S!, Options)
        ?? throw new InvalidOperationException($"Item {item[Keys.PK].S}/{item[Keys.SK].S} has an empty document.");

    /// <summary>A deep copy, through the same JSON as the table (so navigations are left unset).</summary>
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

/// <summary>Small helpers over the low-level DynamoDB client.</summary>
public static class DynamoExtensions
{
    public static AttributeValue S(string value) => new() { S = value };

    public static AttributeValue N(long value) => new() { N = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    public static Dictionary<string, AttributeValue> Key(string pk, string sk) => new() { [Keys.PK] = S(pk), [Keys.SK] = S(sk) };

    public static string? String(this Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var value) ? value.S : null;

    public static long? Number(this Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var value) && value.N is { } n ? long.Parse(n, System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>Every page of a query.</summary>
    public static async Task<List<Dictionary<string, AttributeValue>>> QueryAllAsync(
        this IAmazonDynamoDB client, QueryRequest request, CancellationToken ct)
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        do
        {
            var response = await client.QueryAsync(request, ct);
            items.AddRange(response.Items ?? []);
            request.ExclusiveStartKey = response.LastEvaluatedKey is { Count: > 0 } last ? last : null;
        } while (request.ExclusiveStartKey is not null);
        return items;
    }

    /// <summary>Items under one partition key, optionally only sort keys starting with <paramref name="prefix"/>.</summary>
    public static Task<List<Dictionary<string, AttributeValue>>> PartitionAsync(
        this IAmazonDynamoDB client, string table, string pk, string? prefix, CancellationToken ct, string? index = null)
    {
        var (pkName, skName) = index switch
        {
            Keys.Gsi1 => (Keys.Gsi1PK, Keys.Gsi1SK),
            Keys.Gsi2 => (Keys.Gsi2PK, Keys.Gsi2SK),
            _ => (Keys.PK, Keys.SK),
        };
        return client.QueryAllAsync(new QueryRequest
        {
            TableName = table,
            IndexName = index,
            KeyConditionExpression = prefix is null ? "#pk = :pk" : "#pk = :pk AND begins_with(#sk, :prefix)",
            ExpressionAttributeNames = prefix is null ? new() { ["#pk"] = pkName } : new() { ["#pk"] = pkName, ["#sk"] = skName },
            ExpressionAttributeValues = prefix is null ? new() { [":pk"] = S(pk) } : new() { [":pk"] = S(pk), [":prefix"] = S(prefix) },
        }, ct);
    }

    /// <summary>Reads many items by key, 100 at a time, retrying any the service didn't return.</summary>
    public static async Task<List<Dictionary<string, AttributeValue>>> GetManyAsync(
        this IAmazonDynamoDB client, string table, IEnumerable<Dictionary<string, AttributeValue>> keys, CancellationToken ct)
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        foreach (var chunk in keys.Chunk(100))
        {
            var pending = new Dictionary<string, KeysAndAttributes> { [table] = new() { Keys = [.. chunk] } };
            while (pending.Count > 0)
            {
                var response = await client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = pending }, ct);
                if (response.Responses?.TryGetValue(table, out var found) == true) items.AddRange(found);
                pending = response.UnprocessedKeys is { Count: > 0 } unprocessed ? unprocessed : [];
            }
        }
        return items;
    }

    /// <summary>Writes (puts or deletes) many items, 25 at a time, retrying any the service didn't take.</summary>
    public static async Task WriteManyAsync(this IAmazonDynamoDB client, string table, IEnumerable<WriteRequest> writes, CancellationToken ct)
    {
        foreach (var chunk in writes.Chunk(25))
        {
            var pending = new Dictionary<string, List<WriteRequest>> { [table] = [.. chunk] };
            while (pending.Count > 0)
            {
                var response = await client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = pending }, ct);
                pending = response.UnprocessedItems is { Count: > 0 } unprocessed ? unprocessed : [];
            }
        }
    }

    /// <summary>True when a transaction failed only because one of its conditions wasn't met.</summary>
    public static bool IsConditionFailure(this TransactionCanceledException ex) =>
        ex.CancellationReasons?.Any(r => r.Code == "ConditionalCheckFailed") == true;
}

/// <summary>Gives new items in a character's lists the next free id in that list.</summary>
internal static class ChildIds
{
    private static readonly (PropertyInfo List, PropertyInfo Id)[] Lists = typeof(Character).GetProperties()
        .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
        .Select(p => (List: p, Id: p.PropertyType.GetGenericArguments()[0].GetProperty("Id")))
        .Where(p => p.Id?.PropertyType == typeof(int))
        .Select(p => (p.List, p.Id!))
        .ToArray();

    public static void Assign(Character character)
    {
        foreach (var (listProperty, idProperty) in Lists)
        {
            if (listProperty.GetValue(character) is not System.Collections.IList list || list.Count == 0) continue;
            var next = list.Cast<object>().Max(item => (int)idProperty.GetValue(item)!) + 1;
            foreach (var item in list)
                if ((int)idProperty.GetValue(item)! == 0) idProperty.SetValue(item, next++);
        }
    }
}

/// <summary>Audit entry ids: time-ordered strings, so newest-first is a reverse sort on the key.</summary>
internal static class AuditIds
{
    private static readonly Lock Gate = new();
    private static long _lastMs, _sequence;

    public static string Next(DateTimeOffset now)
    {
        long ms, sequence;
        lock (Gate)
        {
            ms = Math.Max(now.ToUnixTimeMilliseconds(), _lastMs);
            _sequence = ms == _lastMs ? _sequence + 1 : 0;
            _lastMs = ms;
            sequence = _sequence;
        }
        return $"{ms:x12}{sequence:x4}{Random.Shared.Next():x8}";
    }
}
