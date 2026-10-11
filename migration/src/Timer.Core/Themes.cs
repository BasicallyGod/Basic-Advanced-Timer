using System.Globalization;
using System.Text.Json;

namespace Timer.Core;

/// <summary>
/// A theme is a name, whether it sits on a light or a dark base (so the standard controls know what to expect), and a colour for each
/// named part of the app. Everything else about how a theme looks, including the pill's break and overtime tints, is worked out from these.
/// </summary>
public sealed record ThemeDefinition(string Name, bool Dark, IReadOnlyDictionary<string, string> Colours, bool BuiltIn = false, string BlockStyle = ThemeBlockStyle.AsChosen)
{
    public string Colour(string token) => Colours.TryGetValue(token, out var value) ? value : throw new KeyNotFoundException("The theme has no colour for " + token);
}

/// <summary>How account colours are drawn on the timeline and week bars.</summary>
public static class ThemeBlockStyle
{
    /// <summary>The account's own colour, exactly as chosen (very dark colours are still lifted so they show).</summary>
    public const string AsChosen = "asChosen";
    /// <summary>The account's colour softened towards the theme's surface, so blocks sit calmly in it.</summary>
    public const string Softened = "softened";
    public static bool IsKnown(string? style) => style is AsChosen or Softened;
}

public static class ThemeTokens
{
    /// <summary>The coloured parts of the app a theme sets, in the order the editor lists them.</summary>
    public static readonly string[] Names =
    {
        "Surface", "Panel", "Ink", "Muted", "Line", "Hover", "Selected", "Live", "Idle", "Overtime", "BudgetWarning", "Break", "Finish", "Accent", "AccentInk", "Clock", "ShadowColor"
    };
    public static string Describe(string token) => token switch
    {
        "Surface" => "Window background", "Panel" => "Side panel and controls", "Ink" => "Text", "Muted" => "Quiet text", "Line" => "Lines and borders",
        "Hover" => "Hovered row", "Selected" => "Selected row", "Live" => "Tracking and start", "Idle" => "Not tracking", "Overtime" => "Overtime",
        "BudgetWarning" => "Budget nearly used", "Break" => "Breaks", "Finish" => "Finish day", "Accent" => "Accent (tabs, main button)", "AccentInk" => "Text on the accent", "Clock" => "Clock when idle", "ShadowColor" => "Window shadow", _ => token
    };
    /// <summary>#RRGGBB for every colour, or #AARRGGBB for the shadow, which needs transparency.</summary>
    public static bool IsValid(string token, string? value)
    {
        if (value is null || !value.StartsWith('#')) return false;
        var digits = value[1..];
        var length = token == "ShadowColor" ? 8 : 6;
        return digits.Length == length && digits.All(Uri.IsHexDigit);
    }
}

