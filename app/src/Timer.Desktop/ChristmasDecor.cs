using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Timer.Desktop;

/// <summary>What the lights follow: the pill's own state.</summary>
public enum DecorState { Idle, Recording, Break }

/// <summary>
/// Where the card sits inside the decoration and how big the decoration is drawn: the room kept round the card, the card's corner radius and a scale (1 on the pill, smaller
/// round the dashboard sidebar's clock display, which is a small card with little room round it).
/// </summary>
public readonly record struct DecorFrame(double Room, double Radius, double Scale)
{
    public static readonly DecorFrame Pill = new(Ui.ShadowMargin, PillWindow.PillRadius, 1);
}

/// <summary>A point on a rounded rectangle, walked clockwise from the start of the top edge, with the direction of travel there.</summary>
public static class RoundedRing
{
    public static double Length(double w, double h, double r) => 2 * (w - 2 * r) + 2 * (h - 2 * r) + 2 * Math.PI * r;
    public static (double X, double Y, double Tx, double Ty) At(double s, double w, double h, double r)
    {
        double a = w - 2 * r, b = h - 2 * r, q = Math.PI * r / 2, length = 2 * a + 2 * b + 4 * q;
        s = ((s % length) + length) % length;
        double t;
        if (s < a) return (r + s, 0, 1, 0);
        s -= a; if (s < q) { t = s / r; return (w - r + r * Math.Sin(t), r - r * Math.Cos(t), Math.Cos(t), Math.Sin(t)); }
        s -= q; if (s < b) return (w, r + s, 0, 1);
        s -= b; if (s < q) { t = s / r; return (w - r + r * Math.Cos(t), h - r + r * Math.Sin(t), -Math.Sin(t), Math.Cos(t)); }
        s -= q; if (s < a) return (w - r - s, h, -1, 0);
        s -= a; if (s < q) { t = s / r; return (r - r * Math.Sin(t), h - r + r * Math.Cos(t), -Math.Cos(t), -Math.Sin(t)); }
        s -= q; if (s < b) return (0, h - r - s, 0, -1);
        s -= b; t = s / r; return (r - r * Math.Cos(t), r - r * Math.Sin(t), Math.Sin(t), -Math.Cos(t));
    }
}

/// <summary>
/// The Christmas wreath and lights round the pill (the Seasonal Easter eggs board, "Christmas: wreath and lights on the pill"). It is two layers of one control: the
/// garland (needles, berries and the thin wire) drawn behind the card, and the lights and the bow drawn over its edge. Both fill the pill window and work out where
/// the card is from the room the window keeps round it (<see cref="Ui.ShadowMargin"/>), so they follow the full and the compact pill alike. The lights follow the
/// pill's state: four colours twinkling when ready, green and red alternating while recording, amber on a break. They are drawn by hand on one slow timer that runs only
/// while the lights are showing, and stand still, in the state's colour, when Windows asks for less motion.
/// </summary>
public sealed class ChristmasDecor : Control
{
    public enum Layers { Garland, Lights }
    /// <summary>The bulb ring sits this far outside the card, the garland a little nearer.</summary>
    public const double BulbRing = 8, GarlandRing = 4, BulbSpacing = 30, NeedleSpacing = 11;
    public const double BulbSpeed = 0.75;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public Layers Layer { get; }
    private DecorFrame frame = DecorFrame.Pill;
    /// <summary>Where the card is and how big to draw; the pill's by default.</summary>
    public DecorFrame Frame { get => frame; set { if (value == frame) return; frame = value; InvalidateVisual(); } }
    private bool active, dark; private DecorState state;
    private DispatcherTimer? timer;
    private readonly ViewActivity activity;
    /// <summary>Tests set this to read the lights at one moment; null uses the clock.</summary>
    public double? FixedSeconds { get; set; }

    public ChristmasDecor(Layers layer)
    {
        Layer = layer; IsHitTestVisible = false; IsVisible = false; ClipToBounds = false;
        Name = layer == Layers.Garland ? "SeasonGarland" : "SeasonLights";
        // The lights' timer runs only while the control is on screen: attached, every ancestor visible and the window not minimised.
        activity = new ViewActivity(this, on => { UpdateTimer(); if (on) InvalidateVisual(); });
    }

    public bool Active => active;
    public DecorState State => state;

    /// <summary>Shows or hides the decoration and tells it the pill's state and whether the theme is dark (the needles are a little lighter on dark).</summary>
    public void Configure(bool show, DecorState newState, bool isDark)
    {
        var redraw = show != active || newState != state || isDark != dark;
        active = show; state = newState; dark = isDark; IsVisible = show;
        if (redraw) InvalidateVisual();
        UpdateTimer();
    }

