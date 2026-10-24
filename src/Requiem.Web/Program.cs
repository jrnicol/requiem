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
builder.Services.AddRequiemAuth(builder.Environment);
builder.Services.AddProblemDetails();
// The UI runs in the browser (no prerender), but registering MudBlazor keeps server rendering safe if that changes.
builder.Services.AddMudServices();
builder.Services.ConfigureHttpJsonOptions(o => RequiemJson.Configure(o.SerializerOptions));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();

    // Local development only: bring the database up to date and fill in any missing catalog entries.
    // Hosted environments apply migrations from the deployment pipeline instead.
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RequiemDbContext>();
    await db.Database.MigrateAsync();
    var seeded = await CatalogSeeder.SeedAsync(db);
    if (seeded.Total > 0)
        app.Logger.LogInformation("Seeded catalog: {Result}", seeded);
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
app.UseAntiforgery();

// Everything under /api requires a signed-in GM, except the account endpoints (which opt out).
app.MapGroup("/api")
    .RequireAuthorization(Policies.GameMaster)
    .MapAccountEndpoints()
    .MapCatalogEndpoints()
    .MapCharacterEndpoints()
    .MapFullSheetEndpoints()
    .MapPlayEndpoints();

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
