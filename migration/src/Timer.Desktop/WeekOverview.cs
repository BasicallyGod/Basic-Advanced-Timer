using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>One day of the week overview as plain figures, so the screen, its tooltips and the tests read the same numbers.</summary>
public sealed record WeekDay(DateTime Date, bool IsToday, bool Off, bool HasTime, TimeSpan Working, TimeSpan Overtime, TimeSpan Unallocated, TimeSpan Scheduled,
    IReadOnlyList<(TimerAccount Account, TimeSpan Time)> Accounts, int Meetings, int Tasks, bool Overlaps)
{
    /// <summary>A quiet dot marks a day with half an hour or more on no account, or with blocks laid over each other.</summary>
    public bool Marked => Unallocated >= TimeSpan.FromMinutes(30) || Overlaps;
}

/// <summary>The week for the totals strip and the chart.</summary>
public sealed record WeekFigures(DateTime Start, IReadOnlyList<WeekDay> Days, TimeSpan Working, TimeSpan Allocated, TimeSpan Unallocated, TimeSpan Breaks,
    TimeSpan Overtime, TimeSpan Scheduled, bool Current);

/// <summary>The Week overview page, as the mock draws it: a totals strip, the day chart or table, and the accounts with their budgets and pace.</summary>
public static class WeekOverview
{
    /// <summary>Pace words for a budget: over it, ahead of where the period should have got it, or on pace.</summary>
    public static string PaceOf(TimeSpan used, TimeSpan budget, double elapsed, Func<TimeSpan, string> format)
        => used >= budget ? "Over by " + format(used - budget) : budget > TimeSpan.Zero && used.TotalSeconds / budget.TotalSeconds > elapsed + 0.1 ? "Ahead of pace" : "On pace";

