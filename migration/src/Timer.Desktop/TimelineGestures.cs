using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Timer.Core;
using Timer.Storage;

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
        var nudging = false;
        var previews = new Dictionary<Border, (double Top, double Height)>();
        void RestoreNeighbours()
        { foreach (var p in previews) { Canvas.SetTop(p.Key, p.Value.Top); p.Key.Height = p.Value.Height; p.Key.Opacity = 1; } previews.Clear(); }
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { model.OpenEntry(row); e.Handled = true; return; }
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
            delta = SnapDelta(model, row, edge, TimeSpan.FromHours(moved.Y / pixelsPerHour), pixelsPerHour, out nudging);
            RestoreNeighbours();
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            if (from < row.Day || until > row.Day.AddDays(1) || until <= from) return;
            Canvas.SetTop(card, oldTop + (edge <= 0 ? delta.TotalHours * pixelsPerHour : 0));
            card.Height = Math.Max(3, (until - from).TotalHours * pixelsPerHour - 4);
            card.Opacity = .75;
            if (nudging)
            {
                try
                {
                    foreach (var change in model.Editor.PlanTimeline(row, from, until, false, true))
                    {
                        foreach (var neighbour in canvas.Children.OfType<Border>().Where(b => ReferenceEquals(b.Tag, change.Source)))
                        {
                            previews[neighbour] = (Canvas.GetTop(neighbour), neighbour.Height);
                            Canvas.SetTop(neighbour, (change.Updated.From - row.Day).TotalHours * pixelsPerHour + 2); neighbour.Opacity = .75;
                        }
                    }
                }
                catch (ArgumentException) { } // Final validation reports a blocked insertion without changing the ledger.
            }
            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            if (origin is null) return;
            var commit = dragging && delta != TimeSpan.Zero;
            var change = delta; var mode = edge; var push = nudging; var select = !dragging;
            RestoreNeighbours();
            origin = null; dragging = false; e.Pointer.Capture(null); card.Opacity = 1;
            Canvas.SetTop(card, oldTop); card.Height = oldHeight;
            if (commit) model.DragEntry(row, row.From + (mode <= 0 ? change : TimeSpan.Zero),
                row.To + (mode >= 0 ? change : TimeSpan.Zero), push);
            else if (select) model.ToggleSelection(new[] { row });
            e.Handled = true;
        };
        card.PointerCaptureLost += (_, _) =>
        {
            // Context menus can release capture without a left-button gesture.
            if (origin is null) return;
            RestoreNeighbours();
            origin = null; dragging = false; card.Opacity = 1;
            Canvas.SetTop(card, oldTop); card.Height = oldHeight;
        };
        card.Cursor = new Cursor(StandardCursorType.SizeAll);
    }
    private static TimeSpan SnapDelta(TimerModel model, SessionRow row, int edge, TimeSpan requested, double pixelsPerHour, out bool nudge)
    {
        nudge = false;
        var occupied = model.Engine.Sessions.Where(s => !ReferenceEquals(s, row) && s.IsPositioned && s.Duration > TimeSpan.Zero)
            .Select(s => (Start: s.From, End: s.To)).Concat(model.Editor.Positions.Select(p => (Start: p.Start, End: p.End))).ToList();
        if (model.Engine.Active is { } active && model.Engine.Now > active.Start)
            occupied.Add((active.Start, model.Engine.Now));
        var grid = TimeSpan.FromMinutes(Math.Round(requested.TotalMinutes / 5) * 5);
        var candidates = new List<TimeSpan>();
        TimeSpan? lower = null, upper = null;
        foreach (var other in occupied)
        {
            // Starts are stored in whole seconds. Round up fractional legacy ends
            // so snapping never creates a small overlap when the entry is saved.
            if (edge <= 0)
            {
                var end = new DateTime(((other.End.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond, other.End.Kind);
                var boundary = end - row.From;
                candidates.Add(boundary);
                if (other.End <= row.From && (lower is null || boundary > lower.Value)) lower = boundary;
            }
            if (edge >= 0)
            {
                var boundary = other.Start - row.To;
                candidates.Add(boundary);
                if (other.Start >= row.To && (upper is null || boundary < upper.Value)) upper = boundary;
            }
        }
        // Clamp to the first neighbour crossed, even when the pointer travels
        // well beyond it. Apply this to grid rounding as well as raw movement.
        var overshoot = lower is { } l && requested < l ? (l - requested).TotalHours * pixelsPerHour
            : upper is { } u && requested > u ? (requested - u).TotalHours * pixelsPerHour : 0;
        if (overshoot > 24 && edge == 0) { nudge = true; return grid; }
        if (lower is { } minimum && (requested <= minimum || grid <= minimum)) requested = minimum;
        if (upper is { } maximum && (requested >= maximum || grid >= maximum)) requested = maximum;
        foreach (var candidate in candidates.OrderBy(c => Math.Abs((c - requested).TotalHours)))
        {
            if (Math.Abs((candidate - requested).TotalHours * pixelsPerHour) > 8) break;
            var from = row.From + (edge <= 0 ? candidate : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? candidate : TimeSpan.Zero);
            if (from < row.Day || until > row.Day.AddDays(1) || (until - from).TotalSeconds < 3) continue;
            var saved = LedgerEditor.TimedRow(row.Account, from, until, "edited");
            if (!occupied.Any(other => other.Start < saved.To && other.End > saved.From)) return candidate;
        }
        return grid;
    }

}
