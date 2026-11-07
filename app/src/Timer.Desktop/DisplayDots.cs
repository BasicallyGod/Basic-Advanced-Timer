using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>
/// The pieces that continue the pill's display to the right of the clock (the vacuum-fluorescent look): the play and pause tag, the break mug and a sixteen-segment account name,
/// all seen through one honeycomb mesh. They are drawn by hand, and their motion runs on a timer that exists only while something is moving.
/// </summary>
/// <summary>
/// The honeycomb of a vacuum-fluorescent display's grid anode: hexagonal cells a little smaller than their lattice, so the gaps between them are the dark lines of the grid.
/// One brush, used as an opacity mask over every lit and unlit shape in a display (the clock, the count, the name, the lights and the glyphs), so the whole display shares one
/// cell size. A cell is 1.24 pixels from its middle to a corner (about 2.1 across), the size that looked right on the play and break buttons.
/// </summary>
public static class Honeycomb
{
    public const double Cell = 1.24, Inset = 0.93;
    private static readonly Lazy<IBrush> Shared = new(() =>
    {
        double s = Cell, w = Math.Sqrt(3) * s, h = 3 * s; var g = new StreamGeometry();
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
    });
    /// <summary>The mask: opaque inside each cell and clear between them. Start it at a control's top left with <c>OpacityMask</c>.</summary>
    public static IBrush Brush => Shared.Value;
    /// <summary>Pushes the mesh as an opacity mask over <paramref name="bounds"/>; dispose to stop.</summary>
    public static IDisposable Meshed(DrawingContext context, Rect bounds) => context.PushOpacityMask(Brush, bounds);
}

