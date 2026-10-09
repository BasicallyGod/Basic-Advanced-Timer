using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Timer.Desktop;

public static class ReviewViews
{
    public static Control Timeline(TimerModel model)
    {
        var day = model.SelectedDate;
        var root = Ui.Grid("*", "Auto,*");
        var unplaced = TimeSpan.FromMinutes((double)model.Editor.Unplaced(day).Values.Sum());
        var caption = Ui.Text(unplaced > TimeSpan.Zero ? "Unplaced legacy credit " + model.Format(unplaced) : "Recorded time", 12, "muted");
        caption.Margin = new Thickness(0, 0, 0, 10);
        Ui.Put(root, caption);
        const double pixelsPerHour = 160;
        var canvas = new Canvas { Height = 24 * pixelsPerHour, MinWidth = 460, Background = Brushes.Transparent };
        for (var hour = 0; hour < 24; hour++)
        {
            var label = Ui.Text($"{hour:00}:00", 12, "muted");
            Canvas.SetTop(label, hour * pixelsPerHour); canvas.Children.Add(label);
            var rule = Ui.Rule(); rule.Width = 390;
            Canvas.SetLeft(rule, 60); Canvas.SetTop(rule, hour * pixelsPerHour + 8); canvas.Children.Add(rule);
        }
        var intervals = model.Engine.Sessions.Where(s => s.Day == day && s.IsPositioned && s.Duration > TimeSpan.Zero)
            .Select(s => (s.Account, Start: s.From, End: s.To, Row: (SessionRow?)s, Position: (CreditPosition?)null)).ToList();
        if (model.Engine.Active is { } active && active.Start < day.AddDays(1) && model.Engine.Now > day)
            intervals.Add((active.Account, active.Start < day ? day : active.Start, model.Engine.Now < day.AddDays(1) ? model.Engine.Now : day.AddDays(1), null, null));
        intervals.AddRange(model.Editor.Positions.Where(p => p.Start.Date == day).Select(p => (p.Account, p.Start, p.End, (SessionRow?)null, (CreditPosition?)p)));
        var ends = new List<DateTime>();
        var cards = new List<(Border Card, int Lane)>();
        foreach (var interval in intervals.OrderBy(s => s.Start))
        {
            var lane = ends.FindIndex(end => end <= interval.Start);
            if (lane < 0) { lane = ends.Count; ends.Add(interval.End); } else ends[lane] = interval.End;
            var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(interval.Account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
            var end = interval.End > day.AddDays(1) ? day.AddDays(1) : interval.End;
            var duration = end - interval.Start;
            var height = Math.Max(3, duration.TotalHours * pixelsPerHour - 4);
            var text = new StackPanel { Spacing = 6 };
            text.Children.Add(Ui.Text(interval.Account, 15));
            if (height >= 58) text.Children.Add(Ui.Text($"{interval.Start:HH:mm} – {end:HH:mm} · {model.Format(duration)}", 12));
            foreach (TextBlock child in text.Children) child.Foreground = Brush.Parse("#24312B");
            var card = new Border { Background = Brush.Parse(colour), CornerRadius = new CornerRadius(height < 20 ? 2 : 10),
                Width = 380, Height = height, Padding = new Thickness(14, height < 58 ? 2 : 10),
                Child = height >= 24 ? text : null, ClipToBounds = true };
            var note = interval.Position is { } positionNote ? "Existing credit · " + positionNote.Note : interval.Row is null ? "Running · stop tracking to edit" : model.Editor.Note(interval.Row);
            if (height >= 90 && !string.IsNullOrWhiteSpace(note))
            {
                var noteText = Ui.Text(note, 12); noteText.Foreground = Brush.Parse("#24312B");
                noteText.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(noteText);
            }
            ToolTip.SetTip(card, $"{interval.Account} · {interval.Start:HH:mm:ss} – {end:HH:mm:ss} · {model.Format(duration)}\n{note}");
            if (interval.Row is { } row)
            {
                card.Tag = row; card.Name = "TimelineEntry" + model.Engine.Sessions.IndexOf(row);
                card.DoubleTapped += (_, e) => { model.OpenEntry(row); e.Handled = true; };
                var edit = new MenuItem { Header = "Edit entry / note…" }; edit.Click += (_, _) => model.OpenEntry(row);
                card.ContextMenu = new ContextMenu { ItemsSource = new[] { edit } };
                TimelineGestures.Attach(card, canvas, model, row, pixelsPerHour);
            }
            if (interval.Position is { } position)
            {
                card.Tag = position; card.Name = "CreditPosition" + position.Id;
                card.DoubleTapped += (_, e) => { model.OpenPosition(position); e.Handled = true; };
                var editPosition = new MenuItem { Header = "Edit / unplace existing credit…" }; editPosition.Click += (_, _) => model.OpenPosition(position);
                card.ContextMenu = new ContextMenu { ItemsSource = new[] { editPosition } };
                card.BorderThickness = new Thickness(2); card.BorderBrush = Brush.Parse("#24312B");
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
        var add = new MenuItem { Header = "Add new credited time…" }; add.Click += (_, _) => model.OpenGap(clicked);
        canvas.ContextMenu = new ContextMenu { ItemsSource = new[] { add } };
        canvas.PointerPressed += (_, e) => { if (e.GetCurrentPoint(canvas).Properties.IsRightButtonPressed) clicked = At(e.GetPosition(canvas).Y); };
        canvas.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual visual && (visual is Border { Tag: SessionRow or CreditPosition } || visual.GetVisualAncestors().OfType<Border>().Any(b => b.Tag is SessionRow or CreditPosition))) return;
            model.OpenGap(At(e.GetPosition(canvas).Y)); e.Handled = true;
        };
        var plan = model.Engine.Plan(day);
        var first = intervals.Count > 0 ? intervals.Min(i => i.Start) : plan.Working ? plan.Start : day.AddHours(9);
        var scroll = new ScrollViewer { Name = "TimelineScroll", Content = canvas,
            Tag = new Vector(0, Math.Max(0, (first - day).TotalHours * pixelsPerHour - 24)),
            AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        DeferOffset(scroll);
        Ui.Put(root, scroll, row: 1);
        return root;
    }

    public static Control Week(TimerModel model)
    {
        var day = model.WeekStart(model.SelectedDate);
        var days = Enumerable.Range(0, 7).Where(i => model.Engine.Schedule.Days.Contains(day.AddDays(i).DayOfWeek)
            || model.Engine.Sessions.Any(s => s.Day == day.AddDays(i))
            || (model.Engine.Active is not null && day.AddDays(i) == model.Engine.Now.Date)).ToArray();
        if (days.Length == 0) days = Enumerable.Range(0, 7).ToArray();
        var grid = Ui.Grid("125," + string.Join(",", days.Select(_ => "*")) + ",75", "Auto," + string.Join(",", model.ReviewAccounts.Select(_ => "Auto")));
        grid.MinWidth = 200 + days.Length * 58;
        Ui.Put(grid, Ui.Text("Account", 12, "muted"));
        for (var i = 0; i < days.Length; i++) Ui.Put(grid, Ui.Text(day.AddDays(days[i]).ToString("ddd"), 12, "muted"), i + 1);
        Ui.Put(grid, Ui.Text("Total", 12, "muted"), days.Length + 1);
        var reconciled = new WeekExport(model.Editor, model.Engine, day);
        string Value(TimerAccount account, int? index)
        {
            if (!model.DecimalHours || model.Engine.Active is not null) return model.Format(index is { } i ? model.OnDay(account, day.AddDays(i)) : model.Week(account));
            var values = reconciled.Accounts.GetValueOrDefault(account.Name) ?? new long[7];
            return WeekExport.Hours(index is { } d ? values[d] : values.Sum()) + "h";
        }
        var row = 1;
        foreach (var a in model.ReviewAccounts)
        {
            Ui.Put(grid, Ui.Text(a.Name, 13), row: row).Margin = new Thickness(0, 16);
            for (var i = 0; i < days.Length; i++) Ui.Put(grid, Ui.Text(Value(a, days[i]), 12), i + 1, row);
            Ui.Put(grid, Ui.Text(Value(a, null), 12), days.Length + 1, row);
            row++;
        }
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(Ui.Text("Week commencing " + day.ToString("d MMMM"), 12, "muted"));
        root.Children.Add(grid);
        root.Children.Add(Ui.Text("Entries · click to edit time or notes", 12, "muted"));
        foreach (var entry in reconciled.Entries)
        {
            void Open()
            {
                if (entry.PositionId is { } id) model.OpenPosition(model.Editor.Positions.Single(p => p.Id == id));
                else if (entry.GroupId is { } group) model.OpenGroup(group);
                else model.OpenEntry(entry.Sources[0]);
            }
            var caption = entry.Start is { } start && entry.End is { } end ? $"{start:HH:mm} – {end:HH:mm}" : entry.Type;
            var name = entry.Sources.Count == 1 ? "WeekEntry" + model.Engine.Sessions.IndexOf(entry.Sources[0]) : "WeekReviewEntry";
            var button = Ui.Button("", Open, name);
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            var labels = new StackPanel { Spacing = 5 };
            var index = reconciled.Entries.ToList().IndexOf(entry);
            var duration = model.DecimalHours ? WeekExport.Hours(reconciled.EntryUnits[index]) + "h" : model.Format(TimeSpan.FromMinutes((double)entry.Minutes));
            labels.Children.Add(Ui.Text($"{entry.Day:ddd d MMM} · {entry.Account} · {caption} · {duration}", 12));
            if (!string.IsNullOrWhiteSpace(entry.Note)) labels.Children.Add(new TextBlock { Text = entry.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            button.Content = labels; root.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = root, AllowAutoHide = false, Tag = Vector.Zero,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        DeferOffset(scroll);
        return scroll;
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
