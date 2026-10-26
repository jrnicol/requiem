using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Requiem.Data.Dynamo;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Accounts;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>A signed-in client and the campaign it tells.</summary>
public sealed record Storyteller(HttpClient Client, Guid CampaignId);

/// <summary>
/// Runs the real app against DynamoDB Local: one in-memory instance for the whole test run, and a fresh,
/// seeded table per factory. Each test class gets its own factory, so its own empty set of users.
/// </summary>
public sealed class RequiemWebFactory : WebApplicationFactory<Program>
{
    public const string SetupCode = "test-setup-code";
    public const string DefaultPassword = "correct horse battery staple";

    /// <summary>Started on first use; the process ends with the test run.</summary>
    private static readonly Lazy<DynamoDbLocal> Local = new(() => DynamoDbLocal.Start());

    private readonly string _table = $"requiem-test-{Guid.NewGuid():N}";

    /// <summary>Where this factory's uploaded portraits go (the app serves them under /media/).</summary>
    public string MediaFolder { get; } = Path.Combine(Path.GetTempPath(), "requiem-tests", $"media-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DynamoDB:ServiceUrl", Local.Value.ServiceUrl);
        builder.UseSetting("DynamoDB:TableName", _table);
        builder.UseSetting("DynamoDB:CreateTable", "true");
        builder.UseSetting("Auth:SetupCode", SetupCode);
        builder.UseSetting("Media:LocalPath", MediaFolder);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDatabaseSetup>().InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        return host;
    }

    /// <summary>Creates a user directly in the store (bypassing first-run setup).</summary>
    public async Task CreateUserAsync(string email, string password = DefaultPassword, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<RequiemUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (await users.FindByEmailAsync(email) is not null) return;

        var user = new RequiemUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = email.Split('@')[0] };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole(role));
            await users.AddToRoleAsync(user, role);
        }
    }

    /// <summary>A client holding the auth cookie of a signed-in user (every account is alike).</summary>
    public async Task<HttpClient> CreateMemberClientAsync(string email, params string[] roles)
    {
        await CreateUserAsync(email, DefaultPassword, roles);
        return await SignInAsync(email, DefaultPassword);
    }

    /// <summary>A signed-in user who has started a campaign, so tells it.</summary>
    public async Task<Storyteller> CreateStorytellerAsync(
        string email = "st@example.test", string campaign = "Test Chronicle", params string[] roles)
    {
        var client = await CreateMemberClientAsync(email, roles);
        var response = await client.PostAsJsonAsync("/api/campaigns", new CampaignForm { Name = campaign }, RequiemJson.Options);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Creating a campaign as {email} failed: {(int)response.StatusCode}");
        var view = (await response.Content.ReadFromJsonAsync<CampaignView>(RequiemJson.Options))!;
        return new Storyteller(client, view.Id);
    }

    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = email, Password = password }, RequiemJson.Options);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Sign-in as {email} failed: {(int)response.StatusCode}");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(MediaFolder)) Directory.Delete(MediaFolder, recursive: true);
    }
}
