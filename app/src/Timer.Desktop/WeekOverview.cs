using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>One day of the week overview as plain figures, so the screen, its tooltips and the tests read the same numbers.</summary>
public sealed record WeekDay(DateTime Date, bool IsToday, bool Off, bool HasTime, TimeSpan Working, TimeSpan Breaks, TimeSpan Unallocated, TimeSpan Scheduled,
    IReadOnlyList<(TimerAccount Account, TimeSpan Time)> Accounts, int Meetings, int Tasks, bool Overlaps)
{

    /// <summary>A quiet dot marks a day with half an hour or more on no account, or with blocks laid over each other.</summary>
    public bool Marked => Unallocated >= TimeSpan.FromMinutes(30) || Overlaps;
}

/// <summary>The week for the totals strip and the chart.</summary>
public sealed record WeekFigures(DateTime Start, IReadOnlyList<WeekDay> Days, TimeSpan Working, TimeSpan Allocated, TimeSpan Unallocated, TimeSpan Breaks,
    TimeSpan Scheduled, bool Current);

/// <summary>The Week overview page, as the mock draws it: a totals strip, the day chart or table, and the accounts with their budgets and pace.</summary>
public static class WeekOverview
{
    /// <summary>Pace words for a budget: over it, ahead of where the period should have got it, or on pace.</summary>
    public static string PaceOf(TimeSpan used, TimeSpan budget, double elapsed, Func<TimeSpan, string> format)
        => used >= budget ? "Over by " + format(used - budget) : budget > TimeSpan.Zero && used.TotalSeconds / budget.TotalSeconds > elapsed + 0.1 ? "Ahead of pace" : "On pace";

