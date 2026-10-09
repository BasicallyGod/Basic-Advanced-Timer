using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Timer.Core;

namespace Timer.Desktop;

public static class TimelineGestures
{
    public static void Attach(Border card, Canvas canvas, TimerModel model, SessionRow row, double pixelsPerHour)
    {
        Point? origin = null;
        var edge = 0;
        var delta = TimeSpan.Zero;
        var oldTop = Canvas.GetTop(card); var oldHeight = card.Height;
        var dragging = false;
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            origin = e.GetPosition(canvas); delta = TimeSpan.Zero; dragging = false;
            oldTop = Canvas.GetTop(card); oldHeight = card.Height;
            var y = e.GetPosition(card).Y;
            edge = oldHeight >= 24 ? y < 8 ? -1 : y > oldHeight - 8 ? 1 : 0 : 0;
            e.Handled = true; e.Pointer.Capture(card);
        };
        card.PointerMoved += (_, e) =>
        {
            if (origin is not { } start) return;
            var moved = e.GetPosition(canvas) - start;
            if (!dragging && Math.Abs(moved.Y) < 5) return;
            dragging = true;
            delta = TimeSpan.FromMinutes(Math.Round(moved.Y / pixelsPerHour * 60 / 5) * 5);
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            if (from < row.Day || until > row.Day.AddDays(1) || until <= from) return;
            Canvas.SetTop(card, oldTop + (edge <= 0 ? delta.TotalHours * pixelsPerHour : 0));
            card.Height = Math.Max(3, (until - from).TotalHours * pixelsPerHour - 4);
            card.Opacity = .75;
            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            if (origin is null) return;
            var commit = dragging && delta != TimeSpan.Zero;
            var change = delta; var mode = edge;
            origin = null; dragging = false; e.Pointer.Capture(null); card.Opacity = 1;
            Canvas.SetTop(card, oldTop); card.Height = oldHeight;
            if (commit) model.EditEntry(row, row.Account, row.From + (mode <= 0 ? change : TimeSpan.Zero),
                row.To + (mode >= 0 ? change : TimeSpan.Zero), model.Editor.Note(row));
            e.Handled = true;
        };
        card.PointerCaptureLost += (_, _) =>
        { origin = null; dragging = false; card.Opacity = 1; Canvas.SetTop(card, oldTop); card.Height = oldHeight; };
        card.Cursor = new Cursor(StandardCursorType.SizeAll);
    }
}
