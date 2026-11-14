using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Timer.Desktop;

/// <summary>The two looks of every display: the vacuum-fluorescent one the app was built with, and an e-ink one (normal type, grain, calm motion) chosen for accessibility.</summary>
public enum DisplayStyle { Phosphor, Ink }

/// <summary>
/// The e-ink look, in one place: whether it is on, how strong the grain is, the paper and ink colours, the grain and speckle masks, a texture for each account and the type.
/// Every display already draws through a few shared pieces (the honeycomb, the bloom, the segment letters), and each of them asks this class what to draw when the look is e-ink.
/// The model sets <see cref="Style"/> and <see cref="Percent"/> whenever they change or load, so drawing code anywhere can read them without a model in hand.
/// </summary>
public static class DisplayInk
{
    public const int DefaultPercent = 60;
    // Per thread, like Motion's override: the app draws on one thread, and tests that run side by side must not turn each other's displays to e-ink.
    [ThreadStatic] private static int? chosenPercent;
    [ThreadStatic] private static DisplayStyle? chosenStyle;
    private static int percent => chosenPercent ?? DefaultPercent;
    /// <summary>Which look the displays wear. Set by <see cref="TimerModel"/>.</summary>
    public static DisplayStyle Style { get => chosenStyle ?? DisplayStyle.Phosphor; set => chosenStyle = value; }
    public static bool On => Style == DisplayStyle.Ink;
    /// <summary>How strong the e-ink effect is, 0 to 100 in steps of ten: 0 is clean, 100 is the full grain and speckle. Set by <see cref="TimerModel"/>.</summary>
    public static int Percent { get => percent; set => chosenPercent = Math.Clamp((int)Math.Round(value / 10.0) * 10, 0, 100); }
    public static double Strength => percent / 100.0;

    /// <summary>The colours of one e-ink theme. Light is dark ink on near-white paper; Dark is the same screen inverted.</summary>
    public sealed record Colours(Color Paper, Color Ink, Color Muted, Color Ghost, Color Lit, Color Live, Color Amber, Color Red, Color Rule, bool OnLightPaper);
    public static readonly Colours Light = new(Color.Parse("#F3F3EF"), Color.Parse("#0C0C0C"), Color.Parse("#3F3F3C"), Color.FromArgb(18, 0, 0, 0), Color.Parse("#35255F"),
        Color.Parse("#0B5A32"), Color.Parse("#7A4300"), Color.Parse("#A3151B"), Color.FromArgb(66, 0, 0, 0), true);
    public static readonly Colours Dark = new(Color.Parse("#161719"), Color.Parse("#F2F2EE"), Color.Parse("#C8C8C2"), Color.FromArgb(33, 255, 255, 255), Color.Parse("#D2C0F4"),
        Color.Parse("#82E3AC"), Color.Parse("#F2BD52"), Color.Parse("#FF8F89"), Color.FromArgb(72, 255, 255, 255), false);
    public static Colours For(bool dark) => dark ? Dark : Light;
    public static Colours For(Avalonia.Controls.Control control) => For(SoftTint.IsDark(control));

