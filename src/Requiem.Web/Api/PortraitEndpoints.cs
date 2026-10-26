using Microsoft.AspNetCore.Http.HttpResults;
using Requiem.Data.Entities;
using Requiem.Data.Media;
using Requiem.Data.Stores;
using Requiem.Shared.Campaigns;
using Requiem.Shared.Contracts;
using Requiem.Web.Auth;

namespace Requiem.Web.Api;

/// <summary>
/// A character's portrait. The browser shrinks the picture before uploading; it's stored under a new name each
/// time (so it can be cached for good) and the old one is removed. Storytellers change the portraits of the
/// characters in their campaigns; players, their own characters' while they're drafts or in play.
/// </summary>
public static class PortraitEndpoints
{
    public const int MaxBytes = 1024 * 1024;

    public static RouteGroupBuilder MapPortraitEndpoints(this RouteGroupBuilder api)
    {
        var characters = api.MapGroup("/characters").WithTags("Characters");
        characters.MapPut("/{id:guid}/portrait", UploadAsync);
        characters.MapDelete("/{id:guid}/portrait", RemoveAsync);
        return api;
    }

    private static async Task<Results<Ok<PortraitResult>, NotFound, ProblemHttpResult>> UploadAsync(
        Guid id, HttpRequest request, ICharacterStore characters, IMediaStore media, CampaignAccess access, CancellationToken ct)
    {
        var character = await EditableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();

        if (request.ContentLength > MaxBytes) return TooBig();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes) return TooBig();
        var image = buffer.ToArray();
        if (ImageType(image) is not { } contentType)
            return TypedResults.Problem("Upload a JPEG, PNG or WebP picture.", statusCode: StatusCodes.Status415UnsupportedMediaType);

        var previous = character.PortraitUrl;
        character.PortraitUrl = await media.SaveAsync($"portraits/{id}", image, contentType, ct);
        await characters.SaveAsync(character, ct);
        await media.DeleteAsync(previous, ct);
        return TypedResults.Ok(new PortraitResult(character.PortraitUrl, character.Version));
    }

    private static async Task<Results<Ok<PortraitResult>, NotFound>> RemoveAsync(
        Guid id, ICharacterStore characters, IMediaStore media, CampaignAccess access, CancellationToken ct)
    {
        var character = await EditableAsync(access, id, ct);
        if (character is null) return TypedResults.NotFound();
        if (character.PortraitUrl is { } previous)
        {
            character.PortraitUrl = null;
            await characters.SaveAsync(character, ct);
            await media.DeleteAsync(previous, ct);
        }
        return TypedResults.Ok(new PortraitResult(null, character.Version));
    }

    /// <summary>The character, when the user may change its portrait.</summary>
    private static async Task<Character?> EditableAsync(CampaignAccess access, Guid id, CancellationToken ct)
    {
        if (await access.ManagedCharacterAsync(id, ct) is { } managed) return managed;
        return await access.OwnCharacterAsync(id, ct) is { Status: CharacterStatus.Draft or CharacterStatus.Active } own ? own : null;
    }

    /// <summary>The picture's type from its first bytes (not the header the browser sent), or null when it isn't one we take.</summary>
    internal static string? ImageType(ReadOnlySpan<byte> bytes) =>
        bytes is [0xFF, 0xD8, 0xFF, ..] ? "image/jpeg"
        : bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] ? "image/png"
        : bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    private static ProblemHttpResult TooBig() =>
        TypedResults.Problem($"Portraits are limited to {MaxBytes / 1024} KB.", statusCode: StatusCodes.Status413PayloadTooLarge);
}
