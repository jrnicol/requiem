using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using static Requiem.Data.Dynamo.DynamoExtensions;

namespace Requiem.Data.Dynamo;

/// <summary>
/// The request's way to the table. Writes go through <see cref="CommitAsync"/>, which adds the pending
/// audit entries to the same transaction, so a change and its record succeed or fail together.
/// </summary>
public sealed class DynamoWriter(IAmazonDynamoDB client, IOptions<DynamoOptions> options, AuditBatch audit, TimeProvider time)
{
    public IAmazonDynamoDB Client => client;
    public string Table => options.Value.TableName;
    public TimeProvider Time => time;

    /// <summary>
    /// Runs <paramref name="writes"/> and the pending audit work as one transaction. <paramref name="beforeAudit"/>
    /// runs first, to give new items the ids that audit undo data refers to.
    /// </summary>
    /// <returns>False when one of the writes' conditions failed; nothing is written and the audit work stays pending.</returns>
    public async Task<bool> CommitAsync(IReadOnlyList<TransactWriteItem> writes, CancellationToken ct)
    {
        var items = writes.ToList();
        var (added, undone) = audit.Take();
        foreach (var entry in added)
        {
            entry.Id = AuditIds.Next(entry.At);
            items.Add(new TransactWriteItem { Put = new Put { TableName = Table, Item = AuditItem(entry) } });
        }
        foreach (var entry in undone)
            items.Add(new TransactWriteItem { Put = new Put { TableName = Table, Item = AuditItem(entry) } });
        if (items.Count == 0) return true;

        try
        {
            // Single writes skip the transaction (it costs double), but keep their condition.
            if (items.Count == 1 && items[0].Put is { } put)
                await client.PutItemAsync(new PutItemRequest
                {
                    TableName = Table, Item = put.Item, ConditionExpression = put.ConditionExpression,
                    ExpressionAttributeNames = put.ExpressionAttributeNames, ExpressionAttributeValues = put.ExpressionAttributeValues,
                }, ct);
            else
                await client.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = items }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
        catch (TransactionCanceledException ex) when (ex.IsConditionFailure())
        {
            // Put the audit work back for a retry (or for nothing, if the caller gives up).
            foreach (var entry in added) audit.Add(entry, null);
            foreach (var entry in undone) audit.MarkUndone(entry);
            return false;
        }
    }

    public Task<bool> CommitAsync(TransactWriteItem write, CancellationToken ct) => CommitAsync([write], ct);

    public TransactWriteItem Put(Dictionary<string, AttributeValue> item, string? condition = null,
        Dictionary<string, string>? names = null, Dictionary<string, AttributeValue>? values = null) =>
        new() { Put = new Put { TableName = Table, Item = item, ConditionExpression = condition, ExpressionAttributeNames = names, ExpressionAttributeValues = values } };

    public TransactWriteItem Delete(string pk, string sk) =>
        new() { Delete = new Delete { TableName = Table, Key = Key(pk, sk) } };

    public async Task<Dictionary<string, AttributeValue>?> GetItemAsync(string pk, string sk, CancellationToken ct)
    {
        var response = await client.GetItemAsync(new GetItemRequest { TableName = Table, Key = Key(pk, sk), ConsistentRead = true }, ct);
        return response.Item is { Count: > 0 } item ? item : null;
    }

    private static Dictionary<string, AttributeValue> AuditItem(AuditEntry entry)
    {
        var item = Key(Keys.Audit(entry.Id), "AUDIT");
        item[Keys.Doc] = Documents.Write(entry);
        if (entry.CharacterId is { } characterId)
        {
            item[Keys.Gsi1PK] = S(Keys.Character(characterId));
            item[Keys.Gsi1SK] = S(Keys.Audit(entry.Id));
        }
        return item;
    }
}
