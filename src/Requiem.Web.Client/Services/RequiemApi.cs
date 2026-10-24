using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Requiem.Shared.Contracts;

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

    /// <summary>Saves the GM's review of an imported power and refreshes the cached catalog entry.</summary>
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

    public Task<List<SceneSummary>> ListScenesAsync() => GetAsync<List<SceneSummary>>("api/scenes");

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
