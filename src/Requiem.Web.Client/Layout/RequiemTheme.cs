using MudBlazor;

namespace Requiem.Web.Client.Layout;

/// <summary>Dark, blood-red theme. The app always runs in dark mode.</summary>
public static class RequiemTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#c62828",
            PrimaryContrastText = "#ffffff",
            Secondary = "#a1887f",
            Tertiary = "#c9a227",
            Info = "#64b5f6",
            Background = "#121014",
            BackgroundGray = "#18151b",
            Surface = "#1c191f",
            AppbarBackground = "#1a0d10",
            AppbarText = "#f3e9e9",
            DrawerBackground = "#18151b",
            DrawerText = "#ece6e8",
            DrawerIcon = "#b3a9ad",
            TextPrimary = "#ece6e8",
            TextSecondary = "#b3a9ad",
            ActionDefault = "#b3a9ad",
            LinesDefault = "#3a3338",
            LinesInputs = "#5a5057",
            Divider = "#3a3338",
            TableLines = "#3a3338",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px" },
    };
}
