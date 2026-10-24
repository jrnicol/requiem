using System.Net;

namespace Requiem.Web.Client.Services;

/// <summary>
/// Watches API responses: a 401 means the session is gone, so the app switches to signed-out
/// and the router sends the GM to the login page.
/// </summary>
public sealed class UnauthorizedHandler(CookieAuthenticationStateProvider auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) auth.MarkSignedOut();
        return response;
    }
}