public static class ThemeColours
{
    public static (int R, int G, int B) Rgb(string hex)
    {
        var d = hex.Length == 9 ? hex[3..] : hex[1..];
        return (Convert.ToInt32(d[..2], 16), Convert.ToInt32(d.Substring(2, 2), 16), Convert.ToInt32(d.Substring(4, 2), 16));
    }
    public static string Hex(int r, int g, int b) => "#" + Math.Clamp(r, 0, 255).ToString("X2") + Math.Clamp(g, 0, 255).ToString("X2") + Math.Clamp(b, 0, 255).ToString("X2");
    /// <summary>Blends two colours; <paramref name="amount"/> is how much of the second to take, from 0 to 1.</summary>
    public static string Mix(string from, string to, double amount)
    {
        var a = Rgb(from); var b = Rgb(to);
        return Hex((int)Math.Round(a.R + (b.R - a.R) * amount), (int)Math.Round(a.G + (b.G - a.G) * amount), (int)Math.Round(a.B + (b.B - a.B) * amount));
    }
    public static (double H, double S, double L) Hsl(string hex)
    {
        var (r, g, b) = Rgb(hex); double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        var max = Math.Max(R, Math.Max(G, B)); var min = Math.Min(R, Math.Min(G, B)); var l = (max + min) / 2; double h = 0, s = 0;
        if (max != min)
        {
            var d = max - min; s = l > .5 ? d / (2 - max - min) : d / (max + min);
            h = max == R ? (G - B) / d + (G < B ? 6 : 0) : max == G ? (B - R) / d + 2 : (R - G) / d + 4; h *= 60;
        }
        return (h, s, l);
    }
    public static string FromHsl(double h, double s, double l)
    {
        double F(double n) { var k = (n + h / 30) % 12; var a = s * Math.Min(l, 1 - l); return l - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)); }
        return Hex((int)Math.Round(F(0) * 255), (int)Math.Round(F(8) * 255), (int)Math.Round(F(4) * 255));
    }
    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        double C(int v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
    }
    /// <summary>The WCAG contrast ratio between two colours, from 1 (none) to 21. Text wants about 4.5 or more.</summary>
    public static double Contrast(string a, string b)
    {
        var x = Luminance(a); var y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

public static class ThemeDerivation
{
    public const string Break = "Break", Overtime = "Overtime";
    // How strongly each part takes on the state's hue: the same lightness as the theme's own colour, with this much of the hue's
    // saturation, so a break feels amber and overtime red in any theme without hiding what the theme set.
    private static readonly (string Token, double Light, double Dark)[] Tinted =
    {
        ("Surface", .62, .32), ("Panel", .70, .30), ("Ink", .45, .45), ("Muted", .28, .22), ("Line", .50, .30), ("Hover", .55, .30), ("Selected", .55, .35), ("Idle", .22, .20)
    };
    /// <summary>The colours the pill wears during a break or overtime: the theme's own, with its backgrounds, lines and text tinted towards the accent.</summary>
    public static IReadOnlyDictionary<string, string> Variant(ThemeDefinition theme, string state)
    {
        var accent = theme.Colour(state == Break ? "Break" : "Overtime");
        var hue = ThemeColours.Hsl(accent).H;
        var result = new Dictionary<string, string>(theme.Colours);
        foreach (var (token, light, dark) in Tinted)
        {
            var own = ThemeColours.Hsl(theme.Colour(token));
            // Pure black and white have no hue to tint, so lightness is kept just inside the extremes and the state still shows.
            result[token] = ThemeColours.FromHsl(hue, theme.Dark ? dark : light, Math.Clamp(own.L, .04, .96));
        }
        return result;
    }
    /// <summary>A block colour softened towards the theme's surface, for themes that ask for it. Dark colours are lifted first so they show.</summary>
    public static string Soften(ThemeDefinition theme, string colour)
        => theme.BlockStyle == ThemeBlockStyle.Softened ? ThemeColours.Mix(colour, theme.Colour("Surface"), theme.Dark ? .38 : .22) : colour;
}

public static class ThemeLibrary
{
    private static ThemeDefinition Make(string name, bool dark, string surface, string panel, string ink, string muted, string line, string hover, string selected,
        string live, string overtime, string warning, string breakColour, string finish, string accent, string accentInk, string clock, string shadow)
        => new(name, dark, new Dictionary<string, string>
        {
            ["Surface"] = surface, ["Panel"] = panel, ["Ink"] = ink, ["Muted"] = muted, ["Line"] = line, ["Hover"] = hover, ["Selected"] = selected, ["Live"] = live,
            ["Idle"] = muted, ["Overtime"] = overtime, ["BudgetWarning"] = warning, ["Break"] = breakColour, ["Finish"] = finish, ["Accent"] = accent, ["AccentInk"] = accentInk,
            ["Clock"] = clock, ["ShadowColor"] = shadow
        }, true);
    private const string DarkShadow = "#B3000000", LightShadow = "#59000000";
    /// <summary>The default: deep green with a lime accent.</summary>
    public static readonly ThemeDefinition Hawthorn = Make("Hawthorn", true, "#0A4A2C", "#0D5632", "#F6FBF3", "#A9C8B1", "#2E7A52", "#11603A", "#1A7248",
        "#C5F63C", "#FF9A9A", "#FFD166", "#F6CB4D", "#FF7B7B", "#C5F63C", "#0A2E1B", "#C5F63C", DarkShadow);
    public static readonly ThemeDefinition Light = Make("Light", false, "#FAFAFB", "#EEF0F2", "#1B1F24", "#646A73", "#D8DBE0", "#E4E7EB", "#D8E4F8",
        "#2E7D4F", "#C0392B", "#9A6200", "#A87400", "#C62828", "#2F5FCF", "#FFFFFF", "#1B1F24", LightShadow);
    public static readonly ThemeDefinition Dark = Make("Dark", true, "#1E2227", "#272C33", "#ECEFF3", "#A3AAB4", "#3A414A", "#313842", "#2F4560",
        "#7FD6A6", "#FF9494", "#E8BD67", "#F0C45A", "#FF8A8A", "#2F6FDB", "#FFFFFF", "#ECEFF3", DarkShadow);
    public static readonly ThemeDefinition Sage = Make("Sage", true, "#14201B", "#1C2B24", "#E8F2EA", "#A8BBAE", "#34493D", "#26382E", "#35523F",
        "#7FDC9A", "#FF9292", "#E8BD67", "#F0C45A", "#FF9292", "#7FDC9A", "#0F2218", "#E8F2EA", DarkShadow);
    public static readonly ThemeDefinition Graphite = Make("Graphite", true, "#1F2226", "#2A2E34", "#EDEEF0", "#A7ABB2", "#3B4047", "#32363C", "#3C4A60",
        "#8FD3B0", "#FF8F96", "#E8BD67", "#F0C45A", "#FF8F96", "#A9C1E6", "#141A24", "#EDEEF0", DarkShadow);
    public static readonly ThemeDefinition Paper = Make("Paper", false, "#F8F9FC", "#E9ECF2", "#1E2430", "#5F6877", "#D6DBE5", "#E1E6EE", "#D3E1F6",
        "#2E7D5B", "#C0392B", "#946000", "#A06F00", "#C62828", "#3F6FB6", "#FFFFFF", "#1E2430", LightShadow);
    public static readonly ThemeDefinition Midnight = Make("Midnight", true, "#101E33", "#1A2E4A", "#E6EEFA", "#9FB4CF", "#2C4468", "#213A5B", "#2C4F80",
        "#6FD6C8", "#FF9494", "#F2C66B", "#F2C66B", "#FF9494", "#8AB0FF", "#0B1730", "#E6EEFA", DarkShadow);
    public static readonly ThemeDefinition Sandstone = Make("Sandstone", false, "#FAF4EA", "#F0E7D7", "#3A2F25", "#766857", "#E0D3BE", "#EADFCB", "#E6D3B0",
        "#4F7F58", "#B5392A", "#946000", "#A56F00", "#B5392A", "#B25A3B", "#FFFFFF", "#3A2F25", LightShadow);
    public static readonly ThemeDefinition Aubergine = Make("Aubergine", true, "#271B2D", "#382741", "#F2EAF6", "#BBA9C7", "#503860", "#43304D", "#5A3F6C",
        "#8FD7A3", "#FF9BA3", "#E8BD67", "#F0C45A", "#FF9BA3", "#C7A6F0", "#1F1228", "#F2EAF6", DarkShadow);
    public static readonly ThemeDefinition HighContrastLight = Make("High contrast light", false, "#FFFFFF", "#FFFFFF", "#000000", "#333333", "#595959", "#E6E6E6", "#BFD9FF",
        "#006B2D", "#B30000", "#7A4B00", "#7A4B00", "#B30000", "#0050C8", "#FFFFFF", "#000000", "#80000000");
    public static readonly ThemeDefinition HighContrastDark = Make("High contrast dark", true, "#000000", "#0D0D0D", "#FFFFFF", "#D0D0D0", "#8C8C8C", "#262626", "#003C78",
        "#4DFF88", "#FF6B6B", "#FFD23F", "#FFD23F", "#FF6B6B", "#4DFF88", "#000000", "#FFFFFF", "#E6000000");
    public static readonly IReadOnlyList<ThemeDefinition> BuiltIn = new[] { Light, Dark, Hawthorn, Sage, Graphite, Paper, Midnight, Sandstone, Aubergine, HighContrastLight, HighContrastDark };
    /// <summary>"System" is not a theme of its own: it picks Light or Dark to match the computer.</summary>
    public const string System = "System";
    /// <summary>What a new installation starts on.</summary>
    public const string Default = System;
    /// <summary>A built-in theme that was renamed keeps working under its old name in saved settings.</summary>
    public static string Renamed(string name) => name switch { "Ocean" => "Midnight", "Sunset" => "Sandstone", _ => name };
}

/// <summary>Reads and writes a user's own themes as one JSON line each, which is how the settings file keeps lists.</summary>
public static class ThemeStorage
{
    private sealed record Saved(string Name, bool Dark, Dictionary<string, string> Colours, string? BlockStyle);
    public static string ToLine(ThemeDefinition theme)
        => JsonSerializer.Serialize(new Saved(theme.Name, theme.Dark, new Dictionary<string, string>(theme.Colours), theme.BlockStyle));
    /// <summary>Returns the themes in the lines that are sound. A damaged line is skipped, so one bad theme never costs the others.</summary>
    public static List<ThemeDefinition> FromLines(IEnumerable<string> lines)
    {
        var result = new List<ThemeDefinition>();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<Saved>(line);
                if (saved is null || !IsUsableName(saved.Name)) continue;
                // Anything missing is taken from the built-in theme on the same base, so an old or hand-edited theme still works.
                var basis = saved.Dark ? ThemeLibrary.Dark : ThemeLibrary.Light;
                var colours = new Dictionary<string, string>();
                foreach (var token in ThemeTokens.Names)
                    colours[token] = saved.Colours is not null && saved.Colours.TryGetValue(token, out var value) && ThemeTokens.IsValid(token, value) ? value.ToUpperInvariant() : basis.Colour(token);
                if (result.Any(t => t.Name.Equals(saved.Name, StringComparison.OrdinalIgnoreCase)) || ThemeLibrary.BuiltIn.Any(t => t.Name.Equals(saved.Name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new ThemeDefinition(saved.Name.Trim(), saved.Dark, colours, false, ThemeBlockStyle.IsKnown(saved.BlockStyle) ? saved.BlockStyle! : ThemeBlockStyle.AsChosen));
            }
            catch (JsonException) { }
        }
        return result;
    }
    public static bool IsUsableName(string? name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim() && name.Length <= 40
        && name.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0 && !name.Equals(ThemeLibrary.System, StringComparison.OrdinalIgnoreCase);
}