    // ---- grain
    private const int TileSize = 128;
    private static readonly Dictionary<string, IBrush> brushes = new();
    private static IBrush Tile(string key, Func<WriteableBitmap> make)
    {
        if (brushes.TryGetValue(key, out var found)) return found;
        var bitmap = make(); var rect = new RelativeRect(0, 0, TileSize, TileSize, RelativeUnit.Absolute);
        return brushes[key] = new ImageBrush(bitmap) { TileMode = TileMode.Tile, SourceRect = rect, DestinationRect = rect, Stretch = Stretch.Fill };
    }
    private static WriteableBitmap Bitmap(Func<int, int, double, uint> pixel, int cell, int seed)
    {
        var bmp = new WriteableBitmap(new PixelSize(TileSize, TileSize), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var data = new int[TileSize * TileSize]; var rnd = new Random(seed);
        for (var y = 0; y < TileSize; y += cell)
            for (var x = 0; x < TileSize; x += cell)
            {
                var value = pixel(x, y, rnd.NextDouble());
                for (var dy = 0; dy < cell && y + dy < TileSize; dy++) for (var dx = 0; dx < cell && x + dx < TileSize; dx++) data[(y + dy) * TileSize + x + dx] = unchecked((int)value);
            }
        using var fb = bmp.Lock();
        if (fb.RowBytes == TileSize * 4) Marshal.Copy(data, 0, fb.Address, data.Length);
        else for (var y = 0; y < TileSize; y++) Marshal.Copy(data, y * TileSize, fb.Address + y * fb.RowBytes, TileSize);
        return bmp;
    }
    private static uint Premul(byte alpha, byte level) { var c = (uint)(alpha * level / 255); return (uint)alpha << 24 | c << 16 | c << 8 | c; }
    /// <summary>A tile of ink grain: scattered specks, dark on light paper or light on dark, drawn over a filled shape at a low opacity.</summary>
    public static IBrush Specks(bool onLightPaper) => Tile(onLightPaper ? "specks-dark" : "specks-light", () => Bitmap((_, _, r) =>
        r < .22 ? Premul((byte)(255 * (.08 + (r / .22) * .22)), onLightPaper ? (byte)0 : (byte)255)
        : r < .30 ? Premul((byte)(255 * (.10 + ((r - .22) / .08) * .20)), onLightPaper ? (byte)255 : (byte)0) : 0u, 2, onLightPaper ? 7 : 11));
    /// <summary>A very fine grain for the paper itself.</summary>
    public static IBrush Paper(bool onLightPaper) => Tile(onLightPaper ? "paper-dark" : "paper-light", () => Bitmap((_, _, r) =>
        r < .22 ? Premul((byte)(255 * (.08 + (r / .22) * .22)), onLightPaper ? (byte)0 : (byte)255)
        : r < .30 ? Premul((byte)(255 * (.10 + ((r - .22) / .08) * .20)), onLightPaper ? (byte)255 : (byte)0) : 0u, 1, onLightPaper ? 5 : 9));
    private static IBrush Dropout(int tenths) => Tile("dropout" + tenths, () => Bitmap((_, _, r) =>
    {
        var s = tenths / 10.0; var alpha = r < .10 * s ? 1 - .4 * s : 1.0; return Premul((byte)(255 * alpha), 255);
    }, 1, 13));

    /// <summary>Pushes a mask that lets a little ink drop out of whatever is drawn inside it (the speckle in the letters); nothing at all with the effect at 0. Dispose to stop.</summary>
    public static IDisposable Speckled(DrawingContext context, Rect bounds) => On && percent > 0 ? context.PushOpacityMask(Dropout(percent / 10), bounds) : NoMask.Instance;
    private sealed class NoMask : IDisposable { public static readonly NoMask Instance = new(); public void Dispose() { } }

    /// <summary>Fills a rounded shape with <paramref name="fill"/> and lays the ink grain over it, at its strength; an outline as firm as the ink round it.</summary>
    public static void Cell(DrawingContext context, Colours colours, IBrush fill, Rect rect, double radius, bool outline = false)
    {
        context.DrawRectangle(fill, null, rect, radius, radius);
        Grain(context, colours.OnLightPaper, rect, radius, .5);
        if (outline && rect.Width >= 12 && rect.Height >= 12) context.DrawRectangle(null, new Pen(new SolidColorBrush(colours.Ink, .8), 1.4), rect.Deflate(.7), radius, radius);
    }
    /// <summary>The grain over a shape, at <paramref name="scale"/> times the chosen strength.</summary>
    public static void Grain(DrawingContext context, bool onLightPaper, Rect rect, double radius, double scale)
    {
        if (percent == 0) return;
        using (context.PushClip(new RoundedRect(rect, radius))) using (context.PushOpacity(Strength * scale)) context.DrawRectangle(Specks(onLightPaper), null, rect);
    }
    /// <summary>The paper of a display well: a whisper of fine grain over its fill.</summary>
    public static void PaperGrain(DrawingContext context, bool onLightPaper, Rect rect, double radius)
    {
        if (percent == 0) return;
        using (context.PushClip(new RoundedRect(rect, radius))) using (context.PushOpacity(Strength * .35)) context.DrawRectangle(Paper(onLightPaper), null, rect);
    }

    // ---- textures
    public const int TextureCount = 6;
    /// <summary>A texture drawn over an account's colour, so the colour is never the only way to tell accounts apart: plain, diagonal, dots, lines, the other diagonal, cross-hatch.</summary>
    public static void Texture(DrawingContext context, int index, Rect rect, double radius, Color fill)
    {
        var n = ((index % TextureCount) + TextureCount) % TextureCount; if (n == 0 || rect.Width <= 10 || rect.Height <= 10) return;
        var light = (.2126 * fill.R + .7152 * fill.G + .0722 * fill.B) / 255 > .5;
        var ink = new SolidColorBrush(light ? Color.FromArgb(107, 0, 0, 0) : Color.FromArgb(128, 255, 255, 255)); var pen = new Pen(ink, 1);
        using (context.PushClip(new RoundedRect(rect, radius)))
        {
            if (n == 1 || n == 5) for (var d = -rect.Height; d < rect.Width; d += 5) context.DrawLine(pen, new Point(rect.X + d, rect.Bottom), new Point(rect.X + d + rect.Height, rect.Y));
            if (n == 4 || n == 5) for (var d = -rect.Height; d < rect.Width; d += 5) context.DrawLine(pen, new Point(rect.X + d, rect.Y), new Point(rect.X + d + rect.Height, rect.Bottom));
            if (n == 2) for (var gx = 2; gx < rect.Width; gx += 4) for (var gy = 2 + (gx / 4 % 2) * 2; gy < rect.Height; gy += 4) context.DrawRectangle(ink, null, new Rect(rect.X + gx, rect.Y + gy, 1.6, 1.6));
            if (n == 3) for (var gy = 3; gy < rect.Height; gy += 4) context.DrawLine(pen, new Point(rect.X, rect.Y + gy), new Point(rect.Right, rect.Y + gy));
        }
    }
}

/// <summary>A size and weight of e-ink type. Roles are named for what they say, and the sizes are the ones the mockup settled on (nothing under 12, except the tall pill's dense rows).</summary>
public readonly record struct InkRole(double Size, FontWeight Weight)
{
    public static readonly InkRole Caption = new(12, FontWeight.Medium), Label = new(12, FontWeight.SemiBold), Status = new(13, FontWeight.Medium),
        Name = new(15, FontWeight.SemiBold), Figure = new(16, FontWeight.Bold), Hours = new(22, FontWeight.Bold), Clock = new(30, FontWeight.Bold), Dense = new(11, FontWeight.SemiBold);
}

/// <summary>E-ink type: Inter, sentence case, with figures set tabular (every digit takes the width of a zero) so times line up and nothing shifts as they change.</summary>
public static class InkType
{
    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");
    /// <summary>"ON PACE" becomes "On pace": the display words were written in capitals for segment letters.</summary>
    public static string Sentence(string text)
    {
        // Initials ("AO") and other very short capitals stay as they are.
        if (text.Length == 0 || !text.Any(char.IsLetter) || text != text.ToUpperInvariant() || text.Count(char.IsLetter) <= 3) return text;
        var lower = text.ToLowerInvariant().ToCharArray(); var i = Array.FindIndex(lower, char.IsLetter); lower[i] = char.ToUpperInvariant(lower[i]);
        return System.Text.RegularExpressions.Regex.Replace(new string(lower), @"\bcsv\b", "CSV", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    private static FormattedText Format(string text, InkRole role, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Inter, FontStyle.Normal, role.Weight), role.Size, brush);
    private static IEnumerable<(string Text, bool Number)> Runs(string text)
    {
        foreach (var part in System.Text.RegularExpressions.Regex.Split(text, @"(\d[\d:.]*)")) if (part.Length > 0) yield return (part, char.IsDigit(part[0]));
    }
    // A run's width with its spaces counted: Avalonia leaves trailing white space out of a measured line, which glued "Allocated 34:56" together.
    private static double RunWidth(string run, InkRole role) => Format("|" + run + "|", role, Brushes.Black).Width - 2 * Format("|", role, Brushes.Black).Width;
    private static double Zero(InkRole role) => Format("0", role, Brushes.Black).Width;
    private static double Colon(InkRole role) => Format(":", role, Brushes.Black).Width * .9;
    /// <summary>The width of <paramref name="text"/> set in <paramref name="role"/>, with figures tabular.</summary>
    public static double Width(string text, InkRole role)
    {
        text = Sentence(text); double zero = Zero(role), colon = Colon(role), total = 0;
        foreach (var (part, number) in Runs(text)) total += number ? part.Sum(ch => ch is ':' or '.' ? colon : zero) : RunWidth(part, role);
        return total;
    }
    /// <summary>Draws <paramref name="text"/> with its baseline at <paramref name="baseline"/>, starting at <paramref name="x"/>, or ending or centred there; returns its width.</summary>
    public static double Draw(DrawingContext context, string text, InkRole role, IBrush brush, double x, double baseline, TextAlignment align = TextAlignment.Left)
    {
        text = Sentence(text); var total = Width(text, role); var left = align == TextAlignment.Right ? x - total : align == TextAlignment.Center ? x - total / 2 : x;
        var bounds = new Rect(left - 2, baseline - role.Size * 1.1, total + 4, role.Size * 1.5);
        using (DisplayInk.Speckled(context, bounds))
        {
            double zero = Zero(role), colon = Colon(role), px = left;
            foreach (var (part, number) in Runs(text))
            {
                if (number)
                    foreach (var ch in part)
                    {
                        var cell = ch is ':' or '.' ? colon : zero; var glyph = Format(ch.ToString(), role, brush);
                        context.DrawText(glyph, new Point(px + (cell - glyph.Width) / 2, baseline - glyph.Baseline)); px += cell;
                    }
                else { var run = Format(part, role, brush); context.DrawText(run, new Point(px, baseline - run.Baseline)); px += RunWidth(part, role); }
            }
        }
        return total;
    }
}

/// <summary>A block's fill in E-ink: the account's colour with the ink grain over it and its texture, drawn in one piece (nothing is lit, glowing or meshed).</summary>
public sealed class InkFill(Color colour, double radius, int texture) : Avalonia.Controls.Control
{
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size); if (rect.Width <= 0 || rect.Height <= 0) return;
        DisplayInk.Cell(context, DisplayInk.For(SoftTint.IsDark(this)), new SolidColorBrush(colour), rect, radius);
        DisplayInk.Texture(context, texture, rect, radius, colour);
    }
}

