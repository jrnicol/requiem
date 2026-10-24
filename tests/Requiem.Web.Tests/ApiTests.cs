using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

public class ApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>, IAsyncLifetime
{
    private HttpClient _http = null!;
    private Guid _campaign;

    public async Task InitializeAsync() => (_http, _campaign) = await factory.CreateStorytellerAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Catalog ----------

    [Fact]
    public async Task Bloodlines_come_in_weakness_chart_order()
    {
        var bloodlines = await GetAsync<List<BloodlineOption>>("/api/catalog/bloodlines");
        Assert.Equal(13, bloodlines.Count);
        Assert.All(bloodlines.Take(7), b => Assert.Equal("Bloodline", b.Kind));
        Assert.All(bloodlines.Skip(7).Take(5), b => Assert.Equal("MagiHouse", b.Kind));
        Assert.Equal("Caitiff", bloodlines[^1].Name);
        Assert.Equal("Hideous Visage of the Beast", bloodlines.Single(b => b.Name == "Nosferatu").WeaknessName);
    }

    [Fact]
    public async Task Predator_types_and_edges_are_listed()
    {
        Assert.Equal(10, (await GetAsync<List<PredatorTypeOption>>("/api/catalog/predator-types")).Count);
        var edges = await GetAsync<List<EdgeOption>>("/api/catalog/edges");
        Assert.Equal(5, edges.Count);
        Assert.Contains(edges, e => e.Edge == NpcEdge.QuickWitted);
    }

    // ---------- Create and read ----------

    [Fact]
    public async Task Vampire_round_trips_with_catalog_links_and_lists()
    {
        var nosferatu = await BloodlineIdAsync("Nosferatu");
        var sandman = await PredatorIdAsync("Sandman");

        var created = await CreateAsync(new SupportingCastForm
        {
            Name = "  Edmund Grey  ",
            Kind = CharacterKind.Vampire,
            Concept = "Information broker",
            Faction = "Carthians",
            Physical = 2, Mental = 4, Social = 1,
            Edges = NpcEdge.Unshakable | NpcEdge.WellConnected,
            BloodlineId = nosferatu,
            PredatorTypeId = sandman,
            BloodPotency = 3,
            Specialties = ["Sewers", "Blackmail"],
            Assets = ["Tunnel network", "Informant ring"],
        });

        Assert.Equal("Edmund Grey", created.Name);
        Assert.Equal(12, created.Generation);
        Assert.Equal(BloodRules.MaxVitae(3), created.VitaeCurrent);
        Assert.NotEqual(Guid.Empty, created.Version);

        var loaded = await GetAsync<SupportingCastForm>($"/api/characters/{created.Id}/supporting-cast");
        Assert.Equal(nosferatu, loaded.BloodlineId);
        Assert.Equal(sandman, loaded.PredatorTypeId);
        Assert.Equal(NpcEdge.Unshakable | NpcEdge.WellConnected, loaded.Edges);
        Assert.Equal(["Sewers", "Blackmail"], loaded.Specialties);
        Assert.Equal(["Tunnel network", "Informant ring"], loaded.Assets);

        var row = Assert.Single(await ListAsync("search=edmund"));
        Assert.Equal("Nosferatu", row.BloodlineName);
        Assert.Equal(CharacterTier.SupportingCast, row.Tier);
    }

    [Fact]
    public async Task Vampire_only_fields_are_cleared_for_mortals()
    {
        var created = await CreateAsync(new SupportingCastForm
        {
            Name = "Officer Daley",
            Kind = CharacterKind.Mortal,
            BloodlineId = await BloodlineIdAsync("Ventrue"),
            BloodPotency = 4,
            VitaeCurrent = 3,
        });

        Assert.Null(created.BloodlineId);
        Assert.Null(created.BloodPotency);
        Assert.Null(created.VitaeCurrent);
        Assert.Null(created.Generation);
    }

