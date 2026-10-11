using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// Turns theme definitions into what Avalonia uses: a theme variant for each theme, plus a break and an overtime variant of it for the pill,
/// each with its colour resources. The built-in Light and Dark are the standard variants. Every other theme sits on a Light or Dark base,
/// so the standard controls know which way to draw. Registering again replaces the colours, so an edited theme changes at once.
/// </summary>
public static class ThemeEngine
{
    private static readonly Dictionary<string, ThemeVariant> normal = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, string), ThemeVariant> states = new();

    public static ThemeVariant VariantFor(ThemeDefinition theme)
    {
        if (theme.BuiltIn && theme.Name == "Light") return ThemeVariant.Light;
        if (theme.BuiltIn && theme.Name == "Dark") return ThemeVariant.Dark;
        if (!normal.TryGetValue(theme.Name, out var variant)) normal[theme.Name] = variant = new ThemeVariant("Theme " + theme.Name, theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light);
        return variant;
    }
    /// <summary>The variant the pill wears for a break or for overtime, tinted from the theme.</summary>
    public static ThemeVariant StateVariant(ThemeDefinition theme, string state)
    {
        if (!states.TryGetValue((theme.Name, state), out var variant))
            states[(theme.Name, state)] = variant = new ThemeVariant(state + " " + theme.Name, theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light);
        return variant;
    }
    // Colours are written into the dictionary that is already registered when there is one, so screens that are open repaint at once.
    private static void Write(Application app, ThemeVariant variant, IReadOnlyDictionary<string, string> colours)
    {
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var found) || found is not ResourceDictionary dictionary)
            app.Resources.ThemeDictionaries[variant] = dictionary = new ResourceDictionary();
        foreach (var token in ThemeTokens.Names)
        {
            var colour = Color.Parse(colours[token]);
            if (token == "ShadowColor") dictionary[token] = colour; else dictionary[token] = new SolidColorBrush(colour);
        }
        // The standard controls take their colours from their own named resources, which default to the computer's accent colour. Pointing the ones that
        // show "on" or "filled" at the theme's accent keeps sliders and tick boxes in step with the rest of the app.
        var accent = new SolidColorBrush(Color.Parse(colours["Accent"])); var accentInk = new SolidColorBrush(Color.Parse(colours["AccentInk"])); var line = new SolidColorBrush(Color.Parse(colours["Line"]));
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed" }) dictionary[key] = accent;
        foreach (var key in new[] { "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed" }) dictionary[key] = accentInk;
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) dictionary[key] = line;
    }
    /// <summary>Raised after theme colours are written, so windows with their own chrome (title bars) can follow a change or a preview.</summary>
    public static event Action? Applied;
    /// <summary>Makes every theme, built-in and the user's own, available to the app. Safe to call again after a theme changes.</summary>
    public static void Register(Application app, IEnumerable<ThemeDefinition> themes)
    {
        foreach (var theme in themes)
        {
            Write(app, VariantFor(theme), theme.Colours);
            Write(app, StateVariant(theme, ThemeDerivation.Recording), ThemeDerivation.Variant(theme, ThemeDerivation.Recording));
            Write(app, StateVariant(theme, ThemeDerivation.Break), ThemeDerivation.Variant(theme, ThemeDerivation.Break));
            Write(app, StateVariant(theme, ThemeDerivation.Overtime), ThemeDerivation.Variant(theme, ThemeDerivation.Overtime));
        }
        Applied?.Invoke();
    }
}
