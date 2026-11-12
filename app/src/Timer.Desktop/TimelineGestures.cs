using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public static class TimelineGestures
{

    /// <summary>
    /// A selected block also gets a grab handle on each edge that reaches a little outside it, where its ring is. The ring is not part of the block, so a press there would
    /// otherwise land on a neighbour (or the empty canvas) and start dragging that one, which made resizing a selected block fail now and then.
    /// </summary>
    public static void Attach(Border card, Canvas canvas, TimerModel model, SessionRow row, double pixelsPerHour, bool selected = false)
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
        void Press(Control source, PointerPressedEventArgs e, int? forced)
        {
            if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
            model.BeginTimelineGesture();
            origin = e.GetPosition(canvas); delta = TimeSpan.Zero; dragging = false;
            oldLeft = Canvas.GetLeft(card); oldWidth = card.Width;
            edge = forced ?? EdgeAt(e.GetPosition(card).X, oldWidth);
            e.Handled = true; e.Pointer.Capture(source);
        }
        card.PointerPressed += (_, e) => Press(card, e, null);
        void Move(PointerEventArgs e)
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
            var insert = e.KeyModifiers.HasFlag(KeyModifiers.Shift); var free = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            var requested = TimeSpan.FromHours(moved.X / pixelsPerHour);
            SnapNote note = default;
            if (insert) delta = SnapDelta(model, row, edge, requested, pixelsPerHour, out nudging, insert: true);
            else { nudging = false; delta = ClampToDay(row, edge, Magnet(model, row, edge, requested, pixelsPerHour, free, out note), model.DayBegins(model.SelectedDate)); note = note with { Overlap = OverlapOf(model, row, edge, delta) }; }
            RestoreNeighbours();
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            Canvas.SetLeft(card, oldLeft + (edge <= 0 ? delta.TotalHours * pixelsPerHour : 0));
            card.Width = Math.Max(ReviewViews.MinimumBlockWidth, (until - from).TotalHours * pixelsPerHour - ReviewViews.BlockGap);
            card.Opacity = .75;
            ShowSnap(canvas, model, card, row, pixelsPerHour, insert ? default : note);
            if (nudging)
            {
                try
                {
                    foreach (var change in model.Editor.PlanTimeline(row, from, until, true))
                    {
                        foreach (var neighbour in canvas.Children.OfType<Border>().Where(b => ReferenceEquals(b.Tag, change.Source)))
                        {
                            previews[neighbour] = (Canvas.GetLeft(neighbour), neighbour.Width);
                            Canvas.SetLeft(neighbour, ReviewViews.TimelineGutter + (change.Updated.From - model.DayBegins(model.SelectedDate)).TotalHours * pixelsPerHour + ReviewViews.BlockInset); neighbour.Opacity = .75;
                        }
                    }
                }
                catch (ArgumentException) { } // Final validation reports a blocked insertion without changing the ledger.
            }
            e.Handled = true;
        }
        card.PointerMoved += (_, e) => Move(e);
        void Release(PointerReleasedEventArgs e)
        {
            if (origin is null) return;
            var commit = dragging && delta != TimeSpan.Zero;
            var change = delta; var mode = edge; var push = nudging; var select = !dragging; var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            RestoreNeighbours(); ClearSnap(canvas);
            origin = null; dragging = false; e.Pointer.Capture(null); card.Opacity = 1;
            Canvas.SetLeft(card, oldLeft); card.Width = oldWidth;
            if (commit)
            {
                var newFrom = row.From + (mode <= 0 ? change : TimeSpan.Zero); var newTo = row.To + (mode >= 0 ? change : TimeSpan.Zero);
                // A drag may now land on other blocks and share their hours; only Shift (insert) keeps the neighbours clear.
                if (shift) model.DragEntry(row, newFrom, newTo, push); else model.MoveEntry(row, newFrom, newTo);
            }
            else if (select) model.SelectBlock(new[] { row }, shift);
            model.EndTimelineGesture();
            e.Handled = true;
        }
        card.PointerReleased += (_, e) => Release(e);
        void Lost()
        {
            // Context menus can release capture without a left-button gesture.
            if (origin is null) return;
            RestoreNeighbours(); ClearSnap(canvas);
            origin = null; dragging = false; card.Opacity = 1;
            Canvas.SetLeft(card, oldLeft); card.Width = oldWidth;
            model.EndTimelineGesture();
        }
        card.PointerCaptureLost += (_, _) => Lost();
        card.Cursor = new Cursor(StandardCursorType.SizeAll);
        if (!selected) return;
        foreach (var side in new[] { -1, 1 })
        {
            var reach = Math.Min(12, Math.Max(5, card.Width / 2 + 4));
            var handle = new Border { Name = side < 0 ? "TimelineHandleStart" : "TimelineHandleEnd", Width = reach, Height = card.Height, Background = Avalonia.Media.Brushes.Transparent, ZIndex = 6, Cursor = new Cursor(StandardCursorType.SizeWestEast) };
            Canvas.SetLeft(handle, side < 0 ? Canvas.GetLeft(card) - 4 : Canvas.GetLeft(card) + card.Width - reach + 4); Canvas.SetTop(handle, Canvas.GetTop(card));
            handle.PointerPressed += (_, e) => Press(handle, e, side);
            handle.PointerMoved += (_, e) => { if (origin is not null) Move(e); };
            handle.PointerReleased += (_, e) => Release(e);
            handle.PointerCaptureLost += (_, _) => Lost();
            canvas.Children.Add(handle);
        }
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
    public static TimeSpan ClampToDay(SessionRow row, int edge, TimeSpan delta, DateTime? dayBegins = null)
    {
        // A block can be dragged anywhere in the day on show, and never further out than it already is (one that runs in from the day before stays where it begins).
        var begin = dayBegins ?? row.Day; var floor = row.From < begin ? row.From : begin; var ceiling = row.To > begin.AddDays(1) ? row.To : begin.AddDays(1);
        var minimum = edge == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(1);
        var low = edge <= 0 ? floor - row.From : (row.From + minimum) - row.To;
        var high = edge >= 0 ? ceiling - row.To : (row.To - minimum) - row.From;
        return delta < low ? low : delta > high ? high : delta;
    }
    public static TimeSpan SnapDelta(TimerModel model, SessionRow row, int edge, TimeSpan requested, double pixelsPerHour, out bool nudge, bool insert = false, bool free = false)
    {
        var begin = model.DayBegins(model.SelectedDate);
        if (insert) { var snapped = Snap(model, row, edge, requested, pixelsPerHour, out nudge); return ClampToDay(row, edge, snapped, begin); }
        nudge = false; return ClampToDay(row, edge, Magnet(model, row, edge, requested, pixelsPerHour, free, out _), begin);
    }

    /// <summary>How far an edge pulls, in screen pixels either side of a neighbour's edge, before a drag is free to overlap.</summary>
    public const double MagnetPixels = 10;
    /// <summary>What the magnet did: where it snapped (the seam), and how much the block now shares with others.</summary>
    public readonly record struct SnapNote(bool Snapped, DateTime At, TimeSpan Overlap);

    /// <summary>
    /// A drag with a magnet: the edge being moved is pulled flush to the nearest edge of another block (or to now) when it is within <see cref="MagnetPixels"/>,
    /// and otherwise goes where the pointer is, to the 5 minutes, or the minute with <paramref name="free"/> (Alt). Passing through a neighbour is allowed, so
    /// blocks may overlap; only the block being recorded stops a drag. Nothing is pushed.
    /// </summary>
    public static TimeSpan Magnet(TimerModel model, SessionRow row, int edge, TimeSpan requested, double pixelsPerHour, bool free, out SnapNote note)
    {
        note = default;
        var begin = model.DayBegins(model.SelectedDate); var floor = row.From < begin ? row.From : begin; var ceiling = row.To > begin.AddDays(1) ? row.To : begin.AddDays(1);
        // Only blocks that reach into the day on show can be snapped to; the rest of the history is days away and only slowed every move down.
        var others = model.Engine.Sessions.Where(s => !ReferenceEquals(s, row) && s.Duration > TimeSpan.Zero && s.To > floor && s.From < ceiling).Select(s => (Start: s.From, End: s.To)).ToList();
        (DateTime Start, DateTime End)? running = model.Engine.Active is { } active && model.Engine.Now > active.Start ? (active.Start, model.Engine.Now) : null;
        bool Legal(TimeSpan delta)
        {
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero); var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            if (from < floor || until > ceiling || (until - from).TotalSeconds < 3) return false;
            if (edge != 0 && until - from < TimeSpan.FromMinutes(1)) return false;
            return running is not { } r || !(r.Start < until && r.End > from);
        }
        if (!free)
        {
            var candidates = new List<(TimeSpan Delta, DateTime At)>();
            foreach (var other in others.Concat(running is { } run ? new (DateTime Start, DateTime End)[] { run } : Array.Empty<(DateTime Start, DateTime End)>()))
            {
                // Starts are stored in whole seconds, so a fractional end is rounded up and never leaves a sliver of overlap.
                if (edge <= 0) { var end = new DateTime(((other.End.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond, other.End.Kind); candidates.Add((end - row.From, end)); }
                if (edge >= 0) candidates.Add((LastDeltaBefore(row, edge, other.Start), other.Start));
            }
            // Ending something at this moment, on today.
            if (edge >= 0 && model.DayOf(model.Engine.Now) == model.SelectedDate.Date) candidates.Add((LastDeltaBefore(row, edge, model.Engine.Now), model.Engine.Now));
            foreach (var candidate in candidates.OrderBy(c => Math.Abs((c.Delta - requested).TotalHours)))
            {
                if (Math.Abs((candidate.Delta - requested).TotalHours * pixelsPerHour) > MagnetPixels) break;
                if (!Legal(candidate.Delta)) continue;
                note = new(true, candidate.At, TimeSpan.Zero); return candidate.Delta;
            }
        }
        var step = free ? 1.0 : 5.0;
        var grid = TimeSpan.FromMinutes(Math.Round(requested.TotalMinutes / step) * step);
        if (running is { } wall)
        {
            // The block being recorded is the one thing nothing may run into: stop at it.
            if (row.To <= wall.Start) { var limit = LastDeltaBefore(row, edge, wall.Start); if (grid > limit) grid = limit; }
            else if (row.From >= wall.End && edge <= 0) { var wallEnd = new DateTime(((wall.End.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond) - row.From; if (grid < wallEnd) grid = wallEnd; }
        }
        return grid;
    }
    /// <summary>The most this block would share with any other block after a drag of <paramref name="delta"/>, for the chip that says so.</summary>
    public static TimeSpan OverlapOf(TimerModel model, SessionRow row, int edge, TimeSpan delta)
    {
        var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero); var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
        var most = TimeSpan.Zero;
        foreach (var other in model.Engine.Sessions.Where(s => !ReferenceEquals(s, row) && s.Duration > TimeSpan.Zero))
        {
            var share = (until < other.To ? until : other.To) - (from > other.From ? from : other.From);
            if (share > most) most = share;
        }
        return most;
    }
    private const string SnapLineName = "TimelineSnapLine", SnapChipName = "TimelineSnapChip";
    private static void ClearSnap(Canvas canvas)
    { foreach (var item in canvas.Children.Where(c => c.Name is SnapLineName or SnapChipName).ToList()) canvas.Children.Remove(item); }
    /// <summary>An accent line at the seam a drag has snapped to, and a chip saying so, or how much the block now overlaps.</summary>
    private static void ShowSnap(Canvas canvas, TimerModel model, Border card, SessionRow row, double pixelsPerHour, SnapNote note)
    {
        ClearSnap(canvas);
        var text = note.Overlap > TimeSpan.FromSeconds(30) ? $"Overlaps {model.Format(note.Overlap)}" : note.Snapped ? $"Flush · {note.At:HH:mm}" : null;
        if (text is null) return;
        var top = Canvas.GetTop(card);
        double x;
        if (note.Snapped) x = ReviewViews.TimelineGutter + (note.At - model.DayBegins(model.SelectedDate)).TotalHours * pixelsPerHour;
        else x = Canvas.GetLeft(card) + card.Width / 2;
        if (note.Snapped)
        {
            var line = new Border { Name = SnapLineName, Width = 3, Height = card.Height + 16, CornerRadius = new CornerRadius(1.5), IsHitTestVisible = false, ZIndex = 12 };
            line.Bind(Border.BackgroundProperty, line.GetResourceObservable("AccentLit")); Canvas.SetLeft(line, x - 1.5); Canvas.SetTop(line, top - 8); canvas.Children.Add(line);
        }
        var label = new TextBlock { Text = text, FontSize = 11.5, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var chip = new Border { Name = SnapChipName, Child = label, Padding = new Thickness(9, 3), CornerRadius = new CornerRadius(9), IsHitTestVisible = false, ZIndex = 13 };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Porcelain")); chip.Bind(Border.BoxShadowProperty, chip.GetResourceObservable("ShadowPopup"));
        chip.Measure(Size.Infinity); Canvas.SetLeft(chip, Math.Max(2, x - chip.DesiredSize.Width / 2)); Canvas.SetTop(chip, Math.Max(2, top - 28)); canvas.Children.Add(chip);
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
        bool Fits(TimeSpan delta)
        {
            var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero);
            var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
            if (from < row.Day || until > row.Day.AddDays(1) || (until - from).TotalSeconds < 3) return false;
            var saved = LedgerEditor.TimedRow(row.Account, from, until, "edited");
            return !occupied.Any(other => other.Start < saved.To && other.End > saved.From);
        }

        // Keep the first neighbour as the stopping point for resizing and short drags.
        var overshoot = lower is { } l && requested < l ? (l - requested).TotalHours * pixelsPerHour
            : upper is { } u && requested > u ? (requested - u).TotalHours * pixelsPerHour : 0;
        var crossedNeighbour = edge == 0 && overshoot > 24;
        if (!crossedNeighbour)
        {
            if (lower is { } minimum && (requested <= minimum || grid <= minimum)) requested = minimum;
            if (upper is { } maximum && (requested >= maximum || grid >= maximum)) requested = maximum;
        }
        foreach (var candidate in candidates.OrderBy(c => Math.Abs((c - requested).TotalHours)))
        {
            if (Math.Abs((candidate - requested).TotalHours * pixelsPerHour) > 8) break;
            if (Fits(candidate)) return candidate;
        }
        if (crossedNeighbour)
        {
            grid = ClampToDay(row, edge, grid);
            if (Fits(grid)) return grid;

            // A gap can fit the block under the pointer even when rounding to five minutes would overlap a neighbour.
            requested = ClampToDay(row, edge, requested);
            if (Fits(requested)) return requested;

            nudge = true;
            return grid;
        }
        // Apply the same stopping point to grid rounding.
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
            // A block may be no shorter than three seconds and no longer than a day, and TimedRow throws outside that. A limit far away (a block on another day,
            // reached when an edge is dragged) would ask for a block longer than a day, which crashed the app, so such a limit is answered without asking it.
            if ((until - from).TotalSeconds < 3 || until - from > LedgerEditor.MaxBlock) break;
            if (LedgerEditor.TimedRow(row.Account, from, until, "edited").To <= limit) return delta;
        }
        return limit - row.To;
    }
}
