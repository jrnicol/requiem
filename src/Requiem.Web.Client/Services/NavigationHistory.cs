using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Requiem.Web.Client.Services;

/// <summary>
/// The in-app pages visited in this tab, so a page's back arrow can return to wherever the GM came
/// from (roster, scene, play view) instead of one fixed page.
/// </summary>
public sealed class NavigationHistory : IDisposable
{
    private const int MaxEntries = 50;

    private readonly NavigationManager _navigation;
    private readonly List<string> _entries = [];

    public NavigationHistory(NavigationManager navigation)
    {
        _navigation = navigation;
        _entries.Add(Relative(navigation.Uri));
        navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>The page before the current one, or null when this tab started here.</summary>
    public string? Previous => _entries.Count >= 2 ? _entries[^2] : null;

    /// <summary>Goes to the previous in-app page, or to <paramref name="fallback"/> when there isn't one.</summary>
    public void Back(string fallback) => _navigation.NavigateTo(Previous ?? fallback);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var uri = Relative(e.Location);
        if (_entries.Count > 0 && _entries[^1] == uri) return;

        // Going back to the previous page: drop the page being left instead of stacking it.
        if (_entries.Count >= 2 && _entries[^2] == uri)
        {
            _entries.RemoveAt(_entries.Count - 1);
            return;
        }

        // A "new" page is replaced by what it becomes once saved (sheets/new → sheets/{id}).
        if (_entries.Count > 0 && IsNewPage(_entries[^1])) _entries.RemoveAt(_entries.Count - 1);

        _entries.Add(uri);
        if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
    }

    private static bool IsNewPage(string uri) => uri.Split('?')[0].EndsWith("/new", StringComparison.OrdinalIgnoreCase);

    private string Relative(string absolute) => _navigation.ToBaseRelativePath(absolute);

    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;
}
