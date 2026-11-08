using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Timer.Desktop;

/// <summary>
/// The sixteen-segment digits of the clock, slanted like the account name beside them (the name's cell scaled to any height). Built once per height and kept, since the
/// clock redraws every second. A segment is a round-capped bar between two points of the cell, trimmed at both ends so neighbours leave a gap.
/// </summary>
public sealed class SegmentShapes
{
    /// <summary>The slant matches <see cref="SegmentName.Slant"/>; the bar is the name's 1.7 on a 20 tall cell, in proportion; a digit's glyph is 13.4 wide at 28 tall and its cell 19.</summary>
    public const double Slant = SegmentName.Slant, ThicknessAt20 = SegmentName.Thickness, GlyphRatio = 13.4 / 28, CellRatio = 19.0 / 28, MinThickness = 1.2;
    /// <summary>Segment ids in drawing order: a/A top halves, d/D bottom, g/G middle, f/e left upper and lower, b/c right upper and lower, i/l centre, h/j upper diagonals, k/m lower diagonals.</summary>
    public const string Ids = "aAdDgGfebcilhjkm";
    private static readonly (double X1, double Y1, double X2, double Y2)[] Bars =
    {
        (0, 0, .5, 0), (.5, 0, 1, 0), (0, 1, .5, 1), (.5, 1, 1, 1), (0, .5, .5, .5), (.5, .5, 1, .5),
        (0, 0, 0, .5), (0, .5, 0, 1), (1, 0, 1, .5), (1, .5, 1, 1), (.5, 0, .5, .5), (.5, .5, .5, 1),
        (0, 0, .5, .5), (1, 0, .5, .5), (0, 1, .5, .5), (1, 1, .5, .5),
    };
    // The segments each character lights, by the name display's own table so the clock draws nothing the name cannot.
    private static readonly Dictionary<char, string> Lit = new()
    {
        ['0'] = "aAfebcdDjk", ['1'] = "jbc", ['2'] = "aAbgGedD", ['3'] = "aAbcGdD", ['4'] = "fgGbc", ['5'] = "aAfgGcdD", ['6'] = "aAfgGecdD", ['7'] = "aAbc",
        ['8'] = "aAfbecdDgG", ['9'] = "aAfbgGcdD", [' '] = "", ['-'] = "gG"
    };
    public static bool IsLit(char character, string id) => Lit.TryGetValue(character, out var set) && set.Contains(id);
    public double Height { get; }
    /// <summary>The width of a digit's cell, and of the glyph inside it.</summary>
    public double Width { get; }
    public double GlyphWidth { get; }
    public double Thickness { get; }
    public double ColonWidth => Math.Round(Height * 0.2, 1);
    /// <summary>The sixteen bars from the cell's left edge, in the order of <see cref="Ids"/>.</summary>
    public IReadOnlyList<LineGeometry> Segments { get; }
    private readonly double inset;
    private static readonly Dictionary<double, SegmentShapes> Cache = new();
    public static SegmentShapes For(double height) { lock (Cache) { if (!Cache.TryGetValue(height, out var shapes)) Cache[height] = shapes = new SegmentShapes(height); return shapes; } }

    private SegmentShapes(double height)
    {
        Height = height; Width = Math.Round(height * CellRatio, 1); GlyphWidth = Math.Round(height * GlyphRatio, 1);
        Thickness = Math.Max(MinThickness, Math.Round(ThicknessAt20 * height / 20, 2));
        inset = Thickness / 2;
        var trimMax = 1.1 * height / 20; var h = height - 2 * inset; var list = new List<LineGeometry>();
        foreach (var (x1, y1, x2, y2) in Bars)
        {
            var a = At(x1, y1, h); var b = At(x2, y2, h);
            var dx = b.X - a.X; var dy = b.Y - a.Y; var len = Math.Sqrt(dx * dx + dy * dy); var trim = Math.Min(trimMax, len / 4);
            var u = new Vector(dx / len, dy / len);
            list.Add(new LineGeometry(a + u * trim, b - u * trim));
        }
        Segments = list;
    }
    private Point At(double fx, double fy, double h)
    {
        var y = inset + fy * h;
        return new Point(inset + fx * GlyphWidth + (Height - y) * Slant, y);
    }
    /// <summary>Where a colon's dot sits (0.3 and 0.7 of the height) in the colon's own column, leaning with the digits.</summary>
    public Point ColonDot(double fraction)
    {
        var y = inset + fraction * (Height - 2 * inset);
        return new Point(ColonWidth / 2 + (Height - y) * Slant - Height * Slant / 2, y);
    }
    public double DotRadius => Thickness * 0.62;
}

