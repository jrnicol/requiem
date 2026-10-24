using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Requiem.Data;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;
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

        return api;
    }

    /// <summary>
    /// Moves an NPC from one campaign you tell to another. Player characters stay with their campaign.
    /// Sire and domitor links don't cross campaigns, so the move cuts them.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> MoveAsync(
        Guid id, MoveCharacterRequest request, RequiemDbContext db, CampaignAccess access, AuditLog audit, CancellationToken ct)
    {
        var character = await access.ManagedCharacters().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (character is null) return TypedResults.NotFound();
        if (character.IsPlayerCharacter)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = ["Player characters stay in their campaign."] });
        if (await access.CampaignProblemAsync(request.CampaignId, ct) is { } problem)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.CampaignId)] = [problem] });
        if (character.CampaignId == request.CampaignId) return TypedResults.NoContent();

        var from = character.CampaignId!.Value;
        var names = await db.Campaigns.Where(c => c.Id == from || c.Id == request.CampaignId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var summary = $"Moved {character.Name} from {names[from]} to {names[request.CampaignId]}.";
        audit.Record(from, id, "character.move", summary);
        audit.Record(request.CampaignId, id, "character.move", summary);

        await db.MoveCharacterAsync(character, request.CampaignId, ct);
        return TypedResults.NoContent();
    }

    private static async Task<List<CharacterSummary>> ListAsync(
        [AsParameters] CharacterQuery query, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        var characters = access.ManagedCharacters(db.Characters.AsNoTracking())
            .Where(c => c.IsPlayerCharacter == query.PlayerCharacters);
        if (!query.IncludeArchived) characters = characters.Where(c => !c.IsArchived);
        if (query.Tier is { } tier) characters = characters.Where(c => c.Tier == tier);
        if (query.Kind is { } kind) characters = characters.Where(c => c.Kind == kind);
        if (query.CampaignId is { } campaignId) characters = characters.Where(c => c.CampaignId == campaignId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // LIKE is case-insensitive on both SQLite (ASCII) and SQL Server's default collation.
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            characters = characters.Where(c =>
                EF.Functions.Like(c.Name, pattern, "\\")
                || (c.Concept != null && EF.Functions.Like(c.Concept, pattern, "\\"))
                || (c.Faction != null && EF.Functions.Like(c.Faction, pattern, "\\")));
        }

        var rows = await characters
            .Select(c => new CharacterSummary(
                c.Id, c.Name, c.Tier, c.Kind, c.Concept, c.Faction, c.Bloodline != null ? c.Bloodline.Name : null,
                c.Physical, c.Mental, c.Social, c.Edges, c.BloodPotency, c.IsArchived, c.UpdatedAt,
                c.Tier == CharacterTier.FullSheet
                    ? c.Attributes.Where(a => a.Attribute == Shared.Rules.CharacterAttribute.Stamina)
                        .Select(a => (int?)a.Dots).FirstOrDefault() + c.Size
                    : null,
                c.Tier == CharacterTier.FullSheet ? c.WillpowerDots : null,
                c.Tier == CharacterTier.FullSheet ? c.Humanity ?? c.Morality : null,
                c.CampaignId))
            .ToListAsync(ct);

        // Sort in memory: SQLite's default collation sorts case-sensitively.
        return rows.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<Results<Ok<SupportingCastForm>, NotFound>> GetSupportingCastAsync(
        Guid id, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        var character = await LoadSupportingCastAsync(db, access, id, tracked: false, ct);
        return character is null ? TypedResults.NotFound() : TypedResults.Ok(SupportingCastMapper.ToForm(character));
    }

    private static async Task<Results<Created<SupportingCastForm>, ValidationProblem>> CreateSupportingCastAsync(
        SupportingCastForm form, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        if (await ValidateAsync(form, db, ct) is { } errors) return TypedResults.ValidationProblem(errors);
        if (await access.CampaignProblemAsync(form.CampaignId, ct) is { } problem)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(form.CampaignId)] = [problem] });

        var character = new Character { Name = form.Name, OwnerUserId = access.UserId, CampaignId = form.CampaignId };
        SupportingCastMapper.Apply(form, character);
        db.Characters.Add(character);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/characters/{character.Id}/supporting-cast", SupportingCastMapper.ToForm(character));
    }

    private static async Task<Results<Ok<SupportingCastForm>, NotFound, ValidationProblem, Conflict<ProblemDetailsBody>>>
        UpdateSupportingCastAsync(Guid id, SupportingCastForm form, RequiemDbContext db, CampaignAccess access, CancellationToken ct)
    {
        if (await ValidateAsync(form, db, ct) is { } errors) return TypedResults.ValidationProblem(errors);

        var character = await LoadSupportingCastAsync(db, access, id, tracked: true, ct);
        if (character is null) return TypedResults.NotFound();

        // Compare against the version the client loaded, not the one just read.
        db.Entry(character).Property(c => c.Version).OriginalValue = form.Version;
        SupportingCastMapper.Apply(form, character);
        // Always stamp the character, so list-only edits (specialties, assets) are concurrency-checked too.
        db.Entry(character).Property(c => c.UpdatedAt).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict(new ProblemDetailsBody(
                "This NPC was changed somewhere else after you opened it. Reload to see the latest version."));
        }

        return TypedResults.Ok(SupportingCastMapper.ToForm(character));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, RequiemDbContext db, CampaignAccess access, CancellationToken ct) =>
        await access.CanManageCharacterAsync(id, ct) && await db.DeleteCharacterAsync(id, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static Task<Character?> LoadSupportingCastAsync(
        RequiemDbContext db, CampaignAccess access, Guid id, bool tracked, CancellationToken ct)
    {
        var query = access.ManagedCharacters().Include(c => c.Specialties).Include(c => c.Items).AsSplitQuery();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(c => c.Id == id && c.Tier == CharacterTier.SupportingCast, ct);
    }

    /// <returns>Errors keyed by form property name, or null when valid.</returns>
    private static async Task<Dictionary<string, string[]>?> ValidateAsync(
        SupportingCastForm form, RequiemDbContext db, CancellationToken ct)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);

        if (form.IsVampire)
        {
            if (form.BloodlineId is { } bloodlineId && !await db.Bloodlines.AnyAsync(b => b.Id == bloodlineId, ct))
                results.Add(new ValidationResult("Unknown Bloodline.", [nameof(form.BloodlineId)]));
            if (form.PredatorTypeId is { } predatorId && !await db.PredatorTypes.AnyAsync(p => p.Id == predatorId, ct))
                results.Add(new ValidationResult("Unknown Predator type.", [nameof(form.PredatorTypeId)]));
        }

        if (results.Count == 0) return null;
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(e => e.Member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").ToArray());
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}

/// <summary>A minimal error body for 409 responses.</summary>
public sealed record ProblemDetailsBody(string Detail);