    // ---- the lights
    public readonly record struct Bulb(double X, double Y, double Rotation, Color Colour, double Period, double Delay);
    private static readonly Color Red = Color.Parse("#E5384F"), Green = Color.Parse("#2FBF71"), Blue = Color.Parse("#3C8DFF"), Gold = Color.Parse("#FFC53D"), Amber = Color.Parse("#F0A62E");
    /// <summary>The colour of one bulb: four in turn when ready, green and red alternating while recording, amber on a break.</summary>
    public static Color BulbColour(DecorState state, int index) => state switch
    {
        DecorState.Idle => (index % 4) switch { 0 => Red, 1 => Green, 2 => Blue, _ => Gold },
        DecorState.Recording => index % 2 == 0 ? Green : Red,
        _ => Amber
    };
    // How bright a bulb is across one cycle (0 to 1), as the board drew the twinkle. Each state has its own rhythm, and the quicker ones dip further.
    private static readonly (double At, double Opacity)[] IdleCurve = { (0, 1), (.4, .25), (.62, .95), (.8, .5), (1, 1) };
    private static readonly (double At, double Opacity)[] RecordingCurve = { (0, 1), (.22, .3), (.38, 1), (.58, .5), (.72, 1), (1, 1) };
    private static readonly (double At, double Opacity)[] BreakCurve = { (0, 1), (.35, .3), (.55, .9), (.75, .45), (1, 1) };
    private static double Along((double At, double Opacity)[] curve, double phase)
    {
        for (var i = 1; i < curve.Length; i++)
            if (phase <= curve[i].At) { var (a0, o0) = curve[i - 1]; var (a1, o1) = curve[i]; return a1 <= a0 ? o1 : o0 + (o1 - o0) * (phase - a0) / (a1 - a0); }
        return 1;
    }
    /// <summary>A bulb's brightness at a moment: each has its own period and delay, so they never beat together.</summary>
    public static double Twinkle(DecorState state, int index, double seconds)
    {
        var (period, delay) = Rhythm(state, index);
        var phase = ((seconds + delay) / period) % 1.0; if (phase < 0) phase += 1;
        return Along(state switch { DecorState.Idle => IdleCurve, DecorState.Recording => RecordingCurve, _ => BreakCurve }, phase);
    }
    private static (double Period, double Delay) Rhythm(DecorState state, int i) => state switch
    {
        DecorState.Idle => ((1.2 + (i * 7 % 11) / 10.0) / BulbSpeed, (i * 13 % 17) / 10.0),
        DecorState.Recording => ((2.0 + (i * 3 % 5) / 10.0) / BulbSpeed, i * 0.22),
        _ => ((3.2 + (i * 5 % 7) / 10.0) / BulbSpeed, (i * 11 % 19) / 8.0)
    };

    /// <summary>Where the card is inside this control: the window keeps <see cref="Ui.ShadowMargin"/> round it.</summary>
    public static Rect CardIn(Size size, double room = Ui.ShadowMargin) => new(room, room, Math.Max(0, size.Width - 2 * room), Math.Max(0, size.Height - 2 * room));

    /// <summary>The bulbs for a card of this size, with their places in this control's own coordinates.</summary>
    public static IReadOnlyList<Bulb> BulbsFor(Size size, DecorState state, DecorFrame? within = null)
    {
        var f = within ?? DecorFrame.Pill; var card = CardIn(size, f.Room); var d = BulbRing * f.Scale; double w = card.Width + 2 * d, h = card.Height + 2 * d, r = f.Radius + d;
        var n = Math.Max(1, (int)Math.Round(RoundedRing.Length(w, h, r) / (BulbSpacing * f.Scale))); var list = new List<Bulb>(n);
        for (var i = 0; i < n; i++)
        {
            var p = RoundedRing.At((i + .5) * RoundedRing.Length(w, h, r) / n, w, h, r);
            var rotation = Math.Atan2(-p.Ty, -p.Tx) * 180 / Math.PI; // The bulb hangs outward from the wire.
            var (period, delay) = Rhythm(state, i);
            list.Add(new Bulb(card.X - d + p.X, card.Y - d + p.Y, rotation, BulbColour(state, i), period, delay));
        }
        return list;
    }

