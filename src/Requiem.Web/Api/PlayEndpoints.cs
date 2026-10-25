using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

/// <summary>
/// Table mode: run NPCs in a scene. Each action is a small change applied straight to the character,
/// so taps from the scene and from an NPC's play view never overwrite each other.
/// </summary>
public static class PlayEndpoints
{
    public static RouteGroupBuilder MapPlayEndpoints(this RouteGroupBuilder api)
    {
        // Open to any member; CampaignAccess limits each request to the campaigns they tell.
        var characters = api.MapGroup("/characters").WithTags("Play");
        characters.MapGet("/{id:guid}/play", GetAsync);
        characters.MapPost("/{id:guid}/play", ActAsync);

        var scenes = api.MapGroup("/scenes").WithTags("Scenes");
        scenes.MapGet("/", ListScenesAsync);
        scenes.MapPost("/", CreateSceneAsync);
        scenes.MapGet("/{id:guid}", GetSceneAsync);
        scenes.MapPut("/{id:guid}", UpdateSceneAsync);
        scenes.MapDelete("/{id:guid}", DeleteSceneAsync);

        return api;
    }

    // ---------- Characters ----------

    private static async Task<Results<Ok<PlayCharacter>, NotFound>> GetAsync(
        Guid id, ICatalogStore catalog, CampaignAccess access, CancellationToken ct)
    {
        // Players track their own approved characters; Storytellers everything in their campaigns.
        var character = await access.PlayableCharacterAsync(id, ct);
        return character is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(await ForViewerAsync(PlayMapper.ToPlay(character, await CatalogAsync(catalog, ct)), character, access, ct));
    }

    /// <summary>A player tracking their own character never sees the Storyteller's notes on it.</summary>
    private static async Task<PlayCharacter> ForViewerAsync(PlayCharacter play, Character character, CampaignAccess access, CancellationToken ct) =>
        character.CampaignId is { } campaignId && await access.IsStorytellerAsync(campaignId, ct) ? play : play with { GmNotes = null };

