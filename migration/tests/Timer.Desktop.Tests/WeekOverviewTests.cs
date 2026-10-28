using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The Week overview as the mock draws it: the totals strip, the day chart or table, the markers and the accounts with their pace.
public sealed class WeekOverviewTests
{
    private static DateTime Day => new(2026, 10, 9); // A Friday; the week starts on Monday 5 October.
    private static SessionRow Row(string start, string minutes, string account = "Account one", string date = "2026-10-09") => new(date, account, start, "added", minutes);
    private static TimerModel Model(Action<TrackingEngine>? seed = null, Func<DateTime>? clock = null)
    {
        var e = new TrackingEngine(clock: clock ?? (() => Day.AddHours(16))); seed?.Invoke(e);
        var model = new TimerModel(e, new StoreSettings(new() { new("Account one", 10), new("Account two"), new("Account three", 40, "month") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }

    [Fact]
    public void ThePaceWordIsOverAheadOrOnPace()
    {
        string F(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";
        Assert.Equal("Over by 01:36", WeekOverview.PaceOf(TimeSpan.FromHours(11.6), TimeSpan.FromHours(10), .5, F));
        Assert.Equal("Ahead of pace", WeekOverview.PaceOf(TimeSpan.FromHours(7), TimeSpan.FromHours(10), .4, F));
        Assert.Equal("On pace", WeekOverview.PaceOf(TimeSpan.FromHours(4.5), TimeSpan.FromHours(10), .4, F));
        Assert.Equal("On pace", WeekOverview.PaceOf(TimeSpan.Zero, TimeSpan.Zero + TimeSpan.FromHours(1), 0, F));
    }

    [Fact]
    public void TheWeekAddsUpAndComparesWithLastWeek()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two"), Row("12:00:00", "30", "Break"), Row("08:00:00", "60", date: "2026-10-06"), Row("08:00:00", "30", date: "2026-09-29") }));
        var w = WeekOverview.Figures(model, new DateTime(2026, 10, 5));
        Assert.True(w.Current); Assert.Equal(TimeSpan.FromMinutes(180), w.Allocated); Assert.Equal(w.Allocated + w.Unallocated, w.Working);
        Assert.Equal(TimeSpan.FromMinutes(30), w.Breaks); Assert.Equal(5, w.Days.Count);
        Assert.True(w.Scheduled > TimeSpan.Zero);
    }

    [Fact]
    public void ADayWithHalfAnHourOnNoAccountOrOverlappingBlocksIsMarked()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "60"), Row("09:30:00", "30", "Account two"), Row("08:00:00", "60", date: "2026-10-06") }));
        var days = WeekOverview.Figures(model, new DateTime(2026, 10, 5)).Days;
        Assert.True(days.Single(d => d.Date == Day).Overlaps); Assert.True(days.Single(d => d.Date == Day).Marked);
    }

    [AvaloniaFact]
    public void TheTotalsStripShowsWorkingTimeTheBarAndTheFiguresAndTheChartHasAColumnPerDayWithAScheduleLine()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two"), Row("08:00:00", "60", date: "2026-10-06") }));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            var w = WeekOverview.Figures(model, new DateTime(2026, 10, 5));
            Assert.Equal(model.Format(w.Working), Find<TextBlock>(window, "WeekWorking").Text); Assert.Equal(model.FormatShort(w.Allocated), Find<TextBlock>(window, "WeekAllocated").Text);
            Assert.Equal("Scheduled " + model.FormatShort(w.Scheduled), Find<TextBlock>(window, "WeekScheduled").Text); Assert.EndsWith("to go", Find<TextBlock>(window, "WeekToGo").Text);
            Assert.NotNull(Find<Border>(window, "WeekBar")); Assert.NotNull(Find<Border>(window, "WeekBarBreaks"));
            Assert.NotNull(Find<Border>(window, "WeekScheduleLine")); Assert.StartsWith("Schedule", Find<TextBlock>(window, "WeekScheduleLabel").Text); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekChange");
            var friday = Find<Button>(window, "WeekDay_20261009");
            Assert.Contains("working time", Avalonia.Automation.AutomationProperties.GetName(friday)); Assert.NotNull(ToolTip.GetTip(friday));
            Assert.Contains(friday.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekDayLabel" || t.Text == "Fri 9"); // Today's label stands out.
        }
        finally { window.Close(); }
    }

    // A day on the clock: its working time and its breaks together are what the schedule's hours are measured against, so a day whose accounts add up to a little more than
    // the schedule is not drawn as short of it only because its breaks are left out of the column.
    private static TimerModel DayWithBreaks(out WeekDay day, out TimeSpan scheduled)
    {
        var tuesday = new DateTime(2026, 10, 6); var span = TimeSpan.Zero; var from = TimeSpan.Zero;
        var probe = Model(); var plan = probe.Engine.Plan(tuesday); span = plan.End - plan.Start; from = plan.Start.TimeOfDay;
        string At(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:00";
        // Accounts all the way through the schedule and five minutes past it, with forty minutes of breaks laid over the middle of it.
        var model = Model(e => e.Sessions.AddRange(new[] { Row(At(from), ((int)(span.TotalMinutes + 5)).ToString(), "Account one", "2026-10-06"), Row(At(from + span / 2), "40", "Break", "2026-10-06") }));
        day = WeekOverview.Figures(model, new DateTime(2026, 10, 5)).Days.Single(d => d.Date == tuesday); scheduled = span; return model;
    }

    [Fact]
    public void ADaysWorkingTimeAndBreaksTogetherAreWhatTheScheduleIsMeasuredAgainst()
    {
        var model = DayWithBreaks(out var day, out var scheduled);
        Assert.Equal(scheduled + TimeSpan.FromMinutes(5), day.Covered); // Working time and the breaks: the clock the day covered.
        Assert.Equal(TimeSpan.FromMinutes(40), day.Breaks); Assert.True(day.Working < scheduled); // Left out, the day looks short of the schedule.
        Assert.True(day.Covered > day.Scheduled);
        var week = WeekOverview.Figures(model, new DateTime(2026, 10, 5));
        Assert.Equal(week.Working + week.Breaks, week.Covered); Assert.False(typeof(WeekFigures).GetProperties().Any(p => p.Name == "Overtime")); // There is no overtime figure.
    }

    [AvaloniaFact]
    public void TheChartDrawsBreaksOnTopOfTheColumnAndTheStripCountsThemAgainstTheSchedule()
    {
        var model = DayWithBreaks(out var day, out var scheduled);
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            var column = Find<Button>(window, "WeekDay_20261006");
            var slice = column.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WeekBreaksSlice");
            var perHour = slice.Height / TimeSpan.FromMinutes(40).TotalHours;
            var stack = (StackPanel)slice.Parent!; var height = stack.Children.OfType<Border>().Sum(b => b.Height);
            Assert.Equal(day.Covered.TotalHours * perHour, height, 1); // The whole column is the clock time covered, taller than the schedule's own span.
            Assert.True(height > scheduled.TotalHours * perHour);
            Assert.Equal(day.Working.TotalHours * perHour, height - slice.Height, 1); // The accounts' slices are squeezed to the working time, so they sit under the breaks.
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekBarBreaks");
            var week = WeekOverview.Figures(model, new DateTime(2026, 10, 5));
            var left = week.Scheduled - week.Covered; var words = Find<TextBlock>(window, "WeekToGo").Text!;
            Assert.Equal(model.FormatShort(left > TimeSpan.Zero ? left : TimeSpan.Zero) + " to go", words);
            // Nothing is called overtime any more, and nothing is red for being late.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "WeekOvertime" or "WeekOvertimeCap" or "WeekBarOvertime");
            Assert.DoesNotContain("overtime", Find<TextBlock>(window, "WeekWorking").Classes); Assert.DoesNotContain("overtime", Find<TextBlock>(column, "WeekDayTotal").Classes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingADayOpensItInTheTimeline()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("08:00:00", "60", date: "2026-10-06"), Row("09:00:00", "60") }));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            Click(window, "WeekDay_20261006");
            Assert.Equal(new DateTime(2026, 10, 6), model.SelectedDate); Assert.Equal("Timeline", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("clock") && t.Text is "Timeline" or "Week overview").Text);
            Assert.True(Find<Border>(window, "TimelineCard").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTableViewListsAccountsUnallocatedAndWorkingTimeAndTheSwitchGoesBack()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two") }));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<StackPanel>(), p => p.Name == "WeekTableView");
            Click(window, "WeekTable");
            Assert.NotNull(Find<StackPanel>(window, "WeekTableView")); Assert.NotNull(Find<Grid>(window, "WeekTableRow_Workingtime")); Assert.NotNull(Find<Grid>(window, "WeekTableRow_Unallocated")); Assert.NotNull(Find<Grid>(window, "WeekTableRow_Accountone"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Grid>(), g => g.Name == "WeekTableRow_Breaks");
            Click(window, "WeekChart"); Assert.DoesNotContain(window.GetVisualDescendants().OfType<StackPanel>(), p => p.Name == "WeekTableView");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsListIsKeptShortAndTheHeaderNamesTheWeekAndOffersThisWeek()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("07:00:00", "720"), Row("11:00:00", "30", "Account two") }));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            // The row keeps to the time and the budget; the share of the week and the pace are in its hover detail.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekPace" || t.Text is "Share" or "Pace");
            Assert.Contains("Over by 02:00", ToolTip.GetTip((Control)Find<Grid>(window, "WeekAccount_Accountone").Parent!)!.ToString());
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekNoBudget" && t.Text == "No budget");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "5 – 11 Oct");
            var thisWeek = Find<Button>(window, "GoToToday"); Assert.False(thisWeek.IsEnabled);
            model.ChangeWeek(-1); Settle(window); Assert.True(thisWeek.IsEnabled); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "This week");
            Click(window, "GoToToday"); Assert.Equal(model.WeekStart(Day), model.WeekStart(model.SelectedDate)); Assert.False(thisWeek.IsEnabled);
        }
        finally { window.Close(); }
    }
}
