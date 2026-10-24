using Requiem.Web.Client.Components;

namespace Requiem.Web.Tests;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("npcs/new", "npcs/new")]
    [InlineData("/npcs/abc", "npcs/abc")]
    [InlineData("?search=agnes", "?search=agnes")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Local_paths_are_kept(string? input, string expected) =>
        Assert.Equal(expected, ReturnUrl.SafeOrHome(input));

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public void Anything_that_could_leave_the_app_goes_home(string input) =>
        Assert.Equal("", ReturnUrl.SafeOrHome(input));
}