    // ---- the garland
    private readonly record struct Needle(double X, double Y, double Angle, int Shade);
    private static IReadOnlyList<Needle> NeedlesFor(Size size, DecorFrame f, out IReadOnlyList<Point> berries)
    {
        var card = CardIn(size, f.Room); var d = GarlandRing * f.Scale; double w = card.Width + 2 * d, h = card.Height + 2 * d, r = f.Radius + d;
        var length = RoundedRing.Length(w, h, r); var n = Math.Max(1, (int)Math.Round(length / (NeedleSpacing * f.Scale)));
        var needles = new List<Needle>(2 * n); var berryList = new List<Point>();
        for (var i = 0; i < n; i++)
        {
            var p = RoundedRing.At(i * length / n, w, h, r);
            double angle = Math.Atan2(p.Ty, p.Tx) * 180 / Math.PI, nx = p.Ty, ny = -p.Tx;
            for (var k = 0; k < 2; k++)
            {
                var out_ = (k == 0 ? 1.5 : -1.5) * f.Scale;
                needles.Add(new Needle(card.X - d + p.X + nx * out_, card.Y - d + p.Y + ny * out_, angle + (k == 0 ? -42 : 42) + (i * 5 % 9) - 4, (i + k) % 3));
            }
            if (i % 7 == 3) berryList.Add(new Point(card.X - d + p.X + nx * 3 * f.Scale, card.Y - d + p.Y + ny * 3 * f.Scale));
        }
        berries = berryList; return needles;
    }

