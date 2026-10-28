using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>
/// Dragging a planned meeting on the timeline: the middle moves it, an edge resizes it, and a click that goes nowhere still opens it for editing. Meetings are only
/// plans, so unlike recorded blocks they never push or refuse on a neighbour (they may overlap and stack in lanes), and every move is to the five minute grid.
/// </summary>
public static class MeetingGestures
{
    public const int Step = 5;
    private static readonly TimeSpan Last = new(23, 59, 0), Shortest = TimeSpan.FromMinutes(Step);

    /// <summary>
    /// Where a meeting from <paramref name="start"/> to <paramref name="end"/> ends up when the pointer has moved <paramref name="movedMinutes"/>: the edge it grabbed (-1 the
    /// start, 1 the end, 0 the whole meeting) lands on the nearest five minutes, never before midnight or after 23:59, and a meeting is never shorter than five minutes. A
    /// move of less than half a step changes nothing, so a meeting that sits off the grid is not nudged by a click that wobbled.
    /// </summary>
    public static (TimeSpan Start, TimeSpan End) Resolve(TimeSpan start, TimeSpan end, int edge, double movedMinutes)
    {
        if (Math.Abs(movedMinutes) < Step / 2.0) return (start, end);
        static TimeSpan Grid(TimeSpan t) => TimeSpan.FromMinutes(Math.Round(t.TotalMinutes / Step) * Step);
        static TimeSpan Clamp(TimeSpan t, TimeSpan low, TimeSpan high) => t < low ? low : t > high ? high : t;
        var delta = TimeSpan.FromMinutes(movedMinutes);
        if (edge < 0) return (Clamp(Grid(start + delta), TimeSpan.Zero, end - Shortest), end);
        if (edge > 0) return (start, Clamp(Grid(end + delta), start + Shortest, Last));
        var length = end - start; var moved = Clamp(Grid(start + delta), TimeSpan.Zero, Last - length);
        return (moved, moved + length);
    }

    private static string Hm(TimeSpan t) => t.ToString(@"hh\:mm");

    public static void Attach(Grid card, Canvas canvas, TimerModel model, DayNote meeting, double pixelsPerHour, TextBlock? span)
    {
        Point? origin = null; var edge = 0; var dragging = false;
        var start = meeting.At!.Value; var end = meeting.End!.Value;
        var oldLeft = Canvas.GetLeft(card); var oldWidth = card.Width; var oldSpan = span?.Text;
        (TimeSpan Start, TimeSpan End) now = (start, end);
        void Restore() { Canvas.SetLeft(card, oldLeft); card.Width = oldWidth; card.Opacity = 1; if (span is not null) span.Text = oldSpan; }
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            origin = e.GetPosition(canvas); dragging = false; now = (start, end);
            oldLeft = Canvas.GetLeft(card); oldWidth = card.Width; oldSpan = span?.Text;
            edge = TimelineGestures.EdgeAt(e.GetPosition(card).X, card.Width);
            e.Handled = true; e.Pointer.Capture(card);
        };
        card.PointerMoved += (_, e) =>
        {
            if (origin is not { } from) { card.Cursor = new Cursor(TimelineGestures.EdgeAt(e.GetPosition(card).X, card.Width) == 0 ? StandardCursorType.SizeAll : StandardCursorType.SizeWestEast); return; }
            var moved = e.GetPosition(canvas).X - from.X;
            if (!dragging && Math.Abs(moved) < 5) return;
            dragging = true;
            now = Resolve(start, end, edge, moved / pixelsPerHour * 60);
            Canvas.SetLeft(card, ReviewViews.TimelineGutter + now.Start.TotalHours * pixelsPerHour + 1);
            card.Width = Math.Max(24, (now.End - now.Start).TotalHours * pixelsPerHour - 2); card.Opacity = .75;
            if (span is not null) span.Text = Hm(now.Start) + "–" + Hm(now.End);
            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            if (origin is null) return;
            var commit = dragging && now != (start, end); var plain = !dragging; var result = now;
            origin = null; dragging = false; e.Pointer.Capture(null); Restore();
            if (commit)
            {
                // A repeating meeting is moved for this day only; the series carries on as it was.
                var problem = model.Notes.MoveMeeting(meeting.Id, result.Start, result.End);
                if (problem is not null) model.Refuse(problem);
                else model.Tell((model.Notes.SeriesOf(meeting) is not null ? "Moved for this day only" : "Meeting moved") + " · " + Hm(result.Start) + "–" + Hm(result.End));
            }
            else if (plain && e.InitialPressMouseButton == MouseButton.Left) model.Notes.RequestEditMeeting(meeting.Id);
            e.Handled = true;
        };
        card.PointerCaptureLost += (_, _) => { if (origin is null) return; origin = null; dragging = false; Restore(); };
    }
}
