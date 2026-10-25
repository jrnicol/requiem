using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Accounts;

namespace Requiem.Web.Api;

/// <summary>
/// Sign-in, sign-out, sign-up, "who am I", and first-run setup. These (and the invite preview and
/// join) are the only API endpoints open to anonymous callers.
/// </summary>
public static class AccountEndpoints
{
    public const string SetupCodeSetting = "Auth:SetupCode";

    /// <summary>The rate limit shared by everything that creates an account.</summary>
    public const string SignUpLimit = "sign-up";

    // First-run setup must never create two admins, even if two requests race.
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account").WithTags("Account").AllowAnonymous();

        account.MapGet("/me", GetCurrentUserAsync);
        account.MapPost("/login", LoginAsync);
        account.MapPost("/logout", LogoutAsync);
        account.MapPost("/register", RegisterAsync).RequireRateLimiting(SignUpLimit);
        account.MapGet("/setup", GetSetupStatusAsync);
        account.MapPost("/setup", SetupAsync);

        return api;
    }

    /// <summary>
    /// Open sign-up. Every account is the same: what someone can do depends on the campaigns they start
    /// or join. Once the site has its first account, that is; until then this is refused, so the
    /// setup page (and its code) decides who becomes the admin.
    /// </summary>
    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request, UserManager<RequiemUser> users, IUserDirectory directory, SignInManager<RequiemUser> signIn, CancellationToken ct)
    {
        if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);
        if (!await directory.AnyUsersAsync(ct))
            return TypedResults.Problem("This site hasn't been set up yet.", statusCode: StatusCodes.Status409Conflict);

        var email = request.Email.Trim();
        var user = new RequiemUser { UserName = email, Email = email, EmailConfirmed = false, DisplayName = request.DisplayName.Trim() };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return TypedResults.ValidationProblem(CreationErrors(created));

        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(await ToCurrentUserAsync(user, users));
    }

    /// <summary>Identity's reasons an account wasn't created, keyed to the form field they're about.</summary>
    internal static Dictionary<string, string[]> CreationErrors(IdentityResult result) => new()
    {
        [result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
            ? nameof(RegisterRequest.Password)
            : nameof(RegisterRequest.Email)] = result.Errors.Select(e => e.Description).ToArray(),
    };

    private static async Task<CurrentUser> GetCurrentUserAsync(ClaimsPrincipal principal, UserManager<RequiemUser> users)
    {
        if (principal.Identity?.IsAuthenticated != true) return CurrentUser.Anonymous;
        var user = await users.GetUserAsync(principal);
        return user is null ? CurrentUser.Anonymous : await ToCurrentUserAsync(user, users);
    }

    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, UnauthorizedHttpResult, ProblemHttpResult>>
        LoginAsync(LoginRequest request, SignInManager<RequiemUser> signIn, UserManager<RequiemUser> users)
    {
        if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);

        var result = await signIn.PasswordSignInAsync(
            request.Email.Trim(), request.Password, request.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return TypedResults.Problem(
                "Too many failed attempts. Try again in 15 minutes.", statusCode: StatusCodes.Status429TooManyRequests);
        // Same answer for unknown email and wrong password, so accounts can't be probed.
        if (!result.Succeeded) return TypedResults.Unauthorized();

        var user = await users.FindByNameAsync(request.Email.Trim());
        return TypedResults.Ok(await ToCurrentUserAsync(user!, users));
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<RequiemUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<SetupStatus> GetSetupStatusAsync(
        IUserDirectory directory, IConfiguration configuration, IHostEnvironment environment, CancellationToken ct) =>
        new(!await directory.AnyUsersAsync(ct), RequiresSetupCode(configuration, environment));

    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, Conflict<ProblemDetailsBody>, ProblemHttpResult>>
        SetupAsync(
            SetupRequest request,
            IDatabaseSetup database,
            IUserDirectory directory,
            UserManager<RequiemUser> users,
            RoleManager<IdentityRole> roles,
            SignInManager<RequiemUser> signIn,
            IConfiguration configuration,
            IHostEnvironment environment)
    {
        if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);

        await SetupLock.WaitAsync();
        try
        {
            if (await directory.AnyUsersAsync(CancellationToken.None))
                return TypedResults.Conflict(new ProblemDetailsBody("Setup is already complete. Sign in instead."));

            if (!SetupCodeAccepted(request.SetupCode, configuration, environment, out var setupProblem))
                return setupProblem is null
                    ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
                        { [nameof(SetupRequest.SetupCode)] = ["That setup code isn't right."] })
                    : TypedResults.Problem(setupProblem, statusCode: StatusCodes.Status403Forbidden);

            if (!await roles.RoleExistsAsync(Roles.Admin))
                await roles.CreateAsync(new IdentityRole(Roles.Admin));

            var email = request.Email.Trim();
            var user = new RequiemUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = request.DisplayName.Trim(),
            };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded) return TypedResults.ValidationProblem(CreationErrors(created));

            await users.AddToRoleAsync(user, Roles.Admin);
            // Characters made before anyone signed up now have someone to tell them.
            await database.AfterFirstAccountAsync(CancellationToken.None);
            await signIn.SignInAsync(user, isPersistent: true);
            return TypedResults.Ok(await ToCurrentUserAsync(user, users));
        }
        finally
        {
            SetupLock.Release();
        }
    }

    /// <summary>
    /// Outside Development, creating the first account needs the one-time code from configuration,
    /// so whoever reaches a fresh server first can't claim it.
    /// </summary>
    private static bool RequiresSetupCode(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsDevelopment() || !string.IsNullOrEmpty(configuration[SetupCodeSetting]);

    /// <param name="problem">Set when setup is impossible on this server (no code configured outside Development).</param>
    private static bool SetupCodeAccepted(
        string? supplied, IConfiguration configuration, IHostEnvironment environment, out string? problem)
    {
        problem = null;
        if (!RequiresSetupCode(configuration, environment)) return true;

        var expected = configuration[SetupCodeSetting];
        if (string.IsNullOrEmpty(expected))
        {
            problem = $"First-run setup is locked. Set '{SetupCodeSetting}' in the server configuration, then try again.";
            return false;
        }

        return supplied is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied.Trim()), Encoding.UTF8.GetBytes(expected));
    }

    private static async Task<CurrentUser> ToCurrentUserAsync(RequiemUser user, UserManager<RequiemUser> users) =>
        new(true, user.Email, user.DisplayName, [.. await users.GetRolesAsync(user)]);

    private static Dictionary<string, string[]>? Validate(object request)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true)) return null;
        return results
            .SelectMany(r => r.MemberNames.Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(e => e.Member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").ToArray());
    }
}
