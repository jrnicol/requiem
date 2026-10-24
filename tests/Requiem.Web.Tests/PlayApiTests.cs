using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

public class PlayApiTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>, IAsyncLifetime
{
    private HttpClient _http = null!;

    public async Task InitializeAsync() => _http = await factory.CreateGmClientAsync("table@example.test");

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Play view ----------

    [Fact]
    public async Task Full_sheet_play_view_has_trackers_and_power_pools()
    {
        var sheet = await CreateVampireAsync("Brujah", dominate: 2);
        var play = await PlayAsync(sheet.Id!.Value);

        Assert.Equal(3 + 5, play.Health); // Stamina 3 + Size 5
        Assert.Equal(12, play.MaxVitae); // Blood Potency 2
        Assert.Equal(BloodlineRules.BrujahMinimumHungerDice(2), play.MinimumHungerDice);
        Assert.Equal(2, play.Defense); // min(Wits 2, Dexterity 3)

        var mesmerize = play.Pools.Single(p => p.Label == "Mesmerize");
        Assert.Equal(2 + 2, mesmerize.Dice); // Expression 2 + Dominate 2 (the book's pool names no attribute)
        Assert.Equal("Expression 2 + Dominate 2 vs target's Wits", mesmerize.Detail);
        Assert.DoesNotContain(play.Pools, p => p.Label == "The Forgetful Mind"); // needs 3 dots
    }

    [Fact]
    public async Task Supporting_cast_roll_their_ratings_with_edges()
    {
        var response = await _http.PostAsJsonAsync("/api/characters/supporting-cast", new SupportingCastForm
        {
            Name = "Bouncer Vic", Physical = 4, Mental = 2, Social = 3, Edges = NpcEdge.QuickWitted | NpcEdge.Tough,
        }, RequiemJson.Options);
        var npc = (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;

        var play = await PlayAsync(npc.Id!.Value);
        Assert.Equal(4 + 5, play.Health);
        Assert.False(play.CanSpendWillpower);
        Assert.Equal(8, play.Pools.Single(p => p.Label == "Social").Dice);
        Assert.Equal("Supporting Cast never spend Willpower.",
            await ActErrorAsync(npc.Id!.Value, new PlayAction { Kind = PlayActionKind.AdjustWillpower, Amount = -1 }));
    }

    // ---------- Actions ----------

    [Fact]
    public async Task Wounds_are_added_healed_and_set_the_penalty()
    {
        var id = (await CreateVampireAsync("Ventrue")).Id!.Value;

        var hurt = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AddWound, Amount = 3, DamageType = DamageType.Lethal, Text = "knife" });
        var wound = Assert.Single(hurt.Wounds);
        Assert.Equal(("knife", 3), (wound.Description, wound.Amount));
        Assert.Equal(2, hurt.Status.DicePenalty);
        Assert.Equal(5 - 2, hurt.Roll(5).Total);

        var healed = await ActAsync(id, new PlayAction { Kind = PlayActionKind.HealWound, TargetId = wound.Id, Amount = 2 });
        Assert.Equal(1, Assert.Single(healed.Wounds).Amount);
        var gone = await ActAsync(id, new PlayAction { Kind = PlayActionKind.HealWound, TargetId = wound.Id, Amount = 1 });
        Assert.Empty(gone.Wounds);