    private static readonly IBrush[] LightGreens = { new SolidColorBrush(Color.Parse("#1F6B3A")), new SolidColorBrush(Color.Parse("#2E8B4F")), new SolidColorBrush(Color.Parse("#175A31")) };
    private static readonly IBrush[] DarkGreens = { new SolidColorBrush(Color.Parse("#2A7A47")), new SolidColorBrush(Color.Parse("#348F55")), new SolidColorBrush(Color.Parse("#1F6338")) };
    private static readonly IBrush NeedleShade = new SolidColorBrush(Color.FromArgb(56, 0, 0, 0));
    private static readonly IBrush BerryBrush = new RadialGradientBrush { Center = new RelativePoint(.35, .3, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.35, .3, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("70%"), RadiusY = RelativeScalar.Parse("70%"), GradientStops = { new GradientStop(Color.Parse("#FF8A8A"), 0), new GradientStop(Color.Parse("#C4243A"), .6), new GradientStop(Color.Parse("#8A1426"), 1) } };
    private static readonly IPen WirePen = new Pen(new SolidColorBrush(Color.FromArgb(178, 0x2B, 0x5E, 0x3C)), 1.5);
    private static readonly Geometry NeedleShape = new EllipseGeometry(new Rect(-8.5, -3.5, 17, 7));
    private static readonly Geometry BerryShape = new EllipseGeometry(new Rect(-3, -3, 6, 6));

    // ---- the bow, from the board's drawing (46 by 38)
    private static readonly Geometry BowLoops = Geometry.Parse("M23 18C17 6 4 3 3 11c-1 8 12 9 20 7zM23 18C29 6 42 3 43 11c1 8-12 9-20 7z");
    private static readonly Geometry BowCreases = Geometry.Parse("M23 18C16 17 9 15 6 11M23 18c7-1 14-3 17-7");
    private static readonly Geometry BowTails = Geometry.Parse("M20 20l-8 15 6-3 3 5 3-14zM26 20l8 15-6-3-3 5-3-14z");
    private static readonly IBrush BowRed = new SolidColorBrush(Color.Parse("#C4243A")), BowDark = new SolidColorBrush(Color.Parse("#A81E33")), BowKnot = new SolidColorBrush(Color.Parse("#E5384F")), BowShine = new SolidColorBrush(Color.FromArgb(204, 0xFF, 0xB3, 0xBB));
    private static readonly IPen BowCrease = new Pen(new SolidColorBrush(Color.FromArgb(153, 0x8A, 0x14, 0x26)), 1.2);
    private const double BowWidth = 46, BowHeight = 38;
    /// <summary>The bow sits on the card's top edge, near the left end (nearer in on the full pill than on the compact one, which has less edge).</summary>
    public static Rect BowFor(Size size, bool compact, DecorFrame? within = null) { var f = within ?? DecorFrame.Pill; var card = CardIn(size, f.Room); return new Rect(card.X + (compact ? 40 : 64) * f.Scale, card.Y - 24 * f.Scale, BowWidth * f.Scale, BowHeight * f.Scale); }

    // ---- the lights' timer
    private void UpdateTimer()
    {
        var want = active && Layer == Layers.Lights && activity.Active && !Motion.Reduced && FixedSeconds is null;
        if (want)
        {
            timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
            timer.Tick -= OnTick; timer.Tick += OnTick; if (!timer.IsEnabled) timer.Start();
        }
        else if (timer is { IsEnabled: true }) timer.Stop();
    }
    private void OnTick(object? sender, EventArgs e) { if (!activity.Active || !active) { timer?.Stop(); return; } InvalidateVisual(); }
    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!active || Bounds.Width < 2 * frame.Room + 20 || Bounds.Height < 2 * frame.Room + 20) return;
        var size = Bounds.Size; var card = CardIn(size, frame.Room);
        if (Layer == Layers.Garland) DrawGarland(context, size, card); else DrawLights(context, size, card);
    }

    private void DrawGarland(DrawingContext context, Size size, Rect card)
    {
        var d = BulbRing * frame.Scale; var wire = new Rect(card.X - d, card.Y - d, card.Width + 2 * d, card.Height + 2 * d);
        context.DrawRectangle(null, WirePen, wire, frame.Radius + d, frame.Radius + d);
        var greens = dark ? DarkGreens : LightGreens;
        var needles = NeedlesFor(size, frame, out var berries);
        foreach (var n in needles)
        {
            using (context.PushTransform(Matrix.CreateScale(frame.Scale, frame.Scale) * Matrix.CreateRotation(n.Angle * Math.PI / 180) * Matrix.CreateTranslation(n.X, n.Y)))
            {
                using (context.PushTransform(Matrix.CreateTranslation(0, 1))) context.DrawGeometry(NeedleShade, null, NeedleShape);
                context.DrawGeometry(greens[n.Shade], null, NeedleShape);
            }
        }
        foreach (var b in berries) using (context.PushTransform(Matrix.CreateScale(frame.Scale, frame.Scale) * Matrix.CreateTranslation(b.X, b.Y))) context.DrawGeometry(BerryBrush, null, BerryShape);
    }

    private static readonly Dictionary<Color, (IBrush Glass, IBrush Glow, IBrush Halo)> BulbBrushes = new();
    private static (IBrush Glass, IBrush Glow, IBrush Halo) BrushesFor(Color c)
    {
        if (BulbBrushes.TryGetValue(c, out var found)) return found;
        var glass = new RadialGradientBrush
        {
            Center = new RelativePoint(.35, .28, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.35, .28, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("75%"), RadiusY = RelativeScalar.Parse("75%"),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(140, 255, 255, 255), .18), new GradientStop(c, .52), new GradientStop(c, 1) }
        };
        var made = (glass, (IBrush)new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B)), (IBrush)new SolidColorBrush(Color.FromArgb(44, c.R, c.G, c.B)));
        BulbBrushes[c] = made; return made;
    }
    private static readonly IBrush Socket = new SolidColorBrush(Color.Parse("#3B4A3F"));
    private static readonly Geometry GlassShape = new EllipseGeometry(new Rect(-4, 3, 8, 10));

    private void DrawLights(DrawingContext context, Size size, Rect card)
    {
        var seconds = FixedSeconds ?? Clock.Elapsed.TotalSeconds; var steady = Motion.Reduced;
        var bulbs = BulbsFor(size, state, frame);
        for (var i = 0; i < bulbs.Count; i++)
        {
            var b = bulbs[i]; var brushes = BrushesFor(b.Colour);
            var opacity = steady ? 1 : Twinkle(state, i, seconds);
            using (context.PushOpacity(opacity))
            using (context.PushTransform(Matrix.CreateScale(frame.Scale, frame.Scale) * Matrix.CreateRotation(b.Rotation * Math.PI / 180) * Matrix.CreateTranslation(b.X, b.Y)))
            {
                context.DrawEllipse(brushes.Halo, null, new Point(0, 8), 12, 12);
                context.DrawEllipse(brushes.Glow, null, new Point(0, 8), 7, 8);
                context.DrawRectangle(Socket, null, new Rect(-2, 0, 4, 3), 1, 1);
                context.DrawGeometry(brushes.Glass, null, GlassShape);
            }
        }
        var bow = BowFor(size, PillWindow.PillWidth * frame.Scale - card.Width > 1, frame);
        using (context.PushTransform(Matrix.CreateScale(frame.Scale, frame.Scale) * Matrix.CreateTranslation(bow.X, bow.Y)))
        {
            context.DrawGeometry(BowRed, null, BowLoops);
            context.DrawGeometry(null, BowCrease, BowCreases);
            context.DrawGeometry(BowDark, null, BowTails);
            context.DrawEllipse(BowKnot, null, new Point(23, 18), 4.2, 4.2);
            context.DrawEllipse(BowShine, null, new Point(21.8, 16.8), 1.2, 1.2);
        }
    }
}
