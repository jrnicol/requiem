using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

/// <summary>A campaign's own character creation budgets, set by its Storyteller.</summary>
public class CreationSettingsTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task Storytellers_set_the_budgets_and_players_build_to_them()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var campaign = (await table.Storyteller.GetFromJsonAsync<CampaignView>($"/api/campaigns/{table.CampaignId}", RequiemJson.Options))!;
        // Until it's set, a campaign has the book's.
        Assert.Equal(10, campaign.Creation!.MeritPoints);
        Assert.Equal(12, campaign.Creation.StartingGeneration);
        Assert.True(campaign.Creation.CanBuyGeneration);

        var creation = new CreationSettings
        {
            PrimaryAttributes = 6, SecondaryAttributes = 5, TertiaryAttributes = 4, MeritPoints = 12, FlawPoints = 3,
            StartingGeneration = 10, CanBuyGeneration = false,
        };
        var saved = await SaveAsync(table, creation);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var view = (await saved.Content.ReadFromJsonAsync<CampaignView>(RequiemJson.Options))!;
        Assert.Equal(6, view.Creation!.PrimaryAttributes);
        Assert.Equal(10, view.Creation.StartingGeneration);
        Assert.False(view.Creation.CanBuyGeneration);

        // Players get them with their character, for the wizard's budget.
        var draft = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, PlayerTable.CompleteMortal());
        Assert.Equal(12, draft.Creation!.MeritPoints);
        Assert.Equal(4, draft.Creation.TertiaryAttributes);
    }

    [Fact]
    public async Task Starting_xp_is_spent_while_building_and_applied_on_approval()
    {
        var table = await PlayerTable.CreateAsync(factory);
        Assert.True((await SaveAsync(table, new CreationSettings { StartingXp = 20 })).IsSuccessStatusCode);

        // Brawl 3 → 4 at XP prices, on top of the creation dots.
        var form = PlayerTable.CompleteMortal();
        form.StartingXpPurchases = [Bought(Skill.Brawl, 3, 4)];
        var id = await PlayerTable.ActiveCharacterAsync(table, form);

        var sheet = (await table.Storyteller.GetFromJsonAsync<PlayerCharacterView>($"/api/player-characters/{id}", RequiemJson.Options))!.Sheet;
        Assert.Equal(4, sheet.Skills[Skill.Brawl]);
        Assert.Empty(sheet.StartingXpPurchases);
        Assert.Equal([(20, "Starting XP"), (-12, "Brawl 3 → 4")], sheet.Experience.Select(x => (x.Amount, x.Reason)));

        // The 8 left over waits for 4 Links like any XP.
        var advancement = (await table.Player.GetFromJsonAsync<AdvancementView>($"/api/player-characters/{id}/advancement", RequiemJson.Options))!;
        Assert.Equal((8, false), (advancement.Available, advancement.Unlocked));
        var spend = await table.Player.PostAsJsonAsync($"/api/player-characters/{id}/spend",
            new XpPurchase { Kind = PurchaseKind.Skill, Skill = Skill.Athletics }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, spend.StatusCode);
        Assert.Contains(advancement.History, h => h.Summary == "+20 starting XP; spent 12 on Brawl 3 → 4.");

        // Raising it later doesn't add to characters already in play.
        Assert.True((await SaveAsync(table, new CreationSettings { StartingXp = 30 })).IsSuccessStatusCode);
        sheet = (await table.Storyteller.GetFromJsonAsync<PlayerCharacterView>($"/api/player-characters/{id}", RequiemJson.Options))!.Sheet;
        Assert.Equal(8, sheet.ExperienceAvailable);
    }

    [Theory]
    [InlineData(20, 2, "Brawl has changed since it was bought.")]
    [InlineData(10, 3, "Costs 12 XP; 10 available.")]
    public async Task Purchases_that_no_longer_fit_block_submitting(int startingXp, int from, string problem)
    {
        var table = await PlayerTable.CreateAsync(factory);
        Assert.True((await SaveAsync(table, new CreationSettings { StartingXp = startingXp })).IsSuccessStatusCode);
        var form = PlayerTable.CompleteMortal();
        form.StartingXpPurchases = [Bought(Skill.Brawl, from, 4)];
        var draft = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, form);

        var submit = await table.Player.PostAsync($"/api/player-characters/{draft.Sheet.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        Assert.Contains(problem, await submit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Storytellers_see_a_flag_when_cheap_dots_go_on_xp_and_high_ones_on_creation()
    {
        var table = await PlayerTable.CreateAsync(factory);
        Assert.True((await SaveAsync(table, new CreationSettings { StartingXp = 20 })).IsSuccessStatusCode);
        // Five new Skills at 1 dot for 15 XP, beside creation dots at 3: the same dots on top would cost 36.
        var form = PlayerTable.CompleteMortal();
        form.StartingXpPurchases =
        [
            Bought(Skill.Expression, 0, 1), Bought(Skill.Persuasion, 0, 1), Bought(Skill.Subterfuge, 0, 1),
            Bought(Skill.Ballistics, 0, 1), Bought(Skill.Weaponry, 0, 1),
        ];
        var draft = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, form);
        await PlayerTable.PostAsync<PlayerCharacterView>(table.Player, $"/api/player-characters/{draft.Sheet.Id}/submit");

        var cards = (await table.Storyteller.GetFromJsonAsync<List<PlayerCharacterSummary>>(
            $"/api/campaigns/{table.CampaignId}/player-characters", RequiemJson.Options))!;
        Assert.True(Assert.Single(cards).XpSplitFlagged);
        // Only the Storyteller sees it.
        var mine = (await table.Player.GetFromJsonAsync<List<PlayerCharacterSummary>>(
            $"/api/campaigns/{table.CampaignId}/player-characters", RequiemJson.Options))!;
        Assert.False(Assert.Single(mine).XpSplitFlagged);

        var advancement = (await table.Storyteller.GetFromJsonAsync<AdvancementView>(
            $"/api/player-characters/{draft.Sheet.Id}/advancement", RequiemJson.Options))!;
        Assert.Contains(advancement.History, h => h.Summary.EndsWith("Flagged: possible gaming of the XP split."));
    }

    /// <summary>A Skill dot bought with starting XP, at its XP price (new dots × 3).</summary>
    private static StartingXpPurchase Bought(Skill skill, int from, int to) => new()
    {
        Purchase = new XpPurchase { Kind = PurchaseKind.Skill, Skill = skill }, Trait = skill.ToString(), FromDots = from, ToDots = to, Cost = to * 3,
    };

    [Theory]
    [InlineData(4, 5, 3, 12, "Attributes go from most dots to fewest")]
    [InlineData(5, 4, 3, 3, "Vampires start between the 4th and 12th Generation.")]
    public async Task Budgets_out_of_order_or_range_are_refused(int primary, int secondary, int tertiary, int generation, string error)
    {
        var table = await PlayerTable.CreateAsync(factory);
        var response = await SaveAsync(table, new CreationSettings
        {
            PrimaryAttributes = primary, SecondaryAttributes = secondary, TertiaryAttributes = tertiary, StartingGeneration = generation,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Players_cannot_change_the_budgets()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var response = await table.Player.PutAsJsonAsync($"/api/campaigns/{table.CampaignId}",
            new CampaignForm { Name = "Test Chronicle", AllowedTemplates = CharacterTemplates.All, Creation = new() { MeritPoints = 50 } }, RequiemJson.Options);
        Assert.False(response.IsSuccessStatusCode);
        var campaign = (await table.Storyteller.GetFromJsonAsync<CampaignView>($"/api/campaigns/{table.CampaignId}", RequiemJson.Options))!;
        Assert.Equal(10, campaign.Creation!.MeritPoints);
    }

    private static Task<HttpResponseMessage> SaveAsync(Table table, CreationSettings creation) =>
        table.Storyteller.PutAsJsonAsync($"/api/campaigns/{table.CampaignId}", new CampaignForm
        {
            Name = "Test Chronicle", AllowedTemplates = CharacterTemplates.All, CoterieScript = "No one left behind",
            ChronicleTenet = "Blood ties", Creation = creation,
        }, RequiemJson.Options);
}
