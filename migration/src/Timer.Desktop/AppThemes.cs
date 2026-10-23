using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The themes the app offers. Light is the shared package's Light with a quiet warm neutral in place of its blue-grey surfaces, so the accent is the only
/// blue on screen; the shared package is not changed for it, so the desk app keeps its own Light. Dark and the studies are the package's own.
/// </summary>
public static class AppThemes
{
    // Page #F4F4F1, cards white, lines #E2E2DD, hovers #EDEDEA, quiet text #5A6070. Ink, the accent and the selection tint are the package's.
    public static readonly ThemeDefinition Light = ThemeLibrary.Light with
    {
        Colours = new Dictionary<string, string>(ThemeLibrary.Light.Colours)
        {
            ["Surface"] = "#F4F4F1", ["Panel"] = "#FFFFFF", ["Line"] = "#E2E2DD", ["Hover"] = "#EDEDEA", ["Muted"] = "#5A6070", ["Idle"] = "#5A6070",
            ["PillSurface"] = ThemeLibrary.Light.Colour("Ink"), ["PillInk"] = "#F4F4F1"
        }
    };
    public static ThemeDefinition Dark => ThemeLibrary.Dark;
    public static readonly IReadOnlyList<ThemeDefinition> BuiltIn = new[] { Light, ThemeLibrary.Dark, ThemeLibrary.AfterHours, ThemeLibrary.Botanical, ThemeLibrary.Hawthorn };
}
