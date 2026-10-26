using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

namespace Timer.Desktop;

public static class ReviewViews
{
    /// <summary>Even a one-minute block is drawn this wide, so it can be seen and clicked. Zoom in for exact edges.</summary>
    public const double MinimumBlockWidth = 6;
    // A block that has its band to itself has the larger corner, and one sharing the band with others the smaller, and a block too narrow to hold either is rounded as far as it can be.
    public const double BlockRadius = 12, LaneRadius = 8;
    // The hours run across the top, the blocks sit in one band under them, and for today a chip under the band names the time.
    /// <summary>The room before midnight and after the last hour, so the first and last hour labels sit fully in view.</summary>
    public const double TimelineGutter = 34;
    /// <summary>The room a block keeps clear each side, so the selection ring (3 pixels) round one block never lies over its neighbour: blocks are drawn this far in from their times.</summary>
    public const double BlockInset = 3, BlockGap = 2 * BlockInset;
    /// <summary>A block narrower than this keeps its note dot in the middle of its top edge, since beside the edge it looks off-centre on a sliver of colour.</summary>
    public const double NarrowDotWidth = 48;
    // The axis sits under the hour labels, with a clear gap before the row for planned meetings, and the blocks sit under that row. A day with no meetings has no row, so its
    // blocks take the height of both. They always end at BandBottom, where the chip that names the time hangs under them.
    private const double LabelRow = 22, AxisY = LabelRow + 6, MeetGap = 14, MeetingTop = AxisY + MeetGap, MeetingHeight = 48, BandTopWithMeetings = MeetingTop + MeetingHeight + MeetGap, BandTopFree = 46, BandBottom = 176, LaneGap = 4, TimelineHeight = 210, LabelWidth = 44;
    /// <summary>The corner a block has when the band holds this many lanes: the larger for one, the smaller for more.</summary>
    public static double CornerFor(int lanes, double width, double height) => Math.Min(lanes <= 1 ? BlockRadius : LaneRadius, Math.Min(width, height) / 2);
    /// <summary>How much of a block to show. One lane has the full block, two a line of duration and account, three the duration alone, and four or more the colour with the tooltip.</summary>
    public enum BlockDetail { Full, Inline, Tiny, Bare }
    /// <summary>The deeper end of a block's gradient: up to 30 per cent towards its edge colour, less where that would lose the block's text.</summary>
    public static string DeeperEnd(ThemeDerivation.BlockLook look)
    {
        for (var amount = .3; amount > 0; amount -= .05)
        {
            var end = ThemeColours.Mix(look.Fill, look.Edge, amount);
            if (ThemeColours.Contrast(look.Text, end) >= 4.5) return end;
        }
        return look.Fill;
    }
    public static BlockDetail DetailFor(int lanes) => lanes <= 1 ? BlockDetail.Full : lanes == 2 ? BlockDetail.Inline : lanes == 3 ? BlockDetail.Tiny : BlockDetail.Bare;

    // A class, not a record, so two blocks that look the same are still two (lanes are kept by block, not by value).
    private sealed class Interval(string account, DateTime start, DateTime end, SessionRow? row, ReviewEntry? group)
    {
        public string Account { get; } = account; public DateTime Start { get; } = start; public DateTime End { get; } = end; public SessionRow? Row { get; } = row; public ReviewEntry? Group { get; } = group;
    }

    /// <summary>
    /// Cuts the blocks into lanes: blocks that overlap (by more than a second or two, which older recordings can have) form a cluster, and a cluster is cut into as many
    /// equal lanes as it needs, first-fit by start time so the earlier block stays on top. A block with nothing over it keeps the whole band.
    /// </summary>
    public static Dictionary<T, (int Lane, int Count)> PlaceLanes<T>(IEnumerable<(T Item, DateTime Start, DateTime End)> blocks) where T : notnull
    {
        var placed = new Dictionary<T, (int Lane, int Count)>();
        var cluster = new List<(T Item, DateTime Start, DateTime End)>(); var reach = DateTime.MinValue;
        void Flush()
        {
            var ends = new List<DateTime>(); var lanes = new List<(T Item, int Lane)>();
            foreach (var block in cluster)
            {
                var lane = ends.FindIndex(end => end <= block.Start.AddSeconds(2));
                if (lane < 0) { lane = ends.Count; ends.Add(block.End); } else ends[lane] = block.End;
                lanes.Add((block.Item, lane));
            }
            foreach (var (item, lane) in lanes) placed[item] = (lane, ends.Count);
        }
        foreach (var block in blocks.OrderBy(b => b.Start).ThenBy(b => b.End))
        {
            if (cluster.Count > 0 && block.Start < reach.AddSeconds(-2)) { cluster.Add(block); if (block.End > reach) reach = block.End; }
            else { if (cluster.Count > 0) Flush(); cluster = new() { block }; reach = block.End; }
        }
        if (cluster.Count > 0) Flush();
        return placed;
    }

