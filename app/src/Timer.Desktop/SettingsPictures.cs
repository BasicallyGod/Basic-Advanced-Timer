using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The small drawings on the settings page. A picture is used only where a choice changes how something looks: the pill (full or compact, with its clock in segments or
/// plain numerals, with or without an alert count and card) and a card with and without a shadow. Each is a pictogram drawn from plain shapes at its own size, as the theme
/// pictures are, and not the real pill scaled down: scaling the real controls (with their inset shadows) left stray marks in the corners of the frames.
/// </summary>
internal static class SettingsPictures
{
    private static void Fill(Shape shape, string key) => shape.Bind(Shape.FillProperty, shape.GetResourceObservable(key));
    private static void Fill(Border border, string key) => border.Bind(Border.BackgroundProperty, border.GetResourceObservable(key));
    private static void Shadow(Border border, string key) => border.Bind(Border.BoxShadowProperty, border.GetResourceObservable(key));
    private static void Edge(Border border, string key) { border.BorderThickness = new Thickness(1); border.Bind(Border.BorderBrushProperty, border.GetResourceObservable(key)); }

    // ---- a seven-segment digit, drawn from seven small bars (lit ones in the ink, the rest faint, as on the real display)
    private static readonly string[] Lit = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
    private static Control Digit(int digit, double h)
    {
        var w = h * 0.55; var t = Math.Max(1.2, h * 0.14); var canvas = new Canvas { Width = w, Height = h };
        void Bar(char name, double x, double y, double bw, double bh)
        {
            var on = Lit[digit].Contains(name);
            var bar = new Border { Width = bw, Height = bh, CornerRadius = new CornerRadius(t / 2), Opacity = on ? 1 : 0.1 }; Fill(bar, "Ink");
            Canvas.SetLeft(bar, x); Canvas.SetTop(bar, y); canvas.Children.Add(bar);
        }
        var mid = (h - t) / 2; var vh = (h - 3 * t) / 2 + t * 0.4;
        Bar('a', t * 0.5, 0, w - t, t); Bar('g', t * 0.5, mid, w - t, t); Bar('d', t * 0.5, h - t, w - t, t);
        Bar('f', 0, t * 0.8, t, vh); Bar('b', w - t, t * 0.8, t, vh); Bar('e', 0, mid + t * 0.6, t, vh); Bar('c', w - t, mid + t * 0.6, t, vh);
        return canvas;
    }
    private static Control Colon(double h)
    {
        var s = Math.Max(1.5, h * 0.14); var stack = new StackPanel { Spacing = h * 0.22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(h * 0.1, 0) };
        for (var i = 0; i < 2; i++) { var dot = new Ellipse { Width = s, Height = s }; Fill(dot, "Ink"); stack.Children.Add(dot); }
        return stack;
    }
    /// <summary>The clock for a picture: four digits and a colon, as segments or as plain numerals.</summary>
    private static Control ClockFace(bool plain, double k)
    {
        if (plain) { var text = Ui.Text("04:45", 11 * k, "clock"); text.FontWeight = FontWeight.SemiBold; text.VerticalAlignment = VerticalAlignment.Center; text.HorizontalAlignment = HorizontalAlignment.Center; return text; }
        var h = 11 * k; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = h * 0.14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(Digit(0, h)); row.Children.Add(Digit(4, h)); row.Children.Add(Colon(h)); row.Children.Add(Digit(4, h)); row.Children.Add(Digit(5, h));
        return row;
    }

    /// <summary>The pill's recessed display: the state light, the clock and, when asked for, the red count.</summary>
    private static Border Display(bool plain, bool count, double k)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var led = new Border { Width = 3 * k, Height = 7 * k, CornerRadius = new CornerRadius(1.5 * k), VerticalAlignment = VerticalAlignment.Center }; Fill(led, "DisplayGroove");
        var clock = ClockFace(plain, k); Grid.SetColumn(clock, 1);
        var red = new Border { Width = 9 * k, Height = 9 * k, VerticalAlignment = VerticalAlignment.Center };
        if (count) { var t = Ui.Text("03", 7 * k, "overtime"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; red.Child = t; }
        Grid.SetColumn(red, 2);
        grid.Children.Add(led); grid.Children.Add(clock); grid.Children.Add(red);
        var display = new Border { Width = 78 * k, Height = 20 * k, CornerRadius = new CornerRadius(6 * k), Padding = new Thickness(4 * k, 0), Child = grid };
        Fill(display, "DisplayWell"); Edge(display, "Line");
        return display;
    }

    private static Control Key(bool play, double k)
    {
        var key = new Border { Width = 18 * k, Height = 18 * k, CornerRadius = new CornerRadius(9 * k), VerticalAlignment = VerticalAlignment.Center }; Fill(key, "Panel"); Edge(key, "Line");
        if (play)
        {
            var triangle = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 0,0 L 1,0.58 L 0,1.16 Z"), Stretch = Stretch.Fill, Width = 5.5 * k, Height = 6.4 * k, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Fill(triangle, "Live"); key.Child = triangle;
        }
        else
        {
            var cup = new Ellipse { Width = 6 * k, Height = 6 * k, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; Fill(cup, "Break"); key.Child = cup;
        }
        return key;
    }

    /// <summary>The pill as a pictogram. <paramref name="keys"/> adds the play key and the break key (the full pill); without them it is the compact pill. <paramref name="scale"/> is the size
    /// of the whole drawing, 1 being about 135 pixels wide for the full pill.</summary>
    public static Control Pill(bool keys, bool plain, int? alerts, double scale)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 * scale, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Display(plain, alerts is not null, scale));
        if (keys) { row.Children.Add(Key(true, scale)); row.Children.Add(Key(false, scale)); }
        var card = new Border { Height = 28 * scale, CornerRadius = new CornerRadius(9 * scale), Padding = new Thickness(5 * scale, 0), Child = row, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Fill(card, "PillBody"); Edge(card, "Line");
        return card;
    }

    /// <summary>Only the clock display, for the choice between segments and plain numerals.</summary>
    public static Control Clock(bool plain, double scale)
    {
        var display = Display(plain, false, scale * 1.3); display.HorizontalAlignment = HorizontalAlignment.Center; display.VerticalAlignment = VerticalAlignment.Center; return display;
    }

    /// <summary>The pill with the card a new alert opens under it (Peek), with only the count (Light only) or with nothing (Off).</summary>
    public static Control Alerts(string mode, double scale)
    {
        var stack = new StackPanel { Spacing = 5 * scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Pill(false, false, mode == "off" ? null : 3, scale));
        if (mode == "peek")
        {
            var dot = new Ellipse { Width = 6 * scale, Height = 6 * scale, VerticalAlignment = VerticalAlignment.Center }; Fill(dot, "Overtime");
            var line = new Border { Height = 4 * scale, Width = 62 * scale, CornerRadius = new CornerRadius(2 * scale), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6 * scale, 0, 0, 0) }; Fill(line, "Line");
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(dot); row.Children.Add(line);
            var card = new Border { Height = 20 * scale, Width = 100 * scale, CornerRadius = new CornerRadius(8 * scale), Padding = new Thickness(9 * scale, 0), Child = row };
            Fill(card, "PillBody"); Edge(card, "Line"); stack.Children.Add(card);
        }
        return stack;
    }

    /// <summary>A card standing off the page on a shadow, or lying flat with only a hairline.</summary>
    public static Control Shadows(bool on)
    {
        var card = new Border { Width = 70, Height = 34, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        Fill(card, "Panel");
        if (on) Shadow(card, "ShadowCard"); else Edge(card, "Line");
        return card;
    }
}
