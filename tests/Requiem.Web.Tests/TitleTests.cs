using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

/// <summary>Faction titles: the book's catalog, granted by the Storyteller, revealed to players and shown at the table.</summary>
public class TitleTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task The_catalog_lists_each_factions_titles_with_their_benefits()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var titles = (await table.Player.GetFromJsonAsync<List<TitleOption>>("/api/catalog/titles", RequiemJson.Options))!;
        var prince = Assert.Single(titles, t => t.Name == "Prince");
        Assert.Equal((Factions.Camarilla, "Court"), (prince.Faction, prince.Group));
        Assert.Contains(prince.Benefits, b => b.Name == "Sovereign");
        Assert.Contains(titles, t => t.Faction == Factions.Sabbat && t.Name == "Ductus");
        Assert.Contains(titles, t => t.Faction == Factions.CarthianMovement && t.Name == "Speaker of the Night");
    }

    [Fact]
    public async Task Storytellers_grant_catalog_titles_which_players_see_once_revealed()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var form = new SupportingCastForm
        {
            Name = "Prince Alaric", CampaignId = table.CampaignId, Kind = CharacterKind.Vampire, Faction = Factions.Camarilla,
            // Saved under the catalog's spelling, without repeats.
            Titles = ["prince", "Keeper of Elysium", "Prince"], IsShared = true, Reveals = NpcReveal.Faction,
        };
        var created = await table.Storyteller.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var npc = (await created.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
        Assert.Equal(["Prince", "Keeper of Elysium"], npc.Titles);

        // Not revealed yet: the players' card leaves them off.
        Assert.Null(Assert.Single(await KnownAsync(table.Player, table.CampaignId)).Titles);
        npc.Reveals |= NpcReveal.Titles;
        Assert.True((await table.Storyteller.PutAsJsonAsync($"/api/characters/{npc.Id}/supporting-cast", npc, RequiemJson.Options)).IsSuccessStatusCode);
        Assert.Equal(["Prince", "Keeper of Elysium"], Assert.Single(await KnownAsync(table.Player, table.CampaignId)).Titles);

        npc = (await table.Storyteller.GetFromJsonAsync<SupportingCastForm>($"/api/characters/{npc.Id}/supporting-cast", RequiemJson.Options))!;
        npc.Titles = ["Grand Vizier"];
        var bad = await table.Storyteller.PutAsJsonAsync($"/api/characters/{npc.Id}/supporting-cast", npc, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Unknown title: Grand Vizier.", await bad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Players_cannot_grant_their_own_characters_titles()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var form = PlayerTable.CompleteMortal();
        form.Faction = Factions.Camarilla;
        form.Titles = ["Prince"];
        var pc = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, form);
        Assert.Empty(pc.Sheet.Titles);
        Assert.Equal(Factions.Camarilla, pc.Sheet.Faction);
    }

    [Fact]
    public async Task The_table_shows_what_each_title_grants()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var form = new SupportingCastForm
        {
            Name = "Sheriff Dane", CampaignId = table.CampaignId, Kind = CharacterKind.Vampire, Faction = Factions.Camarilla,
            Titles = ["Sheriff", "Censured"],
        };
        var npc = (await (await table.Storyteller.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options))
            .Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;

        var play = (await table.Storyteller.GetFromJsonAsync<PlayCharacter>($"/api/characters/{npc.Id}/play", RequiemJson.Options))!;
        Assert.Equal(["Sheriff", "Censured"], play.Titles!.Select(t => t.Name));
        Assert.Equal(["Authority", "Posse Comitatus", "Hounds", "Nightwatch"], play.Titles![0].Benefits.Select(b => b.Name));
        Assert.Equal("Negative", play.Titles[1].Group);
    }

    private static async Task<List<KnownNpc>> KnownAsync(HttpClient client, Guid campaignId) =>
        (await client.GetFromJsonAsync<List<KnownNpc>>($"/api/campaigns/{campaignId}/known-npcs", RequiemJson.Options))!;
}