    public static WeekFigures Figures(TimerModel model, DateTime weekStart)
    {
        var engine = model.Engine; var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList(); var today = engine.Now.Date;
        TimeSpan WorkingOn(DateTime day) { var t = engine.Totals(day); return t.Working + t.Overtime; }
        var shown = Enumerable.Range(0, 7).Where(i => engine.Schedule.Days.Contains(weekStart.AddDays(i).DayOfWeek)
            || engine.Sessions.Any(s => s.Day == weekStart.AddDays(i)) || (engine.Active is not null && weekStart.AddDays(i) == today)).ToArray();
        if (shown.Length == 0) shown = Enumerable.Range(0, 5).ToArray();
        var days = new List<WeekDay>();
        foreach (var i in shown)
        {
            var date = weekStart.AddDays(i); var totals = engine.Totals(date); var plan = engine.Plan(date);
            var per = accounts.Select(a => (a, model.OnDay(a, date))).Where(p => p.Item2 > TimeSpan.Zero).ToList();
            var allocated = per.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Item2);
            var working = totals.Working + totals.Overtime; var left = working - allocated;
            var blocks = engine.Sessions.Where(s => s.Day == date && s.Duration > TimeSpan.Zero && !BreakAccount.Is(s.Account)).OrderBy(s => s.From).ToList();
            var overlaps = false; var reach = DateTime.MinValue;
            foreach (var block in blocks) { if (block.From.AddSeconds(2) < reach) overlaps = true; if (block.To > reach) reach = block.To; }
            var meetings = model.Notes.MeetingsOn(date).Count;
            var tasks = model.Notes.All.Count(n => n.HasDate && n.Day.Date == date && !n.Done && !NotesModel.IsMeeting(n));
            var hasTime = working > TimeSpan.Zero || allocated > TimeSpan.Zero;
            days.Add(new WeekDay(date, date == today, date < today && !hasTime && !plan.Working, hasTime, working, totals.Overtime, left > TimeSpan.Zero ? left : TimeSpan.Zero,
                plan.Working ? plan.End - plan.Start : TimeSpan.Zero, per.Select(p => (p.a, p.Item2)).ToList(), meetings, tasks, overlaps));
        }
        // Saturday and Sunday are drawn only when they hold time; a quiet weekend is not a column of nothing.
        days = days.Where(d => d.HasTime || d.Scheduled > TimeSpan.Zero || d.Off || d.IsToday || (d.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))).ToList();
        var week = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var working7 = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + WorkingOn(d));
        var overtime = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + engine.Totals(d).Overtime);
        var allocatedWeek = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + accounts.Aggregate(TimeSpan.Zero, (s, a) => s + model.OnDay(a, d)));
        var unallocated = working7 - allocatedWeek; if (unallocated < TimeSpan.Zero) unallocated = TimeSpan.Zero;
        var breaks = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + model.BreakTimeOn(d));
        var scheduled = week.Aggregate(TimeSpan.Zero, (sum, d) => engine.Plan(d) is { Working: true } p ? sum + (p.End - p.Start) : sum);
        return new WeekFigures(weekStart, days, working7, allocatedWeek, unallocated, breaks, overtime, scheduled, today >= weekStart && today < weekStart.AddDays(7));
    }

    public static Control Build(TimerModel model)
    {
        var start = model.WeekStart(model.SelectedDate);
        var figures = Figures(model, start); var (grid, _) = ReviewViews.WeekTableFor(model, start);
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 14, 4) };
        root.Children.Add(Totals(model, figures));
        root.Children.Add(ByDay(model, figures, grid));
        root.Children.Add(ByAccount(model, figures, grid));
        var scroll = new ScrollViewer { Content = root, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        return Ui.WithMoreBelow(scroll);
    }

    private static Border Tile(Control child, Thickness padding)
    {
        var tile = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = padding, Child = child };
        tile.Classes.Add("dayCard"); return tile;
    }
    private static Control Stat(TimerModel model, string name, string caption, string value, string? tip, string? tone = null, string? dot = null)
    {
        var head = Ui.Horizontal(); head.Spacing = 5;
        if (dot is not null) head.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = Brush(model, dot) });
        head.Children.Add(Ui.Text(caption, 11, tone ?? "muted"));
        var v = Ui.Text(value, 16, tone); v.Classes.Add("figures"); v.Name = name; v.FontWeight = FontWeight.SemiBold;
        var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(head); stack.Children.Add(v);
        if (tip is not null) ToolTip.SetTip(stack, tip);
        return stack;
    }
    private static IBrush Brush(TimerModel model, string token) => new SolidColorBrush(Color.Parse(model.CurrentTheme.Colour(token)));

    // ---- The totals strip ----
    private static Control Totals(TimerModel model, WeekFigures w)
    {
        var overtime = w.Overtime > TimeSpan.Zero;
        var big = Ui.Text(model.Format(w.Working), 34, overtime ? "overtime" : null); big.Classes.Add("clock"); big.Name = "WeekWorking";
        var left = new StackPanel { MinWidth = 128, VerticalAlignment = VerticalAlignment.Center }; left.Children.Add(Ui.Text("Working time", 11, "muted")); left.Children.Add(big);

        var gap = w.Scheduled - w.Working;
        var rightWords = w.Current ? model.FormatShort(gap > TimeSpan.Zero ? gap : TimeSpan.Zero) + " to go"
            : gap >= TimeSpan.Zero ? model.FormatShort(gap) + " under the schedule" : model.FormatShort(-gap) + " over the schedule";
        var words = Ui.Grid("*,Auto"); var scheduled = Ui.Text("Scheduled " + model.FormatShort(w.Scheduled), 11, "muted"); scheduled.Name = "WeekScheduled";
        var togo = Ui.Text(rightWords, 11, "muted"); togo.Name = "WeekToGo"; Ui.Put(words, scheduled); Ui.Put(words, togo, 1);
        var bar = new Border { Name = "WeekBar", Height = 12, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Brush(model, "Line") };
        var whole = Math.Max(Math.Max(w.Working.TotalSeconds, w.Scheduled.TotalSeconds), 1);
        // Allocated time solid, working time on no account hatched, overtime in red at the end of the bar, as the mock draws it.
        var parts = new Grid { ColumnDefinitions = new ColumnDefinitions($"{w.Allocated.TotalSeconds:0.###}*,{w.Unallocated.TotalSeconds:0.###}*,{Math.Max(0, whole - w.Allocated.TotalSeconds - w.Unallocated.TotalSeconds):0.###}*") };
        parts.Children.Add(new Border { Name = "WeekBarAllocated", Background = Brush(model, "Muted") });
        parts.Children.Add(new Border { Name = "WeekBarUnallocated", Background = ReviewViews.Stripes(model), Opacity = .7, [Grid.ColumnProperty] = 1 });
        var overlay = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(0, whole - w.Overtime.TotalSeconds):0.###}*,{w.Overtime.TotalSeconds:0.###}*") };
        overlay.Children.Add(new Border { Name = "WeekBarOvertime", Background = Brush(model, "Overtime"), [Grid.ColumnProperty] = 1, IsVisible = overtime });
        var layers = new Grid(); layers.Children.Add(parts); layers.Children.Add(overlay); bar.Child = layers;
        ToolTip.SetTip(bar, $"Allocated {model.FormatShort(w.Allocated)}, unallocated {model.FormatShort(w.Unallocated)}" + (overtime ? $", overtime {model.FormatShort(w.Overtime)}" : ""));
        Avalonia.Automation.AutomationProperties.SetName(bar, $"Working time {model.FormatShort(w.Working)} of {model.FormatShort(w.Scheduled)} scheduled");
        var middle = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center }; middle.Children.Add(words); middle.Children.Add(bar);

        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
        void At(Control c, int column) { Grid.SetColumn(c, column); strip.Children.Add(c); c.Margin = new Thickness(column == 1 ? 22 : column > 1 ? 22 : 0, 0, 0, 0); }
        At(left, 0); At(middle, 1);
        At(Stat(model, "WeekAllocated", "Allocated", model.FormatShort(w.Allocated), "Time given to accounts"), 2);
        At(Stat(model, "WeekUnallocated", "Unallocated", model.FormatShort(w.Unallocated), "Working time with nothing recorded on an account"), 3);
        At(Stat(model, "WeekBreaks", "Breaks", model.FormatShort(w.Breaks), "Kept apart from working time", null, "Break"), 4);
        if (overtime) At(Stat(model, "WeekOvertime", "Overtime", model.FormatShort(w.Overtime), null, "overtime"), 5);
        var tile = Tile(strip, new Thickness(18, 12)); tile.Name = "WeekTotals"; return tile;
    }

    // ---- Time by day: the chart or the table ----
    private static Control ByDay(TimerModel model, WeekFigures w, WeekTable grid)
    {
        var inChart = !model.WeekAsTable;
        var head = Ui.Grid("Auto,*,Auto"); head.Margin = new Thickness(0, 0, 0, 6);
        var title = Ui.Text("Time by day", 15); title.FontWeight = FontWeight.SemiBold; Ui.Put(head, title);
        var hint = Ui.Text(inChart ? "Hover a day for the split, click it to open that day" : "Hours by account and day", 12, "muted"); hint.Margin = new Thickness(12, 0); hint.VerticalAlignment = VerticalAlignment.Center; Ui.Put(head, hint, 1);
        var seg = Ui.Horizontal(NotesPage.Seg("WeekChart", "Chart", inChart, () => model.SetWeekAsTable(false)), NotesPage.Seg("WeekTable", "Table", !inChart, () => model.SetWeekAsTable(true))); seg.Spacing = 2;
        foreach (var b in seg.Children.OfType<Button>()) b.Height = 30;
        Ui.Put(head, seg, 2);
        var body = new StackPanel(); body.Children.Add(head);
        body.Children.Add(inChart ? Chart(model, w) : Table(model, grid));
        var tile = Tile(body, new Thickness(18, 12, 18, 10)); tile.Name = "WeekByDay"; return tile;
    }

    // BelowBars is the room under the columns for the day name and the meeting and task counts, which are never cut off; the schedule line has a gutter of its own at the right so its words never meet a day's total.
    private const double ChartHeight = 232, BelowBars = 62, Gutter = 44, RightGutter = 58;
    private static Control Chart(TimerModel model, WeekFigures w)
    {
        var british = CultureInfo.GetCultureInfo("en-GB");
        var schedule = w.Days.Select(d => d.Scheduled).DefaultIfEmpty().Max();
        var topHours = Math.Max(Math.Max(schedule.TotalHours, w.Days.Select(d => d.Working.TotalHours).DefaultIfEmpty().Max()), 8);
        var perHour = Math.Min(17, (ChartHeight - BelowBars - 22) / topHours);
        var chart = new Grid { Name = "WeekChart_", Height = ChartHeight, Margin = new Thickness(0) };
        // The faint hour lines and their labels sit behind the columns, then the dashed line of the scheduled day.
        var line = Brush(model, "Line"); var muted = Brush(model, "Muted");
        for (var h = 2; h <= topHours; h += 2)
        {
            var at = BelowBars + h * perHour;
            chart.Children.Add(new Border { Height = 1, Background = line, Opacity = .6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Gutter, 0, 0, at) });
            chart.Children.Add(new TextBlock { Text = $"{h:00}:00", FontSize = 11, Foreground = muted, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, at - 8) });
        }
        if (schedule > TimeSpan.Zero)
        {
            var at = BelowBars + schedule.TotalHours * perHour;
            chart.Children.Add(new Border { Name = "WeekScheduleLine", Height = 1.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Gutter, 0, RightGutter - 4, at), Opacity = .7,
                Background = new DrawingBrush { Stretch = Stretch.None, TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 7, 1.5, RelativeUnit.Absolute),
                    Drawing = new GeometryDrawing { Brush = muted, Geometry = new RectangleGeometry(new Rect(0, 0, 4, 1.5)) } } });
            var label = new TextBlock { Name = "WeekScheduleLabel", Text = "Schedule\n" + model.FormatShort(schedule), FontSize = 11, Foreground = muted, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, at - 14) };
            chart.Children.Add(label);
        }
        var columns = new Grid { Margin = new Thickness(Gutter, 0, RightGutter, 0) };
        for (var i = 0; i < w.Days.Count; i++)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var column = Column(model, w.Days[i], perHour, i >= 3 && w.Days.Count > 3, british); Grid.SetColumn(column, i); columns.Children.Add(column);
        }
        chart.Children.Add(columns); return chart;
    }

    private static Control Column(TimerModel model, WeekDay d, double perHour, bool flip, CultureInfo british)
    {
        var overtime = d.Overtime > TimeSpan.Zero;
        var stack = new StackPanel { Width = 46, HorizontalAlignment = HorizontalAlignment.Center };
        var segments = new StackPanel { ClipToBounds = true };
        // Account slices fill from the bottom, with the time on no account as a hatched slice on top, so the column is the day's whole working time.
        var list = new List<Control>();
        foreach (var (account, time) in d.Accounts)
            list.Add(new Border { Height = Math.Max(1.5, time.TotalHours * perHour), Background = Brush(model, "Muted"), Tag = account });
        foreach (var slice in list) ((Border)slice).Background = new SolidColorBrush(Color.Parse(model.ChartColour(((TimerAccount)((Border)slice).Tag!).Colour)));
        if (d.Unallocated > TimeSpan.FromSeconds(30)) list.Add(new Border { Name = "WeekUnallocatedSlice", Height = Math.Max(1.5, d.Unallocated.TotalHours * perHour), Background = ReviewViews.Hatch(model) });
        list.Reverse(); foreach (var c in list) segments.Children.Add(c);
        var body = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = segments, HorizontalAlignment = HorizontalAlignment.Center, Width = 46 };
        if (d.Off) body = new Border { Name = "WeekOff", Width = 46, Height = 28, CornerRadius = new CornerRadius(10), Background = ReviewViews.Hatch(model), Opacity = .7, HorizontalAlignment = HorizontalAlignment.Center };

        var total = Ui.Text(d.Off ? "Off" : d.HasTime ? model.FormatShort(d.Working) : "–", d.Off || !d.HasTime ? 12 : 12.5, overtime ? "overtime" : d.HasTime ? null : "muted");
        total.FontWeight = d.HasTime ? FontWeight.SemiBold : FontWeight.Normal; total.Classes.Add("figures"); total.HorizontalAlignment = HorizontalAlignment.Center; total.Margin = new Thickness(0, 0, 0, 4); total.Name = "WeekDayTotal";
        var cap = new Border { Name = "WeekOvertimeCap", Height = 3, Width = 46, CornerRadius = new CornerRadius(2), Background = Brush(model, "Overtime"), IsVisible = overtime, HorizontalAlignment = HorizontalAlignment.Center };

        var label = Ui.Horizontal(); label.HorizontalAlignment = HorizontalAlignment.Center; label.Spacing = 5; label.VerticalAlignment = VerticalAlignment.Top;
        var name = Ui.Text(d.Date.ToString("ddd d", british), 12, d.IsToday ? "accent" : "muted"); if (d.IsToday) name.FontWeight = FontWeight.Bold;
        var underline = new Border { BorderThickness = new Thickness(0, 0, 0, d.IsToday ? 2 : 0), Child = name, Name = "WeekDayLabel" };
        if (d.IsToday) underline.BorderBrush = Brush(model, "Accent");
        label.Children.Add(underline);
        if (d.Marked) label.Children.Add(new Border { Name = "WeekDayMarker", Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Brush(model, "Ink"), VerticalAlignment = VerticalAlignment.Center });
        label.Margin = new Thickness(0, 6, 0, 0);

        var counts = Ui.Horizontal(); counts.Spacing = 8; counts.HorizontalAlignment = HorizontalAlignment.Center; counts.Margin = new Thickness(0, 2, 0, 8);
        if (d.Meetings > 0) { var m = Ui.Horizontal(new IconGlyph(IconKind.Today) { Width = 12, Height = 12 }, Ui.Text(d.Meetings.ToString(), 11, "muted")); m.Spacing = 5; m.Name = "WeekDayMeetings"; counts.Children.Add(m); }
        if (d.Tasks > 0) { var t = Ui.Horizontal(new IconGlyph(IconKind.Note) { Width = 12, Height = 12 }, Ui.Text(d.Tasks.ToString(), 11, "muted")); t.Spacing = 5; t.Name = "WeekDayTasks"; counts.Children.Add(t); }

        var bottom = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto,Auto,Auto") };
        void Row(Control c, int r) { Grid.SetRow(c, r); bottom.Children.Add(c); }
        Row(total, 1); Row(cap, 2); Row(body, 3); Row(label, 4); Row(counts, 5);
        // The label and the counts always take the same room, so the columns stand on one line whatever they carry.
        label.MinHeight = 22; counts.MinHeight = 17;
        var button = new Button { Name = "WeekDay_" + d.Date.ToString("yyyyMMdd"), Content = bottom, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Bottom };
        button.Classes.Add("weekDay");
        button.Click += (_, _) => model.OpenDayInTimeline(d.Date);
        ToolTip.SetTip(button, DayTip(model, d, british)); ToolTip.SetPlacement(button, flip ? PlacementMode.Left : PlacementMode.Right);
        Avalonia.Automation.AutomationProperties.SetName(button, $"{d.Date.ToString("dddd d MMMM", british)}, " + (d.Off ? "day off" : d.HasTime ? $"working time {model.FormatShort(d.Working)}. Opens this day in the timeline." : "nothing recorded"));
        return button;
    }

    private static Control DayTip(TimerModel model, WeekDay d, CultureInfo british)
    {
        var tip = new StackPanel { Spacing = 2, MinWidth = 180 };
        tip.Children.Add(Ui.Text(d.Date.ToString("dddd d MMMM", british) + (d.IsToday ? " (today)" : ""), 13)); ((TextBlock)tip.Children[0]).FontWeight = FontWeight.SemiBold;
        tip.Children.Add(Ui.Text(d.Off ? "Day off. Only tracked time counts." : d.HasTime ? "Working time " + model.FormatShort(d.Working) : "Nothing recorded yet.", 12, "muted"));
        foreach (var (account, time) in d.Accounts)
        {
            var row = Ui.Grid("Auto,*,Auto"); row.ColumnSpacing = 7;
            Ui.Put(row, new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.Parse(model.ChartColour(account.Colour))), VerticalAlignment = VerticalAlignment.Center });
            Ui.Put(row, Ui.Text(account.Display, 12.5), 1); Ui.Put(row, Ui.Text(model.FormatShort(time), 12.5), 2); tip.Children.Add(row);
        }
        if (d.HasTime)
        {
            var row = Ui.Grid("Auto,*,Auto"); row.ColumnSpacing = 7;
            Ui.Put(row, new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = ReviewViews.Hatch(model), VerticalAlignment = VerticalAlignment.Center });
            Ui.Put(row, Ui.Text("Unallocated", 12.5), 1); Ui.Put(row, Ui.Text(model.FormatShort(d.Unallocated), 12.5), 2); tip.Children.Add(row);
        }
        void Note(string text) { var t = Ui.Text(text, 12, "muted"); t.Margin = new Thickness(0, 3, 0, 0); tip.Children.Add(t); }
        if (d.Overtime > TimeSpan.Zero) Note("Overtime " + model.FormatShort(d.Overtime));
        if (d.Unallocated >= TimeSpan.FromMinutes(30)) Note(model.FormatShort(d.Unallocated) + " unallocated");
        if (d.Overlaps) Note("Some blocks overlap");
        if (d.Meetings > 0 || d.Tasks > 0) Note($"{d.Meetings} meeting{(d.Meetings == 1 ? "" : "s")}, {d.Tasks} open task{(d.Tasks == 1 ? "" : "s")}");
        var open = Ui.Text("Open this day ›", 11.5, "accent"); open.FontWeight = FontWeight.SemiBold; open.Margin = new Thickness(0, 5, 0, 0); tip.Children.Add(open);
        return tip;
    }

    // The old hours table as a view: accounts by day, the time on no account, and the working time under a heavier line. Copy and Export stay in Settings.
    private static Control Table(TimerModel model, WeekTable grid)
    {
        var count = grid.Headers.Length - 2;
        var columns = "26,1.5*," + string.Join(",", Enumerable.Repeat("*", count)) + ",64";
        var holder = new StackPanel { Name = "WeekTableView" };
        // One stretching row, so the words sit in the middle of a tall line.
        Grid Line(double height) { var g = Ui.Grid(columns); g.RowDefinitions = new RowDefinitions("*"); g.MinHeight = height; return g; }
        var heads = Line(26);
        Ui.Put(heads, Ui.Text("Account", 12, "muted"), 1);
        for (var i = 0; i < count; i++) { var h = Ui.Text(grid.Headers[i + 1].Split(' ')[0] + " " + int.Parse(grid.Headers[i + 1].Split(' ')[1][..2]), 12, "muted"); h.HorizontalAlignment = HorizontalAlignment.Right; Ui.Put(heads, h, 2 + i); }
        var totalHead = Ui.Text("Total", 12, "muted"); totalHead.HorizontalAlignment = HorizontalAlignment.Right; Ui.Put(heads, totalHead, 2 + count); holder.Children.Add(heads);
        var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        foreach (var row in grid.Rows.Where(r => r.Label != WeekTable.AllocatedLabel && r.Label != WeekTable.BreaksLabel))
        {
            var heavy = row.Label == WeekTable.WorkingLabel; var line = Line(heavy ? 40 : 28); line.Name = "WeekTableRow_" + row.Label.Replace(" ", "");
            var cell = new Border { BorderThickness = new Thickness(0, heavy ? 2 : 1, 0, 0), Child = line, Margin = new Thickness(0, heavy ? 4 : 0, 0, 0) }; cell.BorderBrush = Brush(model, heavy ? "Line" : "Hover");
            var account = accounts.FirstOrDefault(a => a.Display == row.Label);
            if (account is not null) Ui.Put(line, new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.Parse(model.ChartColour(account.Colour))), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            else if (row.Label == WeekTable.UnallocatedLabel) Ui.Put(line, new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = ReviewViews.Hatch(model), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            var name = Ui.Text(row.Label, 13.5); name.FontWeight = heavy ? FontWeight.Bold : row.Label == WeekTable.UnallocatedLabel ? FontWeight.Normal : FontWeight.Medium; name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, name, 1);
            for (var i = 0; i < count; i++) { var t = Ui.Text(row.Cells[i], 13, heavy ? null : "muted"); t.Classes.Add("figures"); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, t, 2 + i); }
            // The totals are in the ordinary typeface, semibold: the display face at a heavy weight was hard to read at this size.
            var sum = Ui.Text(row.Total, 13.5, null); sum.FontWeight = heavy ? FontWeight.Bold : FontWeight.SemiBold; sum.HorizontalAlignment = HorizontalAlignment.Right; sum.VerticalAlignment = VerticalAlignment.Center; sum.Name = "WeekTableTotal"; Ui.Put(line, sum, 2 + count);
            holder.Children.Add(cell);
        }
        return holder;
    }

    // ---- By account: the week's time and the budget, kept to what can be read at a glance. The share of the week and the pace word are in the hover detail. ----
    private static Control ByAccount(TimerModel model, WeekFigures w, WeekTable grid)
    {
        var tile = new StackPanel { Name = "WeekByAccount", Spacing = 2 };
        var title = Ui.Text("By account", 15); title.FontWeight = FontWeight.SemiBold; title.Margin = new Thickness(0, 0, 0, 6); tile.Children.Add(title);
        const string columns = "26,1.2*,64,18,2*,200";
        var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var weekEnd = w.Start.AddDays(7);
        foreach (var account in accounts)
        {
            var week = Enumerable.Range(0, 7).Aggregate(TimeSpan.Zero, (sum, i) => sum + model.OnDay(account, w.Start.AddDays(i)));
            var line = Ui.Grid(columns); line.Height = 34; line.Name = "WeekAccount_" + account.Name.Replace(" ", "");
            var row = new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush(model, "Hover"), Child = line };
            Ui.Put(line, new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.Parse(model.ChartColour(account.Colour))), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            var name = Ui.Text(account.Display, 14); name.FontWeight = FontWeight.Medium; name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, name, 1);
            var total = Ui.Text(model.FormatShort(week), 14); total.FontWeight = FontWeight.SemiBold; total.HorizontalAlignment = HorizontalAlignment.Right; total.VerticalAlignment = VerticalAlignment.Center; total.Name = "WeekAccountTotal"; Ui.Put(line, total, 2);
            var pct = w.Working > TimeSpan.Zero ? $"{Math.Round(week.TotalSeconds / w.Working.TotalSeconds * 100):0}% of the week's working time" : "";
            var budget = grid.Budgets.FirstOrDefault(b => b.Account == account.Display);
            if (account.Budget is { } target && budget is not null)
            {
                var reference = model.BudgetReference(w.Start); var used = model.BudgetUsedAt(account, reference);
                var (from, to) = model.BudgetWindowAt(account, reference);
                var elapsed = Math.Clamp((Min(model.Engine.Now, Min(weekEnd, to)) - from).TotalSeconds / (to - from).TotalSeconds, 0, 1);
                var over = budget.Fraction >= 1; var warn = budget.Fraction >= .8 && !over;
                var track = Ui.Grid("0*,1*"); track.Height = 6; track.VerticalAlignment = VerticalAlignment.Center;
                var fraction = Math.Clamp(budget.Fraction, 0.0, 1.0);
                track.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star); track.ColumnDefinitions[1].Width = new GridLength(1.0 - fraction, GridUnitType.Star);
                var rest = new Border { CornerRadius = new CornerRadius(3), Height = 6 }; rest.Classes.Add("rule"); Grid.SetColumn(rest, 1);
                var fill = new Border { CornerRadius = new CornerRadius(3), Height = 6 }; fill.Classes.Add("budgetFill"); fill.Classes.Set("budgetWarn", warn); fill.Classes.Set("budgetOver", over);
                track.Children.Add(rest); track.Children.Add(fill); Ui.Put(line, track, 4);
                // One short caption: what is used of the budget, and the period only when it is not the week.
                var period = budget.Period.Equals("This week", StringComparison.OrdinalIgnoreCase) ? "" : "  " + budget.Period.ToLowerInvariant().Replace("this ", "");
                var caption = Ui.Text($"{budget.Used} of {budget.Budget}{period}", 12.5, over ? "overtime" : warn ? "budgetWarning" : "muted");
                caption.Name = "BudgetCaption"; caption.Margin = new Thickness(12, 0, 0, 0); caption.TextTrimming = TextTrimming.CharacterEllipsis; caption.VerticalAlignment = VerticalAlignment.Center;
                if (over || warn) caption.FontWeight = FontWeight.SemiBold;
                Ui.Put(line, caption, 5);
                var detail = $"{budget.Fraction * 100:0}% of the {budget.Period.ToLowerInvariant()} budget used, with {elapsed * 100:0}% of the period gone. {PaceOf(used, target, elapsed, model.FormatShort)}." + (pct.Length > 0 ? " " + pct + "." : "");
                ToolTip.SetTip(row, detail);
            }
            else { var none = Ui.Text("No budget", 12.5, "muted"); none.VerticalAlignment = VerticalAlignment.Center; none.Name = "WeekNoBudget"; Ui.Put(line, none, 4).SetValue(Grid.ColumnSpanProperty, 2); if (pct.Length > 0) ToolTip.SetTip(row, pct + "."); }
            tile.Children.Add(row);
        }
        return Tile(tile, new Thickness(18, 12, 18, 10));
    }
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