    [Fact]
    public async Task Unknown_character_is_not_found() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await _http.GetAsync($"/api/characters/{Guid.NewGuid()}/supporting-cast")).StatusCode);

    // ---------- Validation ----------

    [Theory]
    [MemberData(nameof(InvalidForms))]
    public async Task Invalid_forms_are_rejected_with_field_errors(string field, SupportingCastForm form)
    {
        form.CampaignId = _campaign;
        var response = await _http.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    public static TheoryData<string, SupportingCastForm> InvalidForms() => new()
    {
        { "Name", new SupportingCastForm { Name = "" } },
        { "Physical", new SupportingCastForm { Name = "Too strong", Physical = 6 } },
        { "Specialties", new SupportingCastForm { Name = "Echo", Specialties = ["Guns", "guns"] } },
        { "VitaeCurrent", new SupportingCastForm { Name = "Glutton", Kind = CharacterKind.Vampire, BloodPotency = 1, VitaeCurrent = 11 } },
        { "BloodlineId", new SupportingCastForm { Name = "Orphan", Kind = CharacterKind.Vampire, BloodlineId = 9999 } },
    };

    // ---------- Update and concurrency ----------

    [Fact]
    public async Task Update_returns_a_new_version_and_stale_saves_conflict()
    {
        var original = await CreateAsync(new SupportingCastForm { Name = "Bartender" });

        original.Concept = "Knows everyone";
        var saved = await UpdateAsync(original);
        Assert.Equal("Knows everyone", saved.Concept);
        Assert.NotEqual(original.Version, saved.Version);

        // A second device still holding the original version.
        original.Concept = "Stale edit";
        var conflict = await _http.PutAsJsonAsync($"/api/characters/{original.Id}/supporting-cast", original, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var current = await GetAsync<SupportingCastForm>($"/api/characters/{original.Id}/supporting-cast");
        Assert.Equal("Knows everyone", current.Concept);
    }

    [Fact]
    public async Task List_only_edits_are_concurrency_checked_too()
    {
        var original = await CreateAsync(new SupportingCastForm { Name = "Fence", Assets = ["Pawn shop"] });
        var stale = await GetAsync<SupportingCastForm>($"/api/characters/{original.Id}/supporting-cast");

        original.Assets = ["Pawn shop", "Chop shop"];
        await UpdateAsync(original);

        stale.Specialties = ["Appraisal"];
        var response = await _http.PutAsJsonAsync($"/api/characters/{stale.Id}/supporting-cast", stale, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Roster filters ----------

    [Fact]
    public async Task Roster_search_is_case_insensitive_and_treats_wildcards_literally()
    {
        await CreateAsync(new SupportingCastForm { Name = "Sister Agnes", Faction = "Lancea et Sanctum" });

        Assert.Contains(await ListAsync("search=LANCEA"), c => c.Name == "Sister Agnes");
        Assert.Empty(await ListAsync("search=%25%25zz"));
    }

    [Fact]
    public async Task Archived_npcs_are_hidden_unless_requested()
    {
        var form = await CreateAsync(new SupportingCastForm { Name = "Retired Hound" });
        form.IsArchived = true;
        await UpdateAsync(form);

        Assert.DoesNotContain(await ListAsync(), c => c.Id == form.Id);
        Assert.Contains(await ListAsync("includeArchived=true"), c => c.Id == form.Id && c.IsArchived);
    }

    [Fact]
    public async Task Kind_filter_narrows_the_roster()
    {
        await CreateAsync(new SupportingCastForm { Name = "Ghoul Driver", Kind = CharacterKind.Ghoul });
        Assert.All(await ListAsync("kind=Ghoul"), c => Assert.Equal(CharacterKind.Ghoul, c.Kind));
        Assert.Contains(await ListAsync("kind=Ghoul"), c => c.Name == "Ghoul Driver");
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_removes_the_npc()
    {
        var form = await CreateAsync(new SupportingCastForm { Name = "Doomed Witness" });

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/characters/{form.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/characters/{form.Id}/supporting-cast")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/characters/{form.Id}")).StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<T> GetAsync<T>(string url) =>
        (await _http.GetFromJsonAsync<T>(url, RequiemJson.Options))!;

    private Task<List<CharacterSummary>> ListAsync(string query = "") =>
        GetAsync<List<CharacterSummary>>($"/api/characters?{query}");

    private async Task<SupportingCastForm> CreateAsync(SupportingCastForm form)
    {
        form.CampaignId = _campaign;
        var response = await _http.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private async Task<SupportingCastForm> UpdateAsync(SupportingCastForm form)
    {
        var response = await _http.PutAsJsonAsync($"/api/characters/{form.Id}/supporting-cast", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private async Task<int> BloodlineIdAsync(string name) =>
        (await GetAsync<List<BloodlineOption>>("/api/catalog/bloodlines")).Single(b => b.Name == name).Id;

    private async Task<int> PredatorIdAsync(string name) =>
        (await GetAsync<List<PredatorTypeOption>>("/api/catalog/predator-types")).Single(p => p.Name == name).Id;
}
