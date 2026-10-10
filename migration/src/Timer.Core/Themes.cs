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
        "Surface", "Panel", "Ink", "Muted", "Line", "Hover", "Selected", "Live", "Idle", "Overtime", "BudgetWarning", "Break", "Finish", "ShadowColor"
    };
    public static string Describe(string token) => token switch
    {
        "Surface" => "Window background", "Panel" => "Side panel and controls", "Ink" => "Text", "Muted" => "Quiet text", "Line" => "Lines and borders",
        "Hover" => "Hovered row", "Selected" => "Selected row", "Live" => "Tracking and start", "Idle" => "Not tracking", "Overtime" => "Overtime",
        "BudgetWarning" => "Budget nearly used", "Break" => "Breaks", "Finish" => "Finish day", "ShadowColor" => "Window shadow", _ => token
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
    private static ThemeDefinition Make(string name, bool dark, params (string Token, string Value)[] colours)
        => new(name, dark, colours.ToDictionary(c => c.Token, c => c.Value), true);
    public static readonly ThemeDefinition Light = Make("Light", false,
        ("Surface", "#F5F8F4"), ("Panel", "#FFFFFF"), ("Ink", "#24312B"), ("Muted", "#68726C"), ("Line", "#DDE5DC"), ("Hover", "#E7EEE5"), ("Selected", "#D9E9DA"),
        ("Live", "#337A50"), ("Idle", "#6D7471"), ("Overtime", "#B83B3B"), ("BudgetWarning", "#9D6C13"), ("Break", "#B07A12"), ("Finish", "#B83B3B"), ("ShadowColor", "#59000000"));
    public static readonly ThemeDefinition Dark = Make("Dark", true,
        ("Surface", "#1B221F"), ("Panel", "#242D28"), ("Ink", "#ECF2EC"), ("Muted", "#ABB8AE"), ("Line", "#3B493F"), ("Hover", "#303D34"), ("Selected", "#3B5542"),
        ("Live", "#8FD7A3"), ("Idle", "#ABB3AE"), ("Overtime", "#FF9292"), ("BudgetWarning", "#E8BD67"), ("Break", "#F0C45A"), ("Finish", "#FF9292"), ("ShadowColor", "#B3000000"));
    public static readonly ThemeDefinition HighContrastDark = Make("High contrast dark", true,
        ("Surface", "#000000"), ("Panel", "#0D0D0D"), ("Ink", "#FFFFFF"), ("Muted", "#D0D0D0"), ("Line", "#8C8C8C"), ("Hover", "#262626"), ("Selected", "#003C78"),
        ("Live", "#4DFF88"), ("Idle", "#D0D0D0"), ("Overtime", "#FF6B6B"), ("BudgetWarning", "#FFD23F"), ("Break", "#FFD23F"), ("Finish", "#FF6B6B"), ("ShadowColor", "#E6000000"));
    public static readonly ThemeDefinition HighContrastLight = Make("High contrast light", false,
        ("Surface", "#FFFFFF"), ("Panel", "#FFFFFF"), ("Ink", "#000000"), ("Muted", "#333333"), ("Line", "#595959"), ("Hover", "#E6E6E6"), ("Selected", "#BFD9FF"),
        ("Live", "#006B2D"), ("Idle", "#333333"), ("Overtime", "#B30000"), ("BudgetWarning", "#7A4B00"), ("Break", "#7A4B00"), ("Finish", "#B30000"), ("ShadowColor", "#80000000"));
    public static readonly ThemeDefinition Ocean = Make("Ocean", true,
        ("Surface", "#0F1B2B"), ("Panel", "#152538"), ("Ink", "#E6F0FA"), ("Muted", "#9DB4CB"), ("Line", "#2A4059"), ("Hover", "#1C3350"), ("Selected", "#245078"),
        ("Live", "#5FD3C8"), ("Idle", "#9DB4CB"), ("Overtime", "#FF8F8F"), ("BudgetWarning", "#F2C66B"), ("Break", "#F2C66B"), ("Finish", "#FF8F8F"), ("ShadowColor", "#B3000000"));
    public static readonly ThemeDefinition Sunset = Make("Sunset", false,
        ("Surface", "#FBF3EC"), ("Panel", "#FFFAF5"), ("Ink", "#3A2A24"), ("Muted", "#7B665C"), ("Line", "#EAD8CB"), ("Hover", "#F4E3D6"), ("Selected", "#F2D2BC"),
        ("Live", "#2F7A5A"), ("Idle", "#7B665C"), ("Overtime", "#C0392B"), ("BudgetWarning", "#A8650F"), ("Break", "#B5701A"), ("Finish", "#C0392B"), ("ShadowColor", "#59000000"));
    public static readonly ThemeDefinition Graphite = Make("Graphite", true,
        ("Surface", "#1E1F22"), ("Panel", "#26282C"), ("Ink", "#EDEDED"), ("Muted", "#A6A8AD"), ("Line", "#3A3D42"), ("Hover", "#2F3237"), ("Selected", "#3F4A5C"),
        ("Live", "#7FD6A6"), ("Idle", "#A6A8AD"), ("Overtime", "#FF8A8A"), ("BudgetWarning", "#E8BD67"), ("Break", "#F0C45A"), ("Finish", "#FF8A8A"), ("ShadowColor", "#B3000000"));
    public static readonly IReadOnlyList<ThemeDefinition> BuiltIn = new[] { Light, Dark, HighContrastLight, HighContrastDark, Ocean, Sunset, Graphite };
    /// <summary>"System" is not a theme of its own: it picks Light or Dark to match the computer.</summary>
    public const string System = "System";
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
