using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Timer.Desktop.Tests;

/// <summary>
/// Looks for the two faults a narrower page invites: things that run past the edge of the card (or window) that holds them, or are squeezed (words cut off with no ellipsis
/// or marquee to say so), and neighbours in one row that overlap each other. It reads the laid-out controls, so it needs a rendered window.
/// </summary>
internal static class LayoutAudit
{
    public static List<string> Run(Window window)
    {
        var faults = new List<string>();
        var surface = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DashboardSurface");
        var all = surface.GetVisualDescendants().OfType<Control>().Where(c => Shown(c, window)).ToList(); // Inside the card only: the window round it holds its shadow's room.
        foreach (var c in all)
        {
            var box = BoundsIn(c, surface); if (box is null || box.Value.Width < 1 || box.Value.Height < 1) continue;
            if (InsideSideways(c, window)) continue;
            if (box.Value.Right > surface.Bounds.Width + 1 || box.Value.X < -1) faults.Add($"runs past the window: {Describe(c)} {box}");
            var card = c.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("dayCard") && b != c);
            if (card is not null && BoundsIn(card, surface) is { } cardBox && !(c is Border b2 && b2 == card))
                if (box.Value.Right > cardBox.Right + 1) faults.Add($"runs past its card: {Describe(c)} ends {box.Value.Right:0} in a card ending {cardBox.Right:0}");
            if (c is TextBlock t && !string.IsNullOrEmpty(t.Text) && t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap && t.TextTrimming == Avalonia.Media.TextTrimming.None
                && t.Bounds.Width > 0 && t.DesiredSize.Width > t.Bounds.Width + 1.5 && !t.GetVisualAncestors().Any(a => a is MarqueeText))
                faults.Add($"words squeezed: {Describe(c)} '{t.Text}' wants {t.DesiredSize.Width:0} has {t.Bounds.Width:0}");
        }
        foreach (var panel in all.OfType<StackPanel>().Where(p => p.Orientation == Orientation.Horizontal))
            Neighbours(panel, panel.Children.Where(ch => Shown(ch, window) && ch is not Popup).ToList(), surface, faults);
        foreach (var grid in all.OfType<Grid>().Where(g => g.Children.Select(Grid.GetColumn).Distinct().Count() > 1)) // Not ColumnDefinitions: reading it creates the list on a grid that has none, which breaks that grid's next arrange.
        {
            var cells = grid.Children.Where(ch => Shown(ch, window) && ch is not Popup && Grid.GetColumnSpan(ch) == 1 && Grid.GetRowSpan(ch) == 1).ToList();
            foreach (var group in cells.GroupBy(ch => Grid.GetRow(ch))) Neighbours(grid, group.ToList(), surface, faults);
        }
        return faults.Distinct().ToList();
    }

    private static void Neighbours(Control parent, List<Control> items, Control surface, List<string> faults)
    {
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
            {
                if (BoundsIn(items[i], surface) is not { } a || BoundsIn(items[j], surface) is not { } b) continue;
                if (a.Width < 1 || b.Width < 1 || a.Height < 1 || b.Height < 1) continue;
                var overlap = a.Intersect(b);
                if (overlap.Width > 1.5 && overlap.Height > 1.5) faults.Add($"overlap in {Describe(parent)}: {Describe(items[i])} and {Describe(items[j])} by {overlap.Width:0}x{overlap.Height:0}");
            }
    }

    private static bool Shown(Control c, Window window) => c.IsVisible && c.IsEffectivelyVisible && c.Opacity > .01;
    private static Rect? BoundsIn(Control c, Visual to) { var p = c.TranslatePoint(default, to); return p is null ? null : new Rect(p.Value, c.Bounds.Size); }
    // Things that are meant to run on: the timeline's long canvas inside its own scroll, a row of chips that scrolls sideways.
    private static bool InsideSideways(Control c, Window w) => c.GetVisualAncestors().Any(a => a is ScrollViewer s && s.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)
        || c is Popup || c.GetVisualAncestors().Any(a => a is ComboBox || a is Popup) // (a combo box's drop-down glyph is measured at the popup's size)
        || c.GetVisualAncestors().Any(a => a.GetType().Name is "ContextMenu" or "ToolTip");
    private static string Describe(Visual v) => v is Control c ? $"{c.GetType().Name}{(string.IsNullOrEmpty(c.Name) ? "" : " " + c.Name)}" : v.GetType().Name;
}
