using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>
/// The pieces that continue the pill's display to the right of the clock (the 1980s hi-fi look): a dotted divider, 7 by 7 dot-matrix glyphs for play, pause and the
/// break cup, and a sixteen-segment account name. They are drawn by hand, in whole pixels, and their motion runs on a timer that exists only while something is moving.
/// </summary>
public sealed class EtchedDivider : Control
{
    /// <summary>Every divider in every display is this: an etched mark 2 wide, a dark groove with a pale lip beside it, 28 long when it stands upright.</summary>
    public const double ColumnWidth = 2, ColumnHeight = 28;
    private readonly bool vertical;
    /// <summary>An upright mark, or (with a length) the flat rule between the two rows of the sidebar's display.</summary>
    public EtchedDivider(bool vertical = true, double length = ColumnHeight)
    {
        this.vertical = vertical; IsHitTestVisible = false;
        if (vertical) { Width = ColumnWidth; Height = length; } else { Width = length; Height = ColumnWidth; }
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        // A cut in the display's glass: the groove is the text colour at a low strength and the lip beside it the display's own light edge, so it reads as etched, not drawn.
        var ink = Ui.ThemeColour(this, "Ink", Colors.Gray); var lip = Ui.ThemeColour(this, "DisplayEdge", Colors.White);
        var groove = new SolidColorBrush(SoftTint.WithAlpha(ink, .2)); var edge = new SolidColorBrush(SoftTint.WithAlpha(lip, Math.Max(lip.A / 255.0, .1)));
        if (vertical) { context.FillRectangle(groove, new Rect(0, 0, 1, Height)); context.FillRectangle(edge, new Rect(1, 0, 1, Height)); }
        else { context.FillRectangle(groove, new Rect(0, 0, Width, 1)); context.FillRectangle(edge, new Rect(0, 1, Width, 1)); }
    }
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

/// <summary>A grid of round dots, each with its own brightness: unlit ones faint, lit ones in the glyph's colour with, on a dark theme, a soft glow round them.</summary>
public abstract class DotMatrix : SteppedControl
{
    /// <summary>The distance between dot centres; the dots are a third of it in radius.</summary>
    public const double Pitch = 3.5;
    public int Columns { get; }
    public int Rows { get; }
    public Color Colour { get; set; } = Colors.Green;
    protected DotMatrix(int columns, int rows) { Columns = columns; Rows = rows; Width = columns * Pitch; Height = rows * Pitch; IsHitTestVisible = false; }
    /// <summary>How lit the dot at this row and column is, 0 to 1.</summary>
    public abstract double Level(int row, int col);
    public override void Render(DrawingContext context)
    {
        var ink = Ui.ThemeColour(this, "Ink", Colors.Gray); var dark = SoftTint.IsDark(this);
        var unlit = new SolidColorBrush(SoftTint.WithAlpha(ink, .1));
        var r = Pitch * .33;
        for (var row = 0; row < Rows; row++)
            for (var col = 0; col < Columns; col++)
            {
                var level = Math.Clamp(Level(row, col), 0, 1);
                var centre = new Point((col + .5) * Pitch, (row + .5) * Pitch);
                context.DrawEllipse(unlit, null, centre, r, r);
                // The glow the lit dots give off, as the mock draws it: a wider, fainter disc under the dot, on a dark theme only.
                if (level > 0 && dark) context.DrawEllipse(new SolidColorBrush(SoftTint.WithAlpha(Colour, .22 * level)), null, centre, r * 2.1, r * 2.1);
                if (level > 0) context.DrawEllipse(new SolidColorBrush(SoftTint.WithAlpha(Colour, level)), null, centre, r, r);
            }
    }
}

/// <summary>The play or pause symbol. A change wipes across column by column (40 ms a column, then a 140 ms fade), or swaps at once with less motion asked for.</summary>
public sealed class TransportGlyph : DotMatrix
{
    public const int Cells = 7;
    public TransportGlyph() : base(Cells, Cells) { }
    private static readonly string[] Play = { "1000000", "1110000", "1111100", "1111111", "1111100", "1110000", "1000000" };
    private static readonly string[] Pause = { "0110110", "0110110", "0110110", "0110110", "0110110", "0110110", "0110110" };
    public const double ColumnMilliseconds = 40, FadeMilliseconds = 140;
    private bool pause; private string[] from = Play; private double elapsed = double.PositiveInfinity;
    /// <summary>Whether the pause bars are showing (or on their way); otherwise the play triangle.</summary>
    public bool Paused => pause;
    public static double WipeMilliseconds => (Cells - 1) * ColumnMilliseconds + FadeMilliseconds;
    /// <summary>Shows the pause bars (true) or the play triangle (false), wiping between them unless <paramref name="instant"/>.</summary>
    public void Show(bool pauseBars, bool instant)
    {
        if (pauseBars == pause) return;
        from = pause ? Pause : Play; pause = pauseBars;
        elapsed = instant || Motion.Reduced || !this.IsAttachedToVisualTree() ? double.PositiveInfinity : 0;
        InvalidateVisual(); Wake();
    }
    protected override bool Animating => elapsed < WipeMilliseconds;
    protected override void Advance(double milliseconds) { if (elapsed < WipeMilliseconds) elapsed += milliseconds; }
    public override double Level(int row, int col)
    {
        var to = pause ? Pause : Play;
        var now = to[row][col] == '1' ? 1.0 : 0.0;
        if (!Animating) return now;
        var was = from[row][col] == '1' ? 1.0 : 0.0;
        var p = Math.Clamp((elapsed - col * ColumnMilliseconds) / FadeMilliseconds, 0, 1);
        return was + (now - was) * p;
    }
}

/// <summary>
/// The break mug, nine dots across and nine down: two wisps of steam, a blank row, then the mug with its handle. Its outline fills from the bottom up (70 ms a row) when a
/// break starts and empties from the top down when it ends. The steam is always drawn, dim and still at rest and bright and tilting together (left, upright, right,
/// upright, 330 ms a frame) while a break is on or the pointer is over the mug.
/// </summary>
public sealed class CupGlyph : DotMatrix
{
    public const int Size = 9, BodyTop = 3, BodyRows = 6;
    // The body: a rim, the walls, a handle on the right and the base.
    private static readonly string[] Cup = { "000000000", "000000000", "000000000", "111111000", "100001110", "100001001", "100001110", "100001000", "011110000" };
    private static readonly string[][] Steam = { new[] { "010010000", "001001000" }, new[] { "001001000", "001001000" }, new[] { "000100100", "001001000" }, new[] { "001001000", "001001000" } };
    public const double RowMilliseconds = 70, SteamMilliseconds = 330;
    public CupGlyph() : base(Size, Size) { }
    private bool on, steaming; private double fill; private double steamClock;
    /// <summary>How many body rows are lit (0 to <see cref="BodyRows"/>), as of now.</summary>
    public double FilledRows => fill;
    /// <summary>The steam frame showing, 0 to 3 (1 while it stands still).</summary>
    public int SteamFrame => Animated ? (int)(steamClock / SteamMilliseconds) % Steam.Length : 1;
    public bool On => on;
    /// <summary>Whether the steam is moving (on a break or under the pointer, with motion allowed).</summary>
    public bool Steaming => steaming;
    private bool Animated => steaming && !Motion.Reduced;
    /// <summary>Sets whether a break is on (the mug full) and whether the steam rises (on a break, or under the pointer).</summary>
    public void Show(bool breakOn, bool steam, bool instant)
    {
        on = breakOn; steaming = steam;
        if (instant || Motion.Reduced || !this.IsAttachedToVisualTree()) fill = on ? BodyRows : 0;
        InvalidateVisual(); Wake();
    }
    protected override bool Animating => (on && fill < BodyRows) || (!on && fill > 0) || Animated;
    protected override void Advance(double milliseconds)
    {
        var rows = milliseconds / RowMilliseconds;
        if (on) fill = Math.Min(BodyRows, fill + rows); else fill = Math.Max(0, fill - rows);
        if (Animated) steamClock += milliseconds;
    }
    public override double Level(int row, int col)
    {
        if (row >= BodyTop)
        {
            if (Cup[row][col] != '1') return 0;
            // Rows fill from the base up, and empty from the rim down; an empty mug is its outline in a dimmer tone.
            var rowsFromBottom = Size - 1 - row;
            var lit = Math.Clamp(fill - rowsFromBottom, 0, 1);
            return .5 + .5 * lit;
        }
        if (row > 1) return 0;
        // The steam is always there: dim and still at rest, bright and moving while it steams.
        return Steam[SteamFrame][row][col] == '1' ? (Animated ? .9 : on ? .9 : .5) : 0;
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
        for (var i = 0; i < Cells; i++)
        {
            var on = Font.TryGetValue(shown[i], out var segments) ? segments : "";
            DrawCell(context, i * CellWidth + 1, on, unlit, lit, glow);
        }
    }
    private static Point P(double x0, double fx, double fy)
    {
        const double inset = 1, w = GlyphWidth, h = CellHeight - 2 * inset;
        var y = inset + fy * h; var x = x0 + fx * w;
        return new Point(x + (CellHeight - y) * Slant, y);
    }
    private static void DrawCell(DrawingContext context, double x0, string on, Pen unlit, Pen lit, Pen? glow)
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
            if (isOn && glow is not null) context.DrawLine(glow, a + u * trim, b - u * trim);
            context.DrawLine(isOn ? lit : unlit, a + u * trim, b - u * trim);
        }
        var p = P(x0, 1.18, 1);
        context.DrawEllipse(on.Contains('p') ? lit.Brush : unlit.Brush, null, p, 1, 1);
    }
}
