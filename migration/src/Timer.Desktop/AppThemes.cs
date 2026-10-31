using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The themes the app offers. Light is the shared package's Light with cooler, crisper surfaces, darker text and lines and a stronger blue accent; the shared package is not changed for it, so the desk app keeps its own Light. Dark is the package's own. After Hours, Botanical, Hawthorn and the user's own themes are retired for now; a saved choice of one of them becomes Light or Dark by its base (<see cref="Resolve"/>).
/// </summary>
public static class AppThemes
{
    // The depth redesign: warm neutral greys (page #E9E9E7, cards #F3F3F1, lines #D9D9D5) in place of the cool blue-white, role colours turned up (a deeper green, a
    // stronger blue, a livelier red) and text on colour in white. Light is the shared package's Light with these; Dark keeps its charcoal but warmer and calmer. The
    // shared package, which the desk app also uses, is unchanged. Each holds the contrast the tests ask.
    public static readonly ThemeDefinition Light = ThemeLibrary.Light with
    {
        Colours = new Dictionary<string, string>(ThemeLibrary.Light.Colours)
        {
            ["Surface"] = "#E9E9E7", ["Panel"] = "#F3F3F1", ["Line"] = "#D9D9D5", ["Hover"] = "#E3E3E0", ["Muted"] = "#596070", ["Idle"] = "#596070",
            ["Ink"] = "#1D222B", ["Clock"] = "#1D222B", ["Accent"] = "#2F5BE0", ["Selected"] = "#D9E2FA",
            ["Live"] = "#12864D", ["Start"] = "#12864D", ["Overtime"] = "#C41E36", ["Finish"] = "#C41E36", ["BudgetWarning"] = "#E3920B",
            ["PillSurface"] = "#1D222B", ["PillInk"] = "#E9E9E7", ["PillBreak"] = "#FFA033", ["PillOvertime"] = "#C41E36", ["Break"] = BreakOnTheWarmPage()
        },
        BlockTint = 0.78 // Only for the shared derivation; blocks are drawn from AccountTones.Vivid
    };
    /// <summary>The break amber, a deep orange, taken a little darker only as far as it needs to hold 3 to 1 on the page.</summary>
    private static string BreakOnTheWarmPage()
    {
        var amber = "#C77700";
        for (var amount = 0.0; amount <= 1; amount += .02) { var shade = ThemeColours.Mix(amber, "#1D222B", amount); if (ThemeColours.Contrast(shade, "#E9E9E7") >= 3) return shade; }
        return amber;
    }
    public static readonly ThemeDefinition Dark = ThemeLibrary.Dark with
    {
        Colours = new Dictionary<string, string>(ThemeLibrary.Dark.Colours)
        {
            ["Surface"] = "#1A1C20", ["Panel"] = "#22252A", ["Line"] = "#32363D", ["Hover"] = "#2B2F36", ["Selected"] = "#2C3A5C", ["Muted"] = "#A0A7B3", ["Idle"] = "#A0A7B3",
            ["Ink"] = "#ECEEF2", ["Clock"] = "#ECEEF2", ["Accent"] = "#7FA8FF", ["AccentInk"] = "#0E1422",
            ["Live"] = "#3EDC8E", ["Start"] = "#3EDC8E", ["Overtime"] = "#FF6B6B", ["Finish"] = "#FF6B6B", ["BudgetWarning"] = "#F5C04A", ["Break"] = "#FFA23F",
            ["PillSurface"] = "#2A2D33", ["PillInk"] = "#ECEEF2", ["PillBreak"] = "#FFA23F", ["PillOvertime"] = "#A02A38"
        }
    };
    /// <summary>The choice that is Light by day and Dark by night, switching at sunset and sunrise (or at times set by hand). It is a choice, not a theme of its own: <see cref="TimerModel.CurrentTheme"/> is Light or Dark.</summary>
    public const string Sun = "Sun";
    public static readonly IReadOnlyList<ThemeDefinition> BuiltIn = new[] { Light, Dark };
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