/// <summary>
/// A run of sixteen-segment characters (digits, blanks and colons) in one colour: the segments that are lit in the colour and the ones that are not still drawn faintly, so
/// every digit is always an 8 in outline. Drawn from geometry the renderer keeps, so the clock redraws each second without laying anything out again.
/// </summary>
public sealed class SegmentRun : Control
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SegmentRun, string>(nameof(Text), "");
    public static readonly StyledProperty<double> DigitHeightProperty = AvaloniaProperty.Register<SegmentRun, double>(nameof(DigitHeight), 28);
    public static readonly StyledProperty<string> LitKeyProperty = AvaloniaProperty.Register<SegmentRun, string>(nameof(LitKey), "Ink");
    public static readonly StyledProperty<Color?> ColourProperty = AvaloniaProperty.Register<SegmentRun, Color?>(nameof(Colour));
    static SegmentRun() { AffectsMeasure<SegmentRun>(TextProperty, DigitHeightProperty); AffectsRender<SegmentRun>(TextProperty, DigitHeightProperty, LitKeyProperty, ColourProperty); }
    public SegmentRun() { IsHitTestVisible = false; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double DigitHeight { get => GetValue(DigitHeightProperty); set => SetValue(DigitHeightProperty, value); }
    /// <summary>The theme colour the lit segments are drawn in, unless <see cref="Colour"/> is set.</summary>
    public string LitKey { get => GetValue(LitKeyProperty); set => SetValue(LitKeyProperty, value); }
    /// <summary>A colour of its own for the lit segments (the count takes the urgency colour), or null to use <see cref="LitKey"/>.</summary>
    public Color? Colour { get => GetValue(ColourProperty); set => SetValue(ColourProperty, value); }
    /// <summary>The space between two digit cells (the cell already holds the room), and round a colon on each side.</summary>
    public const double DigitGap = 0, ColonPad = 2;
    /// <summary>The width a run of <paramref name="text"/> takes at <paramref name="height"/>, which depends only on how many digits and colons it has, so the clock never changes width.</summary>
    public static double WidthOf(string text, double height)
    {
        var shapes = SegmentShapes.For(height); var width = 0.0; var previousDigit = false;
        foreach (var c in text)
        {
            if (c == ':') { width += shapes.ColonWidth + 2 * ColonPad; previousDigit = false; }
            else { if (previousDigit) width += DigitGap; width += shapes.Width; previousDigit = true; }
        }
        return width;
    }
    protected override Size MeasureOverride(Size availableSize) => new(WidthOf(Text, DigitHeight), DigitHeight);
    public override void Render(DrawingContext context)
    {
        FrameMonitor.Bump("draw SegmentRun");
        var shapes = SegmentShapes.For(DigitHeight);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var colour = Colour ?? Ui.ThemeColour(this, LitKey, Colors.Gray);
        var lit = new Pen(new SolidColorBrush(colour), shapes.Thickness, lineCap: PenLineCap.Round);
        var ghost = new Pen(new SolidColorBrush(Color.FromArgb((byte)(dark ? 31 : 26), colour.R, colour.G, colour.B)), shapes.Thickness, lineCap: PenLineCap.Round);

        // Like the badges, the halo sits behind the masked phosphor and never lights the ghost segments.
        var bloom = new PhosphorBloom(colour, shapes.Thickness, dark ? .78 : .28);
        DrawRun(context, shapes, lit, ghost, bloom);
        // The mask reaches a little past the control, since the slant leans the top of the last digit out of its cell.
        using (Honeycomb.Meshed(context, new Rect(-4, -4, Bounds.Width + 8, Bounds.Height + 8)))
            DrawRun(context, shapes, lit, ghost, null);
    }

    private void DrawRun(DrawingContext context, SegmentShapes shapes, Pen lit, Pen ghost, PhosphorBloom? bloom)
    {
        var x = 0.0;
        var previousDigit = false;
        foreach (var c in Text)
        {
            if (c == ':')
            {
                using (context.PushTransform(Matrix.CreateTranslation(x + ColonPad, 0)))
                {
                    foreach (var fraction in new[] { .3, .7 })
                    {
                        var dot = new EllipseGeometry(new Rect(shapes.ColonDot(fraction) - new Point(shapes.DotRadius, shapes.DotRadius), new Size(2 * shapes.DotRadius, 2 * shapes.DotRadius)));
                        if (bloom is { } glow) glow.Draw(context, dot, true);
                        else context.DrawGeometry(lit.Brush, null, dot);
                    }
                }
                x += shapes.ColonWidth + 2 * ColonPad;
                previousDigit = false;
                continue;
            }

            if (previousDigit) x += DigitGap;
            if (bloom is null || c != ' ')
            {
                using (context.PushTransform(Matrix.CreateTranslation(x, 0)))
                {
                    for (var s = 0; s < shapes.Segments.Count; s++)
                    {
                        var on = SegmentShapes.IsLit(c, SegmentShapes.Ids[s].ToString());
                        if (bloom is { } glow) { if (on) glow.Draw(context, shapes.Segments[s]); }
                        else context.DrawGeometry(null, on ? lit : ghost, shapes.Segments[s]);
                    }
                }
            }
            x += shapes.Width;
            previousDigit = true;
        }
    }
}

/// <summary>
/// The day clock as a sixteen-segment display: hours and minutes in the text colour and the seconds, the same height, in the quiet one, with the colon kept before them.
/// It is one width whatever the time. Set from one hours:minutes:seconds string.
/// </summary>
public sealed class SegmentClock : StackPanel
{
    /// <summary>The height of every digit on the pill and on the dashboard's sidebar.</summary>
    public const double DigitHeight = 28;
    private readonly SegmentRun main = new() { DigitHeight = DigitHeight, Name = "ClockMain" }, seconds = new() { DigitHeight = DigitHeight, LitKey = "Muted", Name = "ClockSeconds" };
    /// <summary>The width it takes, whatever the time: four digits and a colon, then a colon and two digits.</summary>
    public static double Width => SegmentRun.WidthOf("00:00", DigitHeight) + SegmentRun.DigitGap + SegmentRun.WidthOf(":00", DigitHeight);
    public SegmentClock()
    {
        Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center; Spacing = SegmentRun.DigitGap;
        Children.Add(main); Children.Add(seconds); Text = "00:00:00";
    }
    public string Text
    {
        get => main.Text + seconds.Text;
        set { var cut = value.LastIndexOf(':'); main.Text = cut > 0 ? value[..cut] : value; seconds.Text = cut > 0 ? value[cut..] : ""; }
    }
}
