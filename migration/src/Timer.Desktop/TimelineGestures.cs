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
        var oldLeft = Canvas.GetLeft(card); var oldWidth = card.Width;
        var dragging = false;
        var nudging = false;
        var previews = new Dictionary<Border, (double Left, double Width)>();
        void RestoreNeighbours()
        { foreach (var p in previews) { Canvas.SetLeft(p.Key, p.Value.Left); p.Key.Width = p.Value.Width; p.Key.Opacity = 1; } previews.Clear(); }
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            origin = e.GetPosition(canvas); delta = TimeSpan.Zero; dragging = false;
            oldLeft = Canvas.GetLeft(card); oldWidth = card.Width;
            var x = e.GetPosition(card).X;
            edge = EdgeAt(x, oldWidth);
            e.Handled = true; e.Pointer.Capture(card);
        };
        card.PointerMoved += (_, e) =>
        {
            if (origin is not { } start)
            {
                // Show the resize cursor over an edge handle so short blocks are discoverable.
                card.Cursor = new Cursor(EdgeAt(e.GetPosition(card).X, card.Width) == 0 ? StandardCursorType.SizeAll : StandardCursorType.SizeWestEast);
                return;
            }
            var moved = e.GetPosition(canvas) - start;
            if (!dragging && Math.Abs(moved.X) < 5) return;
            dragging = true;
            delta = SnapDelta(model, row, edge, TimeSpan.FromHours(moved.X / pixelsPerHour), pixelsPerHour, out nudging);
            RestoreNeighbours();
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            Canvas.SetLeft(card, oldLeft + (edge <= 0 ? delta.TotalHours * pixelsPerHour : 0));
            card.Width = Math.Max(ReviewViews.MinimumBlockWidth, (until - from).TotalHours * pixelsPerHour - 2);
            card.Opacity = .75;
            if (nudging)
            {
                try
                {
                    foreach (var change in model.Editor.PlanTimeline(row, from, until, true))
                    {
                        foreach (var neighbour in canvas.Children.OfType<Border>().Where(b => ReferenceEquals(b.Tag, change.Source)))
                        {
                            previews[neighbour] = (Canvas.GetLeft(neighbour), neighbour.Width);
                            Canvas.SetLeft(neighbour, ReviewViews.TimelineGutter + (change.Updated.From - row.Day).TotalHours * pixelsPerHour + 1); neighbour.Opacity = .75;
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
            Canvas.SetLeft(card, oldLeft); card.Width = oldWidth;
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
            Canvas.SetLeft(card, oldLeft); card.Width = oldWidth;
        };
        card.Cursor = new Cursor(StandardCursorType.SizeAll);
    }
    /// <summary>Which edge a press at <paramref name="x"/> grabs, -1 the start (left), 1 the end (right), 0 for moving the whole block.</summary>
    public static int EdgeAt(double x, double width)
    {
        // Wide blocks keep 8px handles. Narrower ones share the width in thirds so the middle still
        // moves the block, and the very narrowest are split in halves because resizing matters more
        // than moving there (zooming in gives a block back its middle).
        var zone = width >= 24 ? 8 : width >= 10 ? width / 3 : width / 2;
        if (x < zone) return -1;
        if (x > width - zone) return 1;
        return 0;
    }
    /// <summary>Keeps a drag inside its day and above the minimum length, so the preview and the saved value always agree.</summary>
    public static TimeSpan ClampToDay(SessionRow row, int edge, TimeSpan delta)
    {
        var minimum = edge == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(1);
        var low = edge <= 0 ? row.Day - row.From : (row.From + minimum) - row.To;
        var high = edge >= 0 ? row.Day.AddDays(1) - row.To : (row.To - minimum) - row.From;
        return delta < low ? low : delta > high ? high : delta;
    }
    public static TimeSpan SnapDelta(TimerModel model, SessionRow row, int edge, TimeSpan requested, double pixelsPerHour, out bool nudge)
    {
        var snapped = Snap(model, row, edge, requested, pixelsPerHour, out nudge);
        return ClampToDay(row, edge, snapped);
    }
    private static TimeSpan Snap(TimerModel model, SessionRow row, int edge, TimeSpan requested, double pixelsPerHour, out bool nudge)
    {
        nudge = false;
        var occupied = model.Engine.Sessions.Where(s => !ReferenceEquals(s, row) && s.Duration > TimeSpan.Zero)
            .Select(s => (Start: s.From, End: s.To)).ToList();
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
                var boundary = LastDeltaBefore(row, edge, other.Start);
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
        // Nothing close enough to snap to. Never go past the nearest neighbour all the same.
        if (lower is { } floor && grid < floor) grid = floor;
        if (upper is { } ceiling && grid > ceiling) grid = ceiling;
        return grid;
    }
    /// <summary>
    /// The furthest this drag can go so that what is saved ends no later than <paramref name="limit"/>. A block's start is kept to the whole second and its
    /// length to hundredths of a minute, so ending it exactly at a moment with a fraction of a second, such as the start of the block being recorded,
    /// can round up past it and be refused. This steps back a second at a time until the saved block really fits.
    /// </summary>
    private static TimeSpan LastDeltaBefore(SessionRow row, int edge, DateTime limit)
    {
        var whole = new DateTime(limit.Ticks - limit.Ticks % TimeSpan.TicksPerSecond, limit.Kind);
        var delta = whole - row.To;
        for (var back = 0; back < 4; back++, delta -= TimeSpan.FromSeconds(1))
        {
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            if ((until - from).TotalSeconds < 3 || until > row.Day.AddDays(1)) break;
            if (LedgerEditor.TimedRow(row.Account, from, until, "edited").To <= limit) return delta;
        }
        return limit - row.To;
    }
}