/// <summary>A control whose motion is stepped by a timer that runs only while <see cref="Animating"/> is true and the control is on screen; tests step it by hand.</summary>
public abstract class SteppedControl : Control
{
    private DispatcherTimer? timer; private long last;
    protected abstract bool Animating { get; }
    protected abstract void Advance(double milliseconds);
    protected virtual int IntervalMilliseconds => 33;
    /// <summary>Moves the motion on by this many milliseconds (what the timer does), and redraws.</summary>
    public void Step(double milliseconds) { Advance(milliseconds); InvalidateVisual(); if (!Animating) StopTimer(); }
    protected void Wake()
    {
        if (!Animating || !this.IsAttachedToVisualTree() || timer is not null) return;
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

/// <summary>
/// The play, pause and break symbols as a vacuum-fluorescent display draws them: solid phosphor strokes seen through the display's honeycomb (<see cref="Honeycomb"/>), with a
/// soft bloom. Each glyph is drawn from true curves in its own units and scaled so its stroke is the same 1.8 pixels in both, so it stays crisp at any scale.
/// </summary>
public abstract class VfdGlyph : SteppedControl
{
    public const double GhostAlpha = 0.2;
    /// <summary>The height of the play tag, of the mug's tile, and of the account name's cells: the three line up.</summary>
    public const double Height20 = 20;
    public Color Colour { get; set; } = Colors.Green;
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
        if (alpha < .4 || Glow <= 0) return;
        foreach (var (extra, strength) in new[] { (3.6, .14), (1.6, .26) })
            context.DrawGeometry(filled ? new SolidColorBrush(SoftTint.WithAlpha(Colour, strength * Glow * alpha)) : null,
                new Pen(new SolidColorBrush(SoftTint.WithAlpha(Colour, strength * Glow * alpha)), width + extra, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}

/// <summary>The play or pause symbol: a pentagon tag, 20 tall, with a triangle or two bars inside it, clear of its edges. A change cross-fades between the two, or swaps at once with less motion asked for.</summary>
public sealed class TransportGlyph : VfdGlyph
{
    public const double WipeMilliseconds = 180;
    /// <summary>The drawing is 34 by 24.2 units; this many pixels to a unit makes the tag 20 tall (the stroke 2.2 units is 1.8 pixels).</summary>
    public const double Unit = Height20 / 24.2;
    public static double TagWidth => 34 * Unit;
    private static readonly StreamGeometry Tag = Poly(true, (1.1, 1.2), (23, 1.2), (32.9, 12.1), (23, 23), (1.1, 23));
    private static readonly StreamGeometry Triangle = Fill((9, 6.8), (19, 12.1), (9, 17.4));
    private static readonly StreamGeometry Bars = Build(c => { foreach (var x in new[] { 8.6, 14.4 }) { c.BeginFigure(new Point(x, 6.8), true); c.LineTo(new Point(x + 3.3, 6.8)); c.LineTo(new Point(x + 3.3, 17.4)); c.LineTo(new Point(x, 17.4)); c.EndFigure(true); } });
    private bool pause; private double progress;
    /// <summary>Whether the pause bars are showing (or on their way); otherwise the play triangle.</summary>
    public bool Paused => pause;
    /// <summary>0 with the triangle showing, 1 with the bars, and between while one fades into the other.</summary>
    public double Progress => progress;
    public TransportGlyph() { Width = TagWidth; Height = Height20; }
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
        var w = 2.2; var scale = Matrix.CreateScale(Unit, Unit);
        using (context.PushTransform(scale)) { Bloom(context, Tag, w, 1); Bloom(context, Triangle, 0, 1 - progress, true); Bloom(context, Bars, 0, progress, true); }
        using (Honeycomb.Meshed(context, new Rect(0, 0, Width, Height)))
        using (context.PushTransform(scale)) { Ink(context, Tag, w, 1); Ink(context, Triangle, 0, 1 - progress, true); Ink(context, Bars, 0, progress, true); }
    }
}

/// <summary>
/// The break mug: a tile broken at the top holding a cup with a ring handle, and three wisps of steam. The tile is 20 tall like the play tag, level with it, and the steam rises
/// above that. The strokes are a faint ghost at rest and light up on a break; the steam is always drawn, and while a break is on (or the pointer is over the mug) its wisps take
/// turns lighting, slowly, each fading as the next comes up.
/// </summary>
public sealed class CupGlyph : VfdGlyph
{
    public const double FadeMilliseconds = 260, TurnMilliseconds = 900;
    public const int Wisps = 3;
    /// <summary>The tile is 16 units tall before its stroke; this many pixels to a unit makes it 20 with the stroke. Its strokes are thinner in units so they are 1.8 pixels like the tag's.</summary>
    public const double Unit = (Height20 - 1.8) / 16, StrokeScale = 1.8 / Unit / 2.2;
    /// <summary>The drawing runs 27 units across and from the steam's top (3.6) to the tile's foot (27.5); the tile's middle is at 18.4. The box is as tall as twice the tile's middle needs, so the tile sits on the box's middle line.</summary>
    public static double MugWidth => 27 * Unit;
    public static double BoxHeight => 2 * (18.4 - 3.9) * Unit;
    private static readonly StreamGeometry Tile = Build(c =>
    {
        c.BeginFigure(new Point(6.2, 10.4), false); c.LineTo(new Point(4.8, 10.4)); c.QuadraticBezierTo(new Point(1.9, 10.4), new Point(1.9, 13.3)); c.LineTo(new Point(1.9, 23.5));
        c.QuadraticBezierTo(new Point(1.9, 26.4), new Point(4.8, 26.4)); c.LineTo(new Point(23.2, 26.4)); c.QuadraticBezierTo(new Point(26.1, 26.4), new Point(26.1, 23.5));
        c.LineTo(new Point(26.1, 13.3)); c.QuadraticBezierTo(new Point(26.1, 10.4), new Point(23.2, 10.4)); c.LineTo(new Point(21.8, 10.4)); c.EndFigure(false);
    });
    private static readonly StreamGeometry Cup = Build(c =>
    {
        c.BeginFigure(new Point(7.2, 15.2), false); c.LineTo(new Point(19.2, 15.2)); c.LineTo(new Point(19.2, 19.4)); c.CubicBezierTo(new Point(19.2, 21.9), new Point(17.7, 23.4), new Point(15.3, 23.4));
        c.LineTo(new Point(11.1, 23.4)); c.CubicBezierTo(new Point(8.7, 23.4), new Point(7.2, 21.9), new Point(7.2, 19.4)); c.EndFigure(true);
    });
    private static readonly StreamGeometry Handle = Build(c => { c.BeginFigure(new Point(19.2, 16.4), false); c.CubicBezierTo(new Point(22.9, 15.8), new Point(22.9, 20.6), new Point(19.2, 20)); c.EndFigure(false); });
    // Each wisp is two bows of an S: a quadratic and its smooth continuation (the reflected control point).
    private static readonly StreamGeometry[] Steam = { Wisp(9.8), Wisp(14), Wisp(18.2) };
    private static StreamGeometry Wisp(double x) => Build(c =>
    {
        c.BeginFigure(new Point(x, 9.4), false); c.QuadraticBezierTo(new Point(x - 1.5, 8), new Point(x, 6.6));
        c.QuadraticBezierTo(new Point(x + 1.5, 5.2), new Point(x, 4.8)); c.EndFigure(false);
    });
    private bool on, steaming; private double lit, clock;
    public CupGlyph() { Width = MugWidth; Height = BoxHeight; }
    public bool On => on;
    /// <summary>Whether the steam is moving (on a break or under the pointer, with motion allowed).</summary>
    public bool Steaming => steaming;
    private bool Animated => steaming && !Motion.Reduced;
    /// <summary>How lit the mug is, 0 (a faint ghost) to 1 (a break is on).</summary>
    public double Lit => lit;
    /// <summary>Sets whether a break is on and whether the steam rises (on a break, or under the pointer).</summary>
    public void Show(bool breakOn, bool steam, bool instant)
    {
        on = breakOn; steaming = steam;
        if (instant || Motion.Reduced || !this.IsAttachedToVisualTree()) lit = on ? 1 : 0;
        InvalidateVisual(); Wake();
    }
    protected override bool Animating => lit != (on ? 1 : 0) || Animated;
    protected override void Advance(double milliseconds)
    {
        var step = milliseconds / FadeMilliseconds; lit = on ? Math.Min(1, lit + step) : Math.Max(0, lit - step);
        if (Animated) clock += milliseconds;
    }
    private double Body => GhostAlpha + (1 - GhostAlpha) * lit;
    /// <summary>How bright wisp <paramref name="i"/> is now: a ghost at rest, and while steaming the one whose turn it is brightest and the others fading.</summary>
    public double WispBrightness(int i)
    {
        if (!Animated) return Body;
        var dist = (((clock / TurnMilliseconds - i) % Wisps) + Wisps) % Wisps;
        return GhostAlpha + (1 - GhostAlpha) * (.35 + .65 * lit) * Math.Max(.15, 1 - dist * .5);
    }
    public override void Render(DrawingContext context)
    {
        var shift = Matrix.CreateTranslation(-14, -18.4) * Matrix.CreateScale(Unit, Unit) * Matrix.CreateTranslation(Width / 2, Height / 2);
        double tile = 2.2 * StrokeScale, cup = 2 * StrokeScale, handle = 1.8 * StrokeScale, steam = 1.9 * StrokeScale;
        using (context.PushTransform(shift))
        {
            Bloom(context, Tile, tile, Body); Bloom(context, Cup, cup, Body); Bloom(context, Handle, handle, Body);
            for (var i = 0; i < Wisps; i++) Bloom(context, Steam[i], steam, WispBrightness(i));
        }
        using (Honeycomb.Meshed(context, new Rect(0, 0, Width, Height)))
        using (context.PushTransform(shift))
        {
            Ink(context, Tile, tile, Body); Ink(context, Cup, cup, Body); Ink(context, Handle, handle, Body);
            for (var i = 0; i < Wisps; i++) Ink(context, Steam[i], steam, WispBrightness(i));
        }
    }
}

/// <summary>
/// A sixteen-segment account name six cells wide, slanted a little like the old displays. Letters it cannot draw are dropped (accents are stripped, a few ligatures
/// spelled out), and a name that cannot be drawn well enough shows as ACCT and its place in the list. A longer name scrolls a cell at a time.
/// </summary>
public sealed class SegmentName : SteppedControl
{
    public const int Cells = 6;
    public const double CellWidth = 13.5, CellHeight = 20, GlyphWidth = 9.6, Slant = .158, StepMilliseconds = 380, Thickness = 1.7;
    public static double RunWidth => Cells * CellWidth;
    // Segments by letter: a/A top left and right halves, d/D bottom, g/G middle, f/e left upper and lower, b/c right upper and lower, i/l centre upper and lower,
    // h/j upper diagonals (left, right), k/m lower diagonals (left, right), p the point.
    private static readonly Dictionary<char, string> Font = new()
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
    public static string ForAccount(string name, int number) => Normalise(name) ?? "ACCT " + (number + 1).ToString("00");

    private string text = "";
    private int offset, hold; private double stepClock; private bool atEnd;
    /// <summary>How many steps a long name rests at each end of its run: about a second at the start and at the end.</summary>
    public const int RestSteps = 3;
    public string Text { get => text; set { if (value == text) return; text = value; Restart(); InvalidateVisual(); Wake(); } }
    /// <summary>How many characters the text is scrolled along.</summary>
    public int Offset => offset;
    public bool Overflows => text.Length > Cells;
    /// <summary>Whether the name is on the move: a name too long for its cells always runs along (rests at each end, then goes round again) unless less motion is asked for.</summary>
    public bool Scrolling => Animating;
    public Color Colour { get; set; } = Colors.Gray;
    public SegmentName() { Width = RunWidth + CellHeight * Slant; Height = CellHeight; IsHitTestVisible = false; }
    private void Restart() { offset = 0; hold = RestSteps; stepClock = 0; atEnd = false; }
    /// <summary>Starts the run again from the first characters.</summary>
    public void Scroll() { Restart(); InvalidateVisual(); Wake(); }
    protected override bool Animating => Overflows && !Motion.Reduced;
    protected override void Advance(double milliseconds)
    {
        if (!Animating) { offset = 0; return; }
        stepClock += milliseconds;
        while (stepClock >= StepMilliseconds)
        {
            stepClock -= StepMilliseconds;
            if (hold > 0) { hold--; continue; }
            if (atEnd) { offset = 0; atEnd = false; hold = RestSteps; }
            else if (offset < text.Length - Cells) { offset++; if (offset == text.Length - Cells) { atEnd = true; hold = RestSteps; } }
        }
    }
    public override void Render(DrawingContext context)
    {
        var ink = Ui.ThemeColour(this, "Ink", Colors.Gray);
        var unlit = new Pen(new SolidColorBrush(SoftTint.WithAlpha(ink, .09)), Thickness, lineCap: PenLineCap.Round);
        var lit = new Pen(new SolidColorBrush(Colour), Thickness, lineCap: PenLineCap.Round);
        // On a dark theme each lit bar gives off a soft glow, as the mock draws it: the same bar again, wider and faint, under it.
        var glow = SoftTint.IsDark(this) ? new Pen(new SolidColorBrush(SoftTint.WithAlpha(Colour, .2)), Thickness * 3.2, lineCap: PenLineCap.Round) : null;
        var shown = text.Length <= Cells ? text.PadRight(Cells) : text.Substring(Math.Min(offset, text.Length - Cells), Cells);
        // The glow is drawn first and outside the honeycomb, so it fills the gaps of the mesh with a soft light; the bars themselves are drawn through the mesh.
        if (glow is not null)
            for (var i = 0; i < Cells; i++) DrawCell(context, i * CellWidth + 1, Font.TryGetValue(shown[i], out var on0) ? on0 : "", unlit, lit, glow, glowPass: true);
        using (Honeycomb.Meshed(context, new Rect(0, 0, Bounds.Width, Bounds.Height)))
            for (var i = 0; i < Cells; i++) DrawCell(context, i * CellWidth + 1, Font.TryGetValue(shown[i], out var on) ? on : "", unlit, lit, null, glowPass: false);
    }
    private static Point P(double x0, double fx, double fy)
    {
        const double inset = 1, w = GlyphWidth, h = CellHeight - 2 * inset;
        var y = inset + fy * h; var x = x0 + fx * w;
        return new Point(x + (CellHeight - y) * Slant, y);
    }
    private static void DrawCell(DrawingContext context, double x0, string on, Pen unlit, Pen lit, Pen? glow, bool glowPass)
    {
        // The ends of each bar are pulled in a hair so neighbouring segments leave a gap, as on the real thing.
        (char Id, double X1, double Y1, double X2, double Y2)[] bars =
        {
            ('a', 0, 0, .5, 0), ('A', .5, 0, 1, 0), ('d', 0, 1, .5, 1), ('D', .5, 1, 1, 1), ('g', 0, .5, .5, .5), ('G', .5, .5, 1, .5),
            ('f', 0, 0, 0, .5), ('e', 0, .5, 0, 1), ('b', 1, 0, 1, .5), ('c', 1, .5, 1, 1), ('i', .5, 0, .5, .5), ('l', .5, .5, .5, 1),
            ('h', 0, 0, .5, .5), ('j', 1, 0, .5, .5), ('k', 0, 1, .5, .5), ('m', 1, 1, .5, .5),
        };
        foreach (var (id, ax, ay, bx, by) in bars)
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
