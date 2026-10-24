using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>
/// Who may see and change what (PHASE2.md §4). There are no account types: the same person can tell
/// one campaign and play in another. Each test makes its own users, so the class can share one app and
/// database.
/// </summary>
public class AccessTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    // ---------- Accounts ----------

    [Fact]
    public async Task Everyone_can_read_the_catalog()
    {
        var user = await MemberAsync("reader");
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/catalog/powers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/catalog/bloodlines")).StatusCode);
    }

    [Fact]
    public async Task Only_the_site_admin_edits_the_catalog()
    {
        var (storyteller, _) = await factory.CreateStorytellerAsync($"st-{Guid.NewGuid():N}@example.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await storyteller.PutAsJsonAsync("/api/catalog/powers/1", new PowerEditForm { Name = "x" }, RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_requests_are_refused() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/catalog/powers")).StatusCode);

    [Fact]
    public async Task Characters_and_scenes_need_a_campaign_the_user_tells()
    {
        var (alice, _) = await factory.CreateStorytellerAsync("alice-own@example.test");
        var (_, bobsCampaign) = await factory.CreateStorytellerAsync("bob-own@example.test");

        foreach (Guid? campaign in new Guid?[] { null, bobsCampaign, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/characters/supporting-cast",
                new SupportingCastForm { Name = "Stray", CampaignId = campaign }, RequiemJson.Options)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/characters/full-sheet",
                new FullSheetForm { Name = "Stray", CampaignId = campaign }, RequiemJson.Options)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/scenes",
                new SceneForm { Name = "Stray", CampaignId = campaign }, RequiemJson.Options)).StatusCode);
        }
    }

    // ---------- Storytellers of different campaigns ----------

    [Fact]
    public async Task A_storyteller_cannot_see_or_change_another_campaigns_npcs()
    {
        var alice = await factory.CreateStorytellerAsync("alice@example.test");
        var (bob, _) = await factory.CreateStorytellerAsync("bob@example.test");

        var sheet = await CreateFullSheetAsync(alice, "Alice's Prince");
        var cast = await CreateSupportingCastAsync(alice, "Alice's Bouncer");

        Assert.DoesNotContain(await ListAsync(bob), c => c.Id == sheet.Id || c.Id == cast.Id);
        Assert.Contains(await ListAsync(alice.Client), c => c.Id == sheet.Id);

        // Everything about another campaign's NPC reads as "not found", so its existence doesn't leak.
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
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/characters/{sheet.Id}/full-sheet")).StatusCode);
    }

    [Fact]
    public async Task A_storyteller_cannot_borrow_another_campaigns_npcs_for_scenes_or_sires()
    {
        var carol = await factory.CreateStorytellerAsync("carol@example.test");
        var dave = await factory.CreateStorytellerAsync("dave@example.test");
        var carols = await CreateFullSheetAsync(carol, "Carol's Sire");

        var scene = await dave.Client.PostAsJsonAsync("/api/scenes",
            new SceneForm { Name = "Heist", CampaignId = dave.CampaignId, CharacterIds = [carols.Id!.Value] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, scene.StatusCode);

        var childe = await dave.Client.PostAsJsonAsync("/api/characters/full-sheet",
            new FullSheetForm { Name = "Dave's Childe", CampaignId = dave.CampaignId, SireId = carols.Id }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, childe.StatusCode);
    }

    [Fact]
    public async Task A_storyteller_cannot_see_another_campaigns_scenes()
    {
        var erin = await factory.CreateStorytellerAsync("erin@example.test");
        var (frank, _) = await factory.CreateStorytellerAsync("frank@example.test");
        var created = await erin.Client.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Erin's Elysium", CampaignId = erin.CampaignId }, RequiemJson.Options);
        var scene = (await created.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options))!;

        Assert.DoesNotContain((await frank.GetFromJsonAsync<List<SceneSummary>>("/api/scenes", RequiemJson.Options))!, s => s.Id == scene.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await frank.GetAsync($"/api/scenes/{scene.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await frank.PutAsJsonAsync($"/api/scenes/{scene.Id}", new SceneForm { Name = "Mine now" }, RequiemJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await frank.DeleteAsync($"/api/scenes/{scene.Id}")).StatusCode);
    }

    // ---------- One person, two roles ----------

    [Fact]
    public async Task Telling_one_campaign_gives_no_storyteller_access_to_another_you_play_in()
    {
        // Lee tells "Lee's Game" and plays in Ivy's "Chicago by Night".
        var lee = await factory.CreateStorytellerAsync("lee@example.test", "Lee's Game");
        var ivy = await factory.CreateStorytellerAsync("ivy@example.test", "Chicago by Night");
        await AddMemberAsync(ivy.CampaignId, "lee@example.test", CampaignRole.Player);

        var leesNpc = await CreateSupportingCastAsync(lee, "Lee's Fixer");
        var ivysNpc = await CreateFullSheetAsync(ivy, "Ivy's Prince");

        var visible = await ListAsync(lee.Client);
        Assert.Contains(visible, c => c.Id == leesNpc.Id);
        Assert.DoesNotContain(visible, c => c.Id == ivysNpc.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await lee.Client.GetAsync($"/api/characters/{ivysNpc.Id}/full-sheet")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lee.Client.PostAsJsonAsync("/api/characters/supporting-cast",
            new SupportingCastForm { Name = "Smuggled in", CampaignId = ivy.CampaignId }, RequiemJson.Options)).StatusCode);

        // Both campaigns are on Lee's list, each with Lee's role in it.
        var campaigns = (await lee.Client.GetFromJsonAsync<List<CampaignSummary>>("/api/campaigns", RequiemJson.Options))!;
        Assert.Equal(CampaignRole.Storyteller, campaigns.Single(c => c.Id == lee.CampaignId).MyRole);
        Assert.Equal(CampaignRole.Player, campaigns.Single(c => c.Id == ivy.CampaignId).MyRole);
    }

    // ---------- Helpers ----------

    private async Task<HttpClient> MemberAsync(string name) =>
        await factory.CreateMemberClientAsync($"{name}-{Guid.NewGuid():N}@example.test");

    private static async Task<List<CharacterSummary>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<CharacterSummary>>("/api/characters", RequiemJson.Options))!;

    private static async Task<FullSheetForm> CreateFullSheetAsync(Storyteller storyteller, string name)
    {
        var response = await storyteller.Client.PostAsJsonAsync("/api/characters/full-sheet",
            new FullSheetForm { Name = name, CampaignId = storyteller.CampaignId }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options))!;
    }

    private static async Task<SupportingCastForm> CreateSupportingCastAsync(Storyteller storyteller, string name)
    {
        var response = await storyteller.Client.PostAsJsonAsync("/api/characters/supporting-cast",
            new SupportingCastForm { Name = name, CampaignId = storyteller.CampaignId }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private async Task AddMemberAsync(Guid campaignId, string email, CampaignRole role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<RequiemUser>>().FindByEmailAsync(email))!;
        db.CampaignMembers.Add(new CampaignMember { CampaignId = campaignId, UserId = user.Id, Role = role, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        Assert.True(await db.CampaignMembers.AnyAsync(m => m.CampaignId == campaignId && m.UserId == user.Id));
    }
}
