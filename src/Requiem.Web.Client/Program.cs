using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Requiem.Web.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using Requiem.Shared.Accounts;
using Requiem.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
// A standalone app: wwwroot/index.html is the page (served from S3 when hosted, by the API locally).
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
});

// Same-origin requests carry the HttpOnly auth cookie automatically.
// The auth provider has its own client; the API client reports 401s back to it.
var auth = new CookieAuthenticationStateProvider(new HttpClient { BaseAddress = baseAddress });
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton<AuthenticationStateProvider>(auth);
builder.Services.AddSingleton(_ => new HttpClient(new UnauthorizedHandler(auth) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = baseAddress,
});
builder.Services.AddScoped<RequiemApi>();
builder.Services.AddScoped<NavigationHistory>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));
    options.AddPolicy(Policies.Member, policy => policy.RequireAuthenticatedUser());
});

await builder.Build().RunAsync();
