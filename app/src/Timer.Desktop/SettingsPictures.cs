using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The small drawings on the settings page. A picture is used where a choice changes how something looks: the pill (full or compact, with or without an alert count
/// and card) and a card with and without a shadow. The pill shares the real glyphs and geometry with fixed sample state; no picture owns a tracking model.
/// </summary>
internal static class SettingsPictures
{
    private static void Fill(Shape shape, string key) => shape.Bind(Shape.FillProperty, shape.GetResourceObservable(key));
    private static void Fill(Border border, string key) => border.Bind(Border.BackgroundProperty, border.GetResourceObservable(key));
    private static void Edge(Border border, string key) { border.BorderThickness = new Thickness(1); border.Bind(Border.BorderBrushProperty, border.GetResourceObservable(key)); }

    /// <summary>A fixed sample of the current display; compact keeps the keys and omits the account name.</summary>
    public static Control Pill(bool full, int? alerts, double scale, bool tall = false) => new PillPicture(full, alerts, scale, tall);

    /// <summary>The pill with the card a new alert opens under it (Peek), with only the count (Light only) or with nothing (Off).</summary>
    public static Control Alerts(string mode, double scale)
    {
        var stack = new StackPanel { Spacing = 5 * scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Pill(true, mode == "off" ? null : 3, scale));
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

}
