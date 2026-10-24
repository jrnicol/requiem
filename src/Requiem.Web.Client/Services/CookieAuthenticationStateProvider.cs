using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Requiem.Shared.Accounts;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Client.Services;

public sealed record AuthResult(bool Succeeded, string? Message = null, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>
/// Knows who is signed in by asking the server (the auth cookie is HttpOnly, so the browser app
/// can't read it). Also performs sign-in, sign-out and first-run setup.
/// </summary>
public sealed class CookieAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= FetchAsync();

    public Task<SetupStatus?> GetSetupStatusAsync() =>
        http.GetFromJsonAsync<SetupStatus>("api/account/setup", RequiemJson.Options);

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/account/login", request, RequiemJson.Options);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => await SignedInAsync(response),
            HttpStatusCode.Unauthorized => new AuthResult(false, "That email and password don't match an account."),
            HttpStatusCode.TooManyRequests => new AuthResult(false, await ReadDetailAsync(response)
                ?? "Too many failed attempts. Try again later."),
            HttpStatusCode.BadRequest => new AuthResult(false, "Check the highlighted fields.", await ReadErrorsAsync(response)),
            _ => new AuthResult(false, $"Sign-in failed ({(int)response.StatusCode}). Try again."),
        };
    }

    public async Task<AuthResult> SetupAsync(SetupRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/account/setup", request, RequiemJson.Options);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => await SignedInAsync(response),
            HttpStatusCode.BadRequest => new AuthResult(false, "Check the highlighted fields.", await ReadErrorsAsync(response)),
            HttpStatusCode.Conflict => new AuthResult(false, "Setup is already complete. Sign in instead."),
            _ => new AuthResult(false, await ReadDetailAsync(response) ?? $"Setup failed ({(int)response.StatusCode})."),
        };
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var _ = await http.PostAsync("api/account/logout", content: null);
        }
        finally
        {
            Set(CurrentUser.Anonymous);
        }
    }

    /// <summary>Open sign-up: makes an account and signs in.</summary>
    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/account/register", request, RequiemJson.Options);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => await SignedInAsync(response),
            HttpStatusCode.BadRequest => new AuthResult(false, "Check the highlighted fields.", await ReadErrorsAsync(response)),
            HttpStatusCode.TooManyRequests => new AuthResult(false, TooManySignUps),
            _ => new AuthResult(false, await ReadDetailAsync(response) ?? $"Sign-up failed ({(int)response.StatusCode})."),
        };
    }

    private const string TooManySignUps = "A lot of accounts were made in the last hour. Try again later.";

    /// <summary>Makes an account through an invite link, joins the campaign and signs in.</summary>
    /// <returns>The campaign joined, or null with the reason in the result.</returns>
    public async Task<(AuthResult Result, Guid? CampaignId)> JoinAsync(string token, RegisterRequest request)
    {
        using var response = await http.PostAsJsonAsync($"api/invites/{Uri.EscapeDataString(token)}/join", request, RequiemJson.Options);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var joined = (await response.Content.ReadFromJsonAsync<JoinedCampaign>(RequiemJson.Options))!;
                Set(joined.User);
                return (new AuthResult(true), joined.CampaignId);
            case HttpStatusCode.BadRequest:
                return (new AuthResult(false, "Check the highlighted fields.", await ReadErrorsAsync(response)), null);
            case HttpStatusCode.TooManyRequests:
                return (new AuthResult(false, TooManySignUps), null);
            default:
                return (new AuthResult(false, await ReadDetailAsync(response) ?? $"Joining failed ({(int)response.StatusCode})."), null);
        }
    }

    /// <summary>Called when the API answers 401: the session expired or was signed out elsewhere.</summary>
    public void MarkSignedOut()
    {
        if (_state is { IsCompletedSuccessfully: true } state && state.Result.User.Identity?.IsAuthenticated != true) return;
        Set(CurrentUser.Anonymous);
    }

    private async Task<AuthenticationState> FetchAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync<CurrentUser>("api/account/me", RequiemJson.Options);
            return ToState(me ?? CurrentUser.Anonymous);
        }
        catch (HttpRequestException)
        {
            // Server unreachable: treat as signed out; the login page will retry.
            _state = null;
            return Anonymous;
        }
    }

    private async Task<AuthResult> SignedInAsync(HttpResponseMessage response)
    {
        var me = await response.Content.ReadFromJsonAsync<CurrentUser>(RequiemJson.Options) ?? CurrentUser.Anonymous;
        Set(me);
        return new AuthResult(me.IsAuthenticated);
    }

    private void Set(CurrentUser me)
    {
        _state = Task.FromResult(ToState(me));
        NotifyAuthenticationStateChanged(_state);
    }

    private static AuthenticationState ToState(CurrentUser me)
    {
        if (!me.IsAuthenticated) return Anonymous;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, me.DisplayName ?? me.Email ?? "GM"),
            new(ClaimTypes.Email, me.Email ?? ""),
        };
        claims.AddRange(me.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Cookie")));
    }

    private static async Task<IReadOnlyDictionary<string, string[]>> ReadErrorsAsync(HttpResponseMessage response)
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
