using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;
using Requiem.Data.Stores;
using static Requiem.Data.Dynamo.DynamoExtensions;

namespace Requiem.Data.Dynamo;

/// <summary>
/// A campaign is <c>CAMPAIGN#id</c> / <c>CAMPAIGN</c>; its members (<c>MEMBER#userId</c>) and invites
/// (<c>INVITE#id</c>) share its partition. GSI1 finds a user's memberships (<c>USER#id</c>) and an invite by
/// its token hash (<c>INVITE#hash</c>). Members and invites are plain attributes, so writes can test them.
/// </summary>
internal sealed class DynamoCampaignStore(DynamoWriter table) : ICampaignStore
{
    private const string SK = "CAMPAIGN";

    public async Task<IReadOnlyDictionary<Guid, CampaignRole>> RolesOfAsync(string userId, CancellationToken ct)
    {
        var items = await table.Client.PartitionAsync(table.Table, Keys.User(userId), "CAMPAIGN#", ct, Keys.Gsi1);
        return items.Select(ToMember).ToDictionary(m => m.CampaignId, m => m.Role);
    }

    public async Task<CampaignRole?> RoleAsync(Guid campaignId, string userId, CancellationToken ct) =>
        await table.GetItemAsync(Keys.Campaign(campaignId), Keys.Member(userId), ct) is { } item ? ToMember(item).Role : null;

    public async Task<Campaign?> GetAsync(Guid id, CancellationToken ct) =>
        await table.GetItemAsync(Keys.Campaign(id), SK, ct) is { } item ? Documents.Read<Campaign>(item) : null;

    public async Task AddAsync(Campaign campaign, CancellationToken ct)
    {
        var writes = new List<TransactWriteItem> { table.Put(CampaignItem(campaign), "attribute_not_exists(PK)") };
        writes.AddRange(campaign.Members.Select(m => table.Put(MemberItem(m))));
        if (!await table.CommitAsync(writes, ct)) throw new InvalidOperationException($"Campaign {campaign.Id} already exists.");
    }

    public Task SaveAsync(Campaign campaign, CancellationToken ct) => table.CommitAsync(table.Put(CampaignItem(campaign)), ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await table.CommitAsync([], ct); // any pending audit entries
        var items = await table.Client.PartitionAsync(table.Table, Keys.Campaign(id), null, ct);
        await table.Client.WriteManyAsync(table.Table,
            items.Select(i => new WriteRequest { DeleteRequest = new DeleteRequest { Key = Key(i[Keys.PK].S!, i[Keys.SK].S!) } }), ct);
    }

    public async Task<List<CampaignMember>> MembersAsync(Guid campaignId, CancellationToken ct) =>
        (await table.Client.PartitionAsync(table.Table, Keys.Campaign(campaignId), "MEMBER#", ct)).Select(ToMember).ToList();

    public Task AddMemberAsync(CampaignMember member, CancellationToken ct) => table.CommitAsync(table.Put(MemberItem(member)), ct);

    public Task RemoveMemberAsync(Guid campaignId, string userId, CancellationToken ct) =>
        table.CommitAsync(table.Delete(Keys.Campaign(campaignId), Keys.Member(userId)), ct);

    public async Task<List<CampaignInvite>> InvitesAsync(Guid campaignId, CancellationToken ct) =>
        (await table.Client.PartitionAsync(table.Table, Keys.Campaign(campaignId), "INVITE#", ct)).Select(ToInvite).ToList();

    public async Task<CampaignInvite?> FindInviteAsync(string tokenHash, CancellationToken ct) =>
        (await table.Client.PartitionAsync(table.Table, $"INVITE#{tokenHash}", null, ct, Keys.Gsi1)).Select(ToInvite).SingleOrDefault();

    public Task AddInviteAsync(CampaignInvite invite, CancellationToken ct) => table.CommitAsync(table.Put(InviteItem(invite)), ct);

    public Task SaveInviteAsync(CampaignInvite invite, CancellationToken ct) => table.CommitAsync(table.Put(InviteItem(invite)), ct);

