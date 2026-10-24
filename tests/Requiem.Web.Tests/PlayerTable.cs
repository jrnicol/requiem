using System.Net;
using System.Net.Http.Json;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Tests;

/// <summary>A Storyteller's campaign with one player in it.</summary>
internal sealed record Table(HttpClient Storyteller, HttpClient Player, Guid CampaignId);

/// <summary>Sets up tables and player characters for the player-facing API tests.</summary>
internal static class PlayerTable
{
    public static async Task<Table> CreateAsync(RequiemWebFactory factory, CharacterTemplates templates = CharacterTemplates.All)
    {
        var (storyteller, campaignId) = await factory.CreateStorytellerAsync($"st-{Guid.NewGuid():N}@example.test");
        var settings = await storyteller.PutAsJsonAsync($"/api/campaigns/{campaignId}",
            new CampaignForm { Name = "Test Chronicle", AllowedTemplates = templates, CoterieScript = "No one left behind", ChronicleTenet = "Blood ties" }, RequiemJson.Options);
        Assert.True(settings.IsSuccessStatusCode, await settings.Content.ReadAsStringAsync());
        var table = new Table(storyteller, null!, campaignId);
        return table with { Player = await JoinAsync(factory, table, "player") };
    }

    /// <summary>Another player joins through an invite link.</summary>
    public static async Task<HttpClient> JoinAsync(RequiemWebFactory factory, Table table, string name)
    {
        var invite = await table.Storyteller.PostAsJsonAsync($"/api/campaigns/{table.CampaignId}/invites", new InviteForm(), RequiemJson.Options);
        var token = (await invite.Content.ReadFromJsonAsync<CreatedInvite>(RequiemJson.Options))!.Token;
        var player = await factory.CreateMemberClientAsync($"{name}-{Guid.NewGuid():N}@example.test");
        Assert.True((await player.PostAsync($"/api/invites/{token}/accept", null)).IsSuccessStatusCode);
        return player;
    }

    /// <summary>The player's character, created, submitted and approved.</summary>
    public static async Task<Guid> ActiveCharacterAsync(Table table, FullSheetForm form, HttpClient? player = null)
    {
        player ??= table.Player;
        var draft = await CreateCharacterAsync(player, table.CampaignId, form);
        await PostAsync<PlayerCharacterView>(player, $"/api/player-characters/{draft.Sheet.Id}/submit");
        await PostAsync<PlayerCharacterView>(table.Storyteller, $"/api/player-characters/{draft.Sheet.Id}/approve");
        return draft.Sheet.Id!.Value;
    }

    public static async Task<PlayerCharacterView> CreateCharacterAsync(HttpClient player, Guid campaignId, FullSheetForm form)
    {
        var response = await player.PostAsJsonAsync($"/api/campaigns/{campaignId}/player-characters", form, RequiemJson.Options);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlayerCharacterView>(RequiemJson.Options))!;
    }

    /// <summary>Posts (with an optional body), expects success and reads the reply.</summary>
    public static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsync(url, body is null ? null : JsonContent.Create(body, body.GetType(), options: RequiemJson.Options));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(RequiemJson.Options))!;
    }

    /// <summary>A mortal built to the book: everything the creation rules ask for.</summary>
    public static FullSheetForm CompleteMortal(string name = "Dana Reyes")
    {
        var sheet = new FullSheetForm
        {
            Name = name, Kind = CharacterKind.Mortal, Concept = "Survivor", Virtue = "Fortitude", Vice = "Wrath", Morality = 7,
            Relationships =
            [
                new() { Role = RelationshipRole.Anchor, Name = "Marco" },
                new() { Role = RelationshipRole.Complication, Name = "Officer Hale" },
                new() { Role = RelationshipRole.Connection, Name = "The Night Market" },
            ],
            Scripts = FullSheetForm.DefaultScripts(CharacterKind.Mortal),
            Milestones = [new() { Title = "Find who burned the shop" }],
        };
        sheet.Scripts[0].Text = "Chain smoker";
        sheet.Scripts[1].Text = "Never leaves a debt unpaid";
        foreach (var (attribute, dots) in new[]
                 {
                     (CharacterAttribute.Strength, 3), (CharacterAttribute.Dexterity, 3), (CharacterAttribute.Stamina, 2),
                     (CharacterAttribute.Charisma, 2), (CharacterAttribute.Manipulation, 2), (CharacterAttribute.Resolve, 3),
                     (CharacterAttribute.Intelligence, 2), (CharacterAttribute.Wits, 2), (CharacterAttribute.Perception, 2),
                 })
            sheet.Attributes[attribute] = dots;
        sheet.WillpowerDots = sheet.WillpowerCurrent = 5;
        foreach (var (skill, dots) in new[] { (Skill.Academics, 3), (Skill.Investigation, 3), (Skill.Crafts, 3), (Skill.Intrigue, 2), (Skill.Brawl, 3), (Skill.Athletics, 2), (Skill.Stealth, 2), (Skill.Streetwise, 3), (Skill.Empathy, 2) })
            sheet.Skills[skill] = dots;
        sheet.Specialties = [new() { Skill = Skill.Brawl, Name = "Boxing" }];
        sheet.Merits = [new() { Name = "Contacts", Kind = MeritKind.Merit, Dots = 3 }];
        return sheet;
    }

    /// <summary>
    /// The same character as a Caitiff vampire with a dot of Furor (no Bloodline Discipline to add); the API
    /// looks up the catalog ids.
    /// </summary>
    public static async Task<FullSheetForm> CompleteVampireAsync(HttpClient client, string name = "Vera Lund")
    {
        var bloodlines = (await client.GetFromJsonAsync<List<BloodlineOption>>("/api/catalog/bloodlines", RequiemJson.Options))!;
        var predators = (await client.GetFromJsonAsync<List<PredatorTypeOption>>("/api/catalog/predator-types", RequiemJson.Options))!;
        var disciplines = (await client.GetFromJsonAsync<List<DisciplineOption>>("/api/catalog/disciplines", RequiemJson.Options))!;
        var sheet = CompleteMortal(name);
        sheet.Kind = CharacterKind.Vampire;
        sheet.Scripts = FullSheetForm.DefaultScripts();
        sheet.Scripts[0].Text = "Protect Marco";
        sheet.Scripts[1].Text = "Own the Night Market";
        sheet.Morality = null;
        sheet.Humanity = 6;
        sheet.BloodPotency = 1;
        sheet.Generation = 12;
        sheet.BloodlineId = bloodlines.Single(b => b.Kind == "Caitiff").Id;
        sheet.PredatorTypeId = predators[0].Id;
        sheet.Disciplines = [new() { PowerGroupId = disciplines.Single(d => d.Name == "Furor").Id, Dots = 1 }];
        return sheet;
    }
}
