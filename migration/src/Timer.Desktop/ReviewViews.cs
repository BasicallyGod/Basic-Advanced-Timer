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
    public static Control Timeline(TimerModel model)
    {
        var day = model.SelectedDate;
        var root = Ui.Grid("*", "Auto,Auto,*");
        // This line shows selection feedback only when there is a selection. Otherwise it takes no room at all.
        var caption = Ui.Text(model.SelectedBlockCount > 0 ? $"{model.SelectedBlockCount} blocks selected · " + model.MergeHint : "", 12, "muted");
        caption.IsVisible = model.SelectedBlockCount > 0;
        caption.TextWrapping = TextWrapping.Wrap;
        caption.Margin = new Thickness(0, 0, 0, 10);
        Ui.Put(root, caption);
        var pixelsPerHour = model.TimelinePixelsPerHour;
        var canvas = new Canvas { Height = 24 * pixelsPerHour, MinWidth = 460, Background = Brushes.Transparent };
        for (var hour = 0; hour < 24; hour++)
        {
            var label = Ui.Text($"{hour:00}:00", 12, "muted");
            Canvas.SetTop(label, hour * pixelsPerHour); canvas.Children.Add(label);
            var rule = Ui.Rule(); rule.Width = 390;
            Canvas.SetLeft(rule, 60); Canvas.SetTop(rule, hour * pixelsPerHour + 8); canvas.Children.Add(rule);
        }
        var groups = model.Editor.Entries(day, day.AddDays(1)).Where(e => e.GroupId is not null && e.Sources.Any(s => s.IsPositioned && s.Duration > TimeSpan.Zero)).ToArray();
        var grouped = groups.SelectMany(e => e.Sources).ToHashSet();
        var intervals = model.Engine.Sessions.Where(s => s.Day == day && s.IsPositioned && s.Duration > TimeSpan.Zero && !grouped.Contains(s))
            .Select(s => (s.Account, Start: s.From, End: s.To, Row: (SessionRow?)s, Group: (ReviewEntry?)null)).ToList();
        // Time added or taken off an account for the day sits on the timeline too, as a striped block that can be moved but not stretched.
        foreach (var adjustment in model.Engine.Sessions.Where(s => s.Day == day && s.IsAdjustment))
        {
            var length = TimeSpan.FromMinutes(Math.Abs((double)decimal.Parse(adjustment.Minutes, System.Globalization.CultureInfo.InvariantCulture)));
            var from = adjustment.From; var until = from + length > day.AddDays(1) ? day.AddDays(1) : from + length;
            intervals.Add((adjustment.Account, from, until, adjustment, null));
        }
        if (model.Engine.Active is { } active && active.Start < day.AddDays(1) && model.Engine.Now > day)
            intervals.Add((active.Account, active.Start < day ? day : active.Start, model.Engine.Now < day.AddDays(1) ? model.Engine.Now : day.AddDays(1), null, null));
        foreach (var group in groups)
        {
            var sections = new List<List<SessionRow>>();
            foreach (var row in group.Sources.Where(s => s.IsPositioned && s.Duration > TimeSpan.Zero).OrderBy(s => s.From))
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
            var lane = ends.FindIndex(end => end <= interval.Start);
            if (lane < 0) { lane = ends.Count; ends.Add(interval.End); } else ends[lane] = interval.End;
            var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(interval.Account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
            colour = model.BlockColour(colour);
            var end = interval.End > day.AddDays(1) ? day.AddDays(1) : interval.End;
            if (interval.Row is { IsAdjustment: true } adjusted)
            {
                var adjustmentCard = AdjustmentCard(model, canvas, adjusted, colour, interval.Start, end, pixelsPerHour);
                Canvas.SetTop(adjustmentCard, (interval.Start - day).TotalHours * pixelsPerHour + 2);
                canvas.Children.Add(adjustmentCard); cards.Add((adjustmentCard, lane));
                continue;
            }
            var duration = interval.Group is { } groupDuration ? TimeSpan.FromMinutes((double)groupDuration.Minutes) : end - interval.Start;
            // Even a one-minute block is drawn at least eight pixels tall, so it can be seen and clicked. Zoom in for exact edges.
            var height = Math.Max(MinimumBlockHeight, (end - interval.Start).TotalHours * pixelsPerHour - 4);
            var ink = Ui.InkOn(colour);
            var text = new StackPanel { Spacing = 6 };
            text.Children.Add(Ui.Text(model.Label(interval.Account) + (interval.Group is { } labelGroup ? $" · Group ({labelGroup.Sources.Count})" : ""), 15));
            if (height >= 58) text.Children.Add(Ui.Text($"{interval.Start:HH:mm} – {end:HH:mm} · {model.Format(duration)}", 12));
            foreach (TextBlock child in text.Children) child.Foreground = Brush.Parse(ink);
            var card = new Border { Background = Brush.Parse(colour), CornerRadius = new CornerRadius(height < 20 ? 2 : 10),
                Width = 380, Height = height, Padding = new Thickness(14, height < 58 ? 2 : 10),
                ClipToBounds = true };
            if (height >= 24) card.Child = text;
            var selectionSources = interval.Group?.Sources ?? (interval.Row is { } source ? new[] { source } : Array.Empty<SessionRow>());
            // A selected block is shown by its border alone. Nothing is added to the text, so the block never shifts or clips.
            if (model.IsSelected(selectionSources)) card.Classes.Add("mergeSelected");
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
                card.ContextMenu = new ContextMenu { ItemsSource = new Control[] { edit, MoveMenu(model, new[] { row }), MergeMenu(model), new Separator(), DeleteMenu(model, new[] { row }) } };
                TimelineGestures.Attach(card, canvas, model, row, pixelsPerHour);
            }
            if (interval.Row is null && interval.Group is null)
            {
                // The block that is recording now. A green outline, a label and the stop menu make it clear why it can't be dragged.
                card.Classes.Add("recording"); card.Name = "TimelineRecording"; card.Cursor = new Cursor(StandardCursorType.Arrow);
                var live = Ui.Text("● Recording now", 12); live.Foreground = Brush.Parse(ink == "#FFFFFF" ? ink : "#1E5C38"); live.FontWeight = FontWeight.SemiBold;
                if (height >= 96) text.Children.Insert(model.IsSelected(selectionSources) ? 1 : 0, live);
                else if (text.Children.Count > 0 && text.Children[0] is TextBlock liveTitle) liveTitle.Text = "● " + liveTitle.Text;
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
                    if (e.ClickCount == 2) { model.OpenGroup(group.GroupId!); groupPress = null; }
                    else groupPress = e.GetPosition(canvas);
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
                card.ContextMenu = new ContextMenu { ItemsSource = new[] { editGroup, ungroup, MergeMenu(model) } };
                ToolTip.SetTip(card, $"{model.Label(group.Account)} · {group.Sources.Count} grouped entries · {model.Format(duration)} credited\nSeparate sections preserve any gaps. Double-click to inspect; right-click to ungroup.");
            }
            Canvas.SetTop(card, (interval.Start - day).TotalHours * pixelsPerHour + 2);
            canvas.Children.Add(card); cards.Add((card, lane));
        }
        void Resize()
        {
            foreach (var rule in canvas.Children.OfType<Border>().Where(b => b.Classes.Contains("rule"))) rule.Width = Math.Max(220, canvas.Bounds.Width - 70);
            var width = Math.Max(220, canvas.Bounds.Width - 80) / Math.Max(1, ends.Count);
            foreach (var card in cards) { card.Card.Width = Math.Max(40, width - 6); Canvas.SetLeft(card.Card, 64 + card.Lane * width); }
        }
        canvas.SizeChanged += (_, _) => Resize();
        DateTime clicked = day.AddHours(9);
        DateTime At(double y) => day.AddMinutes(Math.Clamp(Math.Floor(y / pixelsPerHour * 60 / 5) * 5, 0, 1435));
        var add = new MenuItem { Header = "Add time…" }; add.Click += (_, _) => model.OpenGap(clicked);
        var tidy = new MenuItem { Header = "Tidy day…", Name = "TidyDayMenu" }; tidy.Click += (_, _) => model.OpenTidy();
        canvas.ContextMenu = new ContextMenu { ItemsSource = new[] { add, tidy, MergeMenu(model) } };
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
        var root = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 16, 0) };
        switch (model.WeekSection)
        {
            case "hours": HoursSection(model, table, days, root); break;
            case "entries": EntriesSection(model, reconciled, root); break;
            default: OverviewSection(model, table, day, days, root); break;
        }
        var scroll = new ScrollViewer { Content = root, AllowAutoHide = false, Tag = Vector.Zero,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        DeferOffset(scroll);
        return scroll;
    }
    private static void OverviewSection(TimerModel model, WeekTable table, DateTime day, int[] days, StackPanel root)
    {
        TextBlock Tile(string caption, string value, string? style = null)
        { return Ui.Text(value, 26, style); }
        StackPanel Stat(string caption, string value, string? style = null)
        {
            var stat = new StackPanel { Spacing = 2 };
            stat.Children.Add(Ui.Text(caption, 12, "muted")); stat.Children.Add(Tile(caption, value, style)); return stat;
        }
        WeekTableRow Find(string label) => table.Rows.First(r => r.Label.StartsWith(label));
        var stats = Ui.Grid("*,*,*");
        var worked = Stat("Worked this week", Find("Total").Total == "–" ? "0:00" : Find("Total").Total);
        var breaks = Stat("Breaks", Find("Break").Total == "–" ? "0:00" : Find("Break").Total, "muted");
        var unallocated = Stat("Unallocated", Find("Unallocated").Total == "–" ? "0:00" : Find("Unallocated").Total, "muted");
        Ui.Put(stats, worked); Ui.Put(stats, breaks, 1); Ui.Put(stats, unallocated, 2);
        root.Children.Add(stats);
        var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var bars = new WeekBars
        {
            Bars = days.Select(i =>
            {
                var date = day.AddDays(i);
                var segments = accounts.Select(a => new BarSegment(a.Display, Color.Parse(model.BlockColour(a.Colour)), model.OnDay(a, date).TotalHours)).ToList();
                var total = segments.Sum(s => s.Hours);
                var detail = string.Join("\n", segments.Where(s => s.Hours > 0).Select(s => $"{s.Account}  {model.FormatShort(TimeSpan.FromHours(s.Hours))}"));
                return new DayBar(date.ToString("ddd"), date.ToString("dddd d MMMM") + (detail.Length > 0 ? "\n" + detail : "\nNothing recorded"),
                    total > 0 ? model.FormatShort(TimeSpan.FromHours(total)) : "–", total, segments);
            }).ToList()
        };
        root.Children.Add(bars);
        // A rule and a heading set the accounts apart from the chart above them, with room either side.
        var divide = Ui.Rule(); divide.Margin = new Thickness(0, 14, 0, 0); root.Children.Add(divide);
        var byAccount = Ui.Text("By account", 12, "muted"); byAccount.Margin = new Thickness(0, 2, 0, -4); root.Children.Add(byAccount);
        // One line per account: its colour, its week, and its budget when it has one.
        var list = new StackPanel { Spacing = 10 };
        for (var i = 0; i < accounts.Count; i++)
        {
            var account = accounts[i]; var row = table.Rows[i];
            // Every row is one line with everything centred on it, so a budget meter always sits level with its account.
            var line = Ui.Grid("30,*,64,120,150"); line.MinHeight = 34;
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Brush.Parse(model.BlockColour(account.Colour)), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Ui.Put(line, dot); Ui.Put(line, Ui.Text(account.Display, 14), 1);
            var total = Ui.Text(row.Total == "–" ? "0:00" : row.Total, 14); total.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; Ui.Put(line, total, 2);
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
                var caption = Ui.Text($"{budget.Fraction * 100:0}% of {budget.Budget}", 12, budget.Fraction >= 1 ? "overtime" : budget.Fraction >= .8 ? "budgetWarning" : "muted");
                caption.Margin = new Thickness(12, 0, 0, 0); caption.TextTrimming = TextTrimming.CharacterEllipsis; Ui.Put(line, caption, 4);
            }
            list.Children.Add(line);
        }
        root.Children.Add(list);
    }
    private static void HoursSection(TimerModel model, WeekTable table, int[] days, StackPanel root)
    {
        var grid = Ui.Grid("150," + string.Join(",", days.Select(_ => "*")) + ",70", "Auto," + string.Join(",", table.Rows.Select(_ => "Auto")));
        grid.MinWidth = 220 + days.Length * 50;
        for (var c = 0; c < table.Headers.Length; c++)
        {
            var heading = Ui.Text(c == 0 ? table.Headers[c] : table.Headers[c].Split(' ')[0], 12, "muted");
            if (c > 0) heading.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            Ui.Put(grid, heading, c);
        }
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var item = table.Rows[r];
            TextBlock Cell(string text, bool right)
            {
                var block = Ui.Text(text, item.Style == "bold" ? 14 : 13, item.Style == "faint" || text == "–" ? "muted" : null);
                if (item.Style == "bold") block.FontWeight = FontWeight.SemiBold;
                block.Margin = new Thickness(0, item.Style == "normal" ? 11 : 9);
                if (right) block.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                return block;
            }
            var shown = Ui.Put(grid, Cell(item.Style == "bold" ? "Total" : item.Label, false), 0, r + 1);
            if (item.Style == "bold") ToolTip.SetTip(shown, item.Label);
            for (var i = 0; i < item.Cells.Length; i++) Ui.Put(grid, Cell(item.Cells[i], true), i + 1, r + 1);
            Ui.Put(grid, Cell(item.Total, true), item.Cells.Length + 1, r + 1);
            if (item.Style == "bold") Ui.Put(grid, new Border { Height = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Classes = { "rule" } }, 0, r + 1).SetValue(Grid.ColumnSpanProperty, table.Headers.Length);
        }
        // Both headings on this tab are real headings, so the table and the budgets read as two distinct parts.
        TextBlock Heading(string text) { var h = Ui.Text(text, 16); h.FontWeight = FontWeight.SemiBold; return h; }
        var heading2 = Ui.Grid("*,Auto");
        Ui.Put(heading2, Heading(model.DecimalHours ? "Decimal hours" : "Hours and minutes"));
        // Copy and export live on this line, with the table they act on. Both are icons with tooltips.
        var copy = Ui.IconButton(IconKind.Copy, "Copy hours table", "CopyHours", () => { });
        ToolTip.SetTip(copy, "Copy the table to the clipboard, ready to paste into a timesheet");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(table.ToTsv());
            ToolTip.SetTip(copy, "Copied");
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            reset.Tick += (_, _) => { reset.Stop(); ToolTip.SetTip(copy, "Copy the table to the clipboard, ready to paste into a timesheet"); };
            reset.Start();
        };
        var export = Ui.IconButton(IconKind.Export, "Export week…", "ExportWeek", () => { if (TopLevel.GetTopLevel(root) is Window owner) new ExportWindow(model).Show(owner); });
        Ui.Put(heading2, Ui.Horizontal(copy, export), 1);
        root.Children.Add(heading2);
        root.Children.Add(grid);
        if (table.Budgets.Count > 0)
        {
            var split = Ui.Rule(); split.Margin = new Thickness(0, 14, 0, 2); root.Children.Add(split);
            root.Children.Add(Heading("Budgets"));
            var budgets = Ui.Grid("150,*,*,*,60", "Auto," + string.Join(",", table.Budgets.Select(_ => "Auto")));
            foreach (var (text, c) in new[] { "Account", "Budget", "Used", "Left", "Used" }.Select((t, c) => (t, c)))
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
                    block.Margin = new Thickness(0, 8); if (c > 0) block.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                    Ui.Put(budgets, block, c, r + 1);
                }
            }
            root.Children.Add(budgets);
        }
    }
    private static void EntriesSection(TimerModel model, WeekExport reconciled, StackPanel root)
    {
        root.Children.Add(Ui.Text(model.SelectedBlockCount > 0 ? $"{model.SelectedBlockCount} blocks selected · " + model.MergeHint : "Entries · click to select · right-click to edit or merge", 12, "muted"));
        foreach (var entry in reconciled.Entries)
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
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            var labels = new StackPanel { Spacing = 5 };
            var index = reconciled.Entries.ToList().IndexOf(entry);
            var duration = model.DecimalHours ? WeekExport.Hours(reconciled.EntryUnits[index]) + "h" : model.Format(TimeSpan.FromMinutes((double)entry.Minutes));
            labels.Children.Add(Ui.Text($"{entry.Day:ddd d MMM} · {model.Label(entry.Account)} · {caption} · {duration}", 12));
            if (!string.IsNullOrWhiteSpace(entry.Note)) labels.Children.Add(new TextBlock { Text = entry.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            button.Content = labels;
            if (entry.Sources.Count > 0)
            {
                button.Classes.Set("mergeSelected", model.IsSelected(entry.Sources));
                var edit = new MenuItem { Header = "Edit / inspect entry…" }; edit.Click += (_, _) => Open();
                button.ContextMenu = new ContextMenu { ItemsSource = new Control[] { edit, MoveMenu(model, entry.Sources), MergeMenu(model), new Separator(), DeleteMenu(model, entry.Sources) } };
            }
            if (entry.GroupId is { } groupId)
            {
                var combined = Ui.Grid("*,Auto"); Ui.Put(combined, button);
                Ui.Put(combined, Ui.Button("Ungroup", () => model.UngroupEntries(groupId), "Ungroup" + groupId), 1).Margin = new Thickness(8, 0, 0, 0);
                root.Children.Add(combined);
            }
            else root.Children.Add(button);
        }
    }
    // A correction is drawn as a striped block of the account's colour, muted towards grey, so it is plainly not recorded time. It is as tall as the
    // minutes it adds or removes and sits where it was made (or where it was dragged to). A negative one carries the overtime colour on its label.
    private static Border AdjustmentCard(TimerModel model, Canvas canvas, SessionRow row, string accountColour, DateTime start, DateTime end, double pixelsPerHour)
    {
        var minutes = TimeSpan.FromMinutes((double)decimal.Parse(row.Minutes, System.Globalization.CultureInfo.InvariantCulture));
        var amount = (minutes < TimeSpan.Zero ? "-" : "+") + model.FormatShort(minutes < TimeSpan.Zero ? -minutes : minutes);
        var strong = ThemeColours.Mix(accountColour, "#8C9096", .6); var soft = ThemeColours.Mix(accountColour, "#8C9096", .15);
        var stripes = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(14, 14, RelativeUnit.Absolute), SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops = { new GradientStop(Color.Parse(strong), 0), new GradientStop(Color.Parse(strong), .5), new GradientStop(Color.Parse(soft), .5), new GradientStop(Color.Parse(soft), 1) }
        };
        var height = Math.Max(MinimumBlockHeight, (end - start).TotalHours * pixelsPerHour - 4);
        var ink = Ui.InkOn(ThemeColours.Mix(strong, soft, .5));
        var text = new StackPanel { Spacing = 4 };
        // A short block has room for one line only, so the account and the amount share it.
        if (height < 52) text.Children.Add(Ui.Text($"{model.Label(row.Account)} · Adjustment {amount}", 13));
        else { text.Children.Add(Ui.Text(model.Label(row.Account), 15)); text.Children.Add(Ui.Text($"Adjustment {amount}", 12)); }
        var note = model.Editor.Note(row);
        if (height >= 70 && !string.IsNullOrWhiteSpace(note)) { var noteText = Ui.Text(note, 12); noteText.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(noteText); }
        foreach (TextBlock child in text.Children) child.Foreground = Brush.Parse(ink);
        var card = new Border { Name = "Adjustment" + model.Engine.Sessions.IndexOf(row), Tag = row, Background = stripes, CornerRadius = new CornerRadius(height < 20 ? 2 : 10),
            Width = 380, Height = height, Padding = new Thickness(14, height < 52 ? 3 : 8), ClipToBounds = true };
        card.Classes.Add("adjustment");
        if (height >= 24) card.Child = text;
        ToolTip.SetTip(card, $"Adjustment · {model.Label(row.Account)} · {amount} credited, no recorded time" + (note.Length > 0 ? "\n" + note : "")
            + "\nDrag to move it on the timeline. Double-click to edit the note; right-click to delete.");
        var edit = new MenuItem { Header = "Edit note…" }; edit.Click += (_, _) => model.OpenEntry(row);
        var delete = new MenuItem { Header = "Delete adjustment" }; delete.Click += (_, _) => model.DeleteEntry(row);
        card.ContextMenu = new ContextMenu { ItemsSource = new[] { edit, delete } };
        TimelineGestures.AttachAdjustment(card, canvas, model, row, pixelsPerHour);
        return card;
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
        item.IsEnabled = targets.Count > 0 && rows.All(r => r.IsPositioned);
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