    /// <summary>A diagonal hatch in two quiet shades of the theme, for a break: time that is kept apart from the day's work.</summary>
    internal static Brush Hatch(TimerModel model)
    {
        var theme = model.CurrentTheme;
        var a = Color.Parse(ThemeColours.Mix(theme.Colour("Surface"), theme.Colour("Panel"), .5)); var b = Color.Parse(theme.Colour("Line"));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(12, 12, RelativeUnit.Absolute), SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops = { new GradientStop(a, 0), new GradientStop(a, .5), new GradientStop(b, .5), new GradientStop(b, 1) }
        };
    }

    /// <summary>Thin diagonal stripes in the quiet bar colour on nothing, for the day bar's unallocated time, close together so even a short block shows several as stripes: the stripes of <see cref="Hatch"/> are too faint on the bar's track.</summary>
    internal static Brush Stripes(TimerModel model)
    {
        var stripe = Color.Parse(model.CurrentTheme.Colour("Muted"));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(3.4, 3.4, RelativeUnit.Absolute), SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops = { new GradientStop(stripe, 0), new GradientStop(stripe, .45), new GradientStop(Colors.Transparent, .45), new GradientStop(Colors.Transparent, 1) }
        };
    }

    private static string Hm(TimeSpan? time) => time is { } t ? t.ToString(@"hh\:mm") : "";

    public static Control Timeline(TimerModel model)
    {
        var day = model.SelectedDate;
        var pixelsPerHour = model.TimelinePixelsPerHour;
        var planned = model.Notes.MeetingsOn(day); var bandTop = planned.Count > 0 ? BandTopWithMeetings : BandTopFree; var bandHeight = BandBottom - bandTop;
        double X(DateTime time) => TimelineGutter + (time - day).TotalHours * pixelsPerHour;
        // A little room before the first hour and after the last lets the end labels sit fully in view.
        var canvas = new Canvas { Name = "TimelineCanvas", Width = 24 * pixelsPerHour + 2 * TimelineGutter, Height = TimelineHeight, Background = Brushes.Transparent };
        var axis = new Border { Name = "TimelineAxis", Height = 1, Width = 24 * pixelsPerHour, Opacity = .8 }; axis.Classes.Add("rule");
        Canvas.SetLeft(axis, TimelineGutter); Canvas.SetTop(axis, AxisY); canvas.Children.Add(axis);
        void Tick(double hours, double height, double opacity)
        {
            var tick = new Border { Width = 1.5, Height = height, Opacity = opacity, Tag = "Tick" }; tick.Classes.Add("rule");
            Canvas.SetLeft(tick, TimelineGutter + hours * pixelsPerHour - .75); Canvas.SetTop(tick, LabelRow + 7 - height); canvas.Children.Add(tick);
        }
        var hourLabels = new List<(TextBlock Label, TranslateTransform Shift)>();
        for (var hour = 0; hour < 24; hour++)
        {
            var label = Ui.Text($"{hour:00}:00", 12, "muted"); label.Classes.Add("figures"); label.Width = LabelWidth; label.TextAlignment = TextAlignment.Center;
            label.Name = "TimelineHour_" + hour;
            var shift = new TranslateTransform(); label.RenderTransform = shift; hourLabels.Add((label, shift));
            Canvas.SetLeft(label, TimelineGutter + hour * pixelsPerHour - LabelWidth / 2); Canvas.SetTop(label, 0); canvas.Children.Add(label);
            // The half hours are nearly as tall as the hours and as dark, with a faint line down through the band, so they can be read at a glance.
            Tick(hour, 9, 1);
            Tick(hour + .5, 6, 1);
            if (pixelsPerHour >= 260) { Tick(hour + .25, 4, .75); Tick(hour + .75, 4, .75); }
        }
        // A faint line down through the band at each hour, so a block can be read against the hours.
        for (var hour = 0; hour <= 24; hour++)
        {
            var guide = new Border { Width = 1, Height = TimelineHeight - LabelRow - 12, Opacity = .45, Tag = "HourGuide", IsHitTestVisible = false }; guide.Classes.Add("rule");
            Canvas.SetLeft(guide, TimelineGutter + hour * pixelsPerHour - .5); Canvas.SetTop(guide, LabelRow + 8); canvas.Children.Add(guide);
        }
        for (var hour = 0; hour < 24; hour++)
            {
                var half = new Border { Width = 1, Height = TimelineHeight - LabelRow - 12, Opacity = .22, Tag = "HalfGuide", IsHitTestVisible = false }; half.Classes.Add("rule");
                Canvas.SetLeft(half, TimelineGutter + (hour + .5) * pixelsPerHour - .5); Canvas.SetTop(half, LabelRow + 8); canvas.Children.Add(half);
            }
        var groups = model.Editor.Entries(day, day.AddDays(1)).Where(e => e.GroupId is not null && e.Sources.Any(s => s.Duration > TimeSpan.Zero)).ToArray();
        var grouped = groups.SelectMany(e => e.Sources).ToHashSet();
        var intervals = model.Engine.Sessions.Where(s => s.Day == day && s.Duration > TimeSpan.Zero && !grouped.Contains(s))
            .Select(s => new Interval(s.Account, s.From, s.To, s, null)).ToList();
        if (model.Engine.Active is { } active && active.Start < day.AddDays(1) && model.Engine.Now > day)
            intervals.Add(new(active.Account, active.Start < day ? day : active.Start, model.Engine.Now < day.AddDays(1) ? model.Engine.Now : day.AddDays(1), null, null));
        // A merged block is one block from its first entry to its last, with the gaps between its entries drawn lighter.
        foreach (var group in groups)
        {
            var members = group.Sources.Where(s => s.Duration > TimeSpan.Zero).ToList();
            var display = group with { Start = members.Min(s => s.From), End = members.Max(s => s.To), Minutes = members.Sum(s => decimal.Parse(s.Minutes, System.Globalization.CultureInfo.InvariantCulture)) };
            intervals.Add(new(group.Account, display.Start!.Value, display.End!.Value, null, display));
        }
        var placement = PlaceLanes(intervals.Select(i => (i, i.Start, i.End)));
        var accent = Color.Parse(model.CurrentTheme.Colour("Accent"));
        foreach (var interval in intervals.OrderBy(i => i.Start))
        {
            var (lane, laneCount) = placement[interval];
            var detail = DetailFor(laneCount);
            var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(interval.Account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
            var look = model.BlockLook(colour);
            var isBreak = BreakAccount.Is(interval.Account);
            var end = interval.End > day.AddDays(1) ? day.AddDays(1) : interval.End;
            var duration = interval.Group is { } groupDuration ? TimeSpan.FromMinutes((double)groupDuration.Minutes) : end - interval.Start;
            var width = Math.Max(MinimumBlockWidth, (end - interval.Start).TotalHours * pixelsPerHour - BlockGap);
            var height = laneCount == 1 ? bandHeight : (bandHeight - LaneGap * (laneCount - 1)) / laneCount;
            var ink = isBreak ? model.CurrentTheme.Colour("Muted") : look.Text;
            var display = model.Label(interval.Account);
            // What is written on the block depends on the room it has: the whole block, a line, the duration alone, or nothing but the colour.
            var text = new StackPanel { Spacing = 2, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = detail == BlockDetail.Full ? new Thickness(width < 84 ? 7 : 10, 6) : new Thickness(8, 0) };
            // The duration is in the ordinary typeface, semibold: the display face at bold was too heavy to read at this size.
            TextBlock Duration(double size) { var d = Ui.Text(model.Format(duration), size); d.FontWeight = FontWeight.SemiBold; d.Name = "BlockDuration"; return d; }
            // A name too long for its block slides past on hover so the whole of it can be read (the same MarqueeText as the account lists).
            var marquees = new List<MarqueeText>();
            Control Name(double size, bool short_)
            {
                if (short_) { var n = Ui.Text(PillWindow.Initials(display), size); n.FontWeight = FontWeight.SemiBold; n.TextTrimming = TextTrimming.CharacterEllipsis; n.Name = "BlockName"; return n; }
                var m = new MarqueeText(display, size); m.Label.FontWeight = FontWeight.SemiBold; m.Label.Name = "BlockName"; marquees.Add(m); return m;
            }
            var recording = interval.Row is null && interval.Group is null;
            var note = interval.Group is { } groupNote ? groupNote.Note : recording ? "Recording now" : interval.Row is { } noteRow ? model.Editor.Note(noteRow) : "";
            switch (detail)
            {
                case BlockDetail.Full when width >= 56:
                    text.Children.Add(Duration(15));
                    text.Children.Add(Name(13, width < 84));
                    if (width >= 140 && !string.IsNullOrWhiteSpace(note) && !recording) { var line = Ui.Text("• " + note, 12); line.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(line); }
                    break;
                case BlockDetail.Inline when width >= 56:
                    var inline = Ui.Horizontal(Duration(13), Name(12.5, width < 120)); inline.Spacing = 8; text.Children.Add(inline);
                    break;
                case BlockDetail.Tiny when width >= 50:
                    text.Children.Add(Duration(11));
                    break;
            }
            foreach (var child in text.GetLogicalDescendants().OfType<TextBlock>()) child.Foreground = Brush.Parse(ink);
            // The card itself is not clipped, or the selection ring round it would be cut off; what is inside it is clipped instead.
            var card = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(CornerFor(laneCount, width, height)) };
            if (marquees.Count > 0) { card.PointerEntered += (_, _) => marquees.ForEach(m => m.Hover(true)); card.PointerExited += (_, _) => marquees.ForEach(m => m.Hover(false)); }
            if (isBreak) { card.Background = Hatch(model); card.BorderBrush = Brush.Parse(model.CurrentTheme.Colour("Line")); card.BorderThickness = new Thickness(1); }
            else
            {
                // A gentle gradient from the block's own fill to a slightly deeper shade of it, so a block has body without any text losing its contrast.
                var deeper = DeeperEnd(look);
                card.Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(look.Fill), 0), new GradientStop(Color.Parse(deeper), 1) } };
            }
            var content = new Grid { ClipToBounds = true }; content.Children.Add(text);
            // The gaps inside a merged block are lighter, and a small link marks it as merged.
            if (interval.Group is { } mergedGroup)
            {
                var members = mergedGroup.Sources.Where(s => s.Duration > TimeSpan.Zero).OrderBy(s => s.From).ToList();
                var seams = new Canvas { IsHitTestVisible = false };
                for (var m = 0; m + 1 < members.Count; m++)
                {
                    var gap = (members[m + 1].From - members[m].To).TotalHours * pixelsPerHour;
                    if (gap > .5) { var seam = new Border { Width = gap, Height = height, Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)), Tag = "Seam" }; Canvas.SetLeft(seam, (members[m].To - interval.Start).TotalHours * pixelsPerHour); seams.Children.Add(seam); }
                }
                content.Children.Add(seams);
                if (detail == BlockDetail.Full && width >= 60)
                {
                    var link = new IconGlyph(IconKind.Merge) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 7), IsHitTestVisible = false, Opacity = .8, Name = "MergedMark" };
                    content.Children.Add(link);
                }
            }
            // A block with a note carries a small page mark in its corner, when it is wide enough for the note's first line.
            if (interval.Row is { } notedRow && detail == BlockDetail.Full && width >= 140 && !string.IsNullOrWhiteSpace(model.Editor.Note(notedRow)))
                content.Children.Add(new IconGlyph(IconKind.Note) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 7), IsHitTestVisible = false, Opacity = .75, Name = "NoteMark" });
            // A block too small for the page mark still shows that it has a note, with a small dot at its top right, well inside the block so the selection ring never meets it.
            if (!content.Children.OfType<IconGlyph>().Any(g => g.Name == "NoteMark") && width >= 16 && !recording && !string.IsNullOrWhiteSpace(note) && (interval.Row is not null || interval.Group is not null))
                content.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Name = "NoteDot", Width = 7, Height = 7, Fill = Brush.Parse(ink), Opacity = .8, HorizontalAlignment = width < NarrowDotWidth ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = width < NarrowDotWidth ? new Thickness(0, 8, 0, 0) : new Thickness(0, 8, 8, 0), IsHitTestVisible = false });
            card.Child = content;
            var addedEarly = false;
            var selectionSources = interval.Group?.Sources ?? (interval.Row is { } source ? new[] { source } : Array.Empty<SessionRow>());
            if (model.IsSelected(selectionSources))
            {
                // A selected block has a ring in the theme's accent colour round it, and the `mergeSelected` class so it can be found.
                card.Classes.Add("mergeSelected"); card.BoxShadow = new BoxShadows(new BoxShadow { Spread = 3, Color = accent }); card.ZIndex = 3;
            }
            ToolTip.SetTip(card, $"{display} · {interval.Start:HH:mm:ss} – {end:HH:mm:ss} · {model.Format(duration)}" + (string.IsNullOrWhiteSpace(note) ? "" : "\n" + note));
            if (interval.Row is { } row)
            {
                card.Tag = row; card.Name = "TimelineEntry" + model.Engine.Sessions.IndexOf(row);
                card.ContextMenu = new ContextMenu { ItemsSource = new Control[] { MoveMenu(model, new[] { row }), CopyMenu(model, new[] { row }), MergeMenu(model), new Separator(), DeleteMenu(model, new[] { row }) } };
                Canvas.SetLeft(card, X(interval.Start) + BlockInset); Canvas.SetTop(card, bandTop + lane * (height + LaneGap)); canvas.Children.Add(card); addedEarly = true;
                TimelineGestures.Attach(card, canvas, model, row, pixelsPerHour, model.IsSelected(selectionSources));
            }
            if (recording)
            {
                // The block that is recording now. A green outline and the stop menu make it clear why it can't be dragged.
                card.Classes.Add("recording"); card.Name = "TimelineRecording"; card.Cursor = new Cursor(StandardCursorType.Arrow);
                if (text.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "BlockName") is { } liveName) liveName.Text = "● " + liveName.Text;
                var stop = new MenuItem { Header = "Stop tracking" }; stop.Click += (_, _) => model.TogglePause();
                card.ContextMenu = new ContextMenu { ItemsSource = new[] { stop } };
                card.PointerPressed += (_, e) =>
                { if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) { model.ExplainRunning(); e.Handled = true; } };
            }
            if (interval.Group is { } group)
            {
                Point? groupPress = null;
                card.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                    groupPress = e.GetPosition(canvas);
                    e.Handled = true;
                };
                card.PointerReleased += (_, e) =>
                {
                    if (groupPress is { } p)
                    { var movement = e.GetPosition(canvas) - p; if (movement.X * movement.X + movement.Y * movement.Y <= 25) model.SelectBlock(group.Sources, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); }
                    groupPress = null; e.Handled = true;
                };
                card.Tag = group; card.Name = "TimelineGroup" + group.GroupId + "-" + interval.Start.ToString("HHmmss");
                var ungroup = new MenuItem { Header = "Unmerge", Name = "UnmergeMenu" }; ungroup.Click += (_, _) => model.UngroupEntries(group.GroupId!);
                card.ContextMenu = new ContextMenu { ItemsSource = new Control[] { ungroup, CopyMenu(model, group.Sources), MergeMenu(model) } };
                ToolTip.SetTip(card, $"{model.Label(group.Account)} · {model.Format(duration)}\nMerged, {group.Sources.Count} blocks");
            }
            if (!addedEarly) { Canvas.SetLeft(card, X(interval.Start) + BlockInset); Canvas.SetTop(card, bandTop + lane * (height + LaneGap)); canvas.Children.Add(card); }
        }
        // Planned meetings sit in the row above the blocks as dashed cards, and a click on a free place in that row starts a new one. A meeting is planned time:
        // it is never counted as tracked time, and a block under it is not affected.
        {
            var lane = new Border { Name = "MeetingLane", Width = 24 * pixelsPerHour, Height = MeetingHeight, Background = Brushes.Transparent, CornerRadius = new CornerRadius(12), Cursor = new Cursor(StandardCursorType.Arrow) };
            lane.Classes.Add("meetingLane");
            ToolTip.SetTip(lane, "Add meeting");
            Avalonia.Automation.AutomationProperties.SetName(lane, "Add a meeting. Click a time in this row");
            lane.PointerEntered += (_, _) => lane.Classes.Add("over");
            lane.PointerExited += (_, _) => lane.Classes.Remove("over");
            Point? pressed = null;
            lane.PointerPressed += (_, e) => { if (e.GetCurrentPoint(lane).Properties.IsLeftButtonPressed) { pressed = e.GetPosition(canvas); e.Handled = true; } };
            lane.PointerReleased += (_, e) =>
            {
                if (pressed is { } p && Math.Abs(e.GetPosition(canvas).X - p.X) <= 5 && e.InitialPressMouseButton == MouseButton.Left)
                    model.Notes.RequestNewMeeting(day.AddHours((e.GetPosition(canvas).X - TimelineGutter) / pixelsPerHour));
                pressed = null; e.Handled = true;
            };
            Canvas.SetLeft(lane, TimelineGutter); Canvas.SetTop(lane, MeetingTop); canvas.Children.Add(lane);
            // With no meetings the row is under the blocks, which take its height; a free place in it still starts a meeting.
            if (planned.Count == 0) lane.ZIndex = -1;
            var places = PlaceLanes(planned.Select(m => (m, day + m.At!.Value, day + m.End!.Value)));
            foreach (var meeting in planned)
            {
                var (row, rows) = places[meeting];
                var width = Math.Max(24, (meeting.End!.Value - meeting.At!.Value).TotalHours * pixelsPerHour - 2);
                var height = rows == 1 ? MeetingHeight : (MeetingHeight - LaneGap * (rows - 1)) / rows;
                // A name cut short slides past on hover so all of it can be read, like a block's name.
                var title = new MarqueeText(model.Notes.Text(meeting), 12) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }; title.Label.FontWeight = FontWeight.SemiBold;
                // A grid, so the name takes what is left beside the marks and is cut with an ellipsis, however narrow the block is at this zoom.
                var head = Ui.Grid(width >= 60 ? "Auto,*,Auto" : "*"); head.ColumnSpacing = 5;
                if (width >= 60) { Ui.Put(head, new IconGlyph(IconKind.Today)); Ui.Put(head, title, 1); if (model.Notes.SeriesOf(meeting) is not null) Ui.Put(head, new IconGlyph(IconKind.Repeat) { Opacity = .8 }, 2); }
                else Ui.Put(head, title);
                var body = new StackPanel { Spacing = 2, Margin = rows == 1 ? new Thickness(8, 6) : new Thickness(8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                body.Children.Add(head);
                if (rows == 1) { var span = new TextBlock { Text = Hm(meeting.At) + "–" + Hm(meeting.End), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }; span.Classes.Add("muted"); body.Children.Add(span); }
                var frame = new Avalonia.Controls.Shapes.Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 } };
                frame.Classes.Add("meetingFrame");
                var card = new Grid { Width = width, Height = height, ClipToBounds = true, Name = "TimelineMeeting" + meeting.Id, Tag = meeting, Cursor = new Cursor(StandardCursorType.Hand) };
                card.Children.Add(frame); card.Children.Add(body);
                ToolTip.SetTip(card, model.Notes.Text(meeting) + "\n" + Hm(meeting.At) + "–" + Hm(meeting.End) + " · planned");
                Avalonia.Automation.AutomationProperties.SetName(card, "Meeting " + model.Notes.Text(meeting) + ", " + Hm(meeting.At) + " to " + Hm(meeting.End));
                card.PointerEntered += (_, _) => { frame.Classes.Add("over"); title.Hover(true); }; card.PointerExited += (_, _) => { frame.Classes.Remove("over"); title.Hover(false); };
                card.PointerPressed += (_, e) => { if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) e.Handled = true; };
                card.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) model.Notes.RequestEditMeeting(meeting.Id); e.Handled = true; };
                Canvas.SetLeft(card, X(day + meeting.At.Value) + 1); Canvas.SetTop(card, MeetingTop + row * (height + LaneGap)); canvas.Children.Add(card);
            }
        }
        // For today, a line down through the band at the current time, a dot at the top and a chip under it saying what it is. It moves on every half minute while on screen.
        if (day == model.Engine.Now.Date)
        {
            var nowLine = new Border { Name = "NowMarker", Width = 2, Height = BandBottom + 8 - AxisY, Tag = "NowLine", IsHitTestVisible = false, ZIndex = 10 }; nowLine.Classes.Add("nowLine");
            var nowDot = new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Tag = "NowDot", IsHitTestVisible = false, ZIndex = 10 }; nowDot.Classes.Add("nowDot");
            var nowText = new TextBlock { FontSize = 12, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Classes = { "nowChipText" } };
            var nowChip = new Border { Name = "NowChip", Child = nowText, CornerRadius = new CornerRadius(7), Height = 20, Width = 52, IsHitTestVisible = false, ZIndex = 10 }; nowChip.Classes.Add("nowChip"); // A fixed width, so it stays centred on the line whatever the digits.
            canvas.Children.Add(nowLine); canvas.Children.Add(nowDot); canvas.Children.Add(nowChip);
            void PlaceNow()
            {
                var now = model.Engine.Now; var x = X(now);
                Canvas.SetLeft(nowLine, x - 1); Canvas.SetTop(nowLine, AxisY);
                Canvas.SetLeft(nowDot, x - 5); Canvas.SetTop(nowDot, AxisY - 4);
                nowText.Text = now.ToString("HH:mm"); Canvas.SetLeft(nowChip, x - nowChip.Width / 2); Canvas.SetTop(nowChip, BandBottom + 10);
            }
            PlaceNow();
            DispatcherTimer? nowTimer = null;
            canvas.AttachedToVisualTree += (_, _) => { nowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; nowTimer.Tick += (_, _) => PlaceNow(); nowTimer.Start(); };
            canvas.DetachedFromVisualTree += (_, _) => { nowTimer?.Stop(); nowTimer = null; };
        }
        DateTime clicked = day.AddHours(9);
        DateTime At(double x) => day.AddMinutes(Math.Clamp(Math.Floor((x - TimelineGutter) / pixelsPerHour * 60 / 5) * 5, 0, 1435));
        var add = new MenuItem { Header = "Add time…" }; add.Click += (_, _) => model.OpenGap(clicked);
        var addMeeting = new MenuItem { Header = "Add meeting…", Name = "AddMeetingMenu" }; addMeeting.Click += (_, _) => model.Notes.RequestNewMeeting(clicked);
        // Paste puts the copied blocks with the first at the time that was right-clicked, snapped to five minutes like Add time.
        var paste = new MenuItem { Header = "Paste here", Name = "PasteHereMenu", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control), IsEnabled = model.CanPaste };
        paste.Click += (_, _) => model.PasteBlocks(clicked);
        canvas.ContextMenu = new ContextMenu { ItemsSource = new Control[] { add, addMeeting, paste, MergeMenu(model) } };
        // Ctrl+V pastes where the pointer last was on this timeline.
        canvas.PointerMoved += (_, e) => model.PasteTime = At(e.GetPosition(canvas).X);
        canvas.PointerExited += (_, _) => model.PasteTime = null;
        bool OnBlock(object? source) => source is Visual visual && (visual is Border { Tag: SessionRow or ReviewEntry }
            || visual.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is SessionRow or ReviewEntry));
        Point? emptyPress = null;
        canvas.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(canvas).Properties.IsRightButtonPressed) clicked = At(e.GetPosition(canvas).X);
            else if (e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed && !OnBlock(e.Source)
                && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // A double-click adds time. A single click only clears the selection, so stray clicks add nothing.
                if (e.ClickCount == 2) { model.ClearSelection(); model.OpenGap(At(e.GetPosition(canvas).X)); e.Handled = true; }
                else emptyPress = e.GetPosition(canvas);
            }
        };
        canvas.PointerReleased += (_, e) =>
        {
            var start = emptyPress; emptyPress = null;
            if (start is not { } point || e.InitialPressMouseButton != MouseButton.Left || OnBlock(e.Source)
                || Math.Pow(e.GetPosition(canvas).X - point.X, 2) + Math.Pow(e.GetPosition(canvas).Y - point.Y, 2) > 25) return;
            model.ClearSelection(); e.Handled = true;
        };
        var plan = model.Engine.Plan(day);
        var first = intervals.Count > 0 ? intervals.Min(i => i.Start) : plan.Working ? plan.Start : day.AddHours(9);
        // The timeline scrolls along the day, and opens with the first block (or the start of the working day) a little way in from the left.
        var scroll = new ScrollViewer { Name = "TimelineScroll", Content = canvas, ClipToBounds = true,
            Tag = new Vector(model.TimelineOffset ?? Math.Max(0, X(first) - 56), 0),
            AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        // Only a partly visible hour label shifts inward. Blocks, ticks and pointer-to-time maths stay on their original coordinates.
        void KeepHourLabelsReadable()
        {
            var width = scroll.Viewport.Width;
            if (width <= 0) return;
            foreach (var (label, shift) in hourLabels)
            {
                var left = Canvas.GetLeft(label) - scroll.Offset.X;
                var labelWidth = label.Bounds.Width;
                var x = labelWidth > 0 && left < width && left + labelWidth > 0
                    ? Math.Clamp(left, 2, Math.Max(2, width - labelWidth - 2)) - left : 0;
                if (Math.Abs(shift.X - x) > .01) shift.X = x;
            }
        }
        scroll.ScrollChanged += (_, _) =>
        {
            if (scroll.Tag is null && scroll.Viewport.Width > 0) model.TimelineOffset = scroll.Offset.X;
            KeepHourLabelsReadable();
        };
        scroll.LayoutUpdated += (_, _) => KeepHourLabelsReadable();
        scroll.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            e.Handled = true;
            // Ctrl and the wheel zoom about the pointer; the wheel alone slides along the day.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) model.ZoomTimeline(e.Delta.Y, e.GetPosition(scroll).X, scroll.Offset.X);
            else scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X - (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y) * 60, 0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)), 0);
        }, RoutingStrategies.Tunnel);
        DeferOffsetAcross(scroll);
        return scroll;
    }

    /// <summary>The hours table for the week that starts on the given day: the days the week shows, and the rows and budgets laid out for them.</summary>
    public static (WeekTable Table, int[] Days) WeekTableFor(TimerModel model, DateTime day)
    {
        var days = Enumerable.Range(0, 7).Where(i => model.Engine.Schedule.Days.Contains(day.AddDays(i).DayOfWeek)
            || model.Engine.Sessions.Any(s => s.Day == day.AddDays(i))
            || (model.Engine.Active is not null && day.AddDays(i) == model.Engine.Now.Date)).ToArray();
        if (days.Length == 0) days = Enumerable.Range(0, 7).ToArray();
        var reconciled = new WeekExport(model.Editor, model.Engine, day);
        return (WeekTable.Build(model, reconciled, day, days), days);
    }
    public static Control Week(TimerModel model) => WeekOverview.Build(model);
    // Moves the clicked entry, or every selected block when the clicked one is selected, to another account.
    private static MenuItem MoveMenu(TimerModel model, IReadOnlyList<SessionRow> clicked)
    {
        var item = new MenuItem { Name = "MoveToAccountMenu", Header = "Move to account" };
        var rows = model.IsSelected(clicked) ? model.Engine.Sessions.Where(model.SelectedRows.Contains).ToList() : clicked.ToList();
        var targets = model.Accounts.Where(a => rows.Any(r => !r.Account.Equals(a.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        item.ItemsSource = targets.Select(a =>
        {
            var target = new MenuItem { Header = a.Display, Name = "MoveTo" + a.Name.Replace(" ", "") };
            target.Click += (_, _) => model.MoveEntries(rows, a.Name);
            return target;
        }).ToList();
        item.IsEnabled = targets.Count > 0;
        return item;
    }
    // Deletes the clicked entry, or every selected block when the clicked one is selected, as one undoable edit. Backspace does the same.
    private static MenuItem DeleteMenu(TimerModel model, IReadOnlyList<SessionRow> clicked)
    {
        var selected = model.IsSelected(clicked);
        var rows = selected ? model.Engine.Sessions.Where(model.SelectedRows.Contains).ToList() : clicked.ToList();
        var item = new MenuItem { Name = "DeleteMenu", Header = rows.Count > 1 ? $"Delete {rows.Count} blocks" : "Delete", IsEnabled = rows.Count > 0 };
        item.Click += (_, _) => { if (selected) model.DeleteSelection(); else model.DeleteEntries(rows); };
        return item;
    }
    // Copies the clicked block, or every selected block when the clicked one is selected. Ctrl+C does the same for the selection.
    private static MenuItem CopyMenu(TimerModel model, IReadOnlyList<SessionRow> clicked)
    {
        var rows = model.IsSelected(clicked) ? model.Engine.Sessions.Where(model.SelectedRows.Contains).ToList() : clicked.ToList();
        var item = new MenuItem { Name = "CopyBlocksMenu", Header = rows.Count > 1 ? $"Copy {rows.Count} blocks" : "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control), IsEnabled = rows.Count > 0 };
        item.Click += (_, _) => model.CopyBlocks(rows);
        return item;
    }
    private static MenuItem MergeMenu(TimerModel model)
    {
        var item = new MenuItem { Name = "MergeSelectedMenu", Header = "Merge selected", IsEnabled = model.CanMergeSelection };
        ToolTip.SetTip(item, model.MergeHint); item.Click += (_, _) => model.MergeSelection(); return item;
    }
    // The same for a timeline that scrolls along its width.
    private static void DeferOffsetAcross(ScrollViewer scroll)
    {
        scroll.LayoutUpdated += (_, _) =>
        {
            if (scroll.Tag is Vector offset && scroll.Viewport.Width > 0 && scroll.Extent.Width > scroll.Viewport.Width)
            { scroll.Offset = offset; scroll.Tag = null; }
        };
    }
}
