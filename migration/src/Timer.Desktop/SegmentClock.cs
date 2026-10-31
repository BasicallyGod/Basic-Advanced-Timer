using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Timer.Desktop;

/// <summary>
/// The shapes of a seven-segment digit, hexagonal segments with pointed ends and a thin gap between them, for any height. Built once per height and kept, since the
/// clock redraws every second.
/// </summary>
public sealed class SegmentShapes
{
    /// <summary>The digit's width is this fraction of its height, its segments are this thick, and the gap between segment ends is this much (at least <see cref="MinGap"/>).</summary>
    public const double WidthRatio = 15.0 / 28, ThicknessRatio = 0.113, GapRatio = 0.03, MinGap = 0.8;
    public double Width { get; }
    public double Height { get; }
    public double Thickness { get; }
    public double ColonWidth => Math.Round(Height * 0.2, 1);
    /// <summary>The seven segments, in the order a, b, c, d, e, f, g.</summary>
    public IReadOnlyList<Geometry> Segments { get; }
    /// <summary>The two dots of a colon, centred in <see cref="ColonWidth"/>.</summary>
    public IReadOnlyList<Rect> ColonDots { get; }
    private static readonly Dictionary<double, SegmentShapes> Cache = new();
    public static SegmentShapes For(double height) { lock (Cache) { if (!Cache.TryGetValue(height, out var shapes)) Cache[height] = shapes = new SegmentShapes(height); return shapes; } }

    // Which segments each character lights: a (top), b (upper right), c (lower right), d (bottom), e (lower left), f (upper left), g (middle).
    private static readonly Dictionary<char, string> Lit = new()
    {
        ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abdeg", ['3'] = "abcdg", ['4'] = "bcfg", ['5'] = "acdfg", ['6'] = "acdefg", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg", [' '] = "", ['-'] = "g"
    };
    public static bool IsLit(char character, int segment) => Lit.TryGetValue(character, out var set) && set.Contains((char)('a' + segment));

    private SegmentShapes(double height)
    {
        Height = height; Width = Math.Round(height * WidthRatio, 1); Thickness = Math.Round(height * ThicknessRatio, 2);
        var half = Thickness / 2; var gap = Math.Max(MinGap, Math.Round(height * GapRatio, 2));
        var xs = half + gap; var xe = Width - half - gap;
        var list = new List<Geometry>();
        Geometry Horizontal(double yc) => Poly((xs, yc), (xs + half, yc - half), (xe - half, yc - half), (xe, yc), (xe - half, yc + half), (xs + half, yc + half));
        Geometry Vertical(double xc, double ys, double ye) => Poly((xc, ys), (xc + half, ys + half), (xc + half, ye - half), (xc, ye), (xc - half, ye - half), (xc - half, ys + half));
        var upper = (half + gap, height / 2 - gap); var lower = (height / 2 + gap, height - half - gap);
        list.Add(Horizontal(half));                                   // a
        list.Add(Vertical(Width - half, upper.Item1, upper.Item2));   // b
        list.Add(Vertical(Width - half, lower.Item1, lower.Item2));   // c
        list.Add(Horizontal(height - half));                          // d
        list.Add(Vertical(half, lower.Item1, lower.Item2));           // e
        list.Add(Vertical(half, upper.Item1, upper.Item2));           // f
        list.Add(Horizontal(height / 2));                             // g
        Segments = list;
        var dot = Thickness * 0.9; var cx = ColonWidth / 2;
        ColonDots = new[] { new Rect(cx - dot / 2, height * 0.3 - dot / 2, dot, dot), new Rect(cx - dot / 2, height * 0.7 - dot / 2, dot, dot) };
    }
    private static Geometry Poly(params (double X, double Y)[] points)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(points[0].X, points[0].Y), true);
        for (var i = 1; i < points.Length; i++) c.LineTo(new Point(points[i].X, points[i].Y));
        c.EndFigure(true);
        return g;
    }
}

/// <summary>
/// A run of seven-segment characters (digits, blanks and colons) in one colour: the segments that are lit in the colour and the ones that are not still drawn faintly, so
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
    /// <summary>The space between two digits, and round a colon on each side.</summary>
    public const double DigitGap = 3, ColonPad = 2;
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
        var shapes = SegmentShapes.For(DigitHeight);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        Color colour;
        if (Colour is { } own) colour = own;
        else colour = this.TryFindResource(LitKey, ActualThemeVariant, out var found) && found is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        var lit = new SolidColorBrush(colour); var ghost = new SolidColorBrush(Color.FromArgb((byte)(dark ? 31 : 26), colour.R, colour.G, colour.B));
        // On a dark card the lit segments carry a faint halo of their own colour, as a lit display does.
        var halo = dark ? new Pen(new SolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B)), 1.3, lineJoin: PenLineJoin.Round) : null;
        var x = 0.0; var previousDigit = false;
        foreach (var c in Text)
        {
            if (c == ':')
            {
                x += ColonPad;
                foreach (var dot in shapes.ColonDots) context.DrawRectangle(lit, null, new Rect(x + dot.X, dot.Y, dot.Width, dot.Height));
                x += shapes.ColonWidth + ColonPad; previousDigit = false; continue;
            }
            if (previousDigit) x += DigitGap;
            using (context.PushTransform(Matrix.CreateTranslation(x, 0)))
                for (var s = 0; s < 7; s++)
                {
                    var on = SegmentShapes.IsLit(c, s);
                    context.DrawGeometry(on ? lit : ghost, on ? halo : null, shapes.Segments[s]);
                }
            x += shapes.Width; previousDigit = true;
        }
    }
}

/// <summary>
/// The day clock as a seven-segment display: hours and minutes in the text colour and the seconds, the same height, in the quiet one, with the colon kept before them.
/// It is one width whatever the time. Set from one hours:minutes:seconds string.
/// </summary>
public sealed class SegmentClock : StackPanel, IClockText
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
