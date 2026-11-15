using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>
/// The pieces that continue the pill's display to the right of the clock (the vacuum-fluorescent look): the transport and break badges and a sixteen-segment account name,
/// all seen through one honeycomb mesh. They are drawn by hand, and their motion runs on a timer that exists only while something is moving.
/// </summary>
/// <summary>
/// The honeycomb of a vacuum-fluorescent display's grid anode: hexagonal cells a little smaller than their lattice, so the gaps between them are the dark lines of the grid.
/// One brush, used as an opacity mask over every lit and unlit shape in a display (the clock, the count, the name, the lights and the glyphs), so the whole display shares one
/// cell size. A cell is 1.45 pixels from its middle to a corner (about 2.5 across), chosen from the sixteen-segment clock study at 3.5 times the first mesh.
/// </summary>
public static class Honeycomb
{
    public const double Cell = 1.45, Inset = 0.93;
    /// <summary>
    /// The cell of the larger mesh used on the dashboard's display timeline and week. Their lit areas are wide and seen from further off than the pill's figures, and
    /// at 1.45 the mesh blurred into a grey wash on Windows; a bigger cell keeps each hexagon distinct.
    /// </summary>
    public const double LargeCell = 2.3;
    private static IBrush Build(double s)
    {
        double w = Math.Sqrt(3) * s, h = 3 * s; var g = new StreamGeometry();
        using (var c = g.Open())
            // One period of the lattice holds five half-cells; the neighbouring tiles complete the ones cut by its edge.
            foreach (var (cx, cy) in new[] { (w / 2, s), (0.0, 2.5 * s), (w, 2.5 * s), (0.0, -0.5 * s), (w, -0.5 * s) })
            {
                var r = s * Inset;
                for (var i = 0; i < 6; i++)
                {
                    var a = Math.PI / 2 + i * Math.PI / 3; var pt = new Point(cx + r * Math.Cos(a), cy - r * Math.Sin(a));
                    if (i == 0) c.BeginFigure(pt, true); else c.LineTo(pt);
                }
                c.EndFigure(true);
            }
        var drawing = new GeometryDrawing { Brush = Brushes.White, Geometry = g };
        var rect = new RelativeRect(0, 0, w, h, RelativeUnit.Absolute);
        return new DrawingBrush(drawing) { TileMode = TileMode.Tile, SourceRect = rect, DestinationRect = rect, Stretch = Stretch.Fill };
    }
    private static readonly Lazy<IBrush> Shared = new(() => Build(Cell));
    private static readonly Lazy<IBrush> SharedLarge = new(() => Build(LargeCell));
    /// <summary>The larger mesh, for the display timeline and week.</summary>
    public static IBrush LargeBrush => SharedLarge.Value;
    /// <summary>Pushes the larger mesh over <paramref name="bounds"/>; dispose to stop.</summary>
    public static IDisposable MeshedLarge(DrawingContext context, Rect bounds) => context.PushOpacityMask(LargeBrush, bounds);
    /// <summary>The mask: opaque inside each cell and clear between them. Start it at a control's top left with <c>OpacityMask</c>.</summary>
    public static IBrush Brush => Shared.Value;
    /// <summary>Pushes the mesh as an opacity mask over <paramref name="bounds"/>; dispose to stop.</summary>
    public static IDisposable Meshed(DrawingContext context, Rect bounds) => context.PushOpacityMask(Brush, bounds);
}

