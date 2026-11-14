using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The display look for the timeline and the week (the board "The timeline as a display", option A): the pill's vacuum-fluorescent glass carried into the
/// dashboard's dark trough. It is how the timeline is always drawn; the original plain look was retired.
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
    /// <summary>In E-ink, a paper plate drawn behind the words (so they stay readable over a texture); none in Phosphor.</summary>
    public Color? Plate { get; set; }
    /// <summary>How much heavier than the name's bars the strokes are (block words are a little heavier, so the ink reads).</summary>
    public double Weight { get; set; } = 1;
    public static double CellWidth(double height) => height * 19 / 28;
    /// <summary>The width a run of <paramref name="cells"/> takes at <paramref name="height"/>, its slant included. In E-ink a cell is a figure of the type for that height.</summary>
    public static double WidthOf(int cells, double height) => DisplayInk.On ? cells * InkType.Width("0", DisplaySkin.RoleFor(height)) : cells * CellWidth(height) + height * SegmentName.Slant;
    /// <summary>Whether <paramref name="text"/> fits in <paramref name="room"/> at <paramref name="height"/>.</summary>
    public static bool Fits(string text, double height, double room) => TextWidth(text, height) <= room;
    /// <summary>The width <paramref name="text"/> takes at <paramref name="height"/>: its cells in Phosphor, its type in E-ink.</summary>
    public static double TextWidth(string text, double height) => DisplayInk.On ? InkType.Width(text, DisplaySkin.RoleFor(height)) : WidthOf(text.Length, height);
    protected override Size MeasureOverride(Size availableSize) => new(TextWidth(Text, CellHeight), CellHeight);

    public override void Render(DrawingContext context)
    {
        if (Plate is { } paper && DisplayInk.On && !string.IsNullOrEmpty(Text))
        {
            var width = TextWidth(Text, CellHeight); var plate = new Rect(-4, -2, width + 8, CellHeight + 4);
            context.DrawRectangle(new SolidColorBrush(paper), new Pen(new SolidColorBrush(SoftTint.WithAlpha(Colour, .35)), 1), plate, 4, 4);
        }
        Draw(context, Text, 0, 0, CellHeight, Colour, Ghosts, Weight);
    }
    /// <summary>Draws <paramref name="text"/> in segment cells with its top left at (<paramref name="x"/>, <paramref name="y"/>); returns its width. Used by drawn panels too.</summary>
    public static double Draw(DrawingContext context, string text, double x, double y, double h, Color colour, bool ghosts = false, double weight = 1)
    {
        if (DisplayInk.On)
        {
            // E-ink: the same words in normal type, tabular figures, with a little ink speckle; the weight asks for a heavier face.
            var role = DisplaySkin.RoleFor(h); if (weight > 1.1 && role.Weight < FontWeight.Bold) role = role with { Weight = FontWeight.Bold };
            return InkType.Draw(context, text, role, new SolidColorBrush(colour), x, y + h / 2 + role.Size * .365);
        }
        var cw = CellWidth(h); var gw = h * 13.4 / 28; var th = Math.Max(1.1, SegmentName.Thickness * h / 20) * weight;
        var ins = th / 2; var inner = h - 2 * ins; var trimMax = 1.1 * h / 20;
        var lit = new Pen(new SolidColorBrush(colour), th, lineCap: PenLineCap.Round);
        var ghost = ghosts ? new Pen(new SolidColorBrush(SoftTint.WithAlpha(colour, .09)), th, lineCap: PenLineCap.Round) : null;
        text = text.ToUpperInvariant();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i]; var x0 = x + i * cw;
            Point P(double fx, double fy) { var yy = ins + fy * inner; return new Point(x0 + ins + fx * gw + (h - yy) * SegmentName.Slant, y + yy); }
            // A colon is two leaning dots, as the clock draws it, not the name's tall pair of bars.
            if (ch == ':') { foreach (var f in new[] { .3, .7 }) context.DrawEllipse(lit.Brush, null, P(.5, f), th * .62, th * .62); continue; }
            var on = SegmentName.Font.TryGetValue(ch, out var segments) ? segments : "";
            foreach (var (id, ax, ay, bx, by) in SegmentName.Bars)
            {
                var isOn = on.Contains(id); if (!isOn && ghost is null) continue;
                var a = P(ax, ay); var b = P(bx, by); var d = b - a; var len = Math.Sqrt(d.X * d.X + d.Y * d.Y); var trim = Math.Min(trimMax, len / 4); var u = d / len;
                context.DrawLine(isOn ? lit : ghost!, a + u * trim, b - u * trim);
            }
        }
        return text.Length * cw;
    }
}


/// <summary>Where a scrolled list is, drawn as the display draws it: one small cell a row down a narrow column, the rows on show lit in the ink and the rest unlit.
/// It draws nothing while the list fits, but keeps its column so the rows beside it never change width.</summary>
public sealed class ScrollCells : Avalonia.Controls.Control
{
    public const double ColumnWidth = 4;
    private readonly Avalonia.Controls.ScrollViewer scroll;
    public ScrollCells(Avalonia.Controls.ScrollViewer scroll)
    {
        this.scroll = scroll;
        scroll.ScrollChanged += (_, _) => InvalidateVisual();
        scroll.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.ScrollViewer.ExtentProperty || e.Property == Avalonia.Controls.ScrollViewer.ViewportProperty) InvalidateVisual(); };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    /// <summary>True while the list is longer than its window, so the cells show.</summary>
    public bool Showing => scroll.Extent.Height > scroll.Viewport.Height + .5;
    public override void Render(DrawingContext context)
    {
        if (!Showing || Bounds.Height < 4) return;
        var ink = Ui.ThemeColour(this, "Ink", Colors.White);
        var rows = Math.Max(1, (int)Math.Round(scroll.Extent.Height / AccountDisplayRow.RowHeight));
        // Small cells like the display's own, never long bars: a short list draws a short column from the top.
        var piece = Math.Min(8, Bounds.Height / rows); var gap = Math.Min(2.5, piece * .3);
        var first = scroll.Offset.Y / AccountDisplayRow.RowHeight; var shown = scroll.Viewport.Height / AccountDisplayRow.RowHeight;
        for (var k = 0; k < rows; k++)
        {
            var r = new Rect(0, k * piece, ColumnWidth, Math.Max(1.5, piece - gap));
            var on = k + .5 > first && k + .5 < first + shown;
            context.DrawRectangle(new SolidColorBrush(on ? ink : VfdLook.Ghost(ink)), null, r, 1, 1);
        }
    }
}
