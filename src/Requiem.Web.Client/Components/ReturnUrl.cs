namespace Requiem.Web.Client.Components;

public static class ReturnUrl
{
    /// <summary>
    /// Only app-relative paths are followed after sign-in, so a crafted link can't bounce the GM
    /// to another site (open redirect).
    /// </summary>
    public static string SafeOrHome(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return "";
        var url = returnUrl.Trim();
        var isLocal = Uri.IsWellFormedUriString(url, UriKind.Relative)
            && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.StartsWith("/\\", StringComparison.Ordinal)
            && !url.Contains('\\')
            && !url.Contains(':');
        return isLocal ? url.TrimStart('/') : "";
    }
}
