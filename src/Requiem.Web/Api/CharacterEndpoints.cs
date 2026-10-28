using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Media;
using Requiem.Data.Stores;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

public static class CharacterEndpoints
{
    public static RouteGroupBuilder MapCharacterEndpoints(this RouteGroupBuilder api)
    {
        // Open to any member; CampaignAccess limits each request to the campaigns they tell.
        var characters = api.MapGroup("/characters").WithTags("Characters");

        characters.MapGet("/", ListAsync);
        characters.MapGet("/{id:guid}/supporting-cast", GetSupportingCastAsync);
        characters.MapPost("/supporting-cast", CreateSupportingCastAsync);
        characters.MapPut("/{id:guid}/supporting-cast", UpdateSupportingCastAsync);
        characters.MapDelete("/{id:guid}", DeleteAsync);
        characters.MapPost("/{id:guid}/move", MoveAsync);
        characters.MapGet("/{id:guid}/profile", GetProfileAsync);

        return api;
    }

    /// <summary>
    /// A character's profile page. For an NPC, the campaign's Storytellers get every detail, or with <paramref name="playersView"/>
    /// exactly what the players see; players get a shared NPC's revealed details. A player character's profile has every
    /// detail for everyone in the campaign, though the other players, as in the campaign's list, only see those in play.
    /// Anything else reads as not found.
    /// </summary>
    private static async Task<Results<Ok<KnownNpc>, NotFound>> GetProfileAsync(
        Guid id, bool? playersView, ICharacterStore characters, IUserDirectory users, CampaignAccess access, CancellationToken ct)
    {
        if (await characters.GetAsync(id, ct) is not { CampaignId: { } campaignId } character) return TypedResults.NotFound();
        var role = await access.RoleInAsync(campaignId, ct);
        if (character.IsPlayerCharacter)
        {
            var canEdit = role == CampaignRole.Storyteller || await access.OwnsAsync(character, ct);
            if (role is null || (!canEdit && character.Status != CharacterStatus.Active)) return TypedResults.NotFound();
            var owner = character.OwnerUserId is { } ownerId ? (await users.GetAsync([ownerId], ct)).GetValueOrDefault(ownerId)?.DisplayName : null;
            return TypedResults.Ok(ProfileMapper.ToPlayerProfile(character, owner ?? "Unknown player", canEdit, await access.CanPlayAsync(character, ct)));
        }
        if (role == CampaignRole.Storyteller && playersView != true) return TypedResults.Ok(ProfileMapper.ToFullProfile(character));
        if (role is null || character.Visibility != NpcVisibility.Known) return TypedResults.NotFound();
        return TypedResults.Ok(ProfileMapper.ToKnownNpc(character, role == CampaignRole.Storyteller));
    }

