using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Requiem.Data;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>Campaigns, members and invite links (PHASE2.md milestone 2.1). Each test makes its own users.</summary>
public class CampaignApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    // ---------- Creating ----------

    [Fact]
    public async Task A_gm_creates_a_campaign_and_runs_it()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Chicago by Night");

        Assert.Equal(CampaignRole.Storyteller, campaign.MyRole);
        Assert.Equal(CampaignRole.Storyteller, Assert.Single(campaign.Members).Role);
        var summary = Assert.Single(await ListAsync(gm));
        Assert.Equal(("Chicago by Night", CampaignRole.Storyteller, 0), (summary.Name, summary.MyRole, summary.PlayerCount));
    }

    [Fact]
    public async Task Players_cannot_create_campaigns() =>
        Assert.Equal(HttpStatusCode.Forbidden,
            (await (await PlayerAsync()).PostAsJsonAsync("/api/campaigns", new CampaignForm { Name = "Mine" }, RequiemJson.Options)).StatusCode);

    [Fact]
    public async Task A_campaign_must_allow_some_kind_of_character()
    {
        var response = await (await GmAsync()).PostAsJsonAsync("/api/campaigns",
            new CampaignForm { Name = "Nobody", AllowedTemplates = CharacterTemplates.None }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Invites ----------

    [Fact]
    public async Task A_new_player_joins_through_an_invite_link()
    {
        var gm = await GmAsync("Ivy");
        var campaign = await CreateCampaignAsync(gm, "Rack Club");
        var invite = await CreateInviteAsync(gm, campaign.Id, maxUses: 1);

        var anonymous = factory.CreateClient();
        var preview = (await anonymous.GetFromJsonAsync<InvitePreview>($"/api/invites/{invite.Token}", RequiemJson.Options))!;
        Assert.Equal(("Rack Club", true), (preview.CampaignName, preview.IsUsable));
        Assert.StartsWith("Ivy", preview.StorytellerName); // the test factory names users after their email

        var join = await anonymous.PostAsJsonAsync($"/api/invites/{invite.Token}/join", NewPlayer("Kim"), RequiemJson.Options);
        Assert.True(join.IsSuccessStatusCode, await join.Content.ReadAsStringAsync());
        var joined = (await join.Content.ReadFromJsonAsync<JoinedCampaign>(RequiemJson.Options))!;
        Assert.Equal(campaign.Id, joined.CampaignId);
        Assert.Empty(joined.User.Roles); // a player, not a GM

        // The new player is signed in and sees the campaign as a player.
        Assert.Equal(CampaignRole.Player, Assert.Single(await ListAsync(anonymous)).MyRole);
        var view = (await anonymous.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!;
        Assert.Empty(view.Invites);
        Assert.All(view.Members, m => Assert.Null(m.Email)); // players see names, not emails
        Assert.Equal("Kim", Assert.Single(view.Members, m => m.IsMe).DisplayName);
        Assert.Contains((await gm.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!.Members,
            m => m.DisplayName == "Kim" && m.Email != null);

        // A one-use link is spent.
        var spent = (await factory.CreateClient().GetFromJsonAsync<InvitePreview>($"/api/invites/{invite.Token}", RequiemJson.Options))!;
        Assert.False(spent.IsUsable);
        var again = await factory.CreateClient().PostAsJsonAsync($"/api/invites/{invite.Token}/join", NewPlayer("Lee"), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Gone, again.StatusCode);
    }

    [Fact]
    public async Task An_existing_user_accepts_an_invite_once()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Elysium");
        var invite = await CreateInviteAsync(gm, campaign.Id, maxUses: 2);
        var player = await PlayerAsync();

        Assert.True((await player.PostAsync($"/api/invites/{invite.Token}/accept", null)).IsSuccessStatusCode);
        Assert.True((await player.PostAsync($"/api/invites/{invite.Token}/accept", null)).IsSuccessStatusCode);

        // Accepting twice joins once and spends one use.
        var view = (await gm.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!;
        Assert.Equal(2, view.Members.Count);
        Assert.Equal(1, Assert.Single(view.Invites).UsesLeft);
    }

    [Fact]
    public async Task Revoked_and_unknown_links_cannot_be_used()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Revoked");
        var invite = await CreateInviteAsync(gm, campaign.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await gm.DeleteAsync($"/api/campaigns/{campaign.Id}/invites/{invite.Invite.Id}")).StatusCode);

        var preview = (await factory.CreateClient().GetFromJsonAsync<InvitePreview>($"/api/invites/{invite.Token}", RequiemJson.Options))!;
        Assert.Contains("withdrawn", preview.Problem);
        Assert.Equal(HttpStatusCode.Gone, (await (await PlayerAsync()).PostAsync($"/api/invites/{invite.Token}/accept", null)).StatusCode);

        var unknown = (await factory.CreateClient().GetFromJsonAsync<InvitePreview>("/api/invites/not-a-real-token", RequiemJson.Options))!;
        Assert.False(unknown.IsUsable);
        Assert.Null(unknown.CampaignName);
    }

    [Fact]
    public async Task Invite_tokens_are_stored_only_as_hashes()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Hashed");
        var invite = await CreateInviteAsync(gm, campaign.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
        var stored = await db.CampaignInvites.SingleAsync(i => i.Id == invite.Invite.Id);
        Assert.NotEqual(invite.Token, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }

    [Fact]
    public async Task Joining_with_an_email_already_in_use_is_refused()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Dupes");
        var invite = await CreateInviteAsync(gm, campaign.Id, maxUses: 1);
        var email = $"dupe-{Guid.NewGuid():N}@example.test";
        await factory.CreateUserAsync(email);

        var response = await factory.CreateClient().PostAsJsonAsync($"/api/invites/{invite.Token}/join",
            NewPlayer("Copycat", email), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(nameof(JoinRequest.Email), (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options))!.Errors.Keys);

        // The failed attempt neither spent the link's one use nor left a member behind.
        var view = (await gm.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!;
        Assert.Equal(1, Assert.Single(view.Invites).UsesLeft);
        Assert.Single(view.Members);
    }

    // ---------- Members and settings ----------

    [Fact]
    public async Task Players_can_edit_the_coterie_script_but_not_the_settings()
    {
        var (gm, player, campaign) = await CampaignWithPlayerAsync();

        var settings = await player.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", new CampaignForm { Name = "Hijacked" }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);

        var coterie = await player.PutAsJsonAsync($"/api/campaigns/{campaign.Id}/coterie-script",
            new CoterieScriptForm { Text = "We never leave one behind" }, RequiemJson.Options);
        Assert.True(coterie.IsSuccessStatusCode);
        Assert.Equal("We never leave one behind",
            (await gm.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!.CoterieScript);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.CampaignId == campaign.Id && a.Action == "campaign.coterie-script"));
    }

    [Fact]
    public async Task Non_members_cannot_see_a_campaign()
    {
        var (_, _, campaign) = await CampaignWithPlayerAsync();
        var outsider = await GmAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/campaigns/{campaign.Id}")).StatusCode);
        Assert.DoesNotContain(await ListAsync(outsider), c => c.Id == campaign.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/invites", new InviteForm(), RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Players_cannot_make_invites_or_remove_others()
    {
        var (gm, player, campaign) = await CampaignWithPlayerAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await player.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/invites", new InviteForm(), RequiemJson.Options)).StatusCode);

        var gmId = (await gm.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!
            .Members.Single(m => m.Role == CampaignRole.Storyteller).UserId;
        Assert.Equal(HttpStatusCode.Forbidden, (await player.DeleteAsync($"/api/campaigns/{campaign.Id}/members/{gmId}")).StatusCode);
    }

    [Fact]
    public async Task Players_can_leave_and_storytellers_can_remove_them_but_not_the_last_storyteller()
    {
        var (gm, player, campaign) = await CampaignWithPlayerAsync();
        var view = (await gm.GetFromJsonAsync<CampaignView>($"/api/campaigns/{campaign.Id}", RequiemJson.Options))!;
        var playerId = view.Members.Single(m => m.Role == CampaignRole.Player).UserId;
        var gmId = view.Members.Single(m => m.Role == CampaignRole.Storyteller).UserId;

        Assert.Equal(HttpStatusCode.Conflict, (await gm.DeleteAsync($"/api/campaigns/{campaign.Id}/members/{gmId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await player.DeleteAsync($"/api/campaigns/{campaign.Id}/members/{playerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/api/campaigns/{campaign.Id}")).StatusCode);
    }

    [Fact]
    public async Task Campaigns_with_characters_are_archived_not_deleted()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Busy");
        await CreateNpcAsync(gm, "Prince", campaign.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await gm.DeleteAsync($"/api/campaigns/{campaign.Id}")).StatusCode);
        var archived = await gm.PostAsync($"/api/campaigns/{campaign.Id}/archive", null);
        Assert.NotNull((await archived.Content.ReadFromJsonAsync<CampaignView>(RequiemJson.Options))!.ArchivedAt);

        // An archived campaign takes no new players.
        var invite = await CreateInviteAsync(gm, campaign.Id);
        Assert.Contains("archived", (await factory.CreateClient().GetFromJsonAsync<InvitePreview>($"/api/invites/{invite.Token}", RequiemJson.Options))!.Problem);

        var empty = await CreateCampaignAsync(gm, "Empty");
        Assert.Equal(HttpStatusCode.NoContent, (await gm.DeleteAsync($"/api/campaigns/{empty.Id}")).StatusCode);
    }

    // ---------- Campaign NPCs and scenes ----------

    [Fact]
    public async Task Npcs_are_created_in_moved_between_and_listed_by_campaign()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Roster");
        var inCampaign = await CreateNpcAsync(gm, "Sheriff", campaign.Id);
        var personal = await CreateNpcAsync(gm, "Contact", null);

        Assert.Equal([inCampaign], (await RosterAsync(gm, $"campaignId={campaign.Id}")).Select(c => c.Id));
        Assert.Contains(await RosterAsync(gm, "personalOnly=true"), c => c.Id == personal);
        Assert.DoesNotContain(await RosterAsync(gm, "personalOnly=true"), c => c.Id == inCampaign);

        Assert.Equal(HttpStatusCode.NoContent,
            (await gm.PostAsJsonAsync($"/api/characters/{personal}/move", new MoveCharacterRequest(campaign.Id), RequiemJson.Options)).StatusCode);
        Assert.Equal(2, (await RosterAsync(gm, $"campaignId={campaign.Id}")).Count);

        Assert.Equal(HttpStatusCode.NoContent,
            (await gm.PostAsJsonAsync($"/api/characters/{inCampaign}/move", new MoveCharacterRequest(null), RequiemJson.Options)).StatusCode);
        Assert.Contains(await RosterAsync(gm, "personalOnly=true"), c => c.Id == inCampaign);
    }

    [Fact]
    public async Task Npcs_cannot_be_put_into_someone_elses_campaign()
    {
        var owner = await GmAsync();
        var campaign = await CreateCampaignAsync(owner, "Not yours");
        var other = await GmAsync();

        var create = await other.PostAsJsonAsync("/api/characters/supporting-cast",
            new SupportingCastForm { Name = "Spy", CampaignId = campaign.Id }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);

        var mine = await CreateNpcAsync(other, "Mine", null);
        var move = await other.PostAsJsonAsync($"/api/characters/{mine}/move", new MoveCharacterRequest(campaign.Id), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);

        var scene = await other.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Crash", CampaignId = campaign.Id }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, scene.StatusCode);
    }

    [Fact]
    public async Task Campaign_scenes_are_listed_by_campaign()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Scenes");
        var created = await gm.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Court", CampaignId = campaign.Id }, RequiemJson.Options);
        var scene = (await created.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options))!;
        Assert.Equal(campaign.Id, scene.CampaignId);

        var listed = (await gm.GetFromJsonAsync<List<SceneSummary>>($"/api/scenes?campaignId={campaign.Id}", RequiemJson.Options))!;
        Assert.Equal(scene.Id, Assert.Single(listed).Id);
    }

    // ---------- Helpers ----------

    private async Task<HttpClient> GmAsync(string? name = null) =>
        await factory.CreateGmClientAsync($"{name ?? "gm"}-{Guid.NewGuid():N}@example.test");

    private async Task<HttpClient> PlayerAsync()
    {
        var email = $"player-{Guid.NewGuid():N}@example.test";
        await factory.CreateUserAsync(email);
        return await factory.SignInAsync(email, RequiemWebFactory.DefaultPassword);
    }

    private static JoinRequest NewPlayer(string name, string? email = null) => new()
    {
        DisplayName = name, Email = email ?? $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
        Password = RequiemWebFactory.DefaultPassword, ConfirmPassword = RequiemWebFactory.DefaultPassword,
    };

    private static async Task<CampaignView> CreateCampaignAsync(HttpClient gm, string name)
    {
        var response = await gm.PostAsJsonAsync("/api/campaigns", new CampaignForm { Name = name }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CampaignView>(RequiemJson.Options))!;
    }

    private static async Task<CreatedInvite> CreateInviteAsync(HttpClient gm, Guid campaignId, int? maxUses = null)
    {
        var response = await gm.PostAsJsonAsync($"/api/campaigns/{campaignId}/invites", new InviteForm { MaxUses = maxUses }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedInvite>(RequiemJson.Options))!;
    }

    private async Task<(HttpClient Gm, HttpClient Player, CampaignView Campaign)> CampaignWithPlayerAsync()
    {
        var gm = await GmAsync();
        var campaign = await CreateCampaignAsync(gm, "Shared");
        var invite = await CreateInviteAsync(gm, campaign.Id);
        var player = await PlayerAsync();
        Assert.True((await player.PostAsync($"/api/invites/{invite.Token}/accept", null)).IsSuccessStatusCode);
        return (gm, player, campaign);
    }

    private static async Task<List<CampaignSummary>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<CampaignSummary>>("/api/campaigns", RequiemJson.Options))!;

    private static async Task<Guid> CreateNpcAsync(HttpClient gm, string name, Guid? campaignId)
    {
        var response = await gm.PostAsJsonAsync("/api/characters/supporting-cast",
            new SupportingCastForm { Name = name, CampaignId = campaignId }, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!.Id!.Value;
    }

    private static async Task<List<CharacterSummary>> RosterAsync(HttpClient gm, string query) =>
        (await gm.GetFromJsonAsync<List<CharacterSummary>>($"/api/characters?{query}", RequiemJson.Options))!;
}