        // The sheet sees the same wounds.
        Assert.StartsWith("Describe the wound",
            await ActErrorAsync(id, new PlayAction { Kind = PlayActionKind.AddWound, Amount = 1, Text = "  " }));
        await ActAsync(id, new PlayAction { Kind = PlayActionKind.AddWound, Amount = 1, DamageType = DamageType.Bashing, Text = "fist" });
        var sheet = (await _http.GetFromJsonAsync<FullSheetForm>($"/api/characters/{id}/full-sheet", RequiemJson.Options))!;
        Assert.Equal(DamageType.Bashing, Assert.Single(sheet.Wounds).Type);
    }

    [Fact]
    public async Task Willpower_and_vitae_stay_within_their_tracks()
    {
        var id = (await CreateVampireAsync("Ventrue")).Id!.Value;

        var spent = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AdjustWillpower, Amount = -2 });
        Assert.Equal(spent.WillpowerDots - 2, spent.WillpowerCurrent);
        var regained = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AdjustWillpower, Amount = 10 });
        Assert.Equal(regained.WillpowerDots, regained.WillpowerCurrent);

        var drained = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AdjustVitae, Amount = -10 });
        Assert.Equal(0, drained.VitaeCurrent);
        Assert.Equal("Only 0 Vitae left.", await ActErrorAsync(id, new PlayAction { Kind = PlayActionKind.AdjustVitae, Amount = -1 }));
        Assert.Equal(12, (await ActAsync(id, new PlayAction { Kind = PlayActionKind.AdjustVitae, Amount = 99 })).VitaeCurrent);
    }

    [Fact]
    public async Task Fortify_form_costs_vitae_and_is_capped_by_the_attribute()
    {
        var id = (await CreateVampireAsync("Ventrue")).Id!.Value; // Strength 4

        var boosted = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AdjustBoost, Boost = FortifyDiscipline.Potence, Amount = 3 });
        Assert.Equal(3, boosted.Boosts[FortifyDiscipline.Potence]);
        Assert.Equal(10 - 3, boosted.VitaeCurrent);

        Assert.StartsWith("Potence from Fortify Form can't exceed Strength",
            await ActErrorAsync(id, new PlayAction { Kind = PlayActionKind.AdjustBoost, Boost = FortifyDiscipline.Potence, Amount = 2 }));

        var cleared = await ActAsync(id, new PlayAction { Kind = PlayActionKind.ClearBoosts });
        Assert.All(cleared.Boosts.Values, v => Assert.Equal(0, v));
        Assert.Equal(7, cleared.VitaeCurrent); // lapsing refunds nothing
    }

    [Fact]
    public async Task Compels_and_conditions_are_noted_and_cleared()
    {
        var id = (await CreateVampireAsync("Ventrue")).Id!.Value;

        var compelled = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AddCompel, Text = "Frenzied", Amount = 3, EndsWhen = "scene ends" });
        var conditioned = await ActAsync(id, new PlayAction { Kind = PlayActionKind.AddCondition, Text = "Blinded" });
        Assert.Equal(new PlayCompel(compelled.Compels[0].Id, "Frenzied", 3, "scene ends"), Assert.Single(conditioned.Compels));
        Assert.Equal("Blinded", Assert.Single(conditioned.Conditions).Name);

        var sheet = (await _http.GetFromJsonAsync<FullSheetForm>($"/api/characters/{id}/full-sheet", RequiemJson.Options))!;
        Assert.Equal(["Blinded"], sheet.Conditions);

        var clear = await ActAsync(id, new PlayAction { Kind = PlayActionKind.RemoveCondition, TargetId = conditioned.Conditions[0].Id });
        Assert.Empty(clear.Conditions);
        Assert.Empty((await ActAsync(id, new PlayAction { Kind = PlayActionKind.RemoveCompel, TargetId = clear.Compels[0].Id })).Compels);
    }

    [Fact]
    public async Task A_table_change_makes_an_open_sheet_editor_stale()
    {
        var sheet = await CreateVampireAsync("Ventrue");
        await ActAsync(sheet.Id!.Value, new PlayAction { Kind = PlayActionKind.AddWound, Amount = 1, Text = "bottle" });

        sheet.Concept = "Edited in another tab";
        var response = await _http.PutAsJsonAsync($"/api/characters/{sheet.Id}/full-sheet", sheet, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Scenes ----------

    [Fact]
    public async Task Scenes_keep_their_npcs_in_order()
    {
        var a = (await CreateVampireAsync("Ventrue", name: "Alpha")).Id!.Value;
        var b = (await CreateVampireAsync("Ventrue", name: "Beta")).Id!.Value;

        var response = await _http.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Elysium", CharacterIds = [b, a] }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scene = (await response.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options))!;
        Assert.Equal(["Beta", "Alpha"], scene.Members.Select(m => m.Name));

        var updated = await _http.PutAsJsonAsync($"/api/scenes/{scene.Id}", new SceneForm { Name = "Elysium, later", CharacterIds = [a] }, RequiemJson.Options);
        var view = (await updated.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options))!;
        Assert.Equal(("Elysium, later", "Alpha"), (view.Name, Assert.Single(view.Members).Name));

        var list = (await _http.GetFromJsonAsync<List<SceneSummary>>("/api/scenes", RequiemJson.Options))!;
        Assert.Equal(1, list.Single(s => s.Id == scene.Id).MemberCount);

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/scenes/{scene.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/scenes/{scene.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_character_takes_it_out_of_its_scenes()
    {
        var id = (await CreateVampireAsync("Ventrue")).Id!.Value;
        var response = await _http.PostAsJsonAsync("/api/scenes", new SceneForm { Name = "Ambush", CharacterIds = [id] }, RequiemJson.Options);
        var scene = (await response.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options))!;

        Assert.True((await _http.DeleteAsync($"/api/characters/{id}")).IsSuccessStatusCode);
        Assert.Empty((await _http.GetFromJsonAsync<SceneView>($"/api/scenes/{scene.Id}", RequiemJson.Options))!.Members);
    }

    [Fact]
    public async Task Scenes_reject_unknown_characters() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/scenes",
            new SceneForm { Name = "Ghosts", CharacterIds = [Guid.NewGuid()] }, RequiemJson.Options)).StatusCode);

    // ---------- Helpers ----------

    private async Task<FullSheetForm> CreateVampireAsync(string bloodline, int dominate = 0, string name = "Table NPC")
    {
        var bloodlines = (await _http.GetFromJsonAsync<List<BloodlineOption>>("/api/catalog/bloodlines", RequiemJson.Options))!;
        var groups = (await _http.GetFromJsonAsync<List<DisciplineOption>>("/api/catalog/disciplines", RequiemJson.Options))!;
        var form = new FullSheetForm
        {
            Name = name, Kind = CharacterKind.Vampire, BloodlineId = bloodlines.Single(b => b.Name == bloodline).Id,
            BloodPotency = 2, VitaeCurrent = 10, WillpowerDots = 5, WillpowerCurrent = 5,
            Disciplines = dominate > 0 ? [new() { PowerGroupId = groups.Single(g => g.Name == "Dominate").Id, Dots = dominate }] : [],
        };
        form.Attributes[CharacterAttribute.Strength] = 4;
        form.Attributes[CharacterAttribute.Stamina] = 3;
        form.Attributes[CharacterAttribute.Dexterity] = 3;
        form.Attributes[CharacterAttribute.Wits] = 2;
        form.Attributes[CharacterAttribute.Manipulation] = 3;
        form.Skills[Skill.Expression] = 2;

        var response = await _http.PostAsJsonAsync("/api/characters/full-sheet", form, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options))!;
    }

    private async Task<PlayCharacter> PlayAsync(Guid id) =>
        (await _http.GetFromJsonAsync<PlayCharacter>($"/api/characters/{id}/play", RequiemJson.Options))!;

    private async Task<PlayCharacter> ActAsync(Guid id, PlayAction action)
    {
        var response = await _http.PostAsJsonAsync($"/api/characters/{id}/play", action, RequiemJson.Options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlayCharacter>(RequiemJson.Options))!;
    }

    private async Task<string> ActErrorAsync(Guid id, PlayAction action)
    {
        var response = await _http.PostAsJsonAsync($"/api/characters/{id}/play", action, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(RequiemJson.Options);
        return problem!.Errors[""].Single();
    }
}
