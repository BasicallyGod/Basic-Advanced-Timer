using Timer.Core;
using Timer.Storage;

namespace Timer.Desk;

/// <summary>
/// The desk app's own settings: the dashboard's size, the desk's order, hidden tiles, sizes, cells and saved layouts, its trinkets, floating widgets, the pill's
/// layout, the quick capture shortcut and the motion choice. They are saved in <see cref="StoreSettings.Extras"/>, which the storage library carries without
/// reading, so it has no field for any of them and another app can use it with settings of its own.
/// </summary>
public sealed record AppPrefs(int? DashboardWidth = null, int? DashboardHeight = null, IReadOnlyList<string>? DeskOrder = null, bool Calm = false, IReadOnlyList<string>? DeskHidden = null,
    TrinketData? Trinkets = null, IReadOnlyList<string>? DeskSizes = null, string PillStyle = "full", IReadOnlyList<string>? DeskPositions = null,
    IReadOnlyList<SavedLayout>? DeskLayouts = null, string? CaptureKey = null, IReadOnlyList<FloatingWidget>? Floating = null, bool FloatOnTop = true)
{
    // The appearance lines this app owns, by their start, and the sections it owns. Anything else in the extras is left as it was found.
    private static readonly string[] Lines = { "dash=", "desk=", "desk-order=", "desk-hidden=", "desk-sizes=", "desk-pos=", "motion=", "pillstyle=", "capturekey=", "floattop=" };
    private static readonly string[] Sections = { "deskTrinkets", "deskLayouts", "deskFloating" };

    /// <summary>Reads the app's settings out of what the store handed over. Anything missing or unreadable gives the default.</summary>
    public static AppPrefs From(StoreSettings settings)
    {
        var extras = settings.Extras ?? SettingsExtras.Empty; var lines = extras.AppearanceLines;
        string? Value(string key) => lines.FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal)) is { } line ? line[key.Length..] : null;
        string[]? List(string key) => Value(key)?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string? Section(string name) => extras.Sections.TryGetValue(name, out var found) ? found.FirstOrDefault() : null;
        // The dashboard's size, as dash=WIDTHxHEIGHT, or the default when there is none or it makes no sense.
        int? dashWidth = null, dashHeight = null;
        if (Value("dash=")?.Split('x') is [var dw, var dh] && int.TryParse(dw, out var w) && int.TryParse(dh, out var h) && w is >= 300 and <= 10000 && h is >= 300 and <= 10000) { dashWidth = w; dashHeight = h; }
        var pillStyle = Value("pillstyle=") is "compact" or "minimal" ? Value("pillstyle=")! : "full";
        return new(dashWidth, dashHeight, List("desk-order="), lines.Contains("motion=calm"), List("desk-hidden="), Timer.Desk.Trinkets.FromJson(Section("deskTrinkets")), List("desk-sizes="), pillStyle,
            List("desk-pos="), Timer.Desk.DeskLayouts.FromJson(Section("deskLayouts")), HotkeySpec.Parse(Value("capturekey="))?.ToString(), FloatingWidgets.FromJson(Section("deskFloating")), !lines.Contains("floattop=off"));
    }

    /// <summary>The settings with these app settings in place. What the extras held that is not this app's is kept as it was.</summary>
    public StoreSettings Into(StoreSettings settings)
    {
        var extras = settings.Extras ?? SettingsExtras.Empty;
        var lines = extras.AppearanceLines.Where(l => !Lines.Any(k => l.StartsWith(k, StringComparison.Ordinal))).ToList();
        lines.Add(DashboardWidth is { } dw && DashboardHeight is { } dh ? $"dash={dw}x{dh}" : "dash=default");
        lines.Add("desk=on"); // Still written, for older builds that had a switch for the desk.
        lines.Add(Calm ? "motion=calm" : "motion=lively");
        lines.Add("desk-order=" + string.Join(',', DeskOrder ?? Array.Empty<string>()));
        lines.Add("desk-hidden=" + string.Join(',', DeskHidden ?? Array.Empty<string>()));
        lines.Add("desk-sizes=" + string.Join(',', DeskSizes ?? Array.Empty<string>()));
        lines.Add(FloatOnTop ? "floattop=on" : "floattop=off");
        lines.Add("capturekey=" + (HotkeySpec.Parse(CaptureKey)?.ToString() ?? ""));
        lines.Add("desk-pos=" + string.Join(',', DeskPositions ?? Array.Empty<string>()));
        lines.Add("pillstyle=" + (PillStyle is "compact" or "minimal" ? PillStyle : "full"));
        var sections = extras.Sections.Where(p => !Sections.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        sections["deskFloating"] = new[] { FloatingWidgets.ToJson(Floating ?? Array.Empty<FloatingWidget>()) };
        sections["deskLayouts"] = new[] { Timer.Desk.DeskLayouts.ToJson(DeskLayouts ?? Array.Empty<SavedLayout>()) };
        sections["deskTrinkets"] = new[] { Timer.Desk.Trinkets.ToJson(Trinkets ?? TrinketData.Empty) };
        return settings with { Extras = new SettingsExtras(lines, sections) };
    }
}
