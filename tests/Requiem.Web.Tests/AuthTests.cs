using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Requiem.Shared.Accounts;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

public class AuthTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Theory]
    [InlineData("GET", "/api/characters")]
    [InlineData("GET", "/api/catalog/bloodlines")]
    [InlineData("POST", "/api/characters/supporting-cast")]
    [InlineData("DELETE", "/api/characters/00000000-0000-0000-0000-000000000001")]
    public async Task Api_requires_sign_in_and_answers_401_not_a_redirect(string method, string url)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new SupportingCastForm { Name = "Intruder" }, options: RequiemJson.Options);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/npcs/new")]
    [InlineData("/login")]
    public async Task App_pages_load_signed_out_so_the_browser_can_show_the_login(string url)
    {
        var response = await factory.CreateClient(new() { AllowAutoRedirect = false }).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("blazor.web", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_check_answers_without_sign_in()
    {
        var response = await factory.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signed_in_user_without_the_gm_role_is_forbidden()
    {
        await factory.CreateUserAsync("player@example.test");
        var client = await factory.SignInAsync("player@example.test", RequiemWebFactory.DefaultPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/characters")).StatusCode);
    }

    [Fact]
    public async Task Me_reports_anonymous_then_the_signed_in_gm()
    {
        var anonymous = await factory.CreateClient().GetFromJsonAsync<CurrentUser>("/api/account/me", RequiemJson.Options);
        Assert.False(anonymous!.IsAuthenticated);

        var gm = await factory.CreateGmClientAsync("me@example.test");
        var me = await gm.GetFromJsonAsync<CurrentUser>("/api/account/me", RequiemJson.Options);
        Assert.True(me!.IsAuthenticated);
        Assert.Equal("me@example.test", me.Email);
        Assert.Contains(Roles.GameMaster, me.Roles);
    }

    [Fact]
    public async Task Auth_cookie_is_http_only_and_same_site_strict()
    {
        await factory.CreateUserAsync("cookie@example.test", roles: Roles.GameMaster);
        var response = await factory.CreateClient(new() { HandleCookies = false }).PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = "cookie@example.test", Password = RequiemWebFactory.DefaultPassword }, RequiemJson.Options);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("requiem.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        await factory.CreateUserAsync("known@example.test", roles: Roles.GameMaster);
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = "known@example.test", Password = "not the password at all" }, RequiemJson.Options);
        var unknownEmail = await client.PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = "nobody@example.test", Password = "not the password at all" }, RequiemJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account()
    {
        await factory.CreateUserAsync("locked@example.test", roles: Roles.GameMaster);
        var client = factory.CreateClient();
        var bad = new LoginRequest { Email = "locked@example.test", Password = "wrong wrong wrong" };

        for (var i = 0; i < 5; i++) await client.PostAsJsonAsync("/api/account/login", bad, RequiemJson.Options);

        // Even the right password is refused while locked out.
        var response = await client.PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = "locked@example.test", Password = RequiemWebFactory.DefaultPassword }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var gm = await factory.CreateGmClientAsync("leaving@example.test");
        Assert.Equal(HttpStatusCode.OK, (await gm.GetAsync("/api/characters")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await gm.PostAsync("/api/account/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await gm.GetAsync("/api/characters")).StatusCode);
    }

    [Fact]
    public async Task Invalid_login_request_is_a_validation_error()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = "not-an-email", Password = "" }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>First-run setup, on its own fresh database so no users exist yet.</summary>
public class FirstRunSetupTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task Setup_creates_the_first_gm_once_and_needs_the_code()
    {
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatus>("/api/account/setup", RequiemJson.Options);
        Assert.True(status!.NeedsSetup);
        Assert.True(status.RequiresSetupCode);

        var request = new SetupRequest
        {
            DisplayName = "The Storyteller",
            Email = "st@example.test",
            Password = "a long enough passphrase",
            ConfirmPassword = "a long enough passphrase",
        };

        // Wrong or missing code is refused, and creates nothing.
        request.SetupCode = "guess";
        var refused = await client.PostAsJsonAsync("/api/account/setup", request, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(nameof(SetupRequest.SetupCode), problem!.Errors.Keys);
        Assert.True((await client.GetFromJsonAsync<SetupStatus>("/api/account/setup", RequiemJson.Options))!.NeedsSetup);

        // The right code creates the GM and signs them in.
        request.SetupCode = RequiemWebFactory.SetupCode;
        var created = await client.PostAsJsonAsync("/api/account/setup", request, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var me = await created.Content.ReadFromJsonAsync<CurrentUser>(RequiemJson.Options);
        Assert.Equal("The Storyteller", me!.DisplayName);
        Assert.Contains(Roles.GameMaster, me.Roles);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/characters")).StatusCode);

        // Setup is closed from now on, even with the code.
        Assert.False((await client.GetFromJsonAsync<SetupStatus>("/api/account/setup", RequiemJson.Options))!.NeedsSetup);
        request.Email = "second@example.test";
        var again = await factory.CreateClient().PostAsJsonAsync("/api/account/setup", request, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Short_passwords_and_mismatches_are_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/account/setup", new SetupRequest
        {
            DisplayName = "ST",
            Email = "short@example.test",
            Password = "tooshort",
            ConfirmPassword = "different",
            SetupCode = RequiemWebFactory.SetupCode,
        }, RequiemJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(nameof(SetupRequest.Password), problem!.Errors.Keys);
        Assert.Contains(nameof(SetupRequest.ConfirmPassword), problem.Errors.Keys);
    }
}
