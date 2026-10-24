using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

public class FullSheetApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>, IAsyncLifetime
{
    private HttpClient _http = null!;
    private Guid _campaign;

    public async Task InitializeAsync() => (_http, _campaign) = await factory.CreateStorytellerAsync("sheets@example.test");

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Catalog ----------

    [Fact]
    public async Task Discipline_catalog_marks_which_groups_take_dots()
    {
        var disciplines = await GetAsync<List<DisciplineOption>>("/api/catalog/disciplines");
        Assert.Equal(25, disciplines.Count);
        Assert.Equal(16, disciplines.Count(d => d.HasDots));
        Assert.Equal("Brujah", disciplines.Single(d => d.Name == "Tartarus").BloodlineName);
        Assert.False(disciplines.Single(d => d.Name == "Hekate").HasDots);
    }

    [Fact]
    public async Task Merit_and_compel_catalogs_are_listed()
    {
        Assert.Equal(115, (await GetAsync<List<MeritOption>>("/api/catalog/merits")).Count);
        var compels = await GetAsync<List<CompelOption>>("/api/catalog/compels");
        Assert.Equal(10, compels.Count(c => c.Kind == CompelKind.Base));
    }

    // ---------- Round trip ----------

    [Fact]
    public async Task Full_vampire_sheet_round_trips_every_section()
    {
        var brujah = await BloodlineIdAsync("Brujah");
        var tartarus = await DisciplineIdAsync("Tartarus");
        var auspex = await DisciplineIdAsync("Auspex");
        var touchstone = (await GetAsync<List<MeritOption>>("/api/catalog/merits")).Single(m => m.Name == "Touchstone");

        var form = new FullSheetForm
        {
            Name = "Ilsa Kade",
            Kind = CharacterKind.Vampire,
            Concept = "Union enforcer",
            Virtue = "Justice", VirtueDetail = "The workers",
            Vice = "Wrath", ViceDetail = "Scabs",
            BloodlineId = brujah,
            BloodPotency = 2,
            Generation = 12,
            VitaeCurrent = 12,
            Humanity = 6,
            WillpowerDots = 5, WillpowerCurrent = 4,
            PotenceBoost = 2,
            Disciplines = [new() { PowerGroupId = tartarus, Dots = 2 }, new() { PowerGroupId = auspex, Dots = 1 }],
            Merits =
            [
                new() { MeritDefinitionId = touchstone.Id, Name = touchstone.Name, Kind = MeritKind.Merit, Dots = 3, Qualifier = "Her brother Tomas" },
                new() { Name = "Hates the Docks", Kind = MeritKind.Flaw, Dots = 1 },
            ],
            Wounds = [new() { Description = "crowbar", Amount = 2, Type = DamageType.Bashing }],
            Stains = ["Laugh at good natured humor"],
            Compels = [new() { Name = "Destroyer", Rating = 6, EndsWhen = "Dominion ends" }],
            Milestones = [new() { Title = "Break the strike", Stage = MilestoneStage.Risk, Benefit = "Allies (Union)" }],
            CorruptionMarks = ["Drank a scab dry"],
            Equipment = ["Crowbar", "Union card"],
            Experience = [new() { Amount = 5, Reason = "Session 1" }, new() { Amount = -3, Reason = "Brawl 1" }],
        };
        form.Attributes[CharacterAttribute.Strength] = 4;
        form.Attributes[CharacterAttribute.Stamina] = 3;
        form.Skills[Skill.Brawl] = 3;
        form.Specialties = [new() { Skill = Skill.Brawl, Name = "Pipes" }];
        form.Scripts[0].Text = "Protect the rank and file";
        form.Scripts[0].IsMarked = true;

        var created = await CreateAsync(form);
        var loaded = await GetAsync<FullSheetForm>($"/api/characters/{created.Id}/full-sheet");

        Assert.Equal(4, loaded.Attribute(CharacterAttribute.Strength));
        Assert.Equal(3, loaded.SkillDots(Skill.Brawl));
        Assert.Equal("Pipes", Assert.Single(loaded.Specialties).Name);
        Assert.Equal(2, loaded.Disciplines.Single(d => d.PowerGroupId == tartarus).Dots);
        Assert.Equal("Her brother Tomas", loaded.Merits.Single(m => m.Kind == MeritKind.Merit).Qualifier);
        Assert.Equal("Hates the Docks", loaded.Merits.Single(m => m.Kind == MeritKind.Flaw).Name);
        Assert.Equal(DamageType.Bashing, Assert.Single(loaded.Wounds).Type);
        Assert.Equal(6, Assert.Single(loaded.Compels).Rating);
        Assert.Equal(MilestoneStage.Risk, Assert.Single(loaded.Milestones).Stage);
        Assert.Single(loaded.CorruptionMarks);
        Assert.Equal(["Crowbar", "Union card"], loaded.Equipment);
        Assert.Equal(2, loaded.ExperienceAvailable);
        Assert.Equal(4, loaded.Scripts.Count);
        Assert.True(loaded.Scripts.Single(s => s.Slot == 1).IsMarked);
        Assert.Equal(8, loaded.Health); // Stamina 3 + Size 5

        var row = Assert.Single(await GetAsync<List<CharacterSummary>>("/api/characters?search=ilsa"));
        Assert.Equal(CharacterTier.FullSheet, row.Tier);
        Assert.Equal(8, row.Health);
        Assert.Equal(5, row.WillpowerDots);
        Assert.Equal(6, row.Morality);
    }