    public static WeekFigures Figures(TimerModel model, DateTime weekStart)
    {
        var engine = model.Engine; var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList(); var today = model.TodayKey;
        var shown = Enumerable.Range(0, 7).Where(i => engine.Schedule.Days.Contains(weekStart.AddDays(i).DayOfWeek)
            || engine.Sessions.Any(s => model.CountsOn(s, weekStart.AddDays(i))) || (engine.Active is not null && weekStart.AddDays(i) == today)).ToArray();
        if (shown.Length == 0) shown = Enumerable.Range(0, 5).ToArray();
        var days = new List<WeekDay>();
        foreach (var i in shown)
        {
            var date = weekStart.AddDays(i); var plan = engine.Plan(date);
            var per = accounts.Select(a => (a, model.OnDay(a, date))).Where(p => p.Item2 > TimeSpan.Zero).ToList();
            var allocated = per.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Item2);
            var working = model.WorkingOn(date); var left = working - allocated; var dayBreaks = model.BreakTimeOn(date);
            var blocks = engine.Sessions.Where(s => model.CountsOn(s, date) && s.Duration > TimeSpan.Zero && !BreakAccount.Is(s.Account)).OrderBy(s => s.From).ToList();
            var overlaps = false; var reach = DateTime.MinValue;
            foreach (var block in blocks) { if (block.From.AddSeconds(2) < reach) overlaps = true; if (block.To > reach) reach = block.To; }
            var meetings = model.Notes.MeetingsOn(date).Count;
            var tasks = model.Notes.All.Count(n => n.HasDate && n.Day.Date == date && !n.Done && !NotesModel.IsMeeting(n));
            var hasTime = working > TimeSpan.Zero || allocated > TimeSpan.Zero;
            days.Add(new WeekDay(date, date == today, date < today && !hasTime && !plan.Working, hasTime, working, dayBreaks, left > TimeSpan.Zero ? left : TimeSpan.Zero,
                model.ScheduledOn(date), per.Select(p => (p.a, p.Item2)).ToList(), meetings, tasks, overlaps));
        }
        // Saturday and Sunday are drawn only when they hold time; a quiet weekend is not a column of nothing.
        days = days.Where(d => d.HasTime || d.Scheduled > TimeSpan.Zero || d.Off || d.IsToday || (d.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))).ToList();
        var week = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();
        var working7 = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + model.WorkingOn(d));
        var allocatedWeek = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + accounts.Aggregate(TimeSpan.Zero, (s, a) => s + model.OnDay(a, d)));
        var unallocated = working7 - allocatedWeek; if (unallocated < TimeSpan.Zero) unallocated = TimeSpan.Zero;
        var breaks = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + model.BreakTimeOn(d));
        var scheduled = week.Aggregate(TimeSpan.Zero, (sum, d) => sum + model.ScheduledOn(d));
        return new WeekFigures(weekStart, days, working7, allocatedWeek, unallocated, breaks, scheduled, today >= weekStart && today < weekStart.AddDays(7));
    }

    public static Control Build(TimerModel model)
    {
        var start = model.WeekStart(model.SelectedDate);
        var figures = Figures(model, start); var (grid, _) = ReviewViews.WeekTableFor(model, start);
        // One instrument on one display: the totals, the days and the accounts, in whichever look is chosen.
        var instrument = new WeekInstrument(model, figures, grid);
        var holder = DisplayInk.On ? InkPanel(model, instrument) : model.CurrentTheme.Dark ? VfdWeek.Panel(instrument, double.NaN, "WeekPanel") : PalePanel(instrument);
        var root = Ui.Grid("*", "*"); root.Margin = new Thickness(0, 0, 14, 4); Ui.Put(root, holder); return root;
    }
    // The Phosphor look on a Light theme: the pale flat well of the pill and sidebar, with a hairline.
    private static Control PalePanel(Control content) => new Border { Name = "WeekPanel", CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = content,
        Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#F0F0EE"), 0), new GradientStop(Color.Parse("#EBEBE9"), 1) } },
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 60, 58, 70)), BorderThickness = new Thickness(1) };
    // The e-ink panel: the theme's paper with a hairline round it, the same corner as the dark glass.
    private static Control InkPanel(TimerModel model, Control content)
    {
        var colours = DisplayInk.For(model.CurrentTheme.Dark);
        return new Border { Name = "WeekPanel", CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = content, Background = new SolidColorBrush(colours.Paper),
            BorderBrush = new SolidColorBrush(Color.FromArgb(model.CurrentTheme.Dark ? (byte)46 : (byte)40, colours.Ink.R, colours.Ink.G, colours.Ink.B)), BorderThickness = new Thickness(1) };
    }

    // A day's tooltip, as the Round 7 board draws it: the day and its working time, a line per account, then what is on no account. No sentences.
    internal static Control DayTip(TimerModel model, WeekDay d, CultureInfo british)
    {
        var tip = new StackPanel { Spacing = 3, MinWidth = 190 };
        var head = Ui.Grid("*,Auto"); head.ColumnSpacing = 16;
        var title = Ui.Text(d.Date.ToString("ddd d MMM", british) + (d.IsToday ? " · today" : ""), 13); title.FontWeight = FontWeight.SemiBold; Ui.Put(head, title);
        Ui.Put(head, Ui.Text(d.Off ? "Off" : d.HasTime ? model.FormatShort(d.Working) : "–", 13), 1); tip.Children.Add(head);
        if (d.Off) return tip;
        void Line(Control mark, string name, TimeSpan time, bool quiet = false)
        {
            var row = Ui.Grid("Auto,*,Auto"); row.ColumnSpacing = 7;
            Ui.Put(row, mark); Ui.Put(row, Ui.Text(name, 12, quiet ? "muted" : null), 1); Ui.Put(row, Ui.Text(model.FormatShort(time), 12, quiet ? "muted" : null), 2); tip.Children.Add(row);
        }
        Control Dot(IBrush fill) => new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = fill, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (account, time) in d.Accounts) Line(Dot(new SolidColorBrush(Color.Parse(model.ChartColour(account.Colour)))), account.Display, time);
        if (d.HasTime && d.Unallocated > TimeSpan.FromSeconds(30)) Line(Dot(ReviewViews.Hatch(model)), "Unallocated", d.Unallocated, quiet: true);
        if (d.Breaks > TimeSpan.FromSeconds(30)) Line(Dot(new SolidColorBrush(Color.Parse(model.CurrentTheme.Colour("Break")))), "Breaks", d.Breaks, quiet: true);
        void Note(string text) { var line = Ui.Text(text, 12, "muted"); line.Margin = new Thickness(0, 2, 0, 0); tip.Children.Add(line); }
        if (d.Overlaps) Note("Some blocks overlap");
        return tip;
    }
}
