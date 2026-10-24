using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

public class PowerCatalogApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>, IAsyncLifetime
{
    private HttpClient _http = null!;

    public async Task InitializeAsync() => _http = await factory.CreateGmClientAsync("powers@example.test");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Powers_are_listed_with_levels_and_bonus_options()
    {
        var powers = await PowersAsync();
        var dominate = await GroupIdAsync("Dominate");

        Assert.Equal(["Command", "Mesmerize", "The Forgetful Mind", "Conditioning", "Possession"],
            powers.Where(p => p.PowerGroupId == dominate).OrderBy(p => p.Level).Select(p => p.Name));
        Assert.Equal(5, powers.Single(p => p.Name == "Aura Perception").BonusOptions.Count);
    }

    [Fact]
    public async Task Review_edits_are_saved_and_mark_the_power_reviewed()
    {
        var awe = (await PowersAsync()).Single(p => p.Name == "Awe");
        var form = PowerEditForm.From(awe);
        form.SuccessText = "  Corrected against the book.  ";
        form.IsReviewed = true;
        form.BonusOptions = [new() { SuccessCost = 2, Effect = "Twice the crowd" }];

        var response = await _http.PutAsJsonAsync($"/api/catalog/powers/{awe.Id}", form, RequiemJson.Options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var saved = (await PowersAsync()).Single(p => p.Id == awe.Id);
        Assert.True(saved.IsReviewed);
        Assert.Equal("Corrected against the book.", saved.SuccessText);
        Assert.Equal(new PowerBonusOptionDto(2, "Twice the crowd"), Assert.Single(saved.BonusOptions));
        Assert.Equal(awe.Flavor, saved.Flavor);
    }

    [Fact]
    public async Task A_power_cannot_take_another_powers_name_in_its_group()
    {
        var powers = await PowersAsync();
        var command = powers.Single(p => p.Name == "Command");
        var form = PowerEditForm.From(command);
        form.Name = "Mesmerize";

        var response = await _http.PutAsJsonAsync($"/api/catalog/powers/{command.Id}", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(nameof(PowerEditForm.Name), problem!.Errors.Keys);
    }

    [Fact]
    public async Task Editing_an_unknown_power_is_not_found()
    {
        var response = await _http.PutAsJsonAsync("/api/catalog/powers/999999", new PowerEditForm { Name = "Nothing" }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Learned_rituals_round_trip_on_the_full_sheet()
    {
        var powers = await PowersAsync();
        var hekate = await GroupIdAsync("Hekate");
        var auctoritas = await GroupIdAsync("Auctoritas");
        var ritual = powers.First(p => p.PowerGroupId == hekate);
        var ritae = powers.First(p => p.PowerGroupId == auctoritas);

        var created = await CreateAsync(new FullSheetForm { Name = "Magus Orrin", LearnedPowerIds = [ritae.Id, ritual.Id] });
        Assert.Equal(new[] { ritual.Id, ritae.Id }.Order(), created.LearnedPowerIds);

        created.LearnedPowerIds = [ritual.Id];
        var response = await _http.PutAsJsonAsync($"/api/characters/{created.Id}/full-sheet", created, RequiemJson.Options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var reloaded = (await _http.GetFromJsonAsync<FullSheetForm>($"/api/characters/{created.Id}/full-sheet", RequiemJson.Options))!;
        Assert.Equal([ritual.Id], reloaded.LearnedPowerIds);
    }

    [Fact]
    public async Task Discipline_powers_are_not_learned_one_at_a_time()
    {
        var command = (await PowersAsync()).Single(p => p.Name == "Command");
        var response = await _http.PostAsJsonAsync("/api/characters/full-sheet",
            new FullSheetForm { Name = "Wrong", LearnedPowerIds = [command.Id] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(nameof(FullSheetForm.LearnedPowerIds), problem!.Errors.Keys);
    }

    [Fact]
    public async Task Investiture_picks_are_saved_up_to_one_per_dot()
    {
        var investiture = await GroupIdAsync("Investiture");
        var picks = (await PowersAsync()).Where(p => p.PowerGroupId == investiture).Take(3).Select(p => p.Id).ToList();
        FullSheetForm Sheet(int dots, IEnumerable<int> chosen) => new()
        {
            Name = "Iron Saint", BloodPotency = 3,
            Disciplines = dots > 0 ? [new() { PowerGroupId = investiture, Dots = dots }] : [],
            LearnedPowerIds = chosen.ToList(),
        };

        var created = await CreateAsync(Sheet(2, picks.Take(2)));
        Assert.Equal(picks.Take(2).Order(), created.LearnedPowerIds);

        foreach (var form in new[] { Sheet(2, picks), Sheet(0, picks.Take(1)) })
        {
            var response = await _http.PostAsJsonAsync("/api/characters/full-sheet", form, RequiemJson.Options);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
            Assert.Contains(nameof(FullSheetForm.LearnedPowerIds), problem!.Errors.Keys);
        }
    }

    [Fact]
    public async Task Mortals_drop_learned_rituals()
    {
        var ritual = (await PowersAsync()).First(p => p.Name == "Banishing Sign");
        var created = await CreateAsync(new FullSheetForm { Name = "Ghoul Ada", Kind = CharacterKind.Ghoul, LearnedPowerIds = [ritual.Id] });
        Assert.Empty(created.LearnedPowerIds);
    }

    private async Task<List<PowerOption>> PowersAsync() =>
        (await _http.GetFromJsonAsync<List<PowerOption>>("/api/catalog/powers", RequiemJson.Options))!;

    private async Task<int> GroupIdAsync(string name) =>
        (await _http.GetFromJsonAsync<List<DisciplineOption>>("/api/catalog/disciplines", RequiemJson.Options))!.Single(d => d.Name == name).Id;

    private async Task<FullSheetForm> CreateAsync(FullSheetForm form)
    {
        var response = await _http.PostAsJsonAsync("/api/characters/full-sheet", form, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options))!;
    }
}
