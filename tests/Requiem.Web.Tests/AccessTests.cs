using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>
/// Who may see and change what (PHASE2.md §4). Each test makes its own users, so the class can share
/// one app and database.
/// </summary>
public class AccessTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    // ---------- Players (signed in, not GMs) ----------

    [Fact]
    public async Task Players_can_read_the_catalog()
    {
        var player = await PlayerClientAsync("reader");
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/api/catalog/powers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/api/catalog/bloodlines")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/characters")]
    [InlineData("GET", "/api/scenes")]
    [InlineData("POST", "/api/characters/supporting-cast")]
    [InlineData("POST", "/api/characters/full-sheet")]
    [InlineData("POST", "/api/scenes")]
    [InlineData("PUT", "/api/catalog/powers/1")]
    public async Task Players_cannot_use_gm_endpoints(string method, string url)
    {
        var player = await PlayerClientAsync("blocked");
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method != "GET") request.Content = JsonContent.Create(new { name = "x" }, options: RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_requests_are_refused() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/catalog/powers")).StatusCode);

    // ---------- Site GMs keep separate personal rosters ----------

    [Fact]
    public async Task A_gm_cannot_see_or_change_another_gms_npcs()
    {
        var alice = await factory.CreateGmClientAsync("alice@example.test");
        var bob = await factory.CreateGmClientAsync("bob@example.test");

        var sheet = await CreateFullSheetAsync(alice, "Alice's Prince");
        var cast = await CreateSupportingCastAsync(alice, "Alice's Bouncer");

        Assert.DoesNotContain(await ListAsync(bob), c => c.Id == sheet.Id || c.Id == cast.Id);
        Assert.Contains(await ListAsync(alice), c => c.Id == sheet.Id);

        // Everything about another GM's NPC reads as "not found", so its existence doesn't leak.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/characters/{sheet.Id}/full-sheet")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/characters/{cast.Id}/supporting-cast")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/characters/{sheet.Id}/play")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/characters/{sheet.Id}/full-sheet", sheet, RequiemJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/characters/{sheet.Id}/play",
            new PlayAction { Kind = PlayActionKind.AdjustWillpower, Amount = -1 }, RequiemJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/characters/{cast.Id}/promote",
            new PromoteRequest(cast.Version), RequiemJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/characters/{sheet.Id}")).StatusCode);

        // Alice's NPC is untouched.
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/characters/{sheet.Id}/full-sheet")).StatusCode);
    }

    [Fact]
    public async Task A_gm_cannot_borrow_another_gms_npcs_for_scenes_or_sires()
    {
        var carol = await factory.CreateGmClientAsync("carol@example.test");
        var dave = await factory.CreateGmClientAsync("dave@example.test");
        var carols = await CreateFullSheetAsync(carol, "Carol's Sire");

        var scene = await dave.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Heist", CharacterIds = [carols.Id!.Value] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, scene.StatusCode);

        var childe = await dave.PostAsJsonAsync("/api/characters/full-sheet",
            new FullSheetForm { Name = "Dave's Childe", SireId = carols.Id }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, childe.StatusCode);
    }

    [Fact]
    public async Task A_gm_cannot_see_another_gms_scenes()
    {
        var erin = await factory.CreateGmClientAsync("erin@example.test");
        var frank = await factory.CreateGmClientAsync("frank@example.test");
        var created = await erin.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Erin's Elysium" }, RequiemJson.Options);
        var scene = (await created.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options))!;

        Assert.DoesNotContain((await frank.GetFromJsonAsync<List<SceneSummary>>("/api/scenes", RequiemJson.Options))!, s => s.Id == scene.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await frank.GetAsync($"/api/scenes/{scene.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await frank.PutAsJsonAsync($"/api/scenes/{scene.Id}", new SceneForm { Name = "Mine now" }, RequiemJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await frank.DeleteAsync($"/api/scenes/{scene.Id}")).StatusCode);
    }

    [Fact]
    public async Task Characters_made_before_phase_2_belong_to_every_gm()
    {
        var gina = await factory.CreateGmClientAsync("gina@example.test");
        var harry = await factory.CreateGmClientAsync("harry@example.test");
        var legacyId = await AddCharacterAsync("Legacy Marcus", owner: null, campaign: null);

        Assert.Contains(await ListAsync(gina), c => c.Id == legacyId);
        Assert.Contains(await ListAsync(harry), c => c.Id == legacyId);
    }

    // ---------- Campaigns ----------

    [Fact]
    public async Task Campaign_characters_are_visible_to_its_storytellers_only()
    {
        var ivy = await factory.CreateGmClientAsync("ivy@example.test");
        var jack = await factory.CreateGmClientAsync("jack@example.test");
        var campaignId = await AddCampaignAsync("Chicago by Night", storytellerEmail: "ivy@example.test", playerEmail: "kim@example.test");
        var npcId = await AddCharacterAsync("Campaign Prince", owner: null, campaign: campaignId);
        var kim = await factory.SignInAsync("kim@example.test", RequiemWebFactory.DefaultPassword);

        Assert.Contains(await ListAsync(ivy), c => c.Id == npcId);
        Assert.Equal(HttpStatusCode.OK, (await ivy.GetAsync($"/api/characters/{npcId}/full-sheet")).StatusCode);

        // Another site GM who isn't in the campaign can't see it.
        Assert.DoesNotContain(await ListAsync(jack), c => c.Id == npcId);
        Assert.Equal(HttpStatusCode.NotFound, (await jack.GetAsync($"/api/characters/{npcId}/full-sheet")).StatusCode);

        // A player in the campaign gets no GM access at all.
        Assert.Equal(HttpStatusCode.Forbidden, (await kim.GetAsync($"/api/characters/{npcId}/full-sheet")).StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<HttpClient> PlayerClientAsync(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@example.test";
        await factory.CreateUserAsync(email);
        return await factory.SignInAsync(email, RequiemWebFactory.DefaultPassword);
    }

    private static async Task<List<CharacterSummary>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<CharacterSummary>>("/api/characters", RequiemJson.Options))!;

    private static async Task<FullSheetForm> CreateFullSheetAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/characters/full-sheet", new FullSheetForm { Name = name }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options))!;
    }

    private static async Task<SupportingCastForm> CreateSupportingCastAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/characters/supporting-cast", new SupportingCastForm { Name = name }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    /// <summary>Writes a character straight to the database, as data made before Phase 2 (or by later features) looks.</summary>
    private async Task<Guid> AddCharacterAsync(string name, string? owner, Guid? campaign)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
        var character = new Character
        {
            Name = name, Tier = CharacterTier.FullSheet, Kind = CharacterKind.Mortal, OwnerUserId = owner, CampaignId = campaign,
            WillpowerDots = 2, WillpowerCurrent = 2, Morality = 7,
        };
        db.Characters.Add(character);
        await db.SaveChangesAsync();
        return character.Id;
    }

    private async Task<Guid> AddCampaignAsync(string name, string storytellerEmail, string playerEmail)
    {
        await factory.CreateUserAsync(playerEmail);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<RequiemUser>>();
        var storyteller = (await users.FindByEmailAsync(storytellerEmail))!;
        var player = (await users.FindByEmailAsync(playerEmail))!;

        var campaign = new Campaign { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
        campaign.Members.Add(new CampaignMember { UserId = storyteller.Id, Role = CampaignRole.Storyteller, JoinedAt = DateTimeOffset.UtcNow });
        campaign.Members.Add(new CampaignMember { UserId = player.Id, Role = CampaignRole.Player, JoinedAt = DateTimeOffset.UtcNow });
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.CampaignMembers.CountAsync(m => m.CampaignId == campaign.Id));
        return campaign.Id;
    }
}