    /// <summary>
    /// Moves an NPC from one campaign you tell to another. Player characters stay with their campaign.
    /// Sire and domitor links don't cross campaigns, so the move cuts them.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> MoveAsync(
        Guid id, MoveCharacterRequest request, ICharacterStore characters, ICampaignStore campaigns,
        CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        var character = await access.ManagedCharacterAsync(id, ct);
        if (character is null) return TypedResults.NotFound();
        if (character.IsPlayerCharacter)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = ["Player characters stay in their campaign."] });
        if (await access.CampaignProblemAsync(request.CampaignId, ct) is { } problem)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.CampaignId)] = [problem] });
        if (character.CampaignId == request.CampaignId) return TypedResults.NoContent();

        var from = character.CampaignId!.Value;
        var fromName = (await campaigns.GetAsync(from, ct))!.Name;
        var toName = (await campaigns.GetAsync(request.CampaignId, ct))!.Name;
        var summary = $"Moved {character.Name} from {fromName} to {toName}.";
        audit.Record(from, id, "character.move", summary);
        audit.Record(request.CampaignId, id, "character.move", summary);

        await characters.MoveAsync(character, request.CampaignId, ct);
        return TypedResults.NoContent();
    }

    private static async Task<List<CharacterSummary>> ListAsync(
        [AsParameters] CharacterQuery query, CampaignAccess access, CancellationToken ct)
    {
        IEnumerable<Character> characters = (await access.ManagedCharactersAsync(query.CampaignId, ct))
            .Where(c => c.IsPlayerCharacter == query.PlayerCharacters);
        if (query.Tier is { } tier) characters = characters.Where(c => c.Tier == tier);
        if (query.Kind is { } kind) characters = characters.Where(c => c.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            characters = characters.Where(c =>
                c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (c.Concept?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.Faction?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return characters
            .Select(c => new CharacterSummary(
                c.Id, c.Name, c.Tier, c.Kind, c.Concept, c.Faction, c.Bloodline?.Name,
                c.Physical, c.Mental, c.Social, c.Edges, c.BloodPotency, c.UpdatedAt,
                c.Tier == CharacterTier.FullSheet
                    ? c.Attributes.Where(a => a.Attribute == Shared.Rules.CharacterAttribute.Stamina)
                        .Select(a => (int?)a.Dots).FirstOrDefault() + c.Size
                    : null,
                c.Tier == CharacterTier.FullSheet ? c.WillpowerDots : null,
                c.Tier == CharacterTier.FullSheet ? c.Humanity ?? c.Morality : null,
                c.CampaignId,
                c.PortraitUrl, c.Generation, c.Appearance, c.Reputation, c.Backstory,
                !c.IsPlayerCharacter && c.Visibility == NpcVisibility.Known,
                c.Titles.Count > 0 ? c.Titles : null))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<Results<Ok<SupportingCastForm>, NotFound>> GetSupportingCastAsync(
        Guid id, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadSupportingCastAsync(access, id, ct);
        return character is null ? TypedResults.NotFound() : TypedResults.Ok(SupportingCastMapper.ToForm(character));
    }

    private static async Task<Results<Created<SupportingCastForm>, ValidationProblem>> CreateSupportingCastAsync(
        SupportingCastForm form, ICharacterStore characters, ICatalogStore catalog, CampaignAccess access, CancellationToken ct)
    {
        if (await ValidateAsync(form, catalog, ct) is { } errors) return TypedResults.ValidationProblem(errors);
        if (await access.CampaignProblemAsync(form.CampaignId, ct) is { } problem)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(form.CampaignId)] = [problem] });

        var character = new Character { Name = form.Name, OwnerUserId = access.UserId, CampaignId = form.CampaignId };
        SupportingCastMapper.Apply(form, character);
        await characters.AddAsync(character, ct);

        return TypedResults.Created($"/api/characters/{character.Id}/supporting-cast", SupportingCastMapper.ToForm(character));
    }

    private static async Task<Results<Ok<SupportingCastForm>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>>
        UpdateSupportingCastAsync(Guid id, SupportingCastForm form, ICharacterStore characters, ICatalogStore catalog,
            CampaignAccess access, CancellationToken ct)
    {
        if (await ValidateAsync(form, catalog, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        var character = await LoadSupportingCastAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();

        // Compare against the version the client loaded, not the one just read.
        SupportingCastMapper.Apply(form, character);
        if (!await characters.TrySaveAsync(character, form.Version, ct))
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This NPC was changed somewhere else after you opened it. Reload to see the latest version."));

        return TypedResults.Ok(SupportingCastMapper.ToForm(character));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, ICharacterStore characters, IMediaStore media, CampaignAccess access, CancellationToken ct)
    {
        if (await access.ManagedCharacterAsync(id, ct) is not { } character || !await characters.DeleteAsync(id, ct))
            return TypedResults.NotFound();
        await media.DeleteAsync(character.PortraitUrl, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Character?> LoadSupportingCastAsync(CampaignAccess access, Guid id, CancellationToken ct) =>
        await access.ManagedCharacterAsync(id, ct) is { Tier: CharacterTier.SupportingCast } character ? character : null;

    /// <returns>Errors keyed by form property name, or null when valid.</returns>
    private static async Task<Dictionary<string, string[]>?> ValidateAsync(
        SupportingCastForm form, ICatalogStore store, CancellationToken ct)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        var catalog = await store.GetAsync(ct);

        if (ProfileMapper.TitlesProblem(form.Titles, catalog) is { } titles) results.Add(titles);
        else form.Titles = ProfileMapper.CanonicalTitles(form.Titles, catalog);

        if (form.IsVampire)
        {
            if (form.BloodlineId is { } bloodlineId && catalog.Bloodline(bloodlineId) is null)
                results.Add(new ValidationResult("Unknown Bloodline.", [nameof(form.BloodlineId)]));
            if (form.PredatorTypeId is { } predatorId && catalog.PredatorTypes.All(p => p.Id != predatorId))
                results.Add(new ValidationResult("Unknown Predator type.", [nameof(form.PredatorTypeId)]));
        }

        if (results.Count == 0) return null;
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(e => e.Member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").ToArray());
    }
}

/// <summary>A minimal error body for 409 responses.</summary>
public sealed record ProblemDetailsBody(string Detail);
