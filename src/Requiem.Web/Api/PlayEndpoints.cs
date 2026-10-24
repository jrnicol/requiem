using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;
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
        Guid id, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadAsync(access, [id], tracked: false, ct);
        return character.Count == 0
            ? TypedResults.NotFound()
            : TypedResults.Ok(PlayMapper.ToPlay(character[0], await CatalogAsync(db, ct)));
    }

    private static async Task<Results<Ok<PlayCharacter>, NotFound, ValidationProblem>> ActAsync(
        Guid id, PlayAction action, RequiemDbContext db, CampaignAccess access, TimeProvider time, CancellationToken ct)
    {
        var loaded = await LoadAsync(access, [id], tracked: true, ct);
        if (loaded.Count == 0) return TypedResults.NotFound();
        var character = loaded[0];

        var error = Apply(character, action, time.GetUtcNow());
        if (error is not null) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = [error] });

        // Touch the character itself so its version changes and an editor open elsewhere can't overwrite this.
        db.Entry(character).Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(PlayMapper.ToPlay(character, await CatalogAsync(db, ct)));
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
    private static async Task<List<SceneSummary>> ListScenesAsync(CampaignAccess access, Guid? campaignId, CancellationToken ct)
    {
        var query = access.ManagedScenes().AsNoTracking();
        if (campaignId is { } id) query = query.Where(s => s.CampaignId == id);
        var scenes = await query
            .Select(s => new { s.Id, s.Name, Count = s.Members.Count, s.UpdatedAt, s.CampaignId })
            .ToListAsync(ct);
        return scenes.OrderByDescending(s => s.UpdatedAt)
            .Select(s => new SceneSummary(s.Id, s.Name, s.Count, s.UpdatedAt, s.CampaignId)).ToList();
    }

    private static async Task<Results<Created<SceneView>, ValidationProblem>> CreateSceneAsync(
        SceneForm form, RequiemDbContext db, CampaignAccess access, TimeProvider time, CancellationToken ct)
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
        db.Scenes.Add(scene);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/scenes/{scene.Id}", await ViewAsync(db, access, scene, ct));
    }

    private static async Task<Results<Ok<SceneView>, NotFound>> GetSceneAsync(
        Guid id, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        var scene = await access.ManagedScenes().AsNoTracking().Include(s => s.Members).SingleOrDefaultAsync(s => s.Id == id, ct);
        return scene is null ? TypedResults.NotFound() : TypedResults.Ok(await ViewAsync(db, access, scene, ct));
    }

    private static async Task<Results<Ok<SceneView>, NotFound, ValidationProblem>> UpdateSceneAsync(
        Guid id, SceneForm form, RequiemDbContext db, CampaignAccess access, TimeProvider time, CancellationToken ct)
    {
        var scene = await access.ManagedScenes().Include(s => s.Members).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (scene is null) return TypedResults.NotFound();
        if (await ValidateSceneAsync(form, scene.CampaignId, access, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        scene.Name = form.Name.Trim();
        scene.UpdatedAt = time.GetUtcNow();
        SetMembers(scene, form.CharacterIds);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ViewAsync(db, access, scene, ct));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSceneAsync(Guid id, CampaignAccess access, CancellationToken ct) =>
        await access.ManagedScenes().Where(s => s.Id == id).ExecuteDeleteAsync(ct) > 0 ? TypedResults.NoContent() : TypedResults.NotFound();

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

    private static async Task<SceneView> ViewAsync(RequiemDbContext db, CampaignAccess access, Scene scene, CancellationToken ct)
    {
        var order = scene.Members.OrderBy(m => m.SortOrder).Select(m => m.CharacterId).ToList();
        var characters = await LoadAsync(access, order, tracked: false, ct);
        var catalog = await CatalogAsync(db, ct);
        var members = order.Select(id => characters.FirstOrDefault(c => c.Id == id)).OfType<Character>()
            .Select(c => PlayMapper.ToPlay(c, catalog)).ToList();
        return new SceneView(scene.Id, scene.Name, members, scene.CampaignId);
    }

    // ---------- Loading ----------

    private static async Task<List<Character>> LoadAsync(CampaignAccess access, List<Guid> ids, bool tracked, CancellationToken ct)
    {
        var query = access.ManagedCharacters()
            .Include(c => c.Bloodline)
            .Include(c => c.Attributes).Include(c => c.Skills).Include(c => c.Specialties)
            .Include(c => c.Disciplines).ThenInclude(d => d.PowerGroup)
            .Include(c => c.LearnedPowers).Include(c => c.Wounds).Include(c => c.Compels).Include(c => c.Conditions)
            .Include(c => c.Items)
            .AsSplitQuery();
        if (!tracked) query = query.AsNoTracking();
        return await query.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
    }

    private static async Task<List<Power>> CatalogAsync(RequiemDbContext db, CancellationToken ct) =>
        await db.Powers.AsNoTracking().Include(p => p.PowerGroup).ToListAsync(ct);
}
