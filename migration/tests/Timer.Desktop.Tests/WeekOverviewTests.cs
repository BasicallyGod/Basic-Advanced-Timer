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
            Assert.NotNull(Find<Border>(window, "WeekBar"));
            Assert.NotNull(Find<Border>(window, "WeekScheduleLine")); Assert.StartsWith("Schedule", Find<TextBlock>(window, "WeekScheduleLabel").Text); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekChange");
            var friday = Find<Button>(window, "WeekDay_20261009");
            Assert.Contains("working time", Avalonia.Automation.AutomationProperties.GetName(friday)); Assert.NotNull(ToolTip.GetTip(friday));
            Assert.Contains(friday.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekDayLabel" || t.Text == "Fri 9"); // Today's label stands out.
        }
        finally { window.Close(); }
    }

    // A day whose accounts run all the way through the schedule's hours and five minutes past them, with forty minutes of breaks laid over the middle: more on accounts
    // than there is working time, which is the breaks' share. The working time is the hours less the breaks and plus the five minutes.
    private static TimerModel DayWithBreaks(out WeekDay day, out TimeSpan span)
    {
        var tuesday = new DateTime(2026, 10, 6);
        var probe = Model(); var plan = probe.Engine.Plan(tuesday); var length = plan.End - plan.Start; var from = plan.Start.TimeOfDay; span = length;
        string At(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:00";
        var model = Model(e => e.Sessions.AddRange(new[] { Row(At(from), ((int)(length.TotalMinutes + 5)).ToString(), "Account one", "2026-10-06"), Row(At(from + length / 2), "40", "Break", "2026-10-06") }));
        day = WeekOverview.Figures(model, new DateTime(2026, 10, 5)).Days.Single(d => d.Date == tuesday); return model;
    }

    [Fact]
    public void TheScheduleIsTheHoursLessTheBreakAllowanceAndADayIsMeasuredAgainstThat()
    {
        var model = DayWithBreaks(out var day, out var span);
        var tuesday = new DateTime(2026, 10, 6);
        Assert.Equal(span, model.ScheduledOn(tuesday)); Assert.Equal(TimeSpan.Zero, model.BreakAllowance); Assert.False(model.BreakAllowanceChosen);
        Assert.True(day.Working < day.Scheduled); // Forty minutes of breaks and the hours measured as they are: the day looks short, as it did.
        model.SetBreakAllowance(TimeSpan.FromMinutes(40));
        Assert.True(model.BreakAllowanceChosen); Assert.Equal(span - TimeSpan.FromMinutes(40), model.ScheduledOn(tuesday));
        day = WeekOverview.Figures(model, new DateTime(2026, 10, 5)).Days.Single(d => d.Date == tuesday);
        Assert.Equal(span - TimeSpan.FromMinutes(40), day.Scheduled); Assert.Equal(TimeSpan.FromMinutes(5), day.Working - day.Scheduled); // Five minutes over the schedule, which is what the day was.
        var week = WeekOverview.Figures(model, new DateTime(2026, 10, 5));
        Assert.Equal(week.Days.Aggregate(TimeSpan.Zero, (sum, d) => sum + d.Scheduled), week.Scheduled);
        Assert.Equal(TimeSpan.Zero, model.ScheduledOn(new DateTime(2026, 10, 10))); // A Saturday is not a working day, so there is nothing to take a break from.
        model.SetBreakAllowance(TimeSpan.FromHours(30)); Assert.Equal(TimeSpan.FromHours(4), model.BreakAllowance); // Kept sensible.
        Assert.Equal(span - TimeSpan.FromHours(4), model.ScheduledOn(tuesday));
        model.SetShifts(new() { [DayOfWeek.Tuesday] = new(TimeSpan.FromHours(10), TimeSpan.FromHours(12)) }); Assert.Equal(TimeSpan.Zero, model.ScheduledOn(tuesday)); // More than the day's hours leaves nothing, never less.
        model.SetBreakAllowance(TimeSpan.Zero); Assert.True(model.BreakAllowanceChosen); Assert.Equal(TimeSpan.FromHours(2), model.ScheduledOn(tuesday)); // Nothing is a choice too, and the whole of the two hours is scheduled again.
    }

    [AvaloniaFact]
    public void TheChartTheBarAndToGoMeasureWorkingTimeAgainstTheNetSchedule()
    {
        var model = DayWithBreaks(out var day, out var span); model.SetBreakAllowance(TimeSpan.FromMinutes(40));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            var week = WeekOverview.Figures(model, new DateTime(2026, 10, 5));
            var column = Find<Button>(window, "WeekDay_20261006");
            Assert.DoesNotContain(column.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekBreaksSlice"); // Breaks are no part of the column.
            // The column is the working time (the accounts' slices squeezed to it), drawn at the same scale as the schedule line stands at.
            var top = Math.Max(Math.Max(week.Days.Max(d => d.Scheduled.TotalHours), week.Days.Max(d => d.Working.TotalHours)), 8);
            var perHour = Math.Min(17, (232 - 62 - 22) / top);
            var slices = column.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is TimerAccount).Sum(b => b.Height);
            Assert.Equal(day.Working.TotalHours * perHour, slices, 1);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekBarBreaks");
            var left = week.Scheduled - week.Working; var words = Find<TextBlock>(window, "WeekToGo").Text!;
            Assert.Equal(model.FormatShort(left > TimeSpan.Zero ? left : TimeSpan.Zero) + " to go", words);
            Assert.Equal("Scheduled " + model.FormatShort(week.Scheduled), Find<TextBlock>(window, "WeekScheduled").Text);
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
