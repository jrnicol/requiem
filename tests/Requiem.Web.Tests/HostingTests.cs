using Microsoft.AspNetCore.Hosting;

namespace Requiem.Web.Tests;

/// <summary>How the API host behaves locally (serving the browser app too) and on Lambda (API only).</summary>
public class HostingTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Fact]
    public async Task Unknown_api_paths_are_404s_not_the_browser_app()
    {
        var response = await factory.CreateClient().GetAsync("/api/no-such-thing");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("blazor.webassembly", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task On_lambda_only_the_api_is_served()
    {
        // S3 serves the browser app there, so the API host leaves its pages alone.
        using var lambda = factory.WithWebHostBuilder(b => b.UseSetting("Hosting:ServeClient", "false"));
        var client = lambda.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/campaigns")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
    }
}
