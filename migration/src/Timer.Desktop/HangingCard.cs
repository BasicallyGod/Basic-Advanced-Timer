using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>
/// A card that hangs from a small point under the button that opened it (the bell, the date), and the closing rule that goes with it: a press anywhere outside
/// the card closes it, and a press on its own button is left to that button, which closes it. Avalonia's own light dismiss closes the card on the press
/// and then lets the click on the button open it again, so a second click never closed it.
/// </summary>
public static class HangingCard
{
    private const double PointWidth = 20, PointHeight = 10.5, Seat = 9.5;
    /// <summary>
    /// The room round the card for its shadow, above it (the point sits in it) and at its sides. A pop-up's window clips what is drawn outside it, so the
    /// room has to hold the whole shadow (<see cref="Shadow"/>: 18 pixels of blur dropped 6, so 18 at the sides, 12 above and 24 below).
    /// </summary>
    public static readonly Thickness Room = new(20, 14, 20, 28);
    /// <summary>The card's shadow, the same soft one as the toast.</summary>
    public static readonly BoxShadows Shadow = BoxShadows.Parse("0 6 18 0 #29141E46");
    /// <summary>The vertical offset that puts the card's top edge 11 pixels under its button, whatever the room above it.</summary>
    public static double TopOffset => 2 + 9 - Room.Top;

    /// <summary>
    /// The card with a point on its top edge. The point is the card's own fill over the edge with only its two slanted sides drawn in the line colour, ending
    /// square on the middle of the card's one pixel edge, so the border runs into it without a notch. <paramref name="fromRight"/> is how far the point's centre
    /// is from the card's right edge, or negative to centre it.
    /// </summary>
    public static Grid Hang(Control card, double fromRight)
    {
        card.Margin = Room;
        var fill = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse($"M0,{Seat} L10,0.9 L20,{Seat} L20,{PointHeight} L0,{PointHeight} Z") };
        fill.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, fill.GetResourceObservable("Panel"));
        var edges = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse($"M0,{Seat} L10,0.9 L20,{Seat}"), StrokeThickness = 1.1, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Flat };
        edges.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, edges.GetResourceObservable("Line"));
        // The point's base sits on the card's top edge, which is Room.Top down from the top of the grid.
        var point = new Canvas { Name = "NotificationsPoint", Width = PointWidth, Height = PointHeight, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Children = { fill, edges }, Margin = new Thickness(0, Room.Top - Seat, 0, 0) };
        if (fromRight < 0) point.HorizontalAlignment = HorizontalAlignment.Center;
        else { point.HorizontalAlignment = HorizontalAlignment.Right; point.Margin = new Thickness(0, Room.Top - Seat, Room.Right + fromRight - PointWidth / 2, 0); }
        return new Grid { Children = { card, point } };
    }

    /// <summary>
    /// Makes the pop-up close on a press outside it, on Escape and when the window loses the focus, and leaves a press on <paramref name="owner"/> alone.
    /// </summary>
    public static void CloseOnOutsidePress(Popup popup, Control owner)
    {
        popup.IsLightDismissEnabled = false;
        var undo = new List<Action>();
        popup.Opened += (_, _) =>
        {
            if (TopLevel.GetTopLevel(owner) is not { } top) return;
            void Pressed(object? sender, PointerPressedEventArgs e)
            {
                if (e.Source is not Visual hit || hit == owner || owner.IsVisualAncestorOf(hit)) return;
                // Inside a pop-up of its own (the card's, or one opened from it, such as the reminder pop-up) is not outside.
                if (hit.GetVisualRoot() is { } root && !ReferenceEquals(root, top)) return;
                if (hit.FindAncestorOfType<OverlayPopupHost>() is not null) return;
                popup.IsOpen = false;
            }
            void Keys(object? sender, KeyEventArgs e) { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } }
            var press = top.AddDisposableHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            var keys = top.AddDisposableHandler(InputElement.KeyDownEvent, Keys, RoutingStrategies.Tunnel);
            undo.Add(press.Dispose); undo.Add(keys.Dispose);
            if (top is Window window) { void Away(object? s, EventArgs e) => popup.IsOpen = false; window.Deactivated += Away; undo.Add(() => window.Deactivated -= Away); }
        };
        popup.Closed += (_, _) => { foreach (var action in undo) action(); undo.Clear(); };
    }
}
