using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;

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
