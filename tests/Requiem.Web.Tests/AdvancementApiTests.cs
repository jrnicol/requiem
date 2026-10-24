using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

/// <summary>Corruption, Links, Scripts, XP awards and spends for characters in play (PHASE2.md §6).</summary>
public class AdvancementApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    private static readonly string[] Acts = ["Took sunlight damage", "Lost Dominion to the Beast", "Committed diablerie", "Caused a mortal great terror or panic"];

    // ---------- Corruption ----------

    [Fact]
    public async Task Four_different_vampiric_acts_unlock_spending()
    {
        var (table, id) = await VampireAsync();
        await AwardAsync(table, id, 20);

        // Locked until then.
        var early = await Spend(table.Player, id, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl });
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);

        foreach (var act in Acts.Take(3)) await CorruptionAsync(table.Player, id, act);
        var repeat = await table.Player.PostAsJsonAsync($"/api/player-characters/{id}/corruption", new CorruptionRequest { Act = Acts[0] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);

        var view = await CorruptionAsync(table.Player, id, Acts[3]);
        Assert.Equal(("Corruption", 4, true), (view.ProgressName, view.Progress, view.Unlocked));
        Assert.Equal(HttpStatusCode.BadRequest, (await table.Player.PostAsJsonAsync($"/api/player-characters/{id}/corruption",
            new CorruptionRequest { Act = "Used a 5-dot Discipline" }, RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Spending_raises_the_trait_and_the_storyteller_can_undo_it()
    {
        var (table, id) = await UnlockedVampireAsync(xp: 20);

        var response = await Spend(table.Player, id, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var view = (await response.Content.ReadFromJsonAsync<AdvancementView>(RequiemJson.Options))!;
        Assert.Equal((20, 12, 8), (view.Earned, view.Spent, view.Available)); // Brawl 3 → 4 costs 12
        Assert.Equal(4, (await SheetAsync(table.Player, id)).Skills[Skill.Brawl]);

        // The player sees the spend but can't undo it; the Storyteller can.
        var spend = view.History.First(h => h.Summary.StartsWith("Spent 12 XP"));
        Assert.False(spend.CanUndo);
        Assert.Equal(HttpStatusCode.NotFound, (await table.Player.PostAsync($"/api/audit/{spend.Id}/undo", null)).StatusCode);
        var stView = await AdvancementAsync(table.Storyteller, id);
        Assert.True(stView.History.Single(h => h.Id == spend.Id).CanUndo);

        Assert.Equal(HttpStatusCode.NoContent, (await table.Storyteller.PostAsync($"/api/audit/{spend.Id}/undo", null)).StatusCode);
        Assert.Equal(3, (await SheetAsync(table.Player, id)).Skills[Skill.Brawl]);
        Assert.Equal(20, (await AdvancementAsync(table.Player, id)).Available);
        Assert.Equal(HttpStatusCode.Conflict, (await table.Storyteller.PostAsync($"/api/audit/{spend.Id}/undo", null)).StatusCode);
    }

    [Fact]
    public async Task Done_spending_resets_corruption()
    {
        var (table, id) = await UnlockedVampireAsync(xp: 20);
        Assert.True((await Spend(table.Player, id, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl })).IsSuccessStatusCode);

        var view = await PlayerTable.PostAsync<AdvancementView>(table.Player, $"/api/player-characters/{id}/done-spending");
        Assert.Equal((0, false), (view.Progress, view.Unlocked));
        Assert.Equal(HttpStatusCode.BadRequest, (await Spend(table.Player, id, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl })).StatusCode);
    }

    [Fact]
    public async Task The_next_award_ends_a_spending_session()
    {
        var (table, id) = await UnlockedVampireAsync(xp: 20);
        Assert.True((await Spend(table.Player, id, new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Brawl })).IsSuccessStatusCode);

        await AwardAsync(table, id, 3);
        var view = await AdvancementAsync(table.Player, id);
        Assert.Equal((0, 11), (view.Progress, view.Available));
    }

    [Fact]
    public async Task An_award_before_spending_leaves_corruption_alone()
    {
        var (table, id) = await UnlockedVampireAsync(xp: 5);
        await AwardAsync(table, id, 3);
        Assert.Equal(4, (await AdvancementAsync(table.Player, id)).Progress);
    }

    // ---------- Awards ----------

    [Fact]
    public async Task Only_storytellers_award_xp_and_only_to_characters_in_play()
    {
        var (table, id) = await VampireAsync();
        var byPlayer = await table.Player.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/xp-awards",
            new XpAwardRequest { Amount = 5, Reason = "Gift", CharacterIds = [id] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, byPlayer.StatusCode);

        var unknown = await table.Storyteller.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/xp-awards",
            new XpAwardRequest { Amount = 5, Reason = "Gift", CharacterIds = [Guid.NewGuid()] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        await AwardAsync(table, id, 2);
        var view = await AdvancementAsync(table.Player, id);
        Assert.Equal(2, view.Earned);
        Assert.Contains(view.History, h => h.Summary == "+2 XP: Botched roll.");
    }

    // ---------- Scripts ----------

    [Fact]
    public async Task Marking_all_four_scripts_earns_xp_and_clears_them()
    {
        var (table, id) = await VampireAsync();
        AdvancementView view = null!;
        for (var slot = 1; slot <= 4; slot++)
            view = await PlayerTable.PostAsync<AdvancementView>(table.Player, $"/api/player-characters/{id}/scripts/{slot}/mark", new ScriptMarkRequest(true));

        Assert.Equal(4, view.Earned); // a vampire's four Scripts are worth 4 XP
        Assert.All(view.Scripts, s => Assert.False(s.IsMarked));
        Assert.Contains(view.History, h => h.Summary == "+4 XP: All four Scripts played out.");
    }

    // ---------- Links ----------

    [Fact]
    public async Task Four_links_unlock_spending_and_earn_one_xp()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var dana = await PlayerTable.ActiveCharacterAsync(table, PlayerTable.CompleteMortal("Dana"));
        var friend = await PlayerTable.JoinAsync(factory, table, "friend");
        var sam = await PlayerTable.ActiveCharacterAsync(table, PlayerTable.CompleteMortal("Sam"), friend);

        // The other player sees Sam's name (not the sheet) to choose for a Link.
        Assert.Contains((await table.Player.GetFromJsonAsync<List<PlayerCharacterSummary>>($"/api/campaigns/{table.CampaignId}/player-characters", RequiemJson.Options))!,
            c => c.Id == sam && !c.IsMine);
        Assert.Equal(HttpStatusCode.NotFound, (await table.Player.GetAsync($"/api/player-characters/{sam}")).StatusCode);

        var withSelf = await table.Player.PostAsJsonAsync($"/api/player-characters/{dana}/links", new LinkRequest { WithCharacterIds = [dana] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, withSelf.StatusCode);

        AdvancementView view = null!;
        foreach (var kind in new[] { LinkKind.Camaraderie, LinkKind.Support, LinkKind.CedeSpotlight, LinkKind.Support })
            view = await PlayerTable.PostAsync<AdvancementView>(table.Player, $"/api/player-characters/{dana}/links",
                new LinkRequest { Kind = kind, WithCharacterIds = [sam] });

        Assert.Equal(("Links", 4, true, 1), (view.ProgressName, view.Progress, view.Unlocked, view.Earned));
        Assert.Contains(view.ProgressEntries, e => e.Text == "Camaraderie with Sam");
    }

    // ---------- Access and undo ----------

    [Fact]
    public async Task Other_players_cannot_act_on_your_character()
    {
        var (table, id) = await VampireAsync();
        var other = await PlayerTable.JoinAsync(factory, table, "other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/player-characters/{id}/advancement")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/player-characters/{id}/corruption",
            new CorruptionRequest { Act = Acts[0] }, RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task The_storyteller_can_undo_a_corruption_act()
    {
        var (table, id) = await VampireAsync();
        var view = await CorruptionAsync(table.Player, id, Acts[0]);
        var entry = view.History.Single(h => h.Summary.StartsWith("Corruption 1/4"));

        Assert.Equal(HttpStatusCode.NoContent, (await table.Storyteller.PostAsync($"/api/audit/{entry.Id}/undo", null)).StatusCode);
        var after = await AdvancementAsync(table.Storyteller, id);
        Assert.Equal(0, after.Progress);
        Assert.True(after.History.Single(h => h.Id == entry.Id).IsUndone);
    }

    // ---------- Helpers ----------

    private async Task<(Table, Guid)> VampireAsync()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var id = await PlayerTable.ActiveCharacterAsync(table, await PlayerTable.CompleteVampireAsync(table.Player));
        return (table, id);
    }

    private async Task<(Table, Guid)> UnlockedVampireAsync(int xp)
    {
        var (table, id) = await VampireAsync();
        await AwardAsync(table, id, xp);
        foreach (var act in Acts) await CorruptionAsync(table.Player, id, act);
        return (table, id);
    }

    private static async Task AwardAsync(Table table, Guid id, int amount)
    {
        var response = await table.Storyteller.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/xp-awards",
            new XpAwardRequest { Amount = amount, Reason = "Botched roll", CharacterIds = [id] }, RequiemJson.Options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static Task<AdvancementView> CorruptionAsync(HttpClient client, Guid id, string act) =>
        PlayerTable.PostAsync<AdvancementView>(client, $"/api/player-characters/{id}/corruption", new CorruptionRequest { Act = act });

    private static Task<HttpResponseMessage> Spend(HttpClient client, Guid id, XpPurchase purchase) =>
        client.PostAsJsonAsync($"/api/player-characters/{id}/spend", purchase, RequiemJson.Options);

    private static async Task<AdvancementView> AdvancementAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<AdvancementView>($"/api/player-characters/{id}/advancement", RequiemJson.Options))!;

    private static async Task<FullSheetForm> SheetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PlayerCharacterView>($"/api/player-characters/{id}", RequiemJson.Options))!.Sheet;
}