    [Fact]
    public async Task Mortals_track_morality_and_lose_vampire_values()
    {
        var created = await CreateAsync(new FullSheetForm
        {
            Name = "Detective Ruiz",
            Kind = CharacterKind.Mortal,
            BloodlineId = await BloodlineIdAsync("Ventrue"),
            Disciplines = [new() { PowerGroupId = await DisciplineIdAsync("Auspex"), Dots = 1 }],
            Humanity = 6,
        });

        Assert.Null(created.Humanity);
        Assert.Equal(7, created.Morality);
        Assert.Null(created.BloodlineId);
        Assert.Null(created.BloodPotency);
        Assert.Empty(created.Disciplines);
    }

    // ---------- Validation ----------

    [Fact]
    public async Task Specialty_needs_a_dot_in_its_skill() =>
        await AssertRejectedAsync("Specialties", new FullSheetForm
        {
            Name = "Dabbler",
            Specialties = [new() { Skill = Skill.Academics, Name = "Occult" }],
        });

    [Fact]
    public async Task Willpower_points_cannot_exceed_dots() =>
        await AssertRejectedAsync("WillpowerCurrent", new FullSheetForm { Name = "Overflow", WillpowerDots = 2, WillpowerCurrent = 3 });

    [Fact]
    public async Task Fortify_form_is_capped_by_the_attribute() =>
        await AssertRejectedAsync("PotenceBoost", new FullSheetForm { Name = "Overreach", PotenceBoost = 2 });

    [Fact]
    public async Task Blood_sorcery_does_not_take_dots() =>
        await AssertRejectedAsync("Disciplines", new FullSheetForm
        {
            Name = "Apprentice",
            Disciplines = [new() { PowerGroupId = await DisciplineIdAsync("Hekate"), Dots = 1 }],
        });

    [Fact]
    public async Task Unknown_merit_is_rejected() =>
        await AssertRejectedAsync("Merits", new FullSheetForm
        {
            Name = "Forger",
            Merits = [new() { MeritDefinitionId = 99999, Name = "Phantom", Kind = MeritKind.Merit }],
        });

    [Fact]
    public async Task Rules_engine_warnings_do_not_block_saving()
    {
        // Discipline dots above Blood Potency is a GM-overridable warning, not an error.
        var created = await CreateAsync(new FullSheetForm
        {
            Name = "Prodigy",
            BloodPotency = 1,
            Disciplines = [new() { PowerGroupId = await DisciplineIdAsync("Auspex"), Dots = 3 }],
        });
        Assert.Equal(3, Assert.Single(created.Disciplines).Dots);
    }

