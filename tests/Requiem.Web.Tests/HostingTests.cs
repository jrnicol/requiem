using Microsoft.AspNetCore.Hosting;

namespace Requiem.Web.Tests;

/// <summary>Running behind CloudFront on AWS (DEPLOY.md), which talks to the app over plain HTTP.</summary>
public class HostingTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Behind_an_https_proxy_plain_http_requests_count_as_https(bool behindProxy, bool expectHsts)
    {
        using var hosted = factory.WithWebHostBuilder(b => b.UseSetting("Hosting:BehindHttpsProxy", behindProxy.ToString()));
        var client = hosted.CreateClient(new() { BaseAddress = new Uri("http://requiem.example") });

        // HSTS is only sent over HTTPS (and never to localhost), so it shows how the app saw the request.
        var response = await client.GetAsync("/healthz");

        response.EnsureSuccessStatusCode();
        Assert.Equal(expectHsts, response.Headers.Contains("Strict-Transport-Security"));
    }
}