    public Task<bool> JoinWithInviteAsync(CampaignInvite invite, CampaignMember member, DateTimeOffset now, CancellationToken ct)
    {
        // Spend a use only while the link is live; a link with no limit has no UsesLeft and isn't counted down.
        var spend = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = table.Table,
                Key = Key(Keys.Campaign(invite.CampaignId), Keys.Invite(invite.Id)),
                ConditionExpression = "attribute_not_exists(RevokedAt) AND ExpiresAt > :now AND (attribute_not_exists(UsesLeft) OR UsesLeft > :zero)",
                UpdateExpression = invite.UsesLeft is null ? "SET Spent = if_not_exists(Spent, :zero) + :one" : "SET UsesLeft = UsesLeft - :one",
                ExpressionAttributeValues = new() { [":now"] = N(now.ToUnixTimeMilliseconds()), [":zero"] = N(0), [":one"] = N(1) },
            },
        };
        return table.CommitAsync([spend, table.Put(MemberItem(member), "attribute_not_exists(PK)")], ct);
    }

    private static Dictionary<string, AttributeValue> CampaignItem(Campaign campaign)
    {
        var item = Key(Keys.Campaign(campaign.Id), SK);
        item[Keys.Doc] = Documents.Write(campaign);
        return item;
    }

    private static Dictionary<string, AttributeValue> MemberItem(CampaignMember m) => new()
    {
        [Keys.PK] = S(Keys.Campaign(m.CampaignId)),
        [Keys.SK] = S(Keys.Member(m.UserId)),
        [Keys.Gsi1PK] = S(Keys.User(m.UserId)),
        [Keys.Gsi1SK] = S(Keys.Campaign(m.CampaignId)),
        ["CampaignId"] = S(m.CampaignId.ToString()),
        ["UserId"] = S(m.UserId),
        ["Role"] = S(m.Role.ToString()),
        ["JoinedAt"] = S(m.JoinedAt.ToString("O", CultureInfo.InvariantCulture)),
    };

    private static CampaignMember ToMember(Dictionary<string, AttributeValue> item) => new()
    {
        CampaignId = Guid.Parse(item.String("CampaignId")!),
        UserId = item.String("UserId")!,
        Role = Enum.Parse<CampaignRole>(item.String("Role")!),
        JoinedAt = DateTimeOffset.Parse(item.String("JoinedAt")!, CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, AttributeValue> InviteItem(CampaignInvite i)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [Keys.PK] = S(Keys.Campaign(i.CampaignId)),
            [Keys.SK] = S(Keys.Invite(i.Id)),
            [Keys.Gsi1PK] = S($"INVITE#{i.TokenHash}"),
            [Keys.Gsi1SK] = S("INVITE"),
            ["Id"] = S(i.Id.ToString()),
            ["CampaignId"] = S(i.CampaignId.ToString()),
            ["TokenHash"] = S(i.TokenHash),
            ["Role"] = S(i.Role.ToString()),
            ["ExpiresAt"] = N(i.ExpiresAt.ToUnixTimeMilliseconds()),
            ["CreatedByUserId"] = S(i.CreatedByUserId),
            ["CreatedAt"] = N(i.CreatedAt.ToUnixTimeMilliseconds()),
        };
        if (i.UsesLeft is { } uses) item["UsesLeft"] = N(uses);
        if (i.RevokedAt is { } revoked) item["RevokedAt"] = N(revoked.ToUnixTimeMilliseconds());
        return item;
    }

    private static CampaignInvite ToInvite(Dictionary<string, AttributeValue> item) => new()
    {
        Id = Guid.Parse(item.String("Id")!),
        CampaignId = Guid.Parse(item.String("CampaignId")!),
        TokenHash = item.String("TokenHash")!,
        Role = Enum.Parse<CampaignRole>(item.String("Role")!),
        ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(item.Number("ExpiresAt")!.Value),
        UsesLeft = item.Number("UsesLeft") is { } uses ? (int)uses : null,
        CreatedByUserId = item.String("CreatedByUserId")!,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(item.Number("CreatedAt")!.Value),
        RevokedAt = item.Number("RevokedAt") is { } revoked ? DateTimeOffset.FromUnixTimeMilliseconds(revoked) : null,
    };
}

