using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The small drawings on the settings page. A picture is used only where a choice changes how something looks, so these are the pill (full or compact, with its
/// clock in segments or plain, with or without an alert count and card) and a card with and without a shadow. The pill is drawn at its real size from the
/// theme's own resources and then scaled down, so a picture is the thing itself and not an icon of it.
/// </summary>
internal static class SettingsPictures
{
    private static void Fill(Border border, string key) => border.Bind(Border.BackgroundProperty, border.GetResourceObservable(key));
    private static void Shadow(Border border, string key) => border.Bind(Border.BoxShadowProperty, border.GetResourceObservable(key));

    private static Control Scaled(Control child, double scale) => new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = child, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

    /// <summary>The recessed display of the pill: the state light, the day clock and, when asked for, the red alert count.</summary>
    private static Border Display(bool plain, int? alerts)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var led = new Border { Width = 6, Height = 14, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; Fill(led, "DisplayGroove");
        var clock = new SegmentClock { Plain = plain, Text = "04:45:00", HorizontalAlignment = HorizontalAlignment.Center };
        var count = new Border { Width = 24, Child = alerts is { } n ? Ui.Text(n.ToString("00"), 15, "overtime") : null, Margin = new Thickness(10, 0, 0, 0) };
        grid.Children.Add(led); Grid.SetColumn(clock, 1); grid.Children.Add(clock); Grid.SetColumn(count, 2); grid.Children.Add(count);
        var display = new Border { Width = StatusFace.DisplayWidth, Height = 52, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 0), Child = grid };
        Fill(display, "DisplayWell"); Shadow(display, "DisplayShade");
        return display;
    }

    private static Border Key(bool play)
    {
        var key = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), VerticalAlignment = VerticalAlignment.Center }; Fill(key, "Panel"); Shadow(key, "ShadowKey");
        if (play)
        {
            var triangle = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 0,0 L 11,6.5 L 0,13 Z"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            triangle.Bind(Shape.FillProperty, triangle.GetResourceObservable("Live")); key.Child = triangle;
        }
        else
        {
            var cup = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cup.Bind(Shape.FillProperty, cup.GetResourceObservable("Break")); key.Child = cup;
        }
        return key;
    }

    /// <summary>The pill at its real shape, scaled. <paramref name="keys"/> adds the play key and the break key (the full pill); without them it is the compact pill.</summary>
    public static Control Pill(bool keys, bool plain, int? alerts, double scale)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Display(plain, alerts));
        if (keys) { row.Children.Add(Key(true)); row.Children.Add(Key(false)); }
        var card = new Border { Height = 72, CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 0, 14, 0), Child = row };
        Fill(card, "PillBody"); Shadow(card, "ShadowKey");
        return Scaled(card, scale);
    }

    /// <summary>Only the clock display, for the choice between segments and plain numerals.</summary>
    public static Control Clock(bool plain, double scale) => Scaled(Display(plain, null), scale);

    /// <summary>The pill with the card a new alert opens under it (Peek), with only the count (Light only) or with nothing (Off).</summary>
    public static Control Alerts(string mode, double scale)
    {
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Pill(false, false, mode == "off" ? null : 3, 1));
        if (mode == "peek")
        {
            var line = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; Fill(line, "Line");
            var dot = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }; dot.Bind(Shape.FillProperty, dot.GetResourceObservable("Overtime"));
            var card = new Border { Height = 64, Width = 280, CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 0), Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { dot, SetColumn(line, 1) } } };
            Fill(card, "PillBody"); Shadow(card, "ShadowKey"); stack.Children.Add(card);
        }
        return Scaled(stack, scale);
    }
    private static T SetColumn<T>(T control, int column) where T : Control { Grid.SetColumn(control, column); return control; }

    /// <summary>A card standing off the page on a shadow, or lying flat with only a hairline.</summary>
    public static Control Shadows(bool on)
    {
        var card = new Border { Width = 70, Height = 34, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        Fill(card, "Panel");
        if (on) Shadow(card, "ShadowCard"); else { card.BorderThickness = new Thickness(1); card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Line")); }
        return card;
    }
}
