using Amazon.Lambda.AspNetCoreServer.Hosting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Requiem.Data.Dynamo;
using Requiem.Data.Stores;
using Requiem.Shared.Accounts;
using Requiem.Shared.Contracts;
using Requiem.Web.Api;
using Requiem.Web.Auth;

var builder = WebApplication.CreateBuilder(args);

// Locally, DynamoDB Local (Java) stands in for DynamoDB (SERVERLESS.md). The app starts it, keeps its data
// in App_Data, and stops it on exit.
if (builder.Configuration.GetValue<bool>("DynamoDB:StartLocal"))
{
    var local = DynamoDbLocal.Start(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dynamodb"));
    builder.Configuration["DynamoDB:ServiceUrl"] = local.ServiceUrl;
    builder.Services.AddSingleton(local);
}

// Serving the browser app from a build (rather than a publish) reads its files from the client project,
// which ASP.NET does only in Development unless asked. Tests run as "Testing", so ask.
var serveClient = builder.Configuration.GetValue("Hosting:ServeClient", true);
if (serveClient) builder.WebHost.UseStaticWebAssets();

// Add services to the container. The UI is a standalone WebAssembly app; this host is the API.
builder.Services.AddRequiemData(builder.Configuration);
builder.Services.AddRequiemAuth(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => RequiemJson.Configure(o.SerializerOptions));
builder.Services.AddHealthChecks();
// On AWS the API runs in Lambda behind API Gateway's HTTP API (SERVERLESS.md); elsewhere this does nothing.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// Azure Monitor (Application Insights) when hosted; the connection string comes from App Service settings.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

// Create the table where allowed (DynamoDB Local) and fill in any missing catalog entries (seeding is insert-only).
// Always in Development; hosted, when Database:SetupOnStartup is set. Seeding is skipped while the seed files
// are unchanged, so a cold start pays one read. A failure shows up as a failed start.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:SetupOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IDatabaseSetup>().InitializeAsync(CancellationToken.None);
}

// On AWS, CloudFront reaches the app over plain HTTP and sends no X-Forwarded-Proto. Visitors always
// arrive over HTTPS (CloudFront redirects HTTP), so requests are treated as HTTPS: HSTS is sent and
// generated URLs use https.
if (app.Configuration.GetValue<bool>("Hosting:BehindHttpsProxy"))
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = Uri.UriSchemeHttps;
        return next(context);
    });
}

// Configure the HTTP request pipeline. The API answers errors with problem details, never HTML pages.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler();
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();

// Locally (and in tests) this app also serves the browser app, as S3 does when hosted. Paths the browser
// app owns (no file extension, not the API) get index.html, and its router shows the page or "not found".
if (serveClient)
{
    app.Use((context, next) =>
    {
        var path = context.Request.Path;
        if (HttpMethods.IsGet(context.Request.Method) && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/healthz")
            && !Path.HasExtension(path.Value))
            context.Request.Path = "/index.html";
        return next(context);
    });
}
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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
    .MapCampaignEndpoints()
    .MapPlayerCharacterEndpoints()
    .MapAdvancementEndpoints();

// Answers 200 while the app is up. Holds no data, so it's open.
app.MapHealthChecks("/healthz").AllowAnonymous();

// The browser app's files hold no data, so they're served to anyone: the app decides what to show
// (pages carry [Authorize] for its router) and the API above enforces access to the data itself.
if (serveClient) app.MapStaticAssets().AllowAnonymous();

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