/// <summary>A control whose motion is stepped by a timer that runs only while <see cref="Animating"/> is true and the control is on screen; tests step it by hand.</summary>
public abstract class SteppedControl : Control
{
    private DispatcherTimer? timer; private long last;
    private readonly ViewActivity activity;
    protected SteppedControl()
    {
        activity = new ViewActivity(this, active => { if (active) Wake(); else StopTimer(); });
    }
    protected abstract bool Animating { get; }
    protected abstract void Advance(double milliseconds);
    protected virtual int IntervalMilliseconds => 33;
    /// <summary>Moves the motion on by this many milliseconds (what the timer does), and redraws.</summary>
    public void Step(double milliseconds) { Advance(milliseconds); InvalidateVisual(); if (!Animating) StopTimer(); }
    protected void Wake()
    {
        if (!Animating || !activity.Active || timer is not null) return;
        last = Environment.TickCount64;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IntervalMilliseconds) };
        timer.Tick += (_, _) => { var now = Environment.TickCount64; var dt = now - last; last = now; Step(dt); };
        timer.Start();
    }
    private void StopTimer() { timer?.Stop(); timer = null; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { StopTimer(); base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Wake(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
}

/// <summary>Two faint strokes behind the lit phosphor, drawn outside the mesh.</summary>
internal readonly struct PhosphorBloom
{
    private readonly Pen outer, inner;

    public PhosphorBloom(Color colour, double width, double strength)
    {
        outer = Halo(3.6, .14);
        inner = Halo(1.6, .26);
        Pen Halo(double extra, double alpha) => new(new SolidColorBrush(SoftTint.WithAlpha(colour, alpha * strength)),
            width + extra, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    }

    public void Draw(DrawingContext context, Geometry shape, bool filled = false)
    {
        context.DrawGeometry(filled ? outer.Brush : null, outer, shape);
        context.DrawGeometry(filled ? inner.Brush : null, inner, shape);
    }
}

/// <summary>Play, pause and break badges drawn through the display's shared honeycomb and bloom.</summary>
public abstract class VfdGlyph : SteppedControl
{
    public const double BadgeWidth = 30, BadgeHeight = 14, StrokeWidth = 1.4, BadgeRadius = 3.5, InkKeyWidth = 40, InkKeyHeight = 20;
    /// <summary>E-ink keys: a rounded outline in the key's colour, 1.5 pixels, with its symbol or word in normal type.</summary>
    protected void InkOutline(DrawingContext context, double alpha, bool filled = false)
    {
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height).Deflate(.75); var brush = new SolidColorBrush(SoftTint.WithAlpha(Colour, alpha));
        context.DrawRectangle(filled ? new SolidColorBrush(SoftTint.WithAlpha(Colour, .16)) : null, new Pen(brush, 1.5), rect, 5, 5);
    }
    private Color colour = Colors.Green;
    public Color Colour
    {
        get => colour;
        set
        {
            if (colour == value) return;
            colour = value;
            InvalidateVisual();
        }
    }
    protected VfdGlyph() { IsHitTestVisible = false; }

    protected static StreamGeometry Build(Action<StreamGeometryContext> draw) { var g = new StreamGeometry(); using (var c = g.Open()) draw(c); return g; }
    protected static StreamGeometry Poly(bool closed, params (double X, double Y)[] pts) => Build(c => { c.BeginFigure(new Point(pts[0].X, pts[0].Y), false); for (var i = 1; i < pts.Length; i++) c.LineTo(new Point(pts[i].X, pts[i].Y)); c.EndFigure(closed); });
    protected static StreamGeometry Fill(params (double X, double Y)[] pts) => Build(c => { c.BeginFigure(new Point(pts[0].X, pts[0].Y), true); for (var i = 1; i < pts.Length; i++) c.LineTo(new Point(pts[i].X, pts[i].Y)); c.EndFigure(true); });

    private double Glow => SoftTint.IsDark(this) ? 1 : .45;
    /// <summary>Draws strokes (or a fill) in the colour at this strength; the honeycomb is cut through them by the mask the caller pushed.</summary>
    protected void Ink(DrawingContext context, Geometry geometry, double width, double alpha, bool filled = false)
    {
        if (alpha <= .01) return;
        var brush = new SolidColorBrush(SoftTint.WithAlpha(Colour, alpha));
        if (!filled) context.DrawGeometry(null, new Pen(brush, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        else context.DrawGeometry(brush, new Pen(brush, .5, lineJoin: PenLineJoin.Round), geometry);
    }
    /// <summary>The bloom: the same shape again, wider and faint, under the strokes and outside the mask, so it fills the gaps of the mesh with a soft light.</summary>
    protected void Bloom(DrawingContext context, Geometry geometry, double width, double alpha, bool filled = false)
    {
        if (alpha < .4) return;
        new PhosphorBloom(Colour, width, Glow * alpha).Draw(context, geometry, filled);
    }
}

/// <summary>The display bell, with coloured bloom only when alerts are waiting.</summary>
internal sealed class AlertBellGlyph : Control
{
    private Color colour;
    private bool lit;
    public AlertBellGlyph()
    {
        Width = Height = 9;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void Show(Color tint, bool waiting)
    {
        if (colour == tint && lit == waiting) return;
        colour = tint;
        lit = waiting;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var scale = Matrix.CreateScale(.5, .5);
        if (DisplayInk.On)
        {
            // E-ink: a plain outline, firm when alerts are waiting and quiet when none are; no bloom, no mesh.
            var ink = new Pen(new SolidColorBrush(lit ? colour : SoftTint.WithAlpha(colour, .6)), IconGlyph.StrokeWidth * 1.15, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(scale)) context.DrawGeometry(null, ink, IconGlyph.BellOutline);
            return;
        }
        if (lit)
        {
            var bloom = new PhosphorBloom(colour, IconGlyph.StrokeWidth, SoftTint.IsDark(this) ? 1 : .45);
            using (context.PushTransform(scale))
                bloom.Draw(context, IconGlyph.BellOutline);
        }
        var brush = new SolidColorBrush(lit ? colour : SoftTint.WithAlpha(colour, .55));
        var pen = new Pen(brush, IconGlyph.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (Honeycomb.Meshed(context, new Rect(Bounds.Size)))
        using (context.PushTransform(scale))
            context.DrawGeometry(null, pen, IconGlyph.BellOutline);
    }
}

/// <summary>A rounded play/pause badge that cross-fades between the triangle and bars, or swaps instantly with reduced motion.</summary>
public sealed class TransportGlyph : VfdGlyph
{
    public const double WipeMilliseconds = 180;
    // The play and pause key is the same rounded box as the BREAK label under it, with its symbol in the middle (the tall pill study), so the two read as a pair of keys.
    private static readonly Geometry BadgeOutline = new RectangleGeometry(new Rect(1, 1, BadgeWidth - 2, BadgeHeight - 2), BadgeRadius, BadgeRadius);
    private static readonly StreamGeometry Triangle = Fill((12.3, 4.1), (18.7, 7), (12.3, 9.9));
    private static readonly StreamGeometry Bars = Build(c =>
    {
        foreach (var x in new[] { 11.6, 16.2 })
        {
            c.BeginFigure(new Point(x, 4.1), true);
            c.LineTo(new Point(x + 2.2, 4.1));
            c.LineTo(new Point(x + 2.2, 9.9));
            c.LineTo(new Point(x, 9.9));
            c.EndFigure(true);
        }
    });
    private bool pause; private double progress;
    /// <summary>Whether the pause bars are showing (or on their way); otherwise the play triangle.</summary>
    public bool Paused => pause;
    /// <summary>0 with the triangle showing, 1 with the bars, and between while one fades into the other.</summary>
    public double Progress => progress;
    public TransportGlyph() { }
    /// <summary>The Phosphor badge is 30 by 14; the E-ink key is 34 by 16, so its word and symbol have room in normal type.</summary>
    protected override Size MeasureOverride(Size availableSize) => DisplayInk.On ? new Size(InkKeyWidth, InkKeyHeight) : new Size(BadgeWidth, BadgeHeight);
    /// <summary>Shows the pause bars (true) or the play triangle (false), fading between them unless <paramref name="instant"/>.</summary>
    public void Show(bool pauseBars, bool instant)
    {
        if (pauseBars == pause) return;
        pause = pauseBars;
        if (instant || Motion.Reduced || !this.IsAttachedToVisualTree()) progress = pause ? 1 : 0;
        InvalidateVisual(); Wake();
    }
    protected override bool Animating => progress != (pause ? 1 : 0);
    protected override void Advance(double milliseconds) { var step = milliseconds / WipeMilliseconds; progress = pause ? Math.Min(1, progress + step) : Math.Max(0, progress - step); }
    public override void Render(DrawingContext context)
    {
        if (DisplayInk.On)
        {
            InkOutline(context, 1);
            double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
            var play = Fill((cx - 3, cy - 4), (cx + 4, cy), (cx - 3, cy + 4)); var pauseBars = Build(c => { foreach (var x in new[] { cx - 4.2, cx + 1.2 }) { c.BeginFigure(new Point(x, cy - 4), true); c.LineTo(new Point(x + 3, cy - 4)); c.LineTo(new Point(x + 3, cy + 4)); c.LineTo(new Point(x, cy + 4)); c.EndFigure(true); } });
            Ink(context, play, 0, 1 - progress, true); Ink(context, pauseBars, 0, progress, true);
            return;
        }
        Bloom(context, BadgeOutline, StrokeWidth, 1);
        Bloom(context, Triangle, 0, 1 - progress, true);
        Bloom(context, Bars, 0, progress, true);
        using (Honeycomb.Meshed(context, new Rect(0, 0, BadgeWidth, BadgeHeight)))
        {
            Ink(context, BadgeOutline, StrokeWidth, 1);
            Ink(context, Triangle, 0, 1 - progress, true);
            Ink(context, Bars, 0, progress, true);
        }
    }
}

/// <summary>The break label uses the same phosphor outline and honeycomb as the transport badge.</summary>
public sealed class BreakBadgeGlyph : VfdGlyph
{
    public const double FadeMilliseconds = 180;
    private static readonly Geometry BadgeOutline = new RectangleGeometry(new Rect(1, 1, BadgeWidth - 2, BadgeHeight - 2), BadgeRadius, BadgeRadius);
    private static readonly Lazy<Geometry> Label = new(() =>
    {
        var family = new FontFamily($"avares://{typeof(BreakBadgeGlyph).Assembly.GetName().Name}/Assets/Fonts#Chakra Petch");
        var face = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
        var text = new FormattedText("BREAK", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 7, Brushes.White);
        return text.BuildGeometry(default) ?? new StreamGeometry();
    });
    public bool Highlighted { get; private set; }
    public double Lit { get; private set; }
    public BreakBadgeGlyph() { }
    protected override Size MeasureOverride(Size availableSize) => DisplayInk.On ? new Size(InkKeyWidth, InkKeyHeight) : new Size(BadgeWidth, BadgeHeight);

    public void Show(bool highlighted, bool instant)
    {
        var snap = instant || Motion.Reduced || !this.IsAttachedToVisualTree();
        if (Highlighted == highlighted && (!snap || Lit == (highlighted ? 1 : 0))) return;
        Highlighted = highlighted;
        if (snap) Lit = Highlighted ? 1 : 0;
        InvalidateVisual();
        Wake();
    }

    protected override bool Animating => Lit != (Highlighted ? 1 : 0);
    protected override void Advance(double milliseconds)
    {
        var step = milliseconds / FadeMilliseconds;
        Lit = Highlighted ? Math.Min(1, Lit + step) : Math.Max(0, Lit - step);
    }

    public override void Render(DrawingContext context)
    {
        const double idleStrength = .62;
        var strength = idleStrength + (1 - idleStrength) * Lit;
        if (DisplayInk.On)
        {
            // The word in normal type in a box; amber and a little filled while a break is on, hovered or focused, muted at rest.
            InkOutline(context, strength, Lit > .5);
            var role = new InkRole(12, FontWeight.SemiBold);
            InkType.Draw(context, "Break", role, new SolidColorBrush(SoftTint.WithAlpha(Colour, Math.Min(1, strength + .2))), Bounds.Width / 2, Bounds.Height / 2 + role.Size * .365, TextAlignment.Center);
            return;
        }
        var label = Label.Value;
        // The badge is 30 by 14 (a little more room round it in the key), so the label is centred on the badge, not on the control.
        var shift = Matrix.CreateTranslation(BadgeWidth / 2 - label.Bounds.Center.X, BadgeHeight / 2 - label.Bounds.Center.Y);
        Bloom(context, BadgeOutline, StrokeWidth, strength);
        using (context.PushTransform(shift))
            Bloom(context, label, 0, strength, true);
        using (Honeycomb.Meshed(context, new Rect(0, 0, BadgeWidth, BadgeHeight)))
        {
            Ink(context, BadgeOutline, StrokeWidth, strength);
            using (context.PushTransform(shift))
                Ink(context, label, 0, strength, true);
        }
    }
}

/// <summary>
/// A sixteen-segment account name slanted a little like the old displays. Letters it cannot draw are dropped (accents are stripped, a few ligatures
/// spelled out), and a name that cannot be drawn well enough shows as ACCT and its place in the list. A longer name scrolls a cell at a time.
/// </summary>
public sealed class SegmentName : SteppedControl
{
    public const int Cells = 6;
    public const double CellWidth = 13.5, CellHeight = 20, GlyphWidth = 9.6, Slant = .158, StepMilliseconds = 380, Thickness = 1.7;
    // Segments by letter: a/A top left and right halves, d/D bottom, g/G middle, f/e left upper and lower, b/c right upper and lower, i/l centre upper and lower,
    // h/j upper diagonals (left, right), k/m lower diagonals (left, right), p the point.
    internal static readonly Dictionary<char, string> Font = new()
    {
        ['A'] = "aAfbegGc", ['B'] = "aAbcdDGil", ['C'] = "aAfedD", ['D'] = "aAbcdDil", ['E'] = "aAfedDg", ['F'] = "aAfeg", ['G'] = "aAfedDcG", ['H'] = "febcgG",
        ['I'] = "aAdDil", ['J'] = "bcedD", ['K'] = "fegjm", ['L'] = "fedD", ['M'] = "febchj", ['N'] = "febchm", ['O'] = "aAfebcdD", ['P'] = "aAfebgG",
        ['Q'] = "aAfebcdDm", ['R'] = "aAfebgGm", ['S'] = "aAfgGcdD", ['T'] = "aAil", ['U'] = "febcdD", ['V'] = "fejk", ['W'] = "febckm", ['X'] = "hjkm",
        ['Y'] = "hjl", ['Z'] = "aAjkdD",
        ['0'] = "aAfebcdDjk", ['1'] = "jbc", ['2'] = "aAbgGedD", ['3'] = "aAbcGdD", ['4'] = "fgGbc", ['5'] = "aAfgGcdD", ['6'] = "aAfgGecdD", ['7'] = "aAbc",
        ['8'] = "aAfbecdDgG", ['9'] = "aAfbgGcdD",
        [' '] = "", ['-'] = "gG", ['&'] = "aAhgkdm", ['/'] = "jk", ['+'] = "gGil", ['.'] = "p", [','] = "pk", [':'] = "il", ['\''] = "i", ['('] = "jm", [')'] = "hk", ['_'] = "dD",
    };

    private static readonly Dictionary<char, string> Spelled = new() { ['ß'] = "SS", ['Æ'] = "AE", ['Œ'] = "OE", ['Ø'] = "O", ['Ð'] = "D", ['Þ'] = "TH", ['Ł'] = "L", ['‘'] = "'", ['’'] = "'", ['“'] = "'", ['”'] = "'", ['–'] = "-", ['—'] = "-" };

    /// <summary>The characters the display can draw for a name: upper case, accents stripped, ligatures spelled out, anything else dropped. Null when too little of it is drawable.</summary>
    public static string? Normalise(string name)
    {
        var upper = name.ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormKD);
        var sb = new System.Text.StringBuilder(); var seen = 0; var drawn = 0;
        foreach (var raw in upper)
        {
            if (char.GetUnicodeCategory(raw) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            seen++;
            if (Spelled.TryGetValue(raw, out var spelled)) { sb.Append(spelled); drawn++; continue; }
            if (Font.ContainsKey(raw)) { sb.Append(raw); drawn++; }
        }
        var text = sb.ToString().Trim();
        return text.Length < 2 || drawn * 2 < seen ? null : text;
    }
    /// <summary>What the display shows for an account: its normalised name, or ACCT and its number when it can't be drawn.</summary>
    public static string ForAccount(string name, int number) => DisplayInk.On
        ? DotFont.Normalise(name) ?? "Acct " + (number + 1).ToString("00")
        : Normalise(name) ?? "ACCT " + (number + 1).ToString("00");

    private string text = "";
    private const string ScrollGap = "   ";
    private string ribbon;
    private int offset;
    private double stepClock;
    public string Text
    {
        get => text;
        set
        {
            if (value == text) return;
            text = value;
            // Append just enough of the next repeat to read a full window across the join.
            ribbon = Overflows ? text + ScrollGap + text[..(VisibleCells - 1)] : text.PadRight(VisibleCells);
            offset = 0;
            stepClock = 0;
            InvalidateVisual();
            Wake();
        }
    }
    public int VisibleCells { get; }
    public bool Overflows => text.Length > VisibleCells;
    /// <summary>The cells currently visible, including the gap between repeats.</summary>
    public string VisibleText => ribbon.Substring(Scrolling ? offset : 0, VisibleCells);
    /// <summary>Long names loop continuously unless reduced motion is enabled.</summary>
    public bool Scrolling => Animating;
    public Color Colour { get; set; } = Colors.Gray;
    public SegmentName() : this(Cells) { }
    public SegmentName(int cells)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cells);
        VisibleCells = cells;
        ribbon = new string(' ', cells);
        Width = cells * CellWidth + CellHeight * Slant;
        Height = InkHeight; // tall enough for E-ink's 16 dot rows; the segment name is drawn in the middle of it
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        IsHitTestVisible = false;
    }
    protected override bool Animating => Overflows && !Motion.Reduced;
    protected override void Advance(double milliseconds)
    {
        if (!Animating) { offset = 0; return; }
        stepClock += milliseconds;
        if (stepClock < StepMilliseconds) return;
        var steps = Math.Floor(stepClock / StepMilliseconds);
        stepClock %= StepMilliseconds;
        offset = (int)((offset + steps) % (text.Length + ScrollGap.Length));
    }
    /// <summary>E-ink's name is 16 dot rows at <see cref="DotPitch"/>: a dot is a little under its pitch, and a bold letter lights each dot's right neighbour too.</summary>
    public const double DotPitch = CellWidth / 8, InkHeight = 16 * CellWidth / 8 + 1;
    private void RenderDots(DrawingContext context)
    {
        var brush = new SolidColorBrush(Colour); var size = DotPitch * .86;
        var shown = VisibleText;
        for (var i = 0; i < VisibleCells; i++)
        {
            if (DotFont.Rows(shown[i]) is not { } rows) continue;
            for (var row = 0; row < DotFont.Height; row++)
            {
                var bits = rows[row] | rows[row] >> 1; // bold: the stroke doubled
                if (bits == 0) continue;
                for (var col = 0; col < DotFont.Width; col++)
                    if ((bits >> (DotFont.Width - 1 - col) & 1) != 0)
                        context.DrawRectangle(brush, null, new Rect(i * CellWidth + col * DotPitch, .5 + row * DotPitch, size, size), size * .3, size * .3);
            }
        }
    }
    public override void Render(DrawingContext context)
    {
        if (DisplayInk.On) { RenderDots(context); return; }
        using var centred = context.PushTransform(Matrix.CreateTranslation(0, (InkHeight - CellHeight) / 2));
        var ink = Ui.ThemeColour(this, "Ink", Colors.Gray);
        var unlit = new Pen(new SolidColorBrush(SoftTint.WithAlpha(ink, .09)), Thickness, lineCap: PenLineCap.Round);
        var lit = new Pen(new SolidColorBrush(Colour), Thickness, lineCap: PenLineCap.Round);
        // On a dark theme each lit bar gives off a soft glow, as the mock draws it: the same bar again, wider and faint, under it.
        var glow = SoftTint.IsDark(this) ? new Pen(new SolidColorBrush(SoftTint.WithAlpha(Colour, .2)), Thickness * 3.2, lineCap: PenLineCap.Round) : null;
        var shown = VisibleText;
        // The glow is drawn first and outside the honeycomb, so it fills the gaps of the mesh with a soft light; the bars themselves are drawn through the mesh.
        if (glow is not null)
            for (var i = 0; i < VisibleCells; i++) DrawCell(context, i * CellWidth + 1, Font.TryGetValue(shown[i], out var on0) ? on0 : "", unlit, lit, glow, glowPass: true);
        using (Honeycomb.Meshed(context, new Rect(0, 0, Bounds.Width, Bounds.Height)))
            for (var i = 0; i < VisibleCells; i++) DrawCell(context, i * CellWidth + 1, Font.TryGetValue(shown[i], out var on) ? on : "", unlit, lit, null, glowPass: false);
    }
    private static Point P(double x0, double fx, double fy)
    {
        const double inset = 1, w = GlyphWidth, h = CellHeight - 2 * inset;
        var y = inset + fy * h; var x = x0 + fx * w;
        return new Point(x + (CellHeight - y) * Slant, y);
    }
    internal static readonly (char Id, double X1, double Y1, double X2, double Y2)[] Bars =
    {
        ('a', 0, 0, .5, 0), ('A', .5, 0, 1, 0), ('d', 0, 1, .5, 1), ('D', .5, 1, 1, 1), ('g', 0, .5, .5, .5), ('G', .5, .5, 1, .5),
        ('f', 0, 0, 0, .5), ('e', 0, .5, 0, 1), ('b', 1, 0, 1, .5), ('c', 1, .5, 1, 1), ('i', .5, 0, .5, .5), ('l', .5, .5, .5, 1),
        ('h', 0, 0, .5, .5), ('j', 1, 0, .5, .5), ('k', 0, 1, .5, .5), ('m', 1, 1, .5, .5),
    };

    private static void DrawCell(DrawingContext context, double x0, string on, Pen unlit, Pen lit, Pen? glow, bool glowPass)
    {
        // The ends of each bar are pulled in a hair so neighbouring segments leave a gap, as on the real thing.
        foreach (var (id, ax, ay, bx, by) in Bars)
        {
            var a = P(x0, ax, ay); var b = P(x0, bx, by);
            var dx = b.X - a.X; var dy = b.Y - a.Y; var len = Math.Sqrt(dx * dx + dy * dy); var trim = Math.Min(1.1, len / 4);
            var u = new Vector(dx / len, dy / len);
            var isOn = on.Contains(id);
            if (glowPass) { if (isOn && glow is not null) context.DrawLine(glow, a + u * trim, b - u * trim); continue; }
            context.DrawLine(isOn ? lit : unlit, a + u * trim, b - u * trim);
        }
        if (glowPass) return;
        var p = P(x0, 1.18, 1);
        context.DrawEllipse(on.Contains('p') ? lit.Brush : unlit.Brush, null, p, 1, 1);
    }
}
