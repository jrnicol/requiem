using System.Xml.Linq;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Requiem.Data.Entities;
using static Requiem.Data.Dynamo.DynamoExtensions;

namespace Requiem.Data.Dynamo;

/// <summary>
/// ASP.NET Identity's users on DynamoDB. A user is <c>USER#id</c> / <c>USER</c> (the document, plus its role
/// names); <c>USERNAME#name</c> and <c>EMAIL#email</c> lookup items point at it and keep both unique, written in
/// the same transaction; <c>USERCOUNT</c> counts accounts for the first-run check.
/// </summary>
internal sealed class DynamoUserStore(DynamoWriter table) :
    IUserPasswordStore<RequiemUser>, IUserEmailStore<RequiemUser>, IUserSecurityStampStore<RequiemUser>,
    IUserLockoutStore<RequiemUser>, IUserRoleStore<RequiemUser>
{
    public const string SK = "USER", CountPK = "USERCOUNT";
    private const string LookupSK = "LOOKUP";

    private static string NameKey(string normalized) => $"USERNAME#{normalized}";
    private static string EmailKey(string normalized) => $"EMAIL#{normalized}";

    // ---------- Create, update, delete ----------

    public async Task<IdentityResult> CreateAsync(RequiemUser user, CancellationToken ct)
    {
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        var writes = new List<TransactWriteItem> { table.Put(UserItem(user, []), "attribute_not_exists(PK)") };
        writes.AddRange(Lookups(user).Select(l => table.Put(l, "attribute_not_exists(PK)")));
        writes.Add(Count(+1));
        return await table.CommitAsync(writes, ct)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.Email ?? user.UserName ?? ""));
    }

    public async Task<IdentityResult> UpdateAsync(RequiemUser user, CancellationToken ct)
    {
        var stored = await table.GetItemAsync(Keys.User(user.Id), SK, ct);
        if (stored is null) return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        var before = Documents.Read<RequiemUser>(stored);
        var expected = user.ConcurrencyStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        var writes = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = table.Table,
                    Key = Key(Keys.User(user.Id), SK),
                    // Roles are kept as they are: they change through Add/RemoveFromRoleAsync.
                    UpdateExpression = "SET #doc = :doc, #stamp = :new",
                    ConditionExpression = "#stamp = :expected",
                    ExpressionAttributeNames = new() { ["#doc"] = Keys.Doc, ["#stamp"] = "ConcurrencyStamp" },
                    ExpressionAttributeValues = new() { [":doc"] = Documents.Write(user), [":new"] = S(user.ConcurrencyStamp), [":expected"] = S(expected ?? "") },
                },
            },
        };
        // A changed user name or email moves its lookup item.
        foreach (var (old, now) in Lookups(before).Zip(Lookups(user)))
        {
            if (old[Keys.PK].S == now[Keys.PK].S) continue;
            writes.Add(table.Delete(old[Keys.PK].S!, LookupSK));
            writes.Add(table.Put(now, "attribute_not_exists(PK)"));
        }

        if (await table.CommitAsync(writes, ct)) return IdentityResult.Success;
        user.ConcurrencyStamp = expected;
        return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    }

    public async Task<IdentityResult> DeleteAsync(RequiemUser user, CancellationToken ct)
    {
        var writes = new List<TransactWriteItem> { table.Delete(Keys.User(user.Id), SK), Count(-1) };
        writes.AddRange(Lookups(user).Select(l => table.Delete(l[Keys.PK].S!, LookupSK)));
        await table.CommitAsync(writes, ct);
        return IdentityResult.Success;
    }

    // ---------- Finding ----------

    public async Task<RequiemUser?> FindByIdAsync(string userId, CancellationToken ct) =>
        await table.GetItemAsync(Keys.User(userId), SK, ct) is { } item ? Documents.Read<RequiemUser>(item) : null;

    public Task<RequiemUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => FindByLookupAsync(NameKey(normalizedUserName), ct);

    public Task<RequiemUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) => FindByLookupAsync(EmailKey(normalizedEmail), ct);

    private async Task<RequiemUser?> FindByLookupAsync(string pk, CancellationToken ct) =>
        await table.GetItemAsync(pk, LookupSK, ct) is { } lookup ? await FindByIdAsync(lookup.String("UserId")!, ct) : null;

    // ---------- Roles (stored as role names on the user item) ----------

    public async Task AddToRoleAsync(RequiemUser user, string normalizedRoleName, CancellationToken ct)
    {
        var role = await table.GetItemAsync(DynamoRoleStore.Key(normalizedRoleName), DynamoRoleStore.SK, ct)
            ?? throw new InvalidOperationException($"Role {normalizedRoleName} does not exist.");
        await UpdateRolesAsync(user, "ADD #roles :role", Documents.Read<IdentityRole>(role).Name!, ct);
    }

    public async Task RemoveFromRoleAsync(RequiemUser user, string normalizedRoleName, CancellationToken ct)
    {
        var name = (await GetRolesAsync(user, ct)).FirstOrDefault(r => r.Equals(normalizedRoleName, StringComparison.OrdinalIgnoreCase));
        if (name is not null) await UpdateRolesAsync(user, "DELETE #roles :role", name, ct);
    }

    private Task UpdateRolesAsync(RequiemUser user, string expression, string roleName, CancellationToken ct) =>
        table.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table.Table,
            Key = Key(Keys.User(user.Id), SK),
            UpdateExpression = expression,
            ExpressionAttributeNames = new() { ["#roles"] = "Roles" },
            ExpressionAttributeValues = new() { [":role"] = new AttributeValue { SS = [roleName] } },
        }, ct);

    public async Task<IList<string>> GetRolesAsync(RequiemUser user, CancellationToken ct) =>
        (await table.GetItemAsync(Keys.User(user.Id), SK, ct))?.GetValueOrDefault("Roles")?.SS?.ToList() ?? [];

    public async Task<bool> IsInRoleAsync(RequiemUser user, string normalizedRoleName, CancellationToken ct) =>
        (await GetRolesAsync(user, ct)).Any(r => r.Equals(normalizedRoleName, StringComparison.OrdinalIgnoreCase));

    public Task<IList<RequiemUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken ct) =>
        throw new NotSupportedException("Listing a role's users isn't needed by the app, so it isn't indexed.");

    // ---------- Plain properties (Identity sets these on the object; Create/Update save them) ----------

    public Task<string> GetUserIdAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(RequiemUser user, string? userName, CancellationToken ct) { user.UserName = userName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(RequiemUser user, string? normalizedName, CancellationToken ct) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
    public Task SetPasswordHashAsync(RequiemUser user, string? passwordHash, CancellationToken ct) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash is not null);
    public Task SetEmailAsync(RequiemUser user, string? email, CancellationToken ct) { user.Email = email; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.Email);
    public Task<bool> GetEmailConfirmedAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.EmailConfirmed);
    public Task SetEmailConfirmedAsync(RequiemUser user, bool confirmed, CancellationToken ct) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
    public Task<string?> GetNormalizedEmailAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.NormalizedEmail);
    public Task SetNormalizedEmailAsync(RequiemUser user, string? normalizedEmail, CancellationToken ct) { user.NormalizedEmail = normalizedEmail; return Task.CompletedTask; }
    public Task SetSecurityStampAsync(RequiemUser user, string stamp, CancellationToken ct) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(RequiemUser user, DateTimeOffset? lockoutEnd, CancellationToken ct) { user.LockoutEnd = lockoutEnd; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(++user.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(RequiemUser user, CancellationToken ct) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(RequiemUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(RequiemUser user, bool enabled, CancellationToken ct) { user.LockoutEnabled = enabled; return Task.CompletedTask; }

    public void Dispose() { }

    // ---------- Items ----------

    private static Dictionary<string, AttributeValue> UserItem(RequiemUser user, List<string> roles)
    {
        var item = Key(Keys.User(user.Id), SK);
        item[Keys.Doc] = Documents.Write(user);
        item["ConcurrencyStamp"] = S(user.ConcurrencyStamp!);
        if (roles.Count > 0) item["Roles"] = new AttributeValue { SS = roles };
        return item;
    }

    private static IEnumerable<Dictionary<string, AttributeValue>> Lookups(RequiemUser user)
    {
        yield return Lookup(NameKey(user.NormalizedUserName ?? throw new InvalidOperationException("User has no normalized name.")), user.Id);
        yield return Lookup(EmailKey(user.NormalizedEmail ?? throw new InvalidOperationException("User has no normalized email.")), user.Id);
    }

    private static Dictionary<string, AttributeValue> Lookup(string pk, string userId)
    {
        var item = Key(pk, LookupSK);
        item["UserId"] = S(userId);
        return item;
    }

    private TransactWriteItem Count(int change) => new()
    {
        Update = new Update
        {
            TableName = table.Table,
            Key = Key(CountPK, CountPK),
            UpdateExpression = "ADD #count :change",
            ExpressionAttributeNames = new() { ["#count"] = "Count" },
            ExpressionAttributeValues = new() { [":change"] = N(change) },
        },
    };
}

/// <summary>Roles are <c>ROLE#name</c> / <c>ROLE</c>. The app has one, Admin, created at first-run setup.</summary>
internal sealed class DynamoRoleStore(DynamoWriter table) : IRoleStore<IdentityRole>
{
    public const string SK = "ROLE";
    public static string Key(string normalizedName) => $"ROLE#{normalizedName}";

    public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken ct)
    {
        var item = DynamoExtensions.Key(Key(role.NormalizedName!), SK);
        item[Keys.Doc] = Documents.Write(role);
        return await table.CommitAsync(table.Put(item, "attribute_not_exists(PK)"), ct)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityErrorDescriber().DuplicateRoleName(role.Name ?? ""));
    }

    public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken ct)
    {
        var item = DynamoExtensions.Key(Key(role.NormalizedName!), SK);
        item[Keys.Doc] = Documents.Write(role);
        await table.CommitAsync(table.Put(item), ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken ct)
    {
        await table.CommitAsync(table.Delete(Key(role.NormalizedName!), SK), ct);
        return IdentityResult.Success;
    }

    public Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken ct) =>
        throw new NotSupportedException("Roles are found by name.");

    public async Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct) =>
        await table.GetItemAsync(Key(normalizedRoleName), SK, ct) is { } item ? Documents.Read<IdentityRole>(item) : null;

    public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken ct) => Task.FromResult(role.Id);
    public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken ct) => Task.FromResult(role.Name);
    public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken ct) { role.Name = roleName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken ct) => Task.FromResult(role.NormalizedName);
    public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken ct) { role.NormalizedName = normalizedName; return Task.CompletedTask; }
    public void Dispose() { }
}

/// <summary>
/// The sign-in cookie's key ring (<c>DATAPROTECTION</c> / <c>KEY#name</c>), shared by every Lambda instance so a
/// cold start doesn't sign anyone out. ASP.NET reads it rarely and caches it, so the synchronous calls are fine.
/// </summary>
internal sealed class DynamoXmlRepository(Amazon.DynamoDBv2.IAmazonDynamoDB client, string tableName) : IXmlRepository
{
    private const string PK = "DATAPROTECTION";

    public IReadOnlyCollection<XElement> GetAllElements() =>
        client.PartitionAsync(tableName, PK, "KEY#", CancellationToken.None).GetAwaiter().GetResult()
            .Select(i => XElement.Parse(i.String("Xml")!)).ToList();

    public void StoreElement(XElement element, string friendlyName)
    {
        var item = Key(PK, $"KEY#{friendlyName}");
        item["Xml"] = S(element.ToString(SaveOptions.DisableFormatting));
        client.PutItemAsync(new PutItemRequest { TableName = tableName, Item = item }).GetAwaiter().GetResult();
    }
}
