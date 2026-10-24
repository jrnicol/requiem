using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;

namespace Requiem.Web.Api;

/// <summary>
/// Sign-in, sign-out, "who am I", and first-run setup. These are the only API endpoints open to
/// anonymous callers.
/// </summary>
public static class AccountEndpoints
{
    public const string SetupCodeSetting = "Auth:SetupCode";

    // First-run setup must never create two GMs, even if two requests race.
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account").WithTags("Account").AllowAnonymous();

        account.MapGet("/me", GetCurrentUserAsync);
        account.MapPost("/login", LoginAsync);
        account.MapPost("/logout", LogoutAsync);
        account.MapGet("/setup", GetSetupStatusAsync);
        account.MapPost("/setup", SetupAsync);

        return api;
    }

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
        UserManager<RequiemUser> users, IConfiguration configuration, IHostEnvironment environment) =>
        new(!await users.Users.AnyAsync(), RequiresSetupCode(configuration, environment));

    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, Conflict<ProblemDetailsBody>, ProblemHttpResult>>
        SetupAsync(
            SetupRequest request,
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
            if (await users.Users.AnyAsync())
                return TypedResults.Conflict(new ProblemDetailsBody("Setup is already complete. Sign in instead."));

            if (!SetupCodeAccepted(request.SetupCode, configuration, environment, out var setupProblem))
                return setupProblem is null
                    ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
                        { [nameof(SetupRequest.SetupCode)] = ["That setup code isn't right."] })
                    : TypedResults.Problem(setupProblem, statusCode: StatusCodes.Status403Forbidden);

            if (!await roles.RoleExistsAsync(Roles.GameMaster))
                await roles.CreateAsync(new IdentityRole(Roles.GameMaster));

            var email = request.Email.Trim();
            var user = new RequiemUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = request.DisplayName.Trim(),
            };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [created.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
                        ? nameof(SetupRequest.Password)
                        : nameof(SetupRequest.Email)] = created.Errors.Select(e => e.Description).ToArray(),
                });

            await users.AddToRoleAsync(user, Roles.GameMaster);
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
