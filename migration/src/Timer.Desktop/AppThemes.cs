using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The themes the app offers. Light is the shared package's Light with cooler, crisper surfaces, darker text and lines and a stronger blue accent; the shared package is not changed for it, so the desk app keeps its own Light. Dark is the package's own. After Hours, Botanical, Hawthorn and the user's own themes are retired for now; a saved choice of one of them becomes Light or Dark by its base (<see cref="Resolve"/>).
/// </summary>
public static class AppThemes
{
    // Round 28, option B (cool and crisp): page #EDF0F5, cards white, lines #D5DBE5, hovers #E4E9F1, text #131B2A, quiet text #4E5A6C, accent #2D5CCB,
    // selection #D6E2FA, and blocks that take 78 per cent of their account's colour so they stand clear of the grey tiles. Each holds the contrast the tests ask.
    public static readonly ThemeDefinition Light = ThemeLibrary.Light with
    {
        Colours = new Dictionary<string, string>(ThemeLibrary.Light.Colours)
        {
            ["Surface"] = "#EDF0F5", ["Panel"] = "#FFFFFF", ["Line"] = "#D5DBE5", ["Hover"] = "#E4E9F1", ["Muted"] = "#4E5A6C", ["Idle"] = "#4E5A6C",
            ["Ink"] = "#131B2A", ["Clock"] = "#131B2A", ["Accent"] = "#2D5CCB", ["Selected"] = "#D6E2FA",
            ["PillSurface"] = "#131B2A", ["PillInk"] = "#EDF0F5", ["Break"] = BreakOnTheCoolPage()
        },
        BlockTint = 0.78
    };
    /// <summary>The package's break amber, taken a little darker only as far as it needs to hold 3 to 1 on the cooler page (it held it on the old warm one).</summary>
    private static string BreakOnTheCoolPage()
    {
        var amber = ThemeLibrary.Light.Colour("Break");
        for (var amount = 0.0; amount <= 1; amount += .02) { var shade = ThemeColours.Mix(amber, "#131B2A", amount); if (ThemeColours.Contrast(shade, "#EDF0F5") >= 3) return shade; }
        return amber;
    }
    public static ThemeDefinition Dark => ThemeLibrary.Dark;
    /// <summary>The choice that is Light by day and Dark by night, switching at sunset and sunrise (or at times set by hand). It is a choice, not a theme of its own: <see cref="TimerModel.CurrentTheme"/> is Light or Dark.</summary>
    public const string Sun = "Sun";
    public static readonly IReadOnlyList<ThemeDefinition> BuiltIn = new[] { Light, ThemeLibrary.Dark };
    /// <summary>
    /// The theme choice to use for a saved one: System, Light or Dark as they are, and a retired built-in or a user's own theme by its base, so a saved
    /// choice never falls back to System by surprise. The theme itself stays in the settings file, untouched.
    /// </summary>
    public static string Resolve(string? saved, IEnumerable<ThemeDefinition>? own)
    {
        if (string.IsNullOrWhiteSpace(saved)) return ThemeLibrary.System;
        foreach (var current in new[] { ThemeLibrary.System, Sun, Light.Name, Dark.Name }) if (current.Equals(saved, StringComparison.OrdinalIgnoreCase)) return current;
        var known = (own ?? System.Array.Empty<ThemeDefinition>()).Concat(ThemeLibrary.BuiltIn).FirstOrDefault(t => t.Name.Equals(saved, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known.Dark ? Dark.Name : Light.Name;
        var renamed = ThemeLibrary.Renamed(saved);
        return renamed.Equals(Light.Name, StringComparison.OrdinalIgnoreCase) ? Light.Name : renamed.Equals(Dark.Name, StringComparison.OrdinalIgnoreCase) ? Dark.Name : ThemeLibrary.System;
    }
}
