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
        // A resource that already holds this colour is left alone. Replacing a resource tells every control using it to look again, so rewriting
        // the whole set on every slider move made editing a theme crawl, when only one colour had changed.
        void Put(string key, Color colour)
        {
            if (dictionary.TryGetValue(key, out var existing) && existing is SolidColorBrush current && current.Color == colour) return;
            dictionary[key] = new SolidColorBrush(colour);
        }
        foreach (var token in ThemeTokens.Names)
        {
            var colour = Color.Parse(colours[token]);
            if (token == "ShadowColor") { if (!(dictionary.TryGetValue(token, out var shadow) && shadow is Color same && same == colour)) dictionary[token] = colour; }
            else Put(token, colour);
        }
        // The standard controls take their colours from their own named resources, which default to the computer's accent colour. Pointing the ones that
        // show "on" or "filled" at the theme's accent keeps sliders and tick boxes in step with the rest of the app.
        var accent = Color.Parse(colours["Accent"]); var accentInk = Color.Parse(colours["AccentInk"]); var line = Color.Parse(colours["Line"]);
        var (sliderFill, sliderTrack) = SliderColours(colours);
        foreach (var key in new[] { "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
            // The export dialog's week and month choices are radio buttons, which follow the theme too.
            "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver", "RadioButtonOuterEllipseCheckedFillPressed",
            "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedStrokePointerOver", "RadioButtonOuterEllipseCheckedStrokePressed" }) Put(key, accent);
        foreach (var key in new[] { "RadioButtonCheckGlyphFill", "RadioButtonCheckGlyphFillPointerOver", "RadioButtonCheckGlyphFillPressed", "RadioButtonCheckGlyphStroke", "RadioButtonCheckGlyphStrokePointerOver", "RadioButtonCheckGlyphStrokePressed" }) Put(key, accentInk);
        foreach (var key in new[] { "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed" }) Put(key, accentInk);
        // Tooltips are drawn by a pop-up that otherwise takes the computer's own light or dark colours, which left dark text on a dark tooltip (or the reverse)
        // when the theme and the computer disagreed, and in the recording pill. They wear the theme's surface, text and line instead.
        // A gentle wash of the tracking colour over the surface, for the card of the account that is recording.
        Put("LiveTint", Color.Parse(ThemeColours.Mix(colours["Surface"], colours["Live"], ThemeColours.Contrast(colours["Surface"], "#FFFFFF") < ThemeColours.Contrast(colours["Surface"], "#000000") ? .24 : .14)));
        // The scroll bar thumb is a quiet grey a little darker than the lines, and darker again under the pointer.
        Put("ScrollThumb", Color.Parse(ThemeColours.Mix(colours["Line"], colours["Ink"], .13)));
        Put("ScrollThumbHover", Color.Parse(ThemeColours.Mix(colours["Line"], colours["Ink"], .3)));
        Put("ToolTipBackground", Color.Parse(colours["Surface"]));
        Put("ToolTipForeground", Color.Parse(colours["Ink"]));
        Put("ToolTipBorderBrush", line);
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" }) Put(key, sliderFill);
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) Put(key, sliderTrack);
        // A slider that is switched off, such as every one in a built-in theme (they cannot be changed), is drawn from its own Disabled resources, which
        // the standard theme makes a dim grey that vanishes on a dark surface. They stay quieter than a live slider but can still be seen.
        var (offFill, offTrack) = DisabledSliderColours(colours);
        foreach (var key in new[] { "SliderTrackValueFillDisabled", "SliderThumbBackgroundDisabled" }) Put(key, offFill);
        Put("SliderTrackFillDisabled", offTrack);
        // The calendar (the date pickers in Notes and the header) draws itself from its own resources, which are the computer's greys and blue. It wears the
        // theme instead: a plain surface, the accent for the chosen day and a ring in it for today, and muted days outside the month.
        var surface = Color.Parse(colours["Surface"]); var ink = Color.Parse(colours["Ink"]); var muted = Color.Parse(colours["Muted"]); var hover = Color.Parse(colours["Hover"]);
        foreach (var key in new[] { "CalendarViewBackground", "CalendarViewOutOfScopeBackground", "CalendarViewCalendarItemRevealBackground", "CalendarViewNavigationButtonBackground", "CalendarViewCalendarItemRevealBorderBrush", "CalendarViewNavigationButtonBorderBrush", "CalendarViewBorderBrush" }) Put(key, key.EndsWith("BorderBrush") ? line : surface);
        foreach (var key in new[] { "CalendarViewForeground", "CalendarViewCalendarItemForeground", "CalendarViewNavigationButtonForegroundPointerOver", "CalendarViewNavigationButtonForegroundPressed" }) Put(key, ink);
        foreach (var key in new[] { "CalendarViewOutOfScopeForeground", "CalendarViewBlackoutForeground", "CalendarViewWeekDayForegroundDisabled" }) Put(key, muted);
        Put("CalendarViewTodayForeground", accent);
        foreach (var key in new[] { "CalendarViewSelectedBorderBrush", "CalendarViewSelectedHoverBorderBrush", "CalendarViewSelectedPressedBorderBrush", "CalendarViewPressedBorderBrush" }) Put(key, accent);
        Put("CalendarViewHoverBorderBrush", hover);
        // The thin lines between days would show as a grid of grey bars, so they are left clear.
        foreach (var key in new[] { "CalendarViewCalendarItemRevealBorderBrush", "CalendarViewNavigationButtonBorderBrush" }) Put(key, Colors.Transparent);
    }
    /// <summary>
    /// The colours a slider is drawn in: the filled part and the thumb, which are the accent unless it is too faint on the theme's window and panels, and
    /// the empty track, which is the theme's quiet text colour pulled towards the surface until it can be seen on both. The theme's own line colour
    /// is only a hairline against its panel, which is nearly invisible as a track (in Hawthorn the whole slider was hard to find).
    /// </summary>
    public static (Color Fill, Color Track) SliderColours(IReadOnlyDictionary<string, string> colours)
    {
        string surface = colours["Surface"], panel = colours["Panel"], ink = colours["Ink"], accent = colours["Accent"];
        bool Seen(string colour) => ThemeColours.Contrast(colour, surface) >= 3 && ThemeColours.Contrast(colour, panel) >= 3;
        var fill = Seen(accent) ? accent : ink;
        var track = surface;
        for (var weight = .3; weight <= 1.0; weight += .05) { track = ThemeColours.Mix(surface, ink, weight); if (Seen(track)) break; }
        return (Color.Parse(fill), Color.Parse(track));
    }
    /// <summary>The colours of a slider that cannot be changed: a thumb and fill about half way between the surface and the text colour, on a fainter track.</summary>
    public static (Color Fill, Color Track) DisabledSliderColours(IReadOnlyDictionary<string, string> colours)
        => (Color.Parse(ThemeColours.Mix(colours["Surface"], colours["Ink"], .5)), Color.Parse(ThemeColours.Mix(colours["Surface"], colours["Ink"], .28)));
    /// <summary>Raised after theme colours are written, so windows with their own chrome (title bars) can follow a change or a preview.</summary>
    public static event Action? Applied;
    /// <summary>Makes the themes available to the app.</summary>
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
