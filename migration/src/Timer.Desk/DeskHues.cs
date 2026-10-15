using Timer.Core;

namespace Timer.Desk;

/// <summary>
/// The soft colour each desk tile wears, one set for a light theme and one for a dark one. They are decoration only and carry no meaning. A light theme takes
/// pale pastels as a wash over its surface; a dark theme takes deeper tones of the same hues, because a pastel washed over a dark surface goes grey and muddy.
/// The badge keeps the pastel in both, since its icon is always drawn dark on it.
/// </summary>
public static class DeskHues
{
    /// <summary>How much of the hue a tile's body and its header band take over what is behind them, 0 to 255.</summary>
    public const byte WashAlpha = 0x38, BandAlpha = 0x30;
    public const byte DarkWashAlpha = 0x5C, DarkBandAlpha = 0x30;

    private static readonly Dictionary<string, (string Light, string Dark)> Table = new()
    {
        ["working"] = ("#7FD1C7", "#2E8A80"), ["week"] = ("#E8C48C", "#9A7536"), ["deadline"] = ("#EE9CB4", "#A8456A"), ["day"] = ("#F0C97A", "#98741E"),
        ["strip"] = ("#D9A8E8", "#8A52A0"), ["timeline"] = ("#D6C3A5", "#7C6644"), ["accounts"] = ("#A99CF5", "#5F52C0"), ["todo"] = ("#F4B58A", "#A8602C"),
        ["notes"] = ("#9ED0E6", "#2F7898"), ["coming"] = ("#F2A7B8", "#A84E68"), ["pomodoro"] = ("#D0D8A0", "#707A30"), ["clock"] = ("#8FA8D8", "#4466A8"),
        ["postit"] = ("#FFF1A8", "#FFF1A8"), ["postit2"] = ("#E3D6FF", "#E3D6FF"), ["postit3"] = ("#FFD3DC", "#FFD3DC"),
    };

    /// <summary>The badge colour of a tile, the same pastel in every theme.</summary>
    public static string Badge(string id) => Table.TryGetValue(id, out var h) ? h.Light : "#B8C4D8";
    /// <summary>The colour a tile's wash and header band are made from in a light or a dark theme.</summary>
    public static string Wash(string id, bool dark) => Table.TryGetValue(id, out var h) ? (dark ? h.Dark : h.Light) : "#B8C4D8";
    public static byte WashAlphaFor(bool dark) => dark ? DarkWashAlpha : WashAlpha;
    public static byte BandAlphaFor(bool dark) => dark ? DarkBandAlpha : BandAlpha;
    public static IEnumerable<string> Ids => Table.Keys;
    /// <summary>What a tile's body and its header band come out as over a theme's surface, for checking that text reads on them.</summary>
    public static (string Body, string Band) Composite(string id, bool dark, string surface)
    {
        var body = ThemeColours.Mix(surface, Wash(id, dark), WashAlphaFor(dark) / 255.0);
        return (body, ThemeColours.Mix(body, Wash(id, dark), BandAlphaFor(dark) / 255.0));
    }
}
