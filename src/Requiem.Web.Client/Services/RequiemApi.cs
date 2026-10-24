using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Client.Services;

/// <summary>Outcome of a create, update or promote.</summary>
public sealed record SaveResult<T>(
    T? Saved,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    bool IsConflict = false,
    string? Message = null)
    where T : class
{
    public bool Succeeded => Saved is not null;
}

/// <summary>Typed client for the Requiem API. Catalog lists are cached for the browser session.</summary>
public sealed class RequiemApi(HttpClient http)
{
    private IReadOnlyList<BloodlineOption>? _bloodlines;
    private IReadOnlyList<PredatorTypeOption>? _predatorTypes;
    private IReadOnlyList<EdgeOption>? _edges;
    private IReadOnlyList<DisciplineOption>? _disciplines;
    private IReadOnlyList<MeritOption>? _merits;
    private IReadOnlyList<CompelOption>? _compels;
    private IReadOnlyList<PowerOption>? _powers;
    private IReadOnlyList<ConceptOption>? _concepts;

    // ---------- Catalog ----------

    public async Task<IReadOnlyList<BloodlineOption>> GetBloodlinesAsync() =>
        _bloodlines ??= await GetAsync<List<BloodlineOption>>("api/catalog/bloodlines");

    public async Task<IReadOnlyList<PredatorTypeOption>> GetPredatorTypesAsync() =>
        _predatorTypes ??= await GetAsync<List<PredatorTypeOption>>("api/catalog/predator-types");

    public async Task<IReadOnlyList<EdgeOption>> GetEdgesAsync() =>
        _edges ??= await GetAsync<List<EdgeOption>>("api/catalog/edges");

    public async Task<IReadOnlyList<DisciplineOption>> GetDisciplinesAsync() =>
        _disciplines ??= await GetAsync<List<DisciplineOption>>("api/catalog/disciplines");

    public async Task<IReadOnlyList<MeritOption>> GetMeritsAsync() =>
        _merits ??= await GetAsync<List<MeritOption>>("api/catalog/merits");

    public async Task<IReadOnlyList<CompelOption>> GetCompelsAsync() =>
        _compels ??= await GetAsync<List<CompelOption>>("api/catalog/compels");

    public async Task<IReadOnlyList<PowerOption>> GetPowersAsync() =>
        _powers ??= await GetAsync<List<PowerOption>>("api/catalog/powers");

    public async Task<IReadOnlyList<ConceptOption>> GetConceptsAsync() =>
        _concepts ??= await GetAsync<List<ConceptOption>>("api/catalog/concepts");

    /// <summary>The catalog entries <see cref="CreationRules"/> needs, from the cached lists.</summary>
    public async Task<CreationCatalog> GetCreationCatalogAsync() => new(
        await GetBloodlinesAsync(),
        await GetDisciplinesAsync(),
        (await GetPowersAsync()).ToDictionary(p => p.Id, p => p.PowerGroupId),
        await GetMeritsAsync());

    /// <summary>Saves the site admin's review of an imported power and refreshes the cached catalog entry.</summary>
    public async Task<SaveResult<PowerOption>> SavePowerAsync(int id, PowerEditForm form)
    {
        using var response = await http.PutAsJsonAsync($"api/catalog/powers/{id}", form, RequiemJson.Options);
        var result = await ReadSaveResultAsync<PowerOption>(response);
        if (result.Saved is { } saved && _powers is not null)
            _powers = _powers.Select(p => p.Id == saved.Id ? saved : p).ToList();
        return result;
    }

    // ---------- Characters ----------

