using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The display look for the timeline and the week (the board "The timeline as a display", option A): the pill's vacuum-fluorescent glass carried into the
/// dashboard's dark trough. It is a setting (<see cref="TimerModel.DisplayTimeline"/>); with it off the trough is drawn as before.
/// </summary>
public static class VfdLook
{
    /// <summary>Words on a lit block: dark ink at about two-thirds strength, so they read clearly but stay part of the glass (solid black was too heavy).</summary>
    public static readonly Color BlockInk = Color.FromArgb(173, 8, 10, 14);
    /// <summary>The faint unlit track the band and empty slots are drawn with.</summary>
    public static Color Ghost(Color ink) => SoftTint.WithAlpha(ink, .07);
    /// <summary>The soft glow a lit block gives off into the mesh round it, after any ring it already wears.</summary>
    public static BoxShadow Glow(Color colour) => new() { Blur = 12, Spread = 0, Color = SoftTint.WithAlpha(colour, .45) };
    /// <summary>
    /// The ring of the block being recorded, drawn just inside its own edge: a dark gap and then a pale green line. A selection rings outside the block, so the two
    /// never meet however close the blocks are.
    /// </summary>
    public static BoxShadows RecordingRing(Color live) => new(
        new BoxShadow { IsInset = true, Spread = 4.5, Color = Color.Parse("#D6FFE4") },
        new[] { new BoxShadow { IsInset = true, Spread = 2, Color = Color.Parse("#0B0E13") }, Glow(live) });
}

/// <summary>
/// Words in sixteen-segment cells, slanted like the pill's account name, at any height (the name's cell scaled: 19 wide for 28 tall). Unlit segments can be drawn
/// faintly, as on the pill. It measures to exactly the cells it holds, so it lines up like a fixed-width face.
/// </summary>
public sealed class SegmentText : Control
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SegmentText, string>(nameof(Text), "");
    public static readonly StyledProperty<double> CellHeightProperty = AvaloniaProperty.Register<SegmentText, double>(nameof(CellHeight), 11);
    public static readonly StyledProperty<Color> ColourProperty = AvaloniaProperty.Register<SegmentText, Color>(nameof(Colour), Colors.White);
    static SegmentText() { AffectsMeasure<SegmentText>(TextProperty, CellHeightProperty); AffectsRender<SegmentText>(TextProperty, CellHeightProperty, ColourProperty); }
    public SegmentText() { IsHitTestVisible = false; }
    public SegmentText(string text, double cellHeight, Color colour, bool ghosts = false, double weight = 1) : this() { Text = text; CellHeight = cellHeight; Colour = colour; Ghosts = ghosts; Weight = weight; }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double CellHeight { get => GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }
    public Color Colour { get => GetValue(ColourProperty); set => SetValue(ColourProperty, value); }
    /// <summary>Whether the unlit segments are drawn faintly.</summary>
    public bool Ghosts { get; set; }
    /// <summary>How much heavier than the name's bars the strokes are (block words are a little heavier, so the ink reads).</summary>
    public double Weight { get; set; } = 1;
    public static double CellWidth(double height) => height * 19 / 28;
    /// <summary>The width a run of <paramref name="cells"/> takes at <paramref name="height"/>, its slant included.</summary>
    public static double WidthOf(int cells, double height) => cells * CellWidth(height) + height * SegmentName.Slant;
    /// <summary>Whether <paramref name="text"/> fits in <paramref name="room"/> at <paramref name="height"/>.</summary>
    public static bool Fits(string text, double height, double room) => WidthOf(text.Length, height) <= room;
    protected override Size MeasureOverride(Size availableSize) => new(WidthOf(Text.Length, CellHeight), CellHeight);

    private static readonly (char Id, double X1, double Y1, double X2, double Y2)[] Bars =
    {
        ('a', 0, 0, .5, 0), ('A', .5, 0, 1, 0), ('d', 0, 1, .5, 1), ('D', .5, 1, 1, 1), ('g', 0, .5, .5, .5), ('G', .5, .5, 1, .5),
        ('f', 0, 0, 0, .5), ('e', 0, .5, 0, 1), ('b', 1, 0, 1, .5), ('c', 1, .5, 1, 1), ('i', .5, 0, .5, .5), ('l', .5, .5, .5, 1),
        ('h', 0, 0, .5, .5), ('j', 1, 0, .5, .5), ('k', 0, 1, .5, .5), ('m', 1, 1, .5, .5),
    };
    public override void Render(DrawingContext context)
    {
        var h = CellHeight; var cw = CellWidth(h); var gw = h * 13.4 / 28; var th = Math.Max(1.1, SegmentName.Thickness * h / 20) * Weight;
        var ins = th / 2; var inner = h - 2 * ins; var trimMax = 1.1 * h / 20;
        var lit = new Pen(new SolidColorBrush(Colour), th, lineCap: PenLineCap.Round);
        var ghost = Ghosts ? new Pen(new SolidColorBrush(SoftTint.WithAlpha(Colour, .09)), th, lineCap: PenLineCap.Round) : null;
        var text = Text.ToUpperInvariant();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i]; var x0 = i * cw;
            Point P(double fx, double fy) { var yy = ins + fy * inner; return new Point(x0 + ins + fx * gw + (h - yy) * SegmentName.Slant, yy); }
            // A colon is two leaning dots, as the clock draws it, not the name's tall pair of bars.
            if (ch == ':') { foreach (var f in new[] { .3, .7 }) context.DrawEllipse(lit.Brush, null, P(.5, f), th * .62, th * .62); continue; }
            var on = SegmentName.Font.TryGetValue(ch, out var segments) ? segments : "";
            foreach (var (id, ax, ay, bx, by) in Bars)
            {
                var isOn = on.Contains(id); if (!isOn && ghost is null) continue;
                var a = P(ax, ay); var b = P(bx, by); var d = b - a; var len = Math.Sqrt(d.X * d.X + d.Y * d.Y); var trim = Math.Min(trimMax, len / 4); var u = d / len;
                context.DrawLine(isOn ? lit : ghost!, a + u * trim, b - u * trim);
            }
        }
    }
}

