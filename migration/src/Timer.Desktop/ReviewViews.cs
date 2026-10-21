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
    public const double MinimumBlockHeight = 8;
    // Every block has the same small corner at every zoom level, only a block too short to hold it is rounded as far as it can be.
    public const double BlockRadius = 6;
    // Where the spine runs and where blocks start, from the left of the timeline.
    private const double SpineX = 58, CardLeft = 80, LabelRight = 40, MeetingLaneWidth = 132, PromptWidth = 380;
    public static Control Timeline(TimerModel model)
    {
        var day = model.SelectedDate;
        var root = Ui.Grid("*", "Auto,Auto,*");
        // The selection line is shown in the message bar over the buttons, not here, so nothing moves when blocks are selected.
        var pixelsPerHour = model.TimelinePixelsPerHour;
        // The hours run down a spine with a tick for each hour, and for each half and quarter hour as you zoom in. Blocks sit to the right of it,
        // A little room above and below lets the first and last labels sit fully in view.
        var canvas = new Canvas { Height = 24 * pixelsPerHour + 4, MinWidth = 460, Background = Brushes.Transparent, Margin = new Thickness(0, 10, 0, 10) };
        var spine = new Border { Name = "TimelineSpine", Width = 2, Height = 24 * pixelsPerHour, CornerRadius = new CornerRadius(1) }; spine.Classes.Add("rule");
        Canvas.SetLeft(spine, SpineX - 1); Canvas.SetTop(spine, 2); canvas.Children.Add(spine);
        void Tick(double hours, double width, double opacity)
        {
            var tick = new Border { Width = width, Height = 1.5, Opacity = opacity, Tag = "Tick" }; tick.Classes.Add("rule");
            Canvas.SetLeft(tick, SpineX - width - 3); Canvas.SetTop(tick, hours * pixelsPerHour + 1.5); canvas.Children.Add(tick);
        }
        for (var hour = 0; hour < 24; hour++)
        {
            // The hour labels share one right edge with the now chip, so the current time lines up with the hours instead of staggering against them.
            var label = Ui.Text($"{hour:00}:00", 12, "muted"); label.Classes.Add("figures"); label.Width = LabelRight; label.TextAlignment = TextAlignment.Right;
            Canvas.SetTop(label, hour * pixelsPerHour - 6); canvas.Children.Add(label);
            Tick(hour, 10, 1);
            if (pixelsPerHour >= 100) Tick(hour + .5, 6, .8);
            if (pixelsPerHour >= 260) { Tick(hour + .25, 4, .6); Tick(hour + .75, 4, .6); }
            // A faint guide across, so a block far from the spine can still be read against the hours.
            var guide = Ui.Rule(); guide.Width = 390; guide.Opacity = .35; guide.Tag = "HourGuide";
            Canvas.SetLeft(guide, CardLeft - 6); Canvas.SetTop(guide, hour * pixelsPerHour + 1.5); canvas.Children.Add(guide);
        }
        // Meetings planned for the day sit in their own lane down the right-hand side, so tracked time recorded over them never hides them. A dashed outline with
        // a faint fill and no account strip keeps planned time from being mistaken for tracked time, and nothing here is ever counted.
        var meetingBands = new List<(Panel Band, int Lane)>(); var meetingEnds = new List<TimeSpan>();
        foreach (var meeting in model.MeetingsOn(day))
        {
            var lane = meetingEnds.FindIndex(end => end <= meeting.At!.Value);
            if (lane < 0) { lane = meetingEnds.Count; meetingEnds.Add(meeting.End!.Value); } else meetingEnds[lane] = meeting.End!.Value;
            var bandHeight = Math.Max(MinimumBlockHeight, (meeting.End!.Value - meeting.At!.Value).TotalHours * pixelsPerHour - 4);
            var outline = new Avalonia.Controls.Shapes.Rectangle { RadiusX = BlockRadius, RadiusY = BlockRadius, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 } };
            outline.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, outline.GetResourceObservable("Muted"));
            outline.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, outline.GetResourceObservable("Hover"));
            var words = NoteShorthand.Display(meeting, model.AccountNames);
            var label = new StackPanel { Spacing = 1, Margin = new Thickness(8, 4, 6, 2) };
            var name = Ui.Text(words, 12); name.FontWeight = FontWeight.SemiBold; name.TextTrimming = TextTrimming.CharacterEllipsis; label.Children.Add(name);
            if (bandHeight >= 40) label.Children.Add(Ui.Text(NoteTimes.Of(meeting) + " · planned", 11, "muted"));
            var band = new Panel { Name = "TimelineMeeting_" + meeting.Id, Tag = "Meeting", Width = MeetingLaneWidth - 6, Height = bandHeight, ClipToBounds = true };
            band.Children.Add(outline); if (bandHeight >= 22) band.Children.Add(label);
            ToolTip.SetTip(band, $"Planned: {words} · {NoteTimes.Of(meeting)}\nNot tracked time. It asks whether it took place when it ends.");
            Avalonia.Automation.AutomationProperties.SetName(band, $"Planned meeting: {words}, {NoteTimes.Of(meeting)}");
            Canvas.SetLeft(band, CardLeft); Canvas.SetTop(band, meeting.At!.Value.TotalHours * pixelsPerHour + 2); canvas.Children.Add(band); meetingBands.Add((band, lane));
        }
        var groups = model.Editor.Entries(day, day.AddDays(1)).Where(e => e.GroupId is not null && e.Sources.Any(s => s.Duration > TimeSpan.Zero)).ToArray();
        var grouped = groups.SelectMany(e => e.Sources).ToHashSet();
        var intervals = model.Engine.Sessions.Where(s => s.Day == day && s.Duration > TimeSpan.Zero && !grouped.Contains(s))
            .Select(s => (s.Account, Start: s.From, End: s.To, Row: (SessionRow?)s, Group: (ReviewEntry?)null)).ToList();
        if (model.Engine.Active is { } active && active.Start < day.AddDays(1) && model.Engine.Now > day)
            intervals.Add((active.Account, active.Start < day ? day : active.Start, model.Engine.Now < day.AddDays(1) ? model.Engine.Now : day.AddDays(1), null, null));
        foreach (var group in groups)
        {
            var sections = new List<List<SessionRow>>();
            foreach (var row in group.Sources.Where(s => s.Duration > TimeSpan.Zero).OrderBy(s => s.From))
            {
                if (sections.Count == 0 || Math.Abs((row.From - sections[^1][^1].To).TotalSeconds) > 1 + 1e-8) sections.Add(new());
                sections[^1].Add(row);
            }
            foreach (var section in sections)
            {
                var display = group with { Start = section.Min(s => s.From), End = section.Max(s => s.To), Minutes = section.Sum(s => decimal.Parse(s.Minutes, System.Globalization.CultureInfo.InvariantCulture)) };
                intervals.Add((group.Account, display.Start!.Value, display.End!.Value, null, display));
            }
        }
        var ends = new List<DateTime>();
        var cards = new List<(Border Card, int Lane)>();
        foreach (var interval in intervals.OrderBy(s => s.Start))
        {
            // Blocks that overlap by a second or two, which older recordings can have, still count as meeting, so they share a lane instead of splitting the whole day in two.
            var lane = ends.FindIndex(end => end <= interval.Start.AddSeconds(2));
            if (lane < 0) { lane = ends.Count; ends.Add(interval.End); } else ends[lane] = interval.End;
            var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(interval.Account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
            var look = model.BlockLook(colour); colour = look.Fill;
            var end = interval.End > day.AddDays(1) ? day.AddDays(1) : interval.End;
            var duration = interval.Group is { } groupDuration ? TimeSpan.FromMinutes((double)groupDuration.Minutes) : end - interval.Start;
            // Even a one-minute block is drawn at least eight pixels tall, so it can be seen and clicked. Zoom in for exact edges.
            var height = Math.Max(MinimumBlockHeight, (end - interval.Start).TotalHours * pixelsPerHour - 4);
            var ink = look.Text;
            var text = new StackPanel { Spacing = 5, Margin = new Thickness(12, height < 58 ? 2 : 10, 14, height < 58 ? 2 : 10) };
            var title = Ui.Text(model.Label(interval.Account) + (interval.Group is { } labelGroup ? $" · Group ({labelGroup.Sources.Count})" : ""), 15); title.FontWeight = FontWeight.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(title);
            // The time range sits quietly, and the credited duration beside it is the figure that matters.
            if (height >= 58)
            {
                var range = Ui.Text($"{interval.Start:HH:mm} – {end:HH:mm}", 12); range.Opacity = .78;
                var length = Ui.Text(model.Format(duration), 12); // Plain weight, so it reads easily on a tinted block in either theme.
                var detail = Ui.Horizontal(range, length); detail.Spacing = 8; detail.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                text.Children.Add(detail);
            }
            foreach (var child in text.GetLogicalDescendants().OfType<TextBlock>()) child.Foreground = Brush.Parse(ink);
            var card = new Border { Background = Brush.Parse(colour), CornerRadius = new CornerRadius(Math.Min(BlockRadius, height / 2)),
                Width = 380, Height = height, ClipToBounds = true };
            var selectionSources = interval.Group?.Sources ?? (interval.Row is { } source ? new[] { source } : Array.Empty<SessionRow>());
            var isSelected = model.IsSelected(selectionSources);
            // A strip down the left side in the account's own colour, so a block can be told apart at a glance whatever its fill is.
            // A selected block shows it in the theme's accent colour instead, a little wider. Nothing is added to the text, so the block never shifts or clips.
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions(isSelected ? "8,*" : "5,*") };
            var edgeStrip = new Border { Name = "BlockEdge" };
            if (isSelected) edgeStrip.Classes.Add("selectedEdge"); else edgeStrip.Background = Brush.Parse(look.Edge);
            content.Children.Add(edgeStrip);
            if (height >= 24) { Grid.SetColumn(text, 1); content.Children.Add(text); }
            card.Child = content;
            if (isSelected) card.Classes.Add("mergeSelected");
            var note = interval.Group is { } groupNote ? "Grouped entries · " + groupNote.Note : interval.Row is null ? "Recording now · stop tracking to move or edit" : model.Editor.Note(interval.Row);
            if (height >= 90 && !string.IsNullOrWhiteSpace(note))
            {
                var noteText = Ui.Text(note, 12); noteText.Foreground = Brush.Parse(ink);
                noteText.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(noteText);
            }
            ToolTip.SetTip(card, $"{model.Label(interval.Account)} · {interval.Start:HH:mm:ss} – {end:HH:mm:ss} · {model.Format(duration)}\n{note}");
            if (interval.Row is { } row)
            {
                card.Tag = row; card.Name = "TimelineEntry" + model.Engine.Sessions.IndexOf(row);
                var edit = new MenuItem { Header = "Edit entry / note…" }; edit.Click += (_, _) => model.OpenEntry(row);
                card.ContextMenu = new ContextMenu { ItemsSource = new Control[] { edit, MoveMenu(model, new[] { row }), CopyMenu(model, new[] { row }), MergeMenu(model), new Separator(), DeleteMenu(model, new[] { row }) } };
                TimelineGestures.Attach(card, canvas, model, row, pixelsPerHour);
            }
            if (interval.Row is null && interval.Group is null)
            {
                // The block that is recording now. A green outline, a label and the stop menu make it clear why it can't be dragged.
                card.Classes.Add("recording"); card.Name = "TimelineRecording"; card.Cursor = new Cursor(StandardCursorType.Arrow);
                var live = Ui.Text("● Recording now", 12); live.Foreground = Brush.Parse(ThemeColours.Contrast("#1E5C38", colour) >= 4.5 ? "#1E5C38" : ink); live.FontWeight = FontWeight.SemiBold;
                // The green only where it reads on the block, otherwise the block's own text colour, which always does.
                if (height >= 96) text.Children.Insert(model.IsSelected(selectionSources) ? 1 : 0, live);
                else if (text.Children.Count > 0 && text.Children[0] is TextBlock liveTitle) liveTitle.Text = "● " + liveTitle.Text;
                var stop = new MenuItem { Header = "Stop tracking" }; stop.Click += (_, _) => model.TogglePause();
                var again = new MenuItem { Name = "ContinuePreviousBlockMenu", Header = model.ContinuePreviousText ?? "Continue earlier block", IsVisible = model.CanContinuePrevious };
                again.Click += (_, _) => model.ContinuePrevious();
                card.ContextMenu = new ContextMenu { ItemsSource = new[] { stop, again } };
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
                    { var movement = e.GetPosition(canvas) - p; if (movement.X * movement.X + movement.Y * movement.Y <= 25) model.ToggleSelection(group.Sources); }
                    groupPress = null; e.Handled = true;
                };
                card.Tag = group; card.Name = "TimelineGroup" + group.GroupId + "-" + interval.Start.ToString("HHmmss");
                var editGroup = new MenuItem { Header = "Inspect / edit grouped entries…" }; editGroup.Click += (_, _) => model.OpenGroup(group.GroupId!);
                var ungroup = new MenuItem { Header = "Ungroup" }; ungroup.Click += (_, _) => model.UngroupEntries(group.GroupId!);
                card.ContextMenu = new ContextMenu { ItemsSource = new Control[] { editGroup, ungroup, CopyMenu(model, group.Sources), MergeMenu(model) } };
                ToolTip.SetTip(card, $"{model.Label(group.Account)} · {group.Sources.Count} grouped entries · {model.Format(duration)}\nClick to select");
            }
            Canvas.SetTop(card, (interval.Start - day).TotalHours * pixelsPerHour + 2);
            canvas.Children.Add(card); cards.Add((card, lane));
        }
        // A meeting that has ended asks here, under its block, whether it took place. It floats over the tracked blocks at the right, and answering it clears it (No ticks the
        // note off; Yes opens Add time on its hours). The dashboard's floating card leaves this one out while the timeline is showing (TimerModel.TimelineVisible).
        var prompts = new List<Border>();
        foreach (var ask in model.Reminders.Where(r => r.State == ReminderState.Followup && r.Note.Day.Date == day.Date && r.Note.End is not null))
        {
            var id = ask.Note.Id;
            var question = new StackPanel { Spacing = 0, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            question.Children.Add(new TextBlock { Text = "Did it take place?", FontSize = 13, FontWeight = FontWeight.SemiBold });
            question.Children.Add(Ui.Text(NoteShorthand.Display(ask.Note, model.AccountNames) + " · " + NoteTimes.Of(ask.Note), 12, "muted"));
            Button Small(Button button, bool main = false) { button.Classes.Add("toolButton"); button.Classes.Add("quiet"); button.Height = 28; button.Padding = new Thickness(10, 0); button.FontSize = 12; if (main) button.Classes.Add("mainAction"); return button; }
            var no = Small(Ui.Button("No", () => model.DoneReminder(id), "TimelinePromptNo_" + id));
            var yes = Small(Ui.Button("Yes", () => model.LogMeeting(id), "TimelinePromptYes_" + id), true);
            var later = Small(ReminderUi.SnoozeButton(model, () => id, "TimelinePromptLater_" + id)); later.Content = "…"; ToolTip.SetTip(later, "Ask me again later");
            Avalonia.Automation.AutomationProperties.SetName(later, "Ask me again later");
            var buttons = Ui.Horizontal(no, yes, later); buttons.Spacing = 6; buttons.Margin = new Thickness(10, 0, 0, 0);
            var promptRow = Ui.Grid("Auto,*,Auto", "Auto"); var clock = new IconGlyph(IconKind.Note) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Ui.Put(promptRow, clock); Ui.Put(promptRow, question, 1); Ui.Put(promptRow, buttons, 2);
            var prompt = new Border { Name = "TimelinePrompt_" + id, Tag = "MeetingPrompt", Width = PromptWidth, Child = promptRow, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 8, 8) };
            prompt.Classes.Add("messageBar");
            Avalonia.Automation.AutomationProperties.SetName(prompt, "Did the meeting take place? " + NoteShorthand.Display(ask.Note, model.AccountNames));
            Canvas.SetLeft(prompt, CardLeft); Canvas.SetTop(prompt, ask.Note.End!.Value.TotalHours * pixelsPerHour + 6); canvas.Children.Add(prompt); prompts.Add(prompt);
        }
        // For today, a line across at the current time with a chip in the margin saying what it is. It moves on every half minute while the timeline is on screen.
        if (day == model.Engine.Now.Date)
        {
            var nowLine = new Border { Name = "NowMarker", Height = 2, Tag = "NowLine", IsHitTestVisible = false }; nowLine.Classes.Add("nowLine");
            var nowDot = new Avalonia.Controls.Shapes.Ellipse { Width = 13, Height = 13, Tag = "NowDot", IsHitTestVisible = false }; nowDot.Classes.Add("nowDot");
            var nowText = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Classes = { "figures", "nowChipText" } };
            // The chip is wider than its text, with the text's right edge where the hour labels' right edges are.
            var nowChip = new Border { Name = "NowChip", Child = nowText, CornerRadius = new CornerRadius(10), Width = LabelRight + 8, Height = 20, Padding = new Thickness(0, 0, 8, 0), IsHitTestVisible = false }; nowChip.Classes.Add("nowChip");
            canvas.Children.Add(nowLine); canvas.Children.Add(nowDot); canvas.Children.Add(nowChip);
            void PlaceNow()
            {
                var now = model.Engine.Now; var y = (now - day).TotalHours * pixelsPerHour + 2;
                Canvas.SetLeft(nowLine, SpineX); Canvas.SetTop(nowLine, y - 1);
                Canvas.SetLeft(nowDot, SpineX - 6.5); Canvas.SetTop(nowDot, y - 6.5);
                nowText.Text = now.ToString("HH:mm"); Canvas.SetLeft(nowChip, 0); Canvas.SetTop(nowChip, y - 10);
            }
            PlaceNow();
            DispatcherTimer? nowTimer = null;
            canvas.AttachedToVisualTree += (_, _) => { nowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; nowTimer.Tick += (_, _) => PlaceNow(); nowTimer.Start(); };
            canvas.DetachedFromVisualTree += (_, _) => { nowTimer?.Stop(); nowTimer = null; };
        }
        void Resize()
        {
            foreach (var guide in canvas.Children.OfType<Border>().Where(b => b.Tag is "HourGuide")) guide.Width = Math.Max(220, canvas.Bounds.Width - CardLeft);
            // Tracked blocks share what is left after the meeting lanes, which sit at the right-hand edge.
            var trackedWidth = Math.Max(220, canvas.Bounds.Width - CardLeft - 10 - meetingEnds.Count * MeetingLaneWidth);
            foreach (var prompt in prompts) Canvas.SetLeft(prompt, Math.Max(CardLeft, canvas.Bounds.Width - 10 - PromptWidth));
            foreach (var meeting in meetingBands) Canvas.SetLeft(meeting.Band, CardLeft + trackedWidth + 6 + meeting.Lane * MeetingLaneWidth);
            var width = trackedWidth / Math.Max(1, ends.Count);
            foreach (var card in cards) { card.Card.Width = Math.Max(40, width - 6); Canvas.SetLeft(card.Card, CardLeft + card.Lane * width); }
            foreach (var marker in canvas.Children.OfType<Border>().Where(b => b.Tag is "NowLine")) marker.Width = Math.Max(220, canvas.Bounds.Width - SpineX);
        }
        canvas.SizeChanged += (_, _) => Resize();
        DateTime clicked = day.AddHours(9);
        DateTime At(double y) => day.AddMinutes(Math.Clamp(Math.Floor(y / pixelsPerHour * 60 / 5) * 5, 0, 1435));
        var add = new MenuItem { Header = "Add time…" }; add.Click += (_, _) => model.OpenGap(clicked);
        var tidy = new MenuItem { Header = "Tidy day…", Name = "TidyDayMenu" }; tidy.Click += (_, _) => model.OpenTidy();
        // Paste puts the copied blocks with the first at the time that was right-clicked, snapped to five minutes like Add time.
        var paste = new MenuItem { Header = "Paste here", Name = "PasteHereMenu", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control), IsEnabled = model.CanPaste };
        paste.Click += (_, _) => model.PasteBlocks(clicked);
        canvas.ContextMenu = new ContextMenu { ItemsSource = new Control[] { add, paste, tidy, MergeMenu(model) } };
        // Ctrl+V pastes where the pointer last was on this timeline.
        canvas.PointerMoved += (_, e) => model.PasteTime = At(e.GetPosition(canvas).Y);
        canvas.PointerExited += (_, _) => model.PasteTime = null;
        bool OnBlock(object? source) => source is Visual visual && (visual is Border { Tag: SessionRow or ReviewEntry }
            || visual.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is SessionRow or ReviewEntry));
        Point? emptyPress = null;
        canvas.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(canvas).Properties.IsRightButtonPressed) clicked = At(e.GetPosition(canvas).Y);
            else if (e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed && !OnBlock(e.Source)
                && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // A double-click adds time. A single click only clears the selection, so stray clicks add nothing.
                if (e.ClickCount == 2) { model.ClearSelection(); model.OpenGap(At(e.GetPosition(canvas).Y)); e.Handled = true; }
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
        var scroll = new ScrollViewer { Name = "TimelineScroll", Content = canvas,
            Tag = new Vector(0, model.TimelineOffset ?? Math.Max(0, (first - day).TotalHours * pixelsPerHour - 24)),
            AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        scroll.ScrollChanged += (_, _) =>
        { if (scroll.Tag is null && scroll.Viewport.Height > 0) model.TimelineOffset = scroll.Offset.Y; };
        scroll.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            e.Handled = true;
            model.ZoomTimeline(e.Delta.Y, e.GetPosition(scroll).Y, scroll.Offset.Y);
        }, RoutingStrategies.Tunnel);
        DeferOffset(scroll);
        Ui.Put(root, scroll, row: 2);
        return root;
    }

    /// <summary>A raised tile in the Time Deck style, used to set the parts of the week views apart.</summary>
    private static Border Card(Control child, Thickness padding)
    {
        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = padding, Child = child };
        card.Classes.Add("dayCard");
        return card;
    }
    public static Control Week(TimerModel model)
    {
        var day = model.WeekStart(model.SelectedDate);
        var days = Enumerable.Range(0, 7).Where(i => model.Engine.Schedule.Days.Contains(day.AddDays(i).DayOfWeek)
            || model.Engine.Sessions.Any(s => s.Day == day.AddDays(i))
            || (model.Engine.Active is not null && day.AddDays(i) == model.Engine.Now.Date)).ToArray();
        if (days.Length == 0) days = Enumerable.Range(0, 7).ToArray();
        var reconciled = new WeekExport(model.Editor, model.Engine, day);
        var table = WeekTable.Build(model, reconciled, day, days);
        // The week is split into three parts so no single screen is a wall of numbers: an overview first, the full table, and the entries.
        // Room on the right so the scroll bar never sits over the figures.
        var root = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 16, 12) };
        switch (model.WeekSection)
        {
            case "hours": HoursSection(model, table, days, root); break;
            case "entries": EntriesSection(model, reconciled, root); break;
            default: OverviewSection(model, table, day, days, root); break;
        }
        var scroll = new ScrollViewer { Content = root, AllowAutoHide = false, Tag = Vector.Zero,
            // The overview is laid out to the width it is given, so it never needs a sideways bar. The table and entries may be wider than the window.
            HorizontalScrollBarVisibility = model.WeekSection is "hours" or "entries" ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        DeferOffset(scroll);
        return Ui.WithMoreBelow(scroll);
    }
    private static void OverviewSection(TimerModel model, WeekTable table, DateTime day, int[] days, StackPanel root)
    {
        TextBlock Tile(string caption, string value, string? style = null)
        { var tile = Ui.Text(value, 30, style); tile.Classes.Add("figures"); return tile; }
        Border Stat(string caption, string value, string tip, string? style = null)
        {
            var stat = new StackPanel { Spacing = 4 };
            stat.Children.Add(Ui.Text(caption, 12, "muted")); stat.Children.Add(Tile(caption, value, style));
            var card = Card(stat, new Thickness(18, 14)); ToolTip.SetTip(card, tip); return card;
        }
        WeekTableRow Find(string label) => table.Rows.First(r => r.Label == label);
        string Shown(string label) => Find(label).Total == "–" ? "0:00" : Find(label).Total;
        // Working time is the whole of the week's work: what was given to accounts plus what was not. Breaks sit apart from it, quieter.
        var stats = Ui.Grid("*,*,*");
        var worked = Stat("Working time", Shown(WeekTable.WorkingLabel), "Allocated plus unallocated. Breaks are not included.");
        var allocated = Stat("Allocated to accounts", Shown(WeekTable.AllocatedLabel), "Time given to client accounts.");
        var unallocated = Stat("Unallocated", Shown(WeekTable.UnallocatedLabel), "Working time not given to any account.", "muted");
        allocated.Margin = new Thickness(6, 0); worked.Margin = new Thickness(0, 0, 6, 0); unallocated.Margin = new Thickness(6, 0, 0, 0);
        Ui.Put(stats, worked); Ui.Put(stats, allocated, 1); Ui.Put(stats, unallocated, 2);
        root.Children.Add(stats);
        var breakNote = Ui.Text($"Breaks {Shown(WeekTable.BreaksLabel)} this week, not counted in working time", 12, "muted"); breakNote.Name = "BreaksNote"; breakNote.Margin = new Thickness(4, -6, 0, 0);
        root.Children.Add(breakNote);
        var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var bars = new WeekBars
        {
            Bars = days.Select(i =>
            {
                var date = day.AddDays(i);
                var segments = accounts.Select(a => new BarSegment(a.Display, Color.Parse(model.ChartColour(a.Colour)), model.OnDay(a, date).TotalHours)).ToList();
                // Time in the working day that went to no account stands on the column as a neutral block, so the column is that day's whole working time.
                var totals = model.Engine.Totals(date); var left = (totals.Working + totals.Overtime).TotalHours - segments.Sum(s => s.Hours);
                if (left > 1.0 / 120) segments.Add(new BarSegment(WeekTable.UnallocatedLabel, Color.Parse(ThemeColours.Mix(model.CurrentTheme.Colour("Surface"), model.CurrentTheme.Colour("Ink"), .22)), left));
                var total = segments.Sum(s => s.Hours);
                var detail = string.Join("\n", segments.Where(s => s.Hours > 0).Select(s => $"{s.Account}  {model.FormatShort(TimeSpan.FromHours(s.Hours))}"));
                return new DayBar(date.ToString("ddd"), date.ToString("dddd d MMMM") + (detail.Length > 0 ? $"\nWorking time {model.FormatShort(TimeSpan.FromHours(total))}\n" + detail : "\nNothing recorded"),
                    total > 0 ? model.FormatShort(TimeSpan.FromHours(total)) : "–", total, segments);
            }).ToList()
        };
        root.Children.Add(Card(bars, new Thickness(14, 10, 14, 6)));
        // The accounts sit on their own tile under the chart, one line each: its colour, its week, and its budget when it has one.
        // Two different things are shown for each account and they are headed apart: the time worked in the week on show, and the budget used over its whole
        // period (which can be longer than the week, and for an earlier week is named so it cannot be mistaken for the present).
        var list = new StackPanel { Spacing = 6 };
        var heads = Ui.Grid("30,*,80,96,220");
        Ui.Put(heads, Ui.Text("By account", 12, "muted"), 1);
        var weekHead = Ui.Text("This week", 12, "muted"); weekHead.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; Ui.Put(heads, weekHead, 2);
        var budgetHead = Ui.Text("Budget used over its period", 12, "muted"); budgetHead.Margin = new Thickness(18, 0, 0, 0); Ui.Put(heads, budgetHead, 3).SetValue(Grid.ColumnSpanProperty, 2);
        list.Children.Add(heads);
        for (var i = 0; i < accounts.Count; i++)
        {
            var account = accounts[i]; var row = table.Rows[i];
            // Every row is one line with everything centred on it, so a budget meter always sits level with its account.
            var line = Ui.Grid("30,*,80,96,220"); line.MinHeight = 36;
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Brush.Parse(model.ChartColour(account.Colour)), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Ui.Put(line, dot); Ui.Put(line, Ui.Text(account.Display, 14), 1);
            var total = Ui.Text(row.Total == "–" ? "0:00" : row.Total, 14); total.Classes.Add("figures"); total.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; Ui.Put(line, total, 2);
            var budget = table.Budgets.FirstOrDefault(b => b.Account == account.Display);
            if (budget is not null)
            {
                var track = Ui.Grid("0*,1*"); track.Height = 6; track.Margin = new Thickness(18, 0, 0, 0); track.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                var fraction = Math.Clamp(budget.Fraction, 0.0, 1.0);
                track.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star); track.ColumnDefinitions[1].Width = new GridLength(1.0 - fraction, GridUnitType.Star);
                var rest = new Border { CornerRadius = new CornerRadius(3), Height = 6 }; rest.Classes.Add("rule"); Grid.SetColumn(rest, 1);
                var fill = new Border { CornerRadius = new CornerRadius(3), Height = 6 }; fill.Classes.Add("budgetFill");
                fill.Classes.Set("budgetWarn", budget.Fraction >= .8 && budget.Fraction < 1); fill.Classes.Set("budgetOver", budget.Fraction >= 1);
                track.Children.Add(rest); track.Children.Add(fill);
                Ui.Put(line, track, 3);
                var caption = Ui.Text($"{budget.Used} / {budget.Budget} {budget.Period.ToLowerInvariant()}", 12, budget.Fraction >= 1 ? "overtime" : budget.Fraction >= .8 ? "budgetWarning" : "muted");
                caption.Name = "BudgetCaption"; ToolTip.SetTip(caption, $"{budget.Fraction * 100:0}% of the {budget.Period.ToLowerInvariant()} budget used");
                caption.Margin = new Thickness(12, 0, 0, 0); caption.TextTrimming = TextTrimming.CharacterEllipsis; Ui.Put(line, caption, 4);
            }
            list.Children.Add(line);
        }
        root.Children.Add(Card(list, new Thickness(18, 14, 18, 8)));
    }
    private static void HoursSection(TimerModel model, WeekTable table, int[] days, StackPanel root)
    {
        var grid = Ui.Grid("150," + string.Join(",", days.Select(_ => "*")) + ",70", "Auto," + string.Join(",", table.Rows.Select(_ => "Auto")));
        grid.MinWidth = 150 + days.Length * 46;
        // Each row is one line with a hairline under it, and the figures use the display face's even-width digits so the columns read straight down.
        for (var c = 0; c < table.Headers.Length; c++)
        {
            var heading = Ui.Text(c == 0 ? table.Headers[c] : table.Headers[c].Split(' ')[0], 12, "muted");
            if (c > 0) heading.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            heading.Margin = new Thickness(0, 0, 0, 6);
            Ui.Put(grid, heading, c);
        }
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var item = table.Rows[r];
            TextBlock Cell(string text, bool right)
            {
                var block = Ui.Text(text, item.Style == "bold" ? 14 : 13, item.Style == "faint" || text == "–" ? "muted" : null);
                if (item.Style is "bold" or "subtotal") block.FontWeight = FontWeight.SemiBold;
                if (right) block.Classes.Add("figures");
                block.Margin = new Thickness(0, 10);
                if (right) block.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                return block;
            }
            var shown = Ui.Put(grid, Cell(item.Label, false), 0, r + 1);
            ToolTip.SetTip(shown, item.Label switch
            {
                WeekTable.AllocatedLabel => "The accounts' rows added up.",
                WeekTable.UnallocatedLabel => "Working time not given to any account.",
                WeekTable.WorkingLabel => "Allocated plus unallocated. Breaks are not included.",
                WeekTable.BreaksLabel => "Kept apart from working time.",
                _ => null
            });
            for (var i = 0; i < item.Cells.Length; i++) Ui.Put(grid, Cell(item.Cells[i], true), i + 1, r + 1);
            Ui.Put(grid, Cell(item.Total, true), item.Cells.Length + 1, r + 1);
            // A strong rule above the total, and a faint hairline under every other row.
            if (item.Style is "bold" or "subtotal") Ui.Put(grid, new Border { Height = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Classes = { "rule" } }, 0, r + 1).SetValue(Grid.ColumnSpanProperty, table.Headers.Length);
            else if (r < table.Rows.Count - 1) Ui.Put(grid, new Border { Height = 1, Opacity = .45, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Classes = { "rule" } }, 0, r + 1).SetValue(Grid.ColumnSpanProperty, table.Headers.Length);
        }
        // Both headings on this tab are real headings, so the table and the budgets read as two distinct parts.
        TextBlock Heading(string text) { var h = Ui.Text(text, 16); h.FontWeight = FontWeight.SemiBold; return h; }
        var heading2 = Ui.Grid("*,Auto");
        Ui.Put(heading2, Heading(model.DecimalHours ? "Decimal hours" : "Hours and minutes"));
        // Copy and export live on this line, with the table they act on. Both are icons with tooltips.
        var copy = Ui.IconButton(IconKind.Copy, "Copy hours table", "CopyHours", () => { });
        ToolTip.SetTip(copy, "Copy table");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(table.ToTsv());
            ToolTip.SetTip(copy, "Copied");
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            reset.Tick += (_, _) => { reset.Stop(); ToolTip.SetTip(copy, "Copy the table to the clipboard, ready to paste into a timesheet"); };
            reset.Start();
        };
        var export = Ui.IconButton(IconKind.Export, "Export hours…", "ExportWeek", () => { if (TopLevel.GetTopLevel(root) is DashboardWindow dashboard) dashboard.Dialogs.Show(new ExportDialog(model)); });
        Ui.Put(heading2, Ui.Horizontal(copy, export), 1);
        root.Children.Add(heading2);
        root.Children.Add(Card(grid, new Thickness(16, 12, 16, 6)));
        if (table.Budgets.Count > 0)
        {
            root.Children.Add(Heading("Budgets"));
            var budgets = Ui.Grid("150,*,*,*,60", "Auto," + string.Join(",", table.Budgets.Select(_ => "Auto")));
            // Four plainly different figures: the budget, how much of it is used, how much is left, and the share used.
            foreach (var (text, c) in new[] { "Account", "Budget", "Used", "Remaining", "% used" }.Select((t, c) => (t, c)))
            {
                var h = Ui.Text(text, 12, "muted"); if (c > 0) h.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; Ui.Put(budgets, h, c);
            }
            for (var r = 0; r < table.Budgets.Count; r++)
            {
                var b = table.Budgets[r];
                var cells = new[] { b.Account, b.Budget, b.Used, b.Remaining, $"{b.Fraction * 100:0}%" };
                for (var c = 0; c < cells.Length; c++)
                {
                    var block = Ui.Text(cells[c], 13, c == 4 ? (b.Fraction >= 1 ? "overtime" : b.Fraction >= .8 ? "budgetWarning" : null) : null);
                    block.Margin = new Thickness(0, 8); if (c > 0) { block.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; block.Classes.Add("figures"); }
                    // The account's cell carries the period the figures belong to, such as "September 2026" when an earlier week is on show.
                    if (c == 0) { var stack = new StackPanel { Spacing = 1, Margin = new Thickness(0, 6) }; block.Margin = new Thickness(0); stack.Children.Add(block); var period = Ui.Text(b.Period, 11, "muted"); period.Name = "BudgetPeriod"; stack.Children.Add(period); Ui.Put(budgets, stack, c, r + 1); continue; }
                    Ui.Put(budgets, block, c, r + 1);
                }
            }
            root.Children.Add(Card(budgets, new Thickness(16, 12, 16, 6)));
        }
    }
    private static void EntriesSection(TimerModel model, WeekExport reconciled, StackPanel root)
    {
        root.Children.Add(Ui.Text(model.SelectedBlockCount > 0 ? "" : "Click an entry to select it, double-click to edit, right-click to edit or merge", 12, "muted"));
        var entries = reconciled.Entries.ToList();
        string Length(int index) => model.DecimalHours ? WeekExport.Hours(reconciled.EntryUnits[index]) + "h" : model.Format(TimeSpan.FromMinutes((double)entries[index].Minutes));
        // Entries are grouped under their day, each day on its own tile with its total, so a week reads as seven short lists rather than one long one.
        foreach (var dayGroup in entries.Select((entry, index) => (entry, index)).GroupBy(x => x.entry.Day))
        {
            var header = Ui.Grid("*,Auto"); header.Margin = new Thickness(4, 6, 4, 0);
            var title = Ui.Text(dayGroup.Key.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), 14); title.FontWeight = FontWeight.SemiBold;
            var counted = dayGroup.Where(x => !BreakAccount.Is(x.entry.Account)).ToList();
            // The recording that is running now is part of the day's total, so this agrees with the live tracker, though it is not an entry until it stops.
            var running = model.Engine.ActiveElapsedOn(dayGroup.Key);
            var dayTotal = model.DecimalHours ? WeekExport.Hours(counted.Sum(x => reconciled.EntryUnits[x.index]) + (long)Math.Round(running.TotalHours * 100)) + "h"
                : model.Format(TimeSpan.FromMinutes((double)counted.Sum(x => x.entry.Minutes)) + running);
            var total = Ui.Text("Allocated " + dayTotal, 13, "muted"); total.Classes.Add("figures"); total.Name = "DayAllocated";
            ToolTip.SetTip(total, running > TimeSpan.Zero ? "Time given to accounts that day, including the recording still running." : "Time given to accounts that day. Breaks are not counted.");
            Ui.Put(header, title); Ui.Put(header, total, 1);
            root.Children.Add(header);
            var rows = new StackPanel { Spacing = 2 };
            // The entries come from the export in account order, which keeps exports tidy but reads oddly on screen. Within a day they run by start time instead,
            // with anything that has no clock time (a plain added entry) after the timed ones.
            foreach (var (entry, index) in dayGroup.OrderBy(x => x.entry.Start ?? DateTime.MaxValue).ThenBy(x => x.index))
            {
                void Open()
                {
                    if (entry.GroupId is { } group) model.OpenGroup(group);
                    else model.OpenEntry(entry.Sources[0]);
                }
                var caption = entry.GroupId is not null ? $"Group · {entry.Sources.Count} entries" : entry.Start is { } start && entry.End is { } end ? $"{start:HH:mm} – {end:HH:mm}" : entry.Type;
                var name = entry.Sources.Count == 1 ? "WeekEntry" + model.Engine.Sessions.IndexOf(entry.Sources[0]) : "WeekReviewEntry";
                var selectable = entry.Sources.Count > 0;
                var button = Ui.Button("", () => { if (selectable) model.ToggleSelection(entry.Sources); else Open(); }, name);
                button.Classes.Add("entryRow");
                button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                // The account's colour runs down the left as a strip, as on a timeline block, then the account and when, then the credited time.
                var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(entry.Account, StringComparison.OrdinalIgnoreCase))?.Colour;
                var selected = entry.Sources.Count > 0 && model.IsSelected(entry.Sources);
                var edge = new Border { Width = selected ? 6 : 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 8) };
                // A selected entry's strip is the theme's accent colour, as on the timeline.
                if (selected) edge.Classes.Add("selectedEdge"); else edge.Background = colour is null ? Brushes.Transparent : Brush.Parse(model.BlockLook(colour).Edge);
                var labels = new StackPanel { Spacing = 2, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(12, 6) };
                var accountText = Ui.Text(model.Label(entry.Account), 14); accountText.FontWeight = FontWeight.SemiBold;
                labels.Children.Add(accountText); labels.Children.Add(Ui.Text(caption, 12, "muted"));
                if (!string.IsNullOrWhiteSpace(entry.Note)) labels.Children.Add(new TextBlock { Text = entry.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
                var length = Ui.Text(Length(index), 14); length.FontWeight = FontWeight.SemiBold; length.Classes.Add("figures"); length.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; length.Margin = new Thickness(0, 0, 14, 0);
                var content = Ui.Grid(selected ? "6,*,Auto" : "4,*,Auto"); Ui.Put(content, edge); Ui.Put(content, labels, 1); Ui.Put(content, length, 2);
                button.Content = content;
                if (entry.Sources.Count > 0)
                {
                    button.Classes.Set("mergeSelected", model.IsSelected(entry.Sources));
                    var edit = new MenuItem { Header = "Edit / inspect entry…" }; edit.Click += (_, _) => Open();
                    button.ContextMenu = new ContextMenu { ItemsSource = new Control[] { edit, MoveMenu(model, entry.Sources), CopyMenu(model, entry.Sources), MergeMenu(model), new Separator(), DeleteMenu(model, entry.Sources) } };
                }
                if (entry.GroupId is { } groupId)
                {
                    var combined = Ui.Grid("*,Auto"); Ui.Put(combined, button);
                    Ui.Put(combined, Ui.Button("Ungroup", () => model.UngroupEntries(groupId), "Ungroup" + groupId), 1).Margin = new Thickness(8, 0, 6, 0);
                    rows.Children.Add(combined);
                }
                else rows.Children.Add(button);
            }
            root.Children.Add(Card(rows, new Thickness(6)));
        }
        // A recording that is running now on a day with no saved entries yet still counts for that day, so it gets its heading and total too.
        var today = model.Engine.Now.Date; var runningToday = model.Engine.ActiveElapsedOn(today);
        if (runningToday > TimeSpan.Zero && model.WeekStart(model.SelectedDate) <= today && today < model.WeekStart(model.SelectedDate).AddDays(7) && !entries.Any(e => e.Day == today))
        {
            var header = Ui.Grid("*,Auto"); header.Margin = new Thickness(4, 6, 4, 0);
            var title = Ui.Text(today.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), 14); title.FontWeight = FontWeight.SemiBold;
            var total = Ui.Text("Allocated " + (model.DecimalHours ? WeekExport.Hours((long)Math.Round(runningToday.TotalHours * 100)) + "h" : model.Format(runningToday)), 13, "muted"); total.Classes.Add("figures"); total.Name = "DayAllocated";
            ToolTip.SetTip(total, "The recording still running. It becomes an entry when it stops.");
            Ui.Put(header, title); Ui.Put(header, total, 1); root.Children.Add(header);
        }
    }
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
    private static void DeferOffset(ScrollViewer scroll)
    {
        // Hidden windows can attach before they have a viewport. Keep the requested
        // position until a visible layout can apply it, rather than saving a clamped zero.
        scroll.LayoutUpdated += (_, _) =>
        {
            if (scroll.Tag is Vector offset && scroll.Viewport.Height > 0 && scroll.Extent.Height > scroll.Viewport.Height)
            { scroll.Offset = offset; scroll.Tag = null; }
        };
    }
}
