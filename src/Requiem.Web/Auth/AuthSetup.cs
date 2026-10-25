using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Requiem.Data.Dynamo;
using Requiem.Data.Entities;
using Requiem.Shared.Accounts;

namespace Requiem.Web.Auth;

public static class AuthSetup
{
    public const string CookieName = "requiem.auth";

    /// <summary>
    /// Cookie sign-in with ASP.NET Core Identity. The browser app and API share an origin, so a
    /// SameSite=Strict, HttpOnly cookie is all the API needs; there are no tokens to store in the browser.
    /// </summary>
    public static IServiceCollection AddRequiemAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddIdentityCore<RequiemUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // The key ring that signs the cookie lives in the table: Lambda instances come and go and share no
        // disk, and keys kept on one would sign everyone out on every cold start.
        services.AddDataProtection()
            .SetApplicationName("Requiem")
            .PersistKeysToDynamo();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Plain http is only used locally; everywhere else the cookie is HTTPS-only.
            options.Cookie.SecurePolicy = environment.IsDevelopment() || environment.IsEnvironment("Testing")
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            // The browser app handles navigation; the API just reports the status.
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => WriteStatus(context, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => WriteStatus(context, StatusCodes.Status403Forbidden),
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Member, policy => policy.RequireAuthenticatedUser());

        // Sign-up is open to anyone, so cap how many accounts can be made in an hour. The hosted app
        // sits behind a proxy, so this is one window for the whole site rather than per address.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(Api.AccountEndpoints.SignUpLimit, window =>
            {
                window.PermitLimit = configuration.GetValue("Auth:SignUpsPerHour", 30);
                window.Window = TimeSpan.FromHours(1);
                window.QueueLimit = 0;
            });
        });

        // Per-request answers to "may this user see or change that?" (PHASE2.md §4).
        services.AddHttpContextAccessor();
        services.AddScoped<CampaignAccess>();
        services.AddScoped<AuditLog>();

        return services;
    }

    private static Task WriteStatus(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