/// <summary>
/// The honeycomb laid over the whole trough: the thin dark lines between the cells, in the trough's own colour, drawn once over everything, so every block,
/// meeting and tile sits behind one continuous mesh that never drifts between them. It never takes a press.
/// </summary>
public sealed class HoneycombOverlay : Control
{
    public HoneycombOverlay() { IsHitTestVisible = false; }
    private static readonly Lazy<IBrush> Lines = new(() =>
    {
        // The same lattice as Honeycomb (the pill's mask), as the outlines of its cells: what the mask leaves dark is what this draws.
        double s = Honeycomb.Cell, w = Math.Sqrt(3) * s, h = 3 * s; var g = new StreamGeometry();
        using (var c = g.Open())
            foreach (var (cx, cy) in new[] { (w / 2, s), (0.0, 2.5 * s), (w, 2.5 * s), (0.0, -0.5 * s), (w, -0.5 * s) })
            {
                for (var i = 0; i < 6; i++)
                {
                    var a = Math.PI / 2 + i * Math.PI / 3; var pt = new Point(cx + s * Math.Cos(a), cy - s * Math.Sin(a));
                    if (i == 0) c.BeginFigure(pt, false); else c.LineTo(pt);
                }
                c.EndFigure(true);
            }
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 11, 14, 19)), s * (1 - Honeycomb.Inset) * 2.4);
        var drawing = new GeometryDrawing { Pen = pen, Geometry = g };
        var rect = new RelativeRect(0, 0, w, h, RelativeUnit.Absolute);
        return new DrawingBrush(drawing) { TileMode = TileMode.Tile, SourceRect = rect, DestinationRect = rect, Stretch = Stretch.Fill };
    });
    public override void Render(DrawingContext context) => context.FillRectangle(Lines.Value, new Rect(Bounds.Size));
}
