using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Requiem.Data;
using Requiem.Data.Seeding;
using Requiem.Shared.Accounts;
using Requiem.Shared.Contracts;
using Requiem.Web.Api;
using Requiem.Web.Auth;
using Requiem.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddRequiemData(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddRequiemAuth(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
// The UI runs in the browser (no prerender), but registering MudBlazor keeps server rendering safe if that changes.
builder.Services.AddMudServices();
builder.Services.ConfigureHttpJsonOptions(o => RequiemJson.Configure(o.SerializerOptions));
builder.Services.AddHealthChecks();

// Azure Monitor (Application Insights) when hosted; the connection string comes from App Service settings.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

// Bring the database up to date and fill in any missing catalog entries (seeding is insert-only).
// Always in Development; hosted, when Database:MigrateOnStartup is set (one App Service instance,
// decided 2026-10-24). A failed migration shows up as a failed start.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
    await db.Database.MigrateAsync();
    var seeded = await CatalogSeeder.SeedAsync(db);
    if (seeded.Total > 0)
        app.Logger.LogInformation("Seeded catalog: {Result}", seeded);
    var upgraded = await AccountsUpgrade.RunAsync(db);
    if (upgraded.Changed)
        app.Logger.LogInformation("Upgraded accounts and loose characters: {Result}", upgraded);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// The HTML not-found page is for browser navigation only; API status codes pass through untouched.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

// Everything under /api needs a signed-in user, except the account endpoints (which opt out).
// CampaignAccess narrows each request to the campaigns the user belongs to and their role in each
// (PHASE2.md §4); only catalog edits need the site Admin role.
app.MapGroup("/api")
    .RequireAuthorization(Policies.Member)
    .MapAccountEndpoints()
    .MapCatalogEndpoints()
    .MapCharacterEndpoints()
    .MapFullSheetEndpoints()
    .MapPlayEndpoints()
    .MapCampaignEndpoints();

// For App Service's health check: answers 200 while the app is up. Holds no data, so it's open.
app.MapHealthChecks("/healthz").AllowAnonymous();

app.MapStaticAssets();
// The page shell holds no data, so it is served to anyone: the browser app decides what to show
// (pages carry [Authorize] for its router) and the API above enforces access to the data itself.
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Requiem.Web.Client._Imports).Assembly)
    .AllowAnonymous();

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
