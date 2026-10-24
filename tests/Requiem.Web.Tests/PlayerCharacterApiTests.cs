using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

/// <summary>Players build, submit and play their own characters; Storytellers approve them (PHASE2.md §5).</summary>
public class PlayerCharacterApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    // ---------- Creating ----------

    [Fact]
    public async Task A_player_starts_a_draft_their_storyteller_can_see()
    {
        var table = await TableAsync();
        var draft = await CreateAsync(table.Player, table.CampaignId, CompleteMortal());

        Assert.Equal((CharacterStatus.Draft, true, true), (draft.Status, draft.IsMine, draft.CanEdit));
        Assert.False(draft.IsStoryteller);

        var listed = Assert.Single(await ListAsync(table.Storyteller, table.CampaignId));
        Assert.Equal((draft.Sheet.Id, CharacterStatus.Draft, false), (listed.Id, listed.Status, listed.IsMine));
        var stView = (await table.Storyteller.GetFromJsonAsync<PlayerCharacterView>($"/api/player-characters/{draft.Sheet.Id}", RequiemJson.Options))!;
        Assert.True(stView.IsStoryteller);
        Assert.False(stView.CanEdit);

        // Player characters aren't NPCs: the Storyteller's roster leaves them out.
        Assert.DoesNotContain((await table.Storyteller.GetFromJsonAsync<List<CharacterSummary>>(
            $"/api/characters?campaignId={table.CampaignId}", RequiemJson.Options))!, c => c.Id == draft.Sheet.Id);
    }

    [Fact]
    public async Task Other_players_and_outsiders_cannot_see_it()
    {
        var table = await TableAsync();
        var draft = await CreateAsync(table.Player, table.CampaignId, CompleteMortal());
        var other = await JoinAsync(table, "other");
        var outsider = await factory.CreateMemberClientAsync($"outsider-{Guid.NewGuid():N}@example.test");

        Assert.Empty(await ListAsync(other, table.CampaignId));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/player-characters/{draft.Sheet.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/player-characters/{draft.Sheet.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/campaigns/{table.CampaignId}/player-characters")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/player-characters/{draft.Sheet.Id}", draft.Sheet, RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task One_character_in_play_at_a_time()
    {
        var table = await TableAsync();
        await CreateAsync(table.Player, table.CampaignId, CompleteMortal());

        var second = await table.Player.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/player-characters", CompleteMortal(), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Only_the_campaigns_templates_and_only_players()
    {
        var table = await TableAsync(CharacterTemplates.Vampire);
        var mortal = await table.Player.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/player-characters", CompleteMortal(), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, mortal.StatusCode);
        Assert.Contains(nameof(FullSheetForm.Kind), (await mortal.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options))!.Errors.Keys);

        var byStoryteller = await table.Storyteller.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/player-characters",
            new FullSheetForm { Name = "Mine", Kind = CharacterKind.Vampire }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, byStoryteller.StatusCode);
    }

    [Fact]
    public async Task Players_cannot_set_links_xp_or_storyteller_notes()
    {
        var table = await TableAsync();
        var form = CompleteMortal();
        form.GmNotes = "I'm secretly the Prince";
        form.Experience = [new() { Amount = 50, Reason = "Gift to myself" }];
        form.SireId = Guid.NewGuid();
        var draft = await CreateAsync(table.Player, table.CampaignId, form);

        Assert.Null(draft.Sheet.GmNotes);
        Assert.Empty(draft.Sheet.Experience);
        Assert.Null(draft.Sheet.SireId);
    }

    // ---------- Submitting and approving ----------

    [Fact]
    public async Task An_incomplete_draft_cannot_be_submitted()
    {
        var table = await TableAsync();
        var draft = await CreateAsync(table.Player, table.CampaignId, new FullSheetForm { Name = "Half done", Kind = CharacterKind.Mortal });

        var response = await table.Player.PostAsync($"/api/player-characters/{draft.Sheet.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var messages = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options))!.Errors[""];
        Assert.Contains("Choose a Concept, or write your own.", messages);
    }

    [Fact]
    public async Task Submit_return_resubmit_approve_then_play()
    {
        var table = await TableAsync();
        var draft = await CreateAsync(table.Player, table.CampaignId, CompleteMortal());
        var id = draft.Sheet.Id!.Value;

        var submitted = await PostAsync(table.Player, $"/api/player-characters/{id}/submit");
        Assert.Equal((CharacterStatus.Submitted, false), (submitted.Status, submitted.CanEdit));
        // Waiting for the Storyteller: no edits until it's withdrawn.
        Assert.Equal(HttpStatusCode.Conflict, (await table.Player.PutAsJsonAsync($"/api/player-characters/{id}", submitted.Sheet, RequiemJson.Options)).StatusCode);
        // Players can't approve their own.
        Assert.Equal(HttpStatusCode.NotFound, (await table.Player.PostAsync($"/api/player-characters/{id}/approve", null)).StatusCode);

        var returned = await PostAsync(table.Storyteller, $"/api/player-characters/{id}/return", new ReturnCharacterRequest("Swap Brawl for Weaponry"));
        Assert.Equal((CharacterStatus.Draft, "Swap Brawl for Weaponry"), (returned.Status, returned.ReviewNote));

        var resubmitted = await PostAsync(table.Player, $"/api/player-characters/{id}/submit");
        Assert.Null(resubmitted.ReviewNote);
        var approved = await PostAsync(table.Storyteller, $"/api/player-characters/{id}/approve");
        Assert.Equal(CharacterStatus.Active, approved.Status);

        // The player tracks their character at the table, without the Storyteller's notes.
        var gmView = (await table.Storyteller.GetFromJsonAsync<FullSheetForm>($"/api/characters/{id}/full-sheet", RequiemJson.Options))!;
        gmView.GmNotes = "Their sire is watching.";
        Assert.True((await table.Storyteller.PutAsJsonAsync($"/api/characters/{id}/full-sheet", gmView, RequiemJson.Options)).IsSuccessStatusCode);
        var play = (await table.Player.GetFromJsonAsync<PlayCharacter>($"/api/characters/{id}/play", RequiemJson.Options))!;
        Assert.Null(play.GmNotes);
        var hurt = await table.Player.PostAsJsonAsync($"/api/characters/{id}/play",
            new PlayAction { Kind = PlayActionKind.AddWound, Amount = 1, DamageType = DamageType.Bashing, Text = "fell down stairs" }, RequiemJson.Options);
        Assert.True(hurt.IsSuccessStatusCode, await hurt.Content.ReadAsStringAsync());
        Assert.Null((await table.Player.GetFromJsonAsync<PlayerCharacterView>($"/api/player-characters/{id}", RequiemJson.Options))!.Sheet.GmNotes);
    }

    [Fact]
    public async Task Drafts_are_not_playable()
    {
        var table = await TableAsync();
        var draft = await CreateAsync(table.Player, table.CampaignId, CompleteMortal());
        Assert.Equal(HttpStatusCode.NotFound, (await table.Player.GetAsync($"/api/characters/{draft.Sheet.Id}/play")).StatusCode);
    }

    [Fact]
    public async Task Once_active_a_player_edits_the_story_not_the_stats()
    {
        var table = await TableAsync();
        var id = await ActiveCharacterAsync(table);
        var view = (await table.Player.GetFromJsonAsync<PlayerCharacterView>($"/api/player-characters/{id}", RequiemJson.Options))!;

        view.Sheet.Notes = "Started keeping a journal.";
        view.Sheet.Attributes[CharacterAttribute.Strength] = 5;
        view.Sheet.Name = "Somebody Else";
        var response = await table.Player.PutAsJsonAsync($"/api/player-characters/{id}", view.Sheet, RequiemJson.Options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<PlayerCharacterView>(RequiemJson.Options))!;

        Assert.Equal("Started keeping a journal.", saved.Sheet.Notes);
        Assert.Equal(3, saved.Sheet.Attributes[CharacterAttribute.Strength]);
        Assert.Equal("Dana Reyes", saved.Sheet.Name);
    }

    [Fact]
    public async Task A_retired_character_makes_room_for_a_new_one()
    {
        var table = await TableAsync();
        var id = await ActiveCharacterAsync(table);

        var retired = await PostAsync(table.Storyteller, $"/api/player-characters/{id}/status", new CharacterStatusRequest(CharacterStatus.Retired));
        Assert.Equal(CharacterStatus.Retired, retired.Status);
        await CreateAsync(table.Player, table.CampaignId, CompleteMortal());

        // With a new one in play, the old one can't come back.
        Assert.Equal(HttpStatusCode.Conflict, (await table.Storyteller.PostAsJsonAsync($"/api/player-characters/{id}/status",
            new CharacterStatusRequest(CharacterStatus.Active), RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task A_player_can_delete_a_draft_only()
    {
        var table = await TableAsync();
        var draft = await CreateAsync(table.Player, table.CampaignId, CompleteMortal());
        Assert.Equal(HttpStatusCode.NoContent, (await table.Player.DeleteAsync($"/api/player-characters/{draft.Sheet.Id}")).StatusCode);

        var id = await ActiveCharacterAsync(table);
        Assert.Equal(HttpStatusCode.Conflict, (await table.Player.DeleteAsync($"/api/player-characters/{id}")).StatusCode);
    }

    [Fact]
    public async Task Concepts_are_in_the_catalog()
    {
        var table = await TableAsync();
        var concepts = (await table.Player.GetFromJsonAsync<List<ConceptOption>>("/api/catalog/concepts", RequiemJson.Options))!;
        Assert.Equal(31, concepts.Count);
        Assert.Equal("Win a competition.", concepts.Single(c => c.Name == "Competitor").WillpowerTrigger);
    }

    // ---------- Helpers (shared with the advancement tests in PlayerTable) ----------

    private Task<Table> TableAsync(CharacterTemplates templates = CharacterTemplates.All) => PlayerTable.CreateAsync(factory, templates);

    private Task<HttpClient> JoinAsync(Table table, string name) => PlayerTable.JoinAsync(factory, table, name);

    private static Task<Guid> ActiveCharacterAsync(Table table) => PlayerTable.ActiveCharacterAsync(table, CompleteMortal());

    private static Task<PlayerCharacterView> CreateAsync(HttpClient player, Guid campaignId, FullSheetForm form) =>
        PlayerTable.CreateCharacterAsync(player, campaignId, form);

    private static Task<PlayerCharacterView> PostAsync(HttpClient client, string url, object? body = null) =>
        PlayerTable.PostAsync<PlayerCharacterView>(client, url, body);

    private static async Task<List<PlayerCharacterSummary>> ListAsync(HttpClient client, Guid campaignId) =>
        (await client.GetFromJsonAsync<List<PlayerCharacterSummary>>($"/api/campaigns/{campaignId}/player-characters", RequiemJson.Options))!;

    private static FullSheetForm CompleteMortal() => PlayerTable.CompleteMortal();
}
