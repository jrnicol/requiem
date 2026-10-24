using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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
    public async Task A_signed_in_user_in_no_campaign_sees_no_characters()
    {
        var client = await factory.CreateMemberClientAsync("newcomer@example.test");
        Assert.Empty((await client.GetFromJsonAsync<List<CharacterSummary>>("/api/characters", RequiemJson.Options))!);
    }

    [Fact]
    public async Task Me_reports_anonymous_then_the_signed_in_user()
    {
        var anonymous = await factory.CreateClient().GetFromJsonAsync<CurrentUser>("/api/account/me", RequiemJson.Options);
        Assert.False(anonymous!.IsAuthenticated);

        var admin = await factory.CreateMemberClientAsync("me@example.test", Roles.Admin);
        var me = await admin.GetFromJsonAsync<CurrentUser>("/api/account/me", RequiemJson.Options);
        Assert.True(me!.IsAuthenticated);
        Assert.Equal("me@example.test", me.Email);
        Assert.Contains(Roles.Admin, me.Roles);
    }

    [Fact]
    public async Task Auth_cookie_is_http_only_and_same_site_strict()
    {
        await factory.CreateUserAsync("cookie@example.test");
        var response = await factory.CreateClient(new() { HandleCookies = false }).PostAsJsonAsync("/api/account/login",
            new LoginRequest { Email = "cookie@example.test", Password = RequiemWebFactory.DefaultPassword }, RequiemJson.Options);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("requiem.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        await factory.CreateUserAsync("known@example.test");
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
        await factory.CreateUserAsync("locked@example.test");
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
        var user = await factory.CreateMemberClientAsync("leaving@example.test");
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/characters")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync("/api/account/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/characters")).StatusCode);
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
    public async Task Setup_creates_the_first_account_as_admin_once_and_needs_the_code()
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

        // Open sign-up waits until the site has its admin.
        var early = await client.PostAsJsonAsync("/api/account/register", new RegisterRequest
        {
            DisplayName = "Early", Email = "early@example.test", Password = "a long enough passphrase", ConfirmPassword = "a long enough passphrase",
        }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        // The right code creates the admin and signs them in.
        request.SetupCode = RequiemWebFactory.SetupCode;
        var created = await client.PostAsJsonAsync("/api/account/setup", request, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var me = await created.Content.ReadFromJsonAsync<CurrentUser>(RequiemJson.Options);
        Assert.Equal("The Storyteller", me!.DisplayName);
        Assert.Equal([Roles.Admin], me.Roles);
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

/// <summary>Open sign-up: anyone can make an account, and every account is alike.</summary>
public class SignUpTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task Anyone_can_sign_up_and_start_a_campaign()
    {
        await factory.CreateUserAsync("admin@example.test", roles: Roles.Admin);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/account/register", NewUser("Nell", "nell@example.test"), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = (await response.Content.ReadFromJsonAsync<CurrentUser>(RequiemJson.Options))!;
        Assert.Equal(("Nell", true), (me.DisplayName, me.IsAuthenticated));
        Assert.Empty(me.Roles); // no account types: what Nell can do depends on her campaigns

        // Signed in, Nell can start a campaign and tells it.
        var created = await client.PostAsJsonAsync("/api/campaigns", new CampaignForm { Name = "Nell's Chronicle" }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True((await created.Content.ReadFromJsonAsync<CampaignView>(RequiemJson.Options))!.IsStoryteller);

        // But not edit the shared catalog.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/api/catalog/powers/1", new PowerEditForm { Name = "Mine" }, RequiemJson.Options)).StatusCode);
    }

    [Fact]
    public async Task An_email_can_only_sign_up_once()
    {
        await factory.CreateUserAsync("admin2@example.test", roles: Roles.Admin);
        var first = await factory.CreateClient().PostAsJsonAsync("/api/account/register", NewUser("Oz", "oz@example.test"), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var again = await factory.CreateClient().PostAsJsonAsync("/api/account/register", NewUser("Oz again", "OZ@example.test"), RequiemJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var problem = await again.Content.ReadFromJsonAsync<ValidationProblemDetails>(RequiemJson.Options);
        Assert.Contains(nameof(RegisterRequest.Email), problem!.Errors.Keys);
    }

    [Fact]
    public async Task Sign_ups_are_capped_per_hour()
    {
        await factory.CreateUserAsync("admin3@example.test", roles: Roles.Admin);
        using var capped = factory.WithWebHostBuilder(b => b.UseSetting("Auth:SignUpsPerHour", "2"));

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await capped.CreateClient().PostAsJsonAsync("/api/account/register",
                NewUser($"Quick {i}", $"quick{i}@example.test"), RequiemJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await capped.CreateClient().PostAsJsonAsync("/api/account/register",
            NewUser("One too many", "late@example.test"), RequiemJson.Options)).StatusCode);
    }

    private static RegisterRequest NewUser(string name, string email) => new()
    {
        DisplayName = name, Email = email, Password = "a long enough passphrase", ConfirmPassword = "a long enough passphrase",
    };
}