    public Task<List<CharacterSummary>> ListCharactersAsync(CharacterQuery query)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Search)) parameters.Add($"search={Uri.EscapeDataString(query.Search.Trim())}");
        if (query.Tier is { } tier) parameters.Add($"tier={tier}");
        if (query.Kind is { } kind) parameters.Add($"kind={kind}");
        if (query.IncludeArchived) parameters.Add("includeArchived=true");
        if (query.CampaignId is { } campaignId) parameters.Add($"campaignId={campaignId}");
        if (query.PlayerCharacters) parameters.Add("playerCharacters=true");
        var url = parameters.Count == 0 ? "api/characters" : $"api/characters?{string.Join('&', parameters)}";
        return GetAsync<List<CharacterSummary>>(url);
    }

    /// <returns>Null when the NPC doesn't exist (or isn't Supporting Cast).</returns>
    public async Task<SupportingCastForm?> GetSupportingCastAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/characters/{id}/supporting-cast");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options);
    }

    public async Task<SaveResult<SupportingCastForm>> SaveSupportingCastAsync(SupportingCastForm form)
    {
        using var response = form.Id is { } id
            ? await http.PutAsJsonAsync($"api/characters/{id}/supporting-cast", form, RequiemJson.Options)
            : await http.PostAsJsonAsync("api/characters/supporting-cast", form, RequiemJson.Options);
        return await ReadSaveResultAsync<SupportingCastForm>(response);
    }

    /// <returns>Null when the character doesn't exist (or isn't a full sheet).</returns>
    public async Task<FullSheetForm?> GetFullSheetAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/characters/{id}/full-sheet");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FullSheetForm>(RequiemJson.Options);
    }

    public async Task<SaveResult<FullSheetForm>> SaveFullSheetAsync(FullSheetForm form)
    {
        using var response = form.Id is { } id
            ? await http.PutAsJsonAsync($"api/characters/{id}/full-sheet", form, RequiemJson.Options)
            : await http.PostAsJsonAsync("api/characters/full-sheet", form, RequiemJson.Options);
        return await ReadSaveResultAsync<FullSheetForm>(response);
    }

    /// <summary>Turns a Supporting Cast NPC into a full sheet.</summary>
    public async Task<SaveResult<FullSheetForm>> PromoteAsync(Guid id, Guid version)
    {
        using var response = await http.PostAsJsonAsync($"api/characters/{id}/promote", new PromoteRequest(version), RequiemJson.Options);
        return await ReadSaveResultAsync<FullSheetForm>(response);
    }

    private static async Task<SaveResult<T>> ReadSaveResultAsync<T>(HttpResponseMessage response) where T : class
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK or HttpStatusCode.Created:
                return new SaveResult<T>(await response.Content.ReadFromJsonAsync<T>(RequiemJson.Options));
            case HttpStatusCode.BadRequest:
                return new SaveResult<T>(null, await ReadValidationErrorsAsync(response), Message: "Some fields need fixing.");
            case HttpStatusCode.Conflict:
                return new SaveResult<T>(null, IsConflict: true, Message: await ReadDetailAsync(response)
                    ?? "This character was changed somewhere else. Reload to see the latest version.");
            case HttpStatusCode.NotFound:
                return new SaveResult<T>(null, Message: "This character no longer exists.");
            default:
                response.EnsureSuccessStatusCode();
                return new SaveResult<T>(null, Message: $"Unexpected response: {(int)response.StatusCode}.");
        }
    }

    // ---------- Table play ----------

    /// <returns>Null when the character doesn't exist.</returns>
    public async Task<PlayCharacter?> GetPlayAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/characters/{id}/play");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlayCharacter>(RequiemJson.Options);
    }

    /// <summary>Applies one table change; a refusal (e.g. no Vitae left) comes back as the message.</summary>
    public async Task<SaveResult<PlayCharacter>> PlayAsync(Guid id, PlayAction action)
    {
        using var response = await http.PostAsJsonAsync($"api/characters/{id}/play", action, RequiemJson.Options);
        var result = await ReadSaveResultAsync<PlayCharacter>(response);
        return result.Errors is { Count: > 0 } errors ? result with { Message = string.Join(" ", errors.Values.SelectMany(v => v)) } : result;
    }


    /// <returns>Null when the scene doesn't exist.</returns>
    public async Task<SceneView?> GetSceneAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/scenes/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SceneView>(RequiemJson.Options);
    }

    public async Task<SaveResult<SceneView>> SaveSceneAsync(Guid? id, SceneForm form)
    {
        using var response = id is { } sceneId
            ? await http.PutAsJsonAsync($"api/scenes/{sceneId}", form, RequiemJson.Options)
            : await http.PostAsJsonAsync("api/scenes", form, RequiemJson.Options);
        return await ReadSaveResultAsync<SceneView>(response);
    }

    public async Task DeleteSceneAsync(Guid id)
    {
        using var response = await http.DeleteAsync($"api/scenes/{id}");
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    // ---------- Campaigns ----------

    public Task<List<CampaignSummary>> ListCampaignsAsync() => GetAsync<List<CampaignSummary>>("api/campaigns");

    /// <returns>Null when the campaign doesn't exist or the user isn't in it.</returns>
    public async Task<CampaignView?> GetCampaignAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/campaigns/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CampaignView>(RequiemJson.Options);
    }

    public async Task<SaveResult<CampaignView>> SaveCampaignAsync(Guid? id, CampaignForm form)
    {
        using var response = id is { } campaignId
            ? await http.PutAsJsonAsync($"api/campaigns/{campaignId}", form, RequiemJson.Options)
            : await http.PostAsJsonAsync("api/campaigns", form, RequiemJson.Options);
        return await ReadCampaignResultAsync<CampaignView>(response);
    }

    public async Task<SaveResult<CampaignView>> SaveCoterieScriptAsync(Guid id, string? text)
    {
        using var response = await http.PutAsJsonAsync($"api/campaigns/{id}/coterie-script", new CoterieScriptForm { Text = text }, RequiemJson.Options);
        return await ReadCampaignResultAsync<CampaignView>(response);
    }

    public async Task<SaveResult<CampaignView>> SetCampaignArchivedAsync(Guid id, bool archived)
    {
        using var response = await http.PostAsync($"api/campaigns/{id}/{(archived ? "archive" : "unarchive")}", content: null);
        return await ReadCampaignResultAsync<CampaignView>(response);
    }

    /// <returns>Null when deleted; otherwise why not (e.g. it still has characters).</returns>
    public async Task<string?> DeleteCampaignAsync(Guid id)
    {
        using var response = await http.DeleteAsync($"api/campaigns/{id}");
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadDetailAsync(response) ?? $"Couldn't delete it ({(int)response.StatusCode}).";
    }

    /// <returns>Null when removed; otherwise why not.</returns>
    public async Task<string?> RemoveMemberAsync(Guid campaignId, string userId)
    {
        using var response = await http.DeleteAsync($"api/campaigns/{campaignId}/members/{Uri.EscapeDataString(userId)}");
        return response.IsSuccessStatusCode
            ? null
            : await ReadDetailAsync(response) ?? $"Couldn't remove them ({(int)response.StatusCode}).";
    }

    public async Task<SaveResult<CreatedInvite>> CreateInviteAsync(Guid campaignId, InviteForm form)
    {
        using var response = await http.PostAsJsonAsync($"api/campaigns/{campaignId}/invites", form, RequiemJson.Options);
        return await ReadCampaignResultAsync<CreatedInvite>(response);
    }

    public async Task RevokeInviteAsync(Guid campaignId, Guid inviteId)
    {
        using var response = await http.DeleteAsync($"api/campaigns/{campaignId}/invites/{inviteId}");
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    /// <summary>Open to anyone: what the join page shows before sign-in.</summary>
    public Task<InvitePreview> PreviewInviteAsync(string token) =>
        GetAsync<InvitePreview>($"api/invites/{Uri.EscapeDataString(token)}");

    /// <summary>Joins as the signed-in user.</summary>
    /// <returns>The campaign joined, or the reason it couldn't be.</returns>
    public async Task<(Guid? CampaignId, string? Problem)> AcceptInviteAsync(string token)
    {
        using var response = await http.PostAsync($"api/invites/{Uri.EscapeDataString(token)}/accept", content: null);
        if (response.IsSuccessStatusCode)
            return ((await response.Content.ReadFromJsonAsync<JoinedCampaign>(RequiemJson.Options))!.CampaignId, null);
        return (null, await ReadDetailAsync(response) ?? $"Couldn't join ({(int)response.StatusCode}).");
    }

    /// <summary>Moves an NPC into another campaign you tell.</summary>
    /// <returns>Null when moved; otherwise why not.</returns>
    public async Task<string?> MoveCharacterAsync(Guid id, Guid campaignId)
    {
        using var response = await http.PostAsJsonAsync($"api/characters/{id}/move", new MoveCharacterRequest(campaignId), RequiemJson.Options);
        if (response.IsSuccessStatusCode) return null;
        var errors = response.StatusCode == HttpStatusCode.BadRequest ? await ReadValidationErrorsAsync(response) : null;
        return errors is { Count: > 0 } ? string.Join(" ", errors.Values.SelectMany(v => v)) : $"Couldn't move it ({(int)response.StatusCode}).";
    }

    public Task<List<SceneSummary>> ListCampaignScenesAsync(Guid campaignId) =>
        GetAsync<List<SceneSummary>>($"api/scenes?campaignId={campaignId}");

    // ---------- Player characters ----------

    /// <summary>A Storyteller gets every PC in the campaign; a player gets their own.</summary>
    public Task<List<PlayerCharacterSummary>> ListPlayerCharactersAsync(Guid campaignId) =>
        GetAsync<List<PlayerCharacterSummary>>($"api/campaigns/{campaignId}/player-characters");

    /// <returns>Null when it doesn't exist or isn't the user's to see.</returns>
    public async Task<PlayerCharacterView?> GetPlayerCharacterAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/player-characters/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlayerCharacterView>(RequiemJson.Options);
    }

    /// <summary>Creates the player's draft in <paramref name="campaignId"/> (no Id yet) or saves it.</summary>
    public async Task<SaveResult<PlayerCharacterView>> SavePlayerCharacterAsync(Guid campaignId, FullSheetForm form)
    {
        using var response = form.Id is { } id
            ? await http.PutAsJsonAsync($"api/player-characters/{id}", form, RequiemJson.Options)
            : await http.PostAsJsonAsync($"api/campaigns/{campaignId}/player-characters", form, RequiemJson.Options);
        return await ReadSaveResultAsync<PlayerCharacterView>(response);
    }

    /// <summary>
    /// submit, withdraw, approve, return (body: <see cref="ReturnCharacterRequest"/>) or status
    /// (body: <see cref="CharacterStatusRequest"/>).
    /// </summary>
    public async Task<SaveResult<PlayerCharacterView>> PlayerCharacterActionAsync(Guid id, string action, object? body = null)
    {
        using var response = await http.PostAsync($"api/player-characters/{id}/{action}",
            body is null ? null : JsonContent.Create(body, body.GetType(), options: RequiemJson.Options));
        return await ReadSaveResultAsync<PlayerCharacterView>(response);
    }

    /// <returns>Null when deleted; otherwise why not.</returns>
    public async Task<string?> DeletePlayerCharacterAsync(Guid id)
    {
        using var response = await http.DeleteAsync($"api/player-characters/{id}");
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadDetailAsync(response) ?? $"Couldn't delete it ({(int)response.StatusCode}).";
    }

    /// <summary>Like <see cref="ReadSaveResultAsync{T}"/>, with campaign wording and 403/410 handled.</summary>
    private static async Task<SaveResult<T>> ReadCampaignResultAsync<T>(HttpResponseMessage response) where T : class
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK or HttpStatusCode.Created:
                return new SaveResult<T>(await response.Content.ReadFromJsonAsync<T>(RequiemJson.Options));
            case HttpStatusCode.BadRequest:
                return new SaveResult<T>(null, await ReadValidationErrorsAsync(response), Message: "Some fields need fixing.");
            case HttpStatusCode.Forbidden:
                return new SaveResult<T>(null, Message: "Only the campaign's Storyteller can do that.");
            case HttpStatusCode.NotFound:
                return new SaveResult<T>(null, Message: "This campaign no longer exists, or you're no longer in it.");
            default:
                return new SaveResult<T>(null, Message: await ReadDetailAsync(response) ?? $"Unexpected response: {(int)response.StatusCode}.");
        }
    }

    /// <returns>False if it was already gone.</returns>
    public async Task<bool> DeleteCharacterAsync(Guid id)
    {
        using var response = await http.DeleteAsync($"api/characters/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<T> GetAsync<T>(string url) =>
        await http.GetFromJsonAsync<T>(url, RequiemJson.Options)
        ?? throw new InvalidOperationException($"Empty response from {url}.");

    private static async Task<IReadOnlyDictionary<string, string[]>> ReadValidationErrorsAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("errors", out var errors)) return new Dictionary<string, string[]>();
        return errors.EnumerateObject().ToDictionary(
            p => p.Name,
            p => p.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
