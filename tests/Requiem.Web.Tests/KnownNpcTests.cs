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

        // Storytellers see the same cards, to check what the players see, plus the tier for the edit link.
        Assert.Equal(card with { Tier = CharacterTier.SupportingCast }, Assert.Single(await KnownAsync(table.Storyteller, table.CampaignId)));
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

    private static async Task<SupportingCastForm> CreateNpcAsync(Table table, string name, bool shared, NpcReveal reveals)
    {
        var form = new SupportingCastForm
        {
            Name = name, CampaignId = table.CampaignId, Kind = CharacterKind.Vampire, Faction = "Invictus", Generation = 11,
            Appearance = "Silver cane, older suit", Reputation = "Never forgets a favor", Backstory = "Sired in Paris in 1789.",
            IsShared = shared, Reveals = reveals,
        };
        var response = await table.Storyteller.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private static async Task<List<KnownNpc>> KnownAsync(HttpClient client, Guid campaignId) =>
        (await client.GetFromJsonAsync<List<KnownNpc>>($"/api/campaigns/{campaignId}/known-npcs", RequiemJson.Options))!;
}