    private static async Task<Results<Ok<PlayCharacter>, NotFound, ValidationProblem>> ActAsync(
        Guid id, PlayAction action, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access, TimeProvider time, CancellationToken ct)
    {
        var character = await access.PlayableCharacterAsync(id, ct);
        if (character is null) return TypedResults.NotFound();

        var error = Apply(character, action, time.GetUtcNow());
        if (error is not null) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = [error] });

        // Saving gives the character a new version, so an editor open elsewhere can't overwrite this.
        await characters.SaveAsync(character, ct);
        return TypedResults.Ok(await ForViewerAsync(PlayMapper.ToPlay(character, await CatalogAsync(catalog, ct)), character, access, ct));
    }

    /// <returns>An error message, or null when the action was applied.</returns>
    internal static string? Apply(Character c, PlayAction action, DateTimeOffset now)
    {
        var text = action.Text?.Trim();
        switch (action.Kind)
        {
            case PlayActionKind.AddWound:
                if (string.IsNullOrEmpty(text)) return "Describe the wound, e.g. \"knife to the ribs\".";
                if (action.Amount is < 1 or > 50) return "Damage must be 1–50.";
                c.Wounds.Add(new CharacterWound
                {
                    Description = text,
                    Amount = action.Amount, Type = action.DamageType, CreatedAt = now,
                });
                return null;

            case PlayActionKind.HealWound:
            {
                var wound = c.Wounds.FirstOrDefault(w => w.Id == action.TargetId);
                if (wound is null) return "That wound is already gone.";
                wound.Amount -= Math.Max(1, action.Amount);
                if (wound.Amount <= 0) c.Wounds.Remove(wound);
                return null;
            }

            case PlayActionKind.RemoveWound:
                c.Wounds.RemoveAll(w => w.Id == action.TargetId);
                return null;

            case PlayActionKind.HealWithVitae:
            {
                if (!GhoulRules.HoldsVitae(c.Kind)) return "Only vampires and ghouls heal with Vitae.";
                var wound = c.Wounds.FirstOrDefault(w => w.Id == action.TargetId);
                if (wound is null) return "That wound is already gone.";
                var cost = HealingRules.Cost(wound.Type, wound.Amount);
                if (cost.Vitae > (c.VitaeCurrent ?? 0)) return $"Healing it takes {cost.Vitae} Vitae; only {c.VitaeCurrent ?? 0} left.";
                if (cost.Willpower > 0)
                {
                    if (c.Tier != CharacterTier.FullSheet) return "Aggravated wounds also cost Willpower, which Supporting Cast never spend.";
                    if (cost.Willpower > (c.WillpowerCurrent ?? 0)) return $"Healing it also takes {cost.Willpower} Willpower; only {c.WillpowerCurrent ?? 0} left.";
                    c.WillpowerCurrent -= cost.Willpower;
                }
                c.VitaeCurrent -= cost.Vitae;
                c.Wounds.Remove(wound);
                return null;
            }

            case PlayActionKind.AdjustWillpower:
                if (c.Tier != CharacterTier.FullSheet) return "Supporting Cast never spend Willpower.";
                var dots = c.WillpowerDots ?? 0;
                var willpower = (c.WillpowerCurrent ?? dots) + action.Amount;
                if (willpower < 0) return "No Willpower left to spend.";
                c.WillpowerCurrent = Math.Min(willpower, dots);
                return null;

            case PlayActionKind.RegainWillpower:
            {
                if (c.Tier != CharacterTier.FullSheet) return "Supporting Cast never spend Willpower.";
                if (action.Source is not { } source) return "Say what restored it: Concept, Virtue or Vice.";
                var max = c.WillpowerDots ?? 0;
                var current = c.WillpowerCurrent ?? max;
                if (current >= max) return "Willpower is already full.";
                c.WillpowerCurrent = WillpowerRules.Regain(source, current, max);
                return null;
            }

            case PlayActionKind.AdjustVitae:
            {
                if (GhoulRules.VitaeCapacity(c.Kind, c.BloodPotency, c.IsThinblood) is not { } capacity)
                    return "Only vampires and ghouls hold Vitae.";
                var vitae = (c.VitaeCurrent ?? 0) + action.Amount;
                if (vitae < 0) return $"Only {c.VitaeCurrent ?? 0} Vitae left.";
                c.VitaeCurrent = Math.Min(vitae, capacity);
                return null;
            }

            case PlayActionKind.AdjustBoost:
            {
                if (!GhoulRules.HoldsVitae(c.Kind)) return "Only vampires and ghouls can spend Vitae on Fortify Form.";
                if (action.Boost is not { } discipline) return "Choose Potence, Celerity or Fortitude.";
                var current = Boost(c, discipline);
                var target = Math.Max(0, current + action.Amount);
                var cap = Cap(c, discipline);
                if (target > cap)
                    return c.Kind == CharacterKind.Ghoul && target > GhoulRules.MaxFortifyBoost
                        ? $"A ghoul gains at most {GhoulRules.MaxFortifyBoost} {discipline} from Vitae."
                        : $"{discipline} from Fortify Form can't exceed {Traits.FortifyCap(discipline)} ({cap}).";
                var cost = Math.Max(0, target - current); // 1 Vitae per point; letting it lapse refunds nothing
                if (cost > (c.VitaeCurrent ?? 0)) return $"Only {c.VitaeCurrent ?? 0} Vitae left.";
                c.VitaeCurrent = (c.VitaeCurrent ?? 0) - cost;
                SetBoost(c, discipline, target);
                return null;
            }

            case PlayActionKind.ClearBoosts:
                c.PotenceBoost = c.CelerityBoost = c.FortitudeBoost = 0;
                return null;

            case PlayActionKind.AddCompel:
                if (string.IsNullOrEmpty(text)) return "Name the compel.";
                c.Compels.Add(new CharacterCompel
                {
                    Name = text, Rating = Math.Clamp(action.Amount, 1, 10),
                    EndsWhen = string.IsNullOrWhiteSpace(action.EndsWhen) ? null : action.EndsWhen.Trim(), CreatedAt = now,
                });
                return null;

            case PlayActionKind.RemoveCompel:
                c.Compels.RemoveAll(x => x.Id == action.TargetId);
                return null;

            case PlayActionKind.AddCondition:
                if (string.IsNullOrEmpty(text)) return "Name the condition.";
                c.Conditions.Add(new CharacterCondition { Name = text, CreatedAt = now });
                return null;

            case PlayActionKind.RemoveCondition:
                c.Conditions.RemoveAll(x => x.Id == action.TargetId);
                return null;

            default:
                return "Unknown action.";
        }
    }

    private static int Boost(Character c, FortifyDiscipline d) => d switch
    {
        FortifyDiscipline.Potence => c.PotenceBoost,
        FortifyDiscipline.Celerity => c.CelerityBoost,
        _ => c.FortitudeBoost,
    };

    private static void SetBoost(Character c, FortifyDiscipline d, int value)
    {
        switch (d)
        {
            case FortifyDiscipline.Potence: c.PotenceBoost = value; break;
            case FortifyDiscipline.Celerity: c.CelerityBoost = value; break;
            default: c.FortitudeBoost = value; break;
        }
    }

    private static int Cap(Character c, FortifyDiscipline d) => GhoulRules.FortifyCap(c.Kind, c.Tier == CharacterTier.FullSheet
        ? c.Attributes.FirstOrDefault(a => a.Attribute == Traits.FortifyCap(d))?.Dots ?? Traits.MinAttribute
        : c.Physical ?? SupportingCastRules.MinRating);

    // ---------- Scenes ----------

    /// <param name="campaignId">Only that campaign's scenes.</param>
    private static async Task<List<SceneSummary>> ListScenesAsync(CampaignAccess access, Guid? campaignId, CancellationToken ct) =>
        (await access.ManagedScenesAsync(campaignId, ct))
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new SceneSummary(s.Id, s.Name, s.Members.Count, s.UpdatedAt, s.CampaignId))
            .ToList();

    private static async Task<Results<Created<SceneView>, ValidationProblem>> CreateSceneAsync(
        SceneForm form, ISceneStore scenes, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access,
        TimeProvider time, CancellationToken ct)
    {
        if (await access.CampaignProblemAsync(form.CampaignId, ct) is { } problem)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(form.CampaignId)] = [problem] });
        if (await ValidateSceneAsync(form, form.CampaignId, access, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        var now = time.GetUtcNow();
        var scene = new Scene
        {
            Id = Guid.CreateVersion7(now), Name = form.Name.Trim(), OwnerUserId = access.UserId, CampaignId = form.CampaignId,
            CreatedAt = now, UpdatedAt = now,
        };
        SetMembers(scene, form.CharacterIds);
        await scenes.AddAsync(scene, ct);
        return TypedResults.Created($"/api/scenes/{scene.Id}", await ViewAsync(characters, catalog, scene, ct));
    }

    private static async Task<Results<Ok<SceneView>, NotFound>> GetSceneAsync(
        Guid id, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access, CancellationToken ct)
    {
        var scene = await access.ManagedSceneAsync(id, ct);
        return scene is null ? TypedResults.NotFound() : TypedResults.Ok(await ViewAsync(characters, catalog, scene, ct));
    }

    private static async Task<Results<Ok<SceneView>, NotFound, ValidationProblem>> UpdateSceneAsync(
        Guid id, SceneForm form, ISceneStore scenes, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access,
        TimeProvider time, CancellationToken ct)
    {
        var scene = await access.ManagedSceneAsync(id, ct);
        if (scene is null) return TypedResults.NotFound();
        if (await ValidateSceneAsync(form, scene.CampaignId, access, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        scene.Name = form.Name.Trim();
        scene.UpdatedAt = time.GetUtcNow();
        SetMembers(scene, form.CharacterIds);
        await scenes.SaveAsync(scene, ct);
        return TypedResults.Ok(await ViewAsync(characters, catalog, scene, ct));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSceneAsync(
        Guid id, ISceneStore scenes, CampaignAccess access, CancellationToken ct)
    {
        if (await access.ManagedSceneAsync(id, ct) is null) return TypedResults.NotFound();
        await scenes.DeleteAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static void SetMembers(Scene scene, List<Guid> characterIds)
    {
        var wanted = characterIds.Distinct().ToList();
        scene.Members.RemoveAll(m => !wanted.Contains(m.CharacterId));
        for (var i = 0; i < wanted.Count; i++)
        {
            var member = scene.Members.FirstOrDefault(m => m.CharacterId == wanted[i]);
            if (member is null) scene.Members.Add(new SceneMember { SceneId = scene.Id, CharacterId = wanted[i], SortOrder = i });
            else member.SortOrder = i;
        }
    }

    private static async Task<Dictionary<string, string[]>?> ValidateSceneAsync(
        SceneForm form, Guid? campaignId, CampaignAccess access, CancellationToken ct)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        // Only the scene's own campaign's characters can join it; anything else reads as unknown.
        if (!await access.AllInCampaignAsync(campaignId, form.CharacterIds, ct))
            results.Add(new ValidationResult("Unknown character.", [nameof(form.CharacterIds)]));
        return results.Count == 0
            ? null
            : results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? "")
                .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? "Invalid value.").ToArray());
    }

    /// <summary>The scene's characters in order; members since moved to another campaign drop out.</summary>
    private static async Task<SceneView> ViewAsync(ICharacterStore store, ICatalogStore catalog, Scene scene, CancellationToken ct)
    {
        var order = scene.Members.OrderBy(m => m.SortOrder).Select(m => m.CharacterId).ToList();
        var characters = (await store.GetManyAsync(order, ct)).Where(c => c.CampaignId == scene.CampaignId).ToList();
        var play = await CatalogAsync(catalog, ct);
        var members = order.Select(id => characters.FirstOrDefault(c => c.Id == id)).OfType<Character>()
            .Select(c => PlayMapper.ToPlay(c, play)).ToList();
        return new SceneView(scene.Id, scene.Name, members, scene.CampaignId);
    }

    private static async Task<PlayCatalog> CatalogAsync(ICatalogStore store, CancellationToken ct)
    {
        var catalog = await store.GetAsync(ct);
        return new(catalog.Powers, catalog.Concepts.ToDictionary(c => c.Name, c => c.WillpowerTrigger, StringComparer.OrdinalIgnoreCase));
    }
}