/// <summary>A scene is <c>SCENE#id</c> / <c>SCENE</c>, with its members in the document; GSI2 lists a campaign's scenes.</summary>
internal sealed class DynamoSceneStore(DynamoWriter table) : ISceneStore
{
    private const string SK = "SCENE";

    public async Task<Scene?> GetAsync(Guid id, CancellationToken ct) =>
        await table.GetItemAsync(Keys.Scene(id), SK, ct) is { } item ? Documents.Read<Scene>(item) : null;

    public async Task<List<Scene>> ListByCampaignAsync(Guid campaignId, CancellationToken ct) =>
        (await table.Client.PartitionAsync(table.Table, Keys.Campaign(campaignId), "SCENE#", ct, Keys.Gsi2)).Select(Documents.Read<Scene>).ToList();

    public async Task<bool> AnyInCampaignAsync(Guid campaignId, CancellationToken ct) =>
        (await ListByCampaignAsync(campaignId, ct)).Count > 0;

    public Task AddAsync(Scene scene, CancellationToken ct) => table.CommitAsync(table.Put(Item(scene), "attribute_not_exists(PK)"), ct);

    public Task SaveAsync(Scene scene, CancellationToken ct) => table.CommitAsync(table.Put(Item(scene)), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct) => table.CommitAsync(table.Delete(Keys.Scene(id), SK), ct);

    private static Dictionary<string, AttributeValue> Item(Scene scene)
    {
        var item = Key(Keys.Scene(scene.Id), SK);
        item[Keys.Doc] = Documents.Write(scene);
        if (scene.CampaignId is { } campaignId)
        {
            item[Keys.Gsi2PK] = S(Keys.Campaign(campaignId));
            item[Keys.Gsi2SK] = S(Keys.Scene(scene.Id));
        }
        return item;
    }
}

/// <summary>Audit entries are <c>AUDIT#id</c> / <c>AUDIT</c>; GSI1 (<c>CHARACTER#id</c> / <c>AUDIT#id</c>) gives a character's history.</summary>
internal sealed class DynamoAuditStore(DynamoWriter table) : IAuditStore
{
    public async Task<AuditEntry?> GetAsync(string id, CancellationToken ct) =>
        id.Length is 0 or > 64 ? null
        : await table.GetItemAsync(Keys.Audit(id), "AUDIT", ct) is { } item ? Documents.Read<AuditEntry>(item) : null;

    public async Task<List<AuditEntry>> ForCharacterAsync(Guid characterId, int take, CancellationToken ct)
    {
        var response = await table.Client.QueryAsync(new QueryRequest
        {
            TableName = table.Table,
            IndexName = Keys.Gsi1,
            KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :prefix)",
            ExpressionAttributeNames = new() { ["#pk"] = Keys.Gsi1PK, ["#sk"] = Keys.Gsi1SK },
            ExpressionAttributeValues = new() { [":pk"] = S(Keys.Character(characterId)), [":prefix"] = S("AUDIT#") },
            ScanIndexForward = false,
            Limit = take,
        }, ct);
        return (response.Items ?? []).Select(Documents.Read<AuditEntry>).ToList();
    }

    public Task FlushAsync(CancellationToken ct) => table.CommitAsync([], ct);
}

/// <summary>Names and emails of other users, read straight from their user items.</summary>
internal sealed class DynamoUserDirectory(DynamoWriter table) : IUserDirectory
{
    public async Task<bool> AnyUsersAsync(CancellationToken ct) =>
        (await table.GetItemAsync(DynamoUserStore.CountPK, DynamoUserStore.CountPK, ct))?.Number("Count") > 0;

    public async Task<IReadOnlyDictionary<string, UserInfo>> GetAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, UserInfo>();
        var items = await table.Client.GetManyAsync(table.Table, ids.Select(id => Key(Keys.User(id), DynamoUserStore.SK)), ct);
        return items.Select(Documents.Read<RequiemUser>).ToDictionary(u => u.Id, u => new UserInfo(u.Id, u.DisplayName, u.Email));
    }
}
