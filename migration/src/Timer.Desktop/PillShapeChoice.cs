using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The choice between the vertical pill and the horizontal one, as two tiles with a small picture of each, the same tiles in Getting started and in Settings,
/// Appearance. Choosing one applies at once, so the pill turns into that shape where it stands.
/// </summary>
public static class PillShapeChoice
{
    /// <summary>Builds the two tiles, named <c>{prefix}Vertical</c> and <c>{prefix}Horizontal</c>, and returns them with the call that keeps the chosen one marked.</summary>
    public static (Control Tiles, Action Sync) Build(TimerModel model, string prefix, bool compact = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
        var tiles = new Dictionary<bool, Button>();
        foreach (var (horizontal, words, about) in new[]
        {
            (false, "Vertical", "Tall and narrow, with the account list sliding out beside it."),
            (true, "Horizontal", "Short and wide, with the account's name and a drop-down.")
        })
        {
            var tile = new Button { Name = prefix + words, Width = compact ? 200 : 250, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10) };
            tile.Classes.Add("choiceTile"); tile.Classes.Add("themeTile");
            var inner = new StackPanel { Spacing = 0 };
            inner.Children.Add(Picture(horizontal, 92));
            inner.Children.Add(new TextBlock { Text = words, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, compact ? 8 : 10, 0, 0) });
            if (!compact) { var detail = new TextBlock { Text = about, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) }; detail.Classes.Add("muted"); inner.Children.Add(detail); }
            tile.Content = inner; tile.Click += (_, _) => model.SetPillHorizontal(horizontal);
            Avalonia.Automation.AutomationProperties.SetName(tile, words + " pill");
            tiles[horizontal] = tile; row.Children.Add(tile);
        }
        void Sync() { foreach (var (horizontal, tile) in tiles) tile.Classes.Set("selected", horizontal == model.PillHorizontal); }
        Sync();
        return (row, Sync);
    }

    // A small picture of the pill in its shape: the card, the lights, the clock as bars, the play tile and the account on one well, and the break.
    private static Control Picture(bool horizontal, double height)
    {
        Border Part(double w, double h, double radius, string colour, double alpha) => new() { Width = w, Height = h, CornerRadius = new CornerRadius(radius), Background = new SolidColorBrush(Color.Parse(colour), alpha) };
        Control inside;
        if (horizontal)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            var lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            lights.Children.Add(Part(3, 8, 1.5, "#7A849C", .8)); lights.Children.Add(Part(3, 8, 1.5, "#7A849C", .35));
            var well = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Background = new SolidColorBrush(Color.Parse("#7A849C"), .18), VerticalAlignment = VerticalAlignment.Center };
            var parts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var selector = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(6), Child = Part(22, 5, 2.5, "#7A849C", .75) };
            ((Border)selector.Child!).HorizontalAlignment = HorizontalAlignment.Left; ((Border)selector.Child!).VerticalAlignment = VerticalAlignment.Center; ((Border)selector.Child!).Margin = new Thickness(8, 0, 0, 0);
            parts.Children.Add(Part(20, 20, 6, "#34D399", .45)); parts.Children.Add(selector); well.Child = parts;
            row.Children.Add(lights); row.Children.Add(Part(24, 9, 3, "#7A849C", .75)); row.Children.Add(well); row.Children.Add(Part(20, 20, 6, "#F59A1B", .4));
            inside = new Border { Width = 164, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1), Child = row, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            var col = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) };
            var lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            lights.Children.Add(Part(3, 7, 1.5, "#7A849C", .8)); lights.Children.Add(Part(3, 7, 1.5, "#7A849C", .35));
            var well = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Background = new SolidColorBrush(Color.Parse("#7A849C"), .18), HorizontalAlignment = HorizontalAlignment.Center };
            var parts = new StackPanel { Spacing = 2 }; parts.Children.Add(Part(20, 13, 5, "#34D399", .45)); parts.Children.Add(Part(20, 13, 5, "#7A849C", .0));
            var bars = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            foreach (var c in new[] { "#7F98F7", "#5FD6AE", "#FF9F84" }) bars.Children.Add(Part(10, 2.5, 1.25, c, 1));
            var wellGrid = new Grid { Children = { parts } }; bars.Margin = new Thickness(0, 15, 0, 0); bars.VerticalAlignment = VerticalAlignment.Top; wellGrid.Children.Add(bars); well.Child = wellGrid;
            col.Children.Add(lights); col.Children.Add(Part(14, 4, 2, "#7A849C", .75)); col.Children.Add(Part(14, 4, 2, "#7A849C", .75)); col.Children.Add(well); col.Children.Add(Part(20, 13, 5, "#F59A1B", .4));
            inside = new Border { Width = 40, Height = height - 8, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Child = col, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        var card = (Border)inside; card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Line")); card.Bind(Border.BackgroundProperty, card.GetResourceObservable("PillBody"));
        var frame = new Border { Height = height, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderThickness = new Thickness(1), Child = inside };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Line")); frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Hover"));
        return frame;
    }
}
