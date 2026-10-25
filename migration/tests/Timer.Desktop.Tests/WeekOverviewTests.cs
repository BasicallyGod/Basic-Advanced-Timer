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
            Assert.NotNull(Find<Border>(window, "WeekBar")); Assert.False(Find<Border>(window, "WeekBarOvertime").IsVisible);
            Assert.NotNull(Find<Border>(window, "WeekScheduleLine")); Assert.StartsWith("Schedule", Find<TextBlock>(window, "WeekScheduleLabel").Text); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekChange");
            var friday = Find<Button>(window, "WeekDay_20261009");
            Assert.Contains("working time", Avalonia.Automation.AutomationProperties.GetName(friday)); Assert.NotNull(ToolTip.GetTip(friday));
            Assert.Contains(friday.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "WeekDayLabel" || t.Text == "Fri 9"); // Today's label stands out.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OvertimeTurnsTheTotalAndTheColumnRedAndAddsAnOvertimeFigure()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("10:00:00", "30"), Row("08:00:00", "60", date: "2026-10-06") }));
        model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(10), TimeSpan.FromHours(11)) }); // Five hours past the end of the day at 16:00.
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            Assert.True(WeekOverview.Figures(model, new DateTime(2026, 10, 5)).Overtime > TimeSpan.Zero);
            Assert.True(Find<Border>(window, "WeekBarOvertime").IsVisible); Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekOvertimeCap" && b.IsVisible);
            Assert.NotNull(Find<TextBlock>(window, "WeekOvertime")); Assert.Contains("overtime", Find<TextBlock>(window, "WeekWorking").Classes);
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