    [Fact]
    public async Task A_character_cannot_be_their_own_sire()
    {
        var created = await CreateAsync(new FullSheetForm { Name = "Ouroboros" });
        created.SireId = created.Id;
        var response = await _http.PutAsJsonAsync($"/api/characters/{created.Id}/full-sheet", created, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sire_link_round_trips()
    {
        var sire = await CreateAsync(new FullSheetForm { Name = "Old Marrow" });
        var childe = await CreateAsync(new FullSheetForm { Name = "Young Marrow", SireId = sire.Id });
        Assert.Equal(sire.Id, (await GetAsync<FullSheetForm>($"/api/characters/{childe.Id}/full-sheet")).SireId);
    }

    // ---------- Concurrency ----------

    [Fact]
    public async Task Stale_full_sheet_saves_conflict()
    {
        var original = await CreateAsync(new FullSheetForm { Name = "Contested Sheet" });
        var stale = await GetAsync<FullSheetForm>($"/api/characters/{original.Id}/full-sheet");

        original.Wounds = [new() { Description = "stake", Amount = 1, Type = DamageType.Lethal }];
        Assert.Equal(HttpStatusCode.OK, (await _http.PutAsJsonAsync($"/api/characters/{original.Id}/full-sheet", original, RequiemJson.Options)).StatusCode);

        stale.Stains = ["Hug"];
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PutAsJsonAsync($"/api/characters/{stale.Id}/full-sheet", stale, RequiemJson.Options)).StatusCode);
    }

    // ---------- Promote ----------

    [Fact]
    public async Task Promote_turns_supporting_cast_into_a_full_sheet()
    {
        var npc = await CreateSupportingCastAsync(new SupportingCastForm
        {
            Name = "Dock Boss",
            Kind = CharacterKind.Vampire,
            Physical = 3, Mental = 2, Social = 4,
            Edges = NpcEdge.Tough,
            BloodPotency = 2,
            Specialties = ["Intimidation"],
            Assets = ["Warehouse 9"],
        });

        var response = await _http.PostAsJsonAsync($"/api/characters/{npc.Id}/promote", new PromoteRequest(npc.Version), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sheet = (await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options))!;

        Assert.Equal(3, sheet.Attribute(CharacterAttribute.Strength));
        Assert.Equal(2, sheet.Attribute(CharacterAttribute.Wits));
        Assert.Equal(4, sheet.Attribute(CharacterAttribute.Resolve));
        Assert.Equal(6, sheet.WillpowerDots); // Wits 2 + Resolve 4
        Assert.Equal(6, sheet.Humanity);
        Assert.All(Enum.GetValues<Skill>(), s => Assert.Equal(0, sheet.SkillDots(s)));
        Assert.Equal(["Warehouse 9"], sheet.Equipment);
        Assert.Contains("Intimidation", sheet.Notes);
        Assert.Contains("Tough", sheet.Notes);
        Assert.Equal(4, sheet.Scripts.Count);

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/characters/{npc.Id}/supporting-cast")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync($"/api/characters/{npc.Id}/full-sheet")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _http.PostAsJsonAsync($"/api/characters/{npc.Id}/promote", new PromoteRequest(sheet.Version), RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Promote_with_a_stale_version_conflicts()
    {
        var npc = await CreateSupportingCastAsync(new SupportingCastForm { Name = "Fickle Informant" });
        var response = await _http.PostAsJsonAsync($"/api/characters/{npc.Id}/promote", new PromoteRequest(Guid.NewGuid()), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Full_sheets_can_be_deleted()
    {
        var created = await CreateAsync(new FullSheetForm { Name = "Ash" });
        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/characters/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/characters/{created.Id}/full-sheet")).StatusCode);
    }

    // ---------- Helpers ----------

    private async Task AssertRejectedAsync(string field, FullSheetForm form)
    {
        form.CampaignId = _campaign;
        var response = await _http.PostAsJsonAsync("/api/characters/full-sheet", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    private async Task<T> GetAsync<T>(string url) => (await _http.GetFromJsonAsync<T>(url, RequiemJson.Options))!;

    private async Task<FullSheetForm> CreateAsync(FullSheetForm form)
    {
        form.CampaignId = _campaign;
        var response = await _http.PostAsJsonAsync("/api/characters/full-sheet", form, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options))!;
    }

    private async Task<SupportingCastForm> CreateSupportingCastAsync(SupportingCastForm form)
    {
        form.CampaignId = _campaign;
        var response = await _http.PostAsJsonAsync("/api/characters/supporting-cast", form, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private async Task<int> BloodlineIdAsync(string name) =>
        (await GetAsync<List<BloodlineOption>>("/api/catalog/bloodlines")).Single(b => b.Name == name).Id;

    private async Task<int> DisciplineIdAsync(string name) =>
        (await GetAsync<List<DisciplineOption>>("/api/catalog/disciplines")).Single(d => d.Name == name).Id;
}
