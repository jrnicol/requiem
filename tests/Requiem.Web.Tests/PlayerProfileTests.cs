using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>Player characters' profile pages: every detail, for everyone in the campaign.</summary>
public class PlayerProfileTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task A_player_characters_profile_has_everything_and_links_only_for_its_player_and_the_storyteller()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var other = await PlayerTable.JoinAsync(factory, table, "other");
        var form = PlayerTable.CompleteMortal();
        form.Faction = Factions.Camarilla;
        form.Appearance = "Paint on her boots";
        form.Reputation = "Owes the Sheriff";
        form.Backstory = "Grew up above the bakery.";
        var draft = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, form);
        var id = draft.Sheet.Id!.Value;

        // A draft: its player and the Storyteller have its profile, with the links; only the Storyteller can take it to the table.
        var mine = await ProfileAsync(table.Player, id);
        Assert.True(mine.IsPlayerCharacter);
        Assert.Equal(draft.OwnerName, mine.PlayedBy);
        Assert.Equal(CharacterTier.FullSheet, mine.Tier);
        Assert.False(mine.CanPlay);
        Assert.True((await ProfileAsync(table.Storyteller, id)).CanPlay);
        // The other players, as in the campaign's list, only see characters in play.
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/characters/{id}/profile")).StatusCode);

        await PlayerTable.PostAsync<PlayerCharacterView>(table.Player, $"/api/player-characters/{id}/submit");
        await PlayerTable.PostAsync<PlayerCharacterView>(table.Storyteller, $"/api/player-characters/{id}/approve");

        // In play: every detail for everyone, whatever's revealed, but no links for the other players.
        var theirs = await ProfileAsync(other, id);
        Assert.Equal("Dana Reyes", theirs.Name);
        Assert.Equal(Factions.Camarilla, theirs.Faction);
        Assert.Equal(CharacterKind.Mortal, theirs.Kind);
        Assert.Equal("Paint on her boots", theirs.Appearance);
        Assert.Equal("Owes the Sheriff", theirs.Reputation);
        Assert.Equal("Grew up above the bakery.", theirs.Backstory);
        Assert.Equal(draft.OwnerName, theirs.PlayedBy);
        Assert.Null(theirs.Tier);
        Assert.False(theirs.CanPlay);
        Assert.Equal(theirs with { Tier = CharacterTier.FullSheet, CanPlay = true }, await ProfileAsync(table.Player, id));
        // The Characters tab's cards carry the same profiles.
        var listed = Assert.Single(await ListAsync(other, table.CampaignId));
        Assert.Equal(theirs, listed.Profile);
        Assert.Equal(await ProfileAsync(table.Storyteller, id), Assert.Single(await ListAsync(table.Storyteller, table.CampaignId)).Profile);
        // Players' View is for NPCs; a player character's profile is the same for everyone.
        Assert.Equal(await ProfileAsync(table.Storyteller, id), await ProfileAsync(table.Storyteller, id, playersView: true));

        var outsider = await factory.CreateMemberClientAsync($"outsider-{Guid.NewGuid():N}@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/characters/{id}/profile")).StatusCode);
    }

    private static async Task<List<PlayerCharacterSummary>> ListAsync(HttpClient client, Guid campaignId) =>
        (await client.GetFromJsonAsync<List<PlayerCharacterSummary>>($"/api/campaigns/{campaignId}/player-characters", RequiemJson.Options))!;

    private static async Task<KnownNpc> ProfileAsync(HttpClient client, Guid id, bool playersView = false) =>
        (await client.GetFromJsonAsync<KnownNpc>($"/api/characters/{id}/profile{(playersView ? "?playersView=true" : "")}", RequiemJson.Options))!;
}