/// <summary>
/// The dot-matrix font of E-ink's account name: GNU Unifont's 8 by 16 glyphs (Latin, Greek and Cyrillic, 921 of them) read lazily from one small resource. A glyph is sixteen
/// row bytes, the left-most dot in the top bit. Characters it has no glyph for are left out, as the segment name leaves out what it cannot draw.
/// </summary>
public static class DotFont
{
    public const int Width = 8, Height = 16;
    private static Dictionary<int, byte[]>? glyphs;
    private static Dictionary<int, byte[]> Glyphs => glyphs ??= Load();
    private static Dictionary<int, byte[]> Load()
    {
        var map = new Dictionary<int, byte[]>();
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://" + typeof(DotFont).Assembly.GetName().Name + "/Assets/Fonts/unifont-latin.bin"));
            using var memory = new MemoryStream(); stream.CopyTo(memory); var data = memory.ToArray();
            var count = data[0] | data[1] << 8;
            for (var i = 0; i < count; i++) { var at = 2 + i * 18; map[data[at] | data[at + 1] << 8] = data[(at + 2)..(at + 18)]; }
        }
        catch (Exception) { /* No resource (a test host without the assets): the name simply draws nothing. */ }
        return map;
    }
    /// <summary>The sixteen row bytes of <paramref name="ch"/>, or null when the font has none (a blank for a space).</summary>
    public static byte[]? Rows(char ch) => Glyphs.TryGetValue(ch, out var rows) ? rows : null;
    public static bool Has(char ch) => ch == ' ' || Glyphs.ContainsKey(ch);
    /// <summary>The text as the dot matrix can draw it: accents and ligatures spelled out as far as the font allows, anything else dropped; null when too little survives.</summary>
    public static string? Normalise(string name)
    {
        var sb = new System.Text.StringBuilder(); var seen = 0; var drawn = 0;
        foreach (var raw in name.Normalize(System.Text.NormalizationForm.FormC))
        {
            seen++;
            if (Has(raw)) { sb.Append(raw); drawn++; continue; }
            var plain = raw.ToString().Normalize(System.Text.NormalizationForm.FormD).FirstOrDefault(char.IsAsciiLetter);
            if (plain != 0 && Has(plain)) { sb.Append(plain); drawn++; }
        }
        var text = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
        return text.Length < 2 || drawn * 2 < seen ? null : text;
    }
}
