using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The themes the app offers. Light is the shared package's Light with a quiet warm neutral in place of its blue-grey surfaces, so the accent is the only
/// blue on screen; the shared package is not changed for it, so the desk app keeps its own Light. Dark is the package's own. After Hours, Botanical, Hawthorn and the user's own themes are retired for now; a saved choice of one of them becomes Light or Dark by its base (<see cref="Resolve"/>).
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
    public static readonly IReadOnlyList<ThemeDefinition> BuiltIn = new[] { Light, ThemeLibrary.Dark };
    /// <summary>
    /// The theme choice to use for a saved one: System, Light or Dark as they are, and a retired built-in or a user's own theme by its base, so a saved
    /// choice never falls back to System by surprise. The theme itself stays in the settings file, untouched.
    /// </summary>
    public static string Resolve(string? saved, IEnumerable<ThemeDefinition>? own)
    {
        if (string.IsNullOrWhiteSpace(saved)) return ThemeLibrary.System;
        foreach (var current in new[] { ThemeLibrary.System, Light.Name, Dark.Name }) if (current.Equals(saved, StringComparison.OrdinalIgnoreCase)) return current;
        var known = (own ?? System.Array.Empty<ThemeDefinition>()).Concat(ThemeLibrary.BuiltIn).FirstOrDefault(t => t.Name.Equals(saved, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known.Dark ? Dark.Name : Light.Name;
        var renamed = ThemeLibrary.Renamed(saved);
        return renamed.Equals(Light.Name, StringComparison.OrdinalIgnoreCase) ? Light.Name : renamed.Equals(Dark.Name, StringComparison.OrdinalIgnoreCase) ? Dark.Name : ThemeLibrary.System;
    }
}
