using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>Sharing NPCs with players: a public card showing only what the Storyteller has revealed.</summary>
public class KnownNpcTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task Players_see_shared_npcs_with_only_the_revealed_details()
    {
        var table = await PlayerTable.CreateAsync(factory);
        await CreateNpcAsync(table, "Hidden Harriet", shared: false, NpcReveal.Faction);
        var marcel = await CreateNpcAsync(table, "Marcel Dubois", shared: true, NpcReveal.Faction | NpcReveal.Appearance);

        var cards = await KnownAsync(table.Player, table.CampaignId);
        var card = Assert.Single(cards);
        Assert.Equal("Marcel Dubois", card.Name);
        Assert.Equal("Invictus", card.Faction);
        Assert.Equal("Silver cane, older suit", card.Appearance);
        // Not revealed: stays off the card even though the sheet has it.
        Assert.Null(card.Reputation);
        Assert.Null(card.Backstory);
        Assert.Null(card.Bloodline);
        Assert.Null(card.Generation);
        Assert.Null(card.Kind);
        Assert.Null(card.Tier);

        // Revealing more as the players find out: the next read shows it.
        marcel.Reveals |= NpcReveal.Backstory | NpcReveal.Generation | NpcReveal.Kind;
        var saved = await table.Storyteller.PutAsJsonAsync($"/api/characters/{marcel.Id}/supporting-cast", marcel, RequiemJson.Options);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        card = Assert.Single(await KnownAsync(table.Player, table.CampaignId));
        Assert.Equal("Sired in Paris in 1789.", card.Backstory);
        Assert.Equal(11, card.Generation);
        Assert.Equal(CharacterKind.Vampire, card.Kind);

        // Storytellers see the same cards, to check what the players see, plus the tier and play view for the links.
        Assert.Equal(card with { Tier = CharacterTier.SupportingCast, CanPlay = true }, Assert.Single(await KnownAsync(table.Storyteller, table.CampaignId)));
    }

    [Fact]
    public async Task The_storytellers_roster_has_every_profile_detail_whatever_is_revealed()
    {
        var table = await PlayerTable.CreateAsync(factory);
        await CreateNpcAsync(table, "Hidden Harriet", shared: false, NpcReveal.None);
        await CreateNpcAsync(table, "Marcel Dubois", shared: true, NpcReveal.Faction);

        var roster = (await table.Storyteller.GetFromJsonAsync<List<CharacterSummary>>(
            $"/api/characters?campaignId={table.CampaignId}", RequiemJson.Options))!;
        var harriet = Assert.Single(roster, c => c.Name == "Hidden Harriet");
        Assert.False(harriet.IsShared);
        Assert.Equal(11, harriet.Generation);
        Assert.Equal("Silver cane, older suit", harriet.Appearance);
        Assert.Equal("Never forgets a favor", harriet.Reputation);
        Assert.Equal("Sired in Paris in 1789.", harriet.Backstory);
        Assert.True(Assert.Single(roster, c => c.Name == "Marcel Dubois").IsShared);
    }

    [Fact]
    public async Task Profile_pages_show_storytellers_everything_and_players_what_is_revealed()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var marcel = await CreateNpcAsync(table, "Marcel Dubois", shared: true, NpcReveal.Faction | NpcReveal.Appearance);
        var harriet = await CreateNpcAsync(table, "Hidden Harriet", shared: false, NpcReveal.Faction);

        var full = await ProfileAsync(table.Storyteller, marcel.Id);
        Assert.Equal("Sired in Paris in 1789.", full.Backstory);
        Assert.Equal(CharacterKind.Vampire, full.Kind);
        Assert.Equal(CharacterTier.SupportingCast, full.Tier);
        Assert.Equal("Sired in Paris in 1789.", (await ProfileAsync(table.Storyteller, harriet.Id)).Backstory);

        // The Storyteller's Players' View is exactly the players' page, plus the tier and play view for the links.
        var asPlayers = await ProfileAsync(table.Storyteller, marcel.Id, playersView: true);
        var player = await ProfileAsync(table.Player, marcel.Id);
        Assert.Equal(player with { Tier = CharacterTier.SupportingCast, CanPlay = true }, asPlayers);
        Assert.Equal("Silver cane, older suit", player.Appearance);
        Assert.Null(player.Backstory);
        Assert.Null(player.Kind);
        Assert.Null(player.Tier);

        // Unshared NPCs aren't there for players, nor in the Storyteller's Players' View.
        Assert.Equal(HttpStatusCode.NotFound, (await table.Player.GetAsync($"/api/characters/{harriet.Id}/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await table.Storyteller.GetAsync($"/api/characters/{harriet.Id}/profile?playersView=true")).StatusCode);

        var outsider = await factory.CreateMemberClientAsync($"outsider-{Guid.NewGuid():N}@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/characters/{marcel.Id}/profile")).StatusCode);
    }

    [Fact]
    public async Task A_magi_house_is_labelled_a_house_only_once_revealed()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var tremere = (await table.Storyteller.GetFromJsonAsync<List<BloodlineOption>>("/api/catalog/bloodlines", RequiemJson.Options))!
            .Single(b => b.Name == "Tremere").Id;
        var magus = await CreateNpcAsync(table, "Magus Orrin", shared: true, NpcReveal.Faction, tremere);

        // Not revealed: neither the House nor that it is one.
        var card = Assert.Single(await KnownAsync(table.Player, table.CampaignId));
        Assert.Equal((null, null), (card.Bloodline, card.BloodlineKind));
        Assert.Equal(("Tremere", "House"), ((await ProfileAsync(table.Storyteller, magus.Id)) is var full ? (full.Bloodline, full.BloodlineLabel) : default));

        magus.Reveals |= NpcReveal.Bloodline;
        var saved = await table.Storyteller.PutAsJsonAsync($"/api/characters/{magus.Id}/supporting-cast", magus, RequiemJson.Options);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        card = Assert.Single(await KnownAsync(table.Player, table.CampaignId));
        Assert.Equal(("Tremere", "MagiHouse", "House"), (card.Bloodline, card.BloodlineKind, card.BloodlineLabel));
    }

    [Fact]
    public async Task Outsiders_see_no_cards_and_player_characters_are_never_shared()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var outsider = await factory.CreateMemberClientAsync($"outsider-{Guid.NewGuid():N}@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/campaigns/{table.CampaignId}/known-npcs")).StatusCode);

        var form = PlayerTable.CompleteMortal();
        form.IsShared = true;
        form.Reveals = NpcReveal.Backstory;
        form.Backstory = "Grew up above the bakery.";
        var pc = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, form);
        Assert.False(pc.Sheet.IsShared);
        Assert.Equal("Grew up above the bakery.", pc.Sheet.Backstory);
        Assert.Empty(await KnownAsync(table.Player, table.CampaignId));
    }

    private static async Task<SupportingCastForm> CreateNpcAsync(Table table, string name, bool shared, NpcReveal reveals, int? bloodlineId = null)
    {
        var form = new SupportingCastForm
        {
            Name = name, CampaignId = table.CampaignId, Kind = CharacterKind.Vampire, Faction = "Invictus", Generation = 11, BloodlineId = bloodlineId,
            Appearance = "Silver cane, older suit", Reputation = "Never forgets a favor", Backstory = "Sired in Paris in 1789.",
            IsShared = shared, Reveals = reveals,
        };
        var response = await table.Storyteller.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private static async Task<KnownNpc> ProfileAsync(HttpClient client, Guid? id, bool playersView = false) =>
        (await client.GetFromJsonAsync<KnownNpc>($"/api/characters/{id}/profile{(playersView ? "?playersView=true" : "")}", RequiemJson.Options))!;

    private static async Task<List<KnownNpc>> KnownAsync(HttpClient client, Guid campaignId) =>
        (await client.GetFromJsonAsync<List<KnownNpc>>($"/api/campaigns/{campaignId}/known-npcs", RequiemJson.Options))!;
}
