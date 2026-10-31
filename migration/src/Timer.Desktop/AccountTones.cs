using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The shades an account's colour is shown in. Blocks and chips take a deeper, livelier version of it that holds white text at 4.5 to 1 (a pale yellow keeps a bright
/// fill and takes dark text instead), and the week's charts take a softer one on Light and a lifted one on Dark, so a column of time is a quiet stack that still reads on
/// either ground. The account's own colour is never changed; these are only how it is drawn.
/// </summary>
public static class AccountTones
{
    public const string DarkInk = "#1D222B";
    /// <summary>The fill and the text colour for a block, chip or bar of this colour.</summary>
    public static (string Fill, string Ink) Vivid(string colour)
    {
        var (h, l, s) = ToHls(colour);
        if (h is >= 35 and <= 75 && l >= .55) // yellows and golds: bright, with dark words
        {
            var fill = FromHls(h, Math.Clamp(l, .6, .72), Math.Max(s, .75));
            return (fill, DarkInk);
        }
        var sat = Math.Clamp(Math.Max(s, .42), 0, .78); var light = Math.Min(l, .62);
        var tone = FromHls(h, light, sat);
        while (ThemeColours.Contrast(tone, "#FFFFFF") < 4.5 && light > .12) { light -= .005; tone = FromHls(h, light, sat); }
        return (tone, "#FFFFFF");
    }
    /// <summary>The colour of an account's slice in a chart: softened toward the card on Light, lifted a little on Dark.</summary>
    public static string Chart(string colour, bool dark)
    {
        var (h, l, s) = ToHls(colour);
        if (dark) return FromHls(h, Math.Clamp(Math.Max(l, .62), .62, .72), Math.Clamp(Math.Max(s, .5), .5, .85));
        return FromHls(h, Math.Clamp(Math.Max(l, .68), .68, .8), Math.Clamp(Math.Max(s, .35), .35, .7));
    }
    public static (double H, double L, double S) ToHls(string hex)
    {
        var text = hex.TrimStart('#'); if (text.Length == 3) text = string.Concat(text.Select(ch => $"{ch}{ch}"));
        double r = Convert.ToInt32(text[..2], 16) / 255.0, g = Convert.ToInt32(text[2..4], 16) / 255.0, b = Convert.ToInt32(text[4..6], 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var l = (max + min) / 2; var d = max - min;
        if (d < 1e-9) return (0, l, 0);
        var s = l > .5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? ((g - b) / d + (g < b ? 6 : 0)) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, l, s);
    }
    public static string FromHls(double h, double l, double s)
    {
        double Channel(double t)
        {
            var q = l < .5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q;
            t = ((t % 1) + 1) % 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        if (s < 1e-9) { var v = (int)Math.Round(l * 255); return $"#{v:X2}{v:X2}{v:X2}"; }
        int Byte(double x) => (int)Math.Round(Math.Clamp(x, 0, 1) * 255);
        return $"#{Byte(Channel(h / 360 + 1.0 / 3)):X2}{Byte(Channel(h / 360)):X2}{Byte(Channel(h / 360 - 1.0 / 3)):X2}";
    }
}
