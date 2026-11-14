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
    public void ClickingADayOpensItInTheTimeline()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("08:00:00", "60", date: "2026-10-06"), Row("09:00:00", "60") }));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            Click(window, "WeekDay_20261006");
            Assert.Equal(new DateTime(2026, 10, 6), model.SelectedDate); Assert.Contains("selected", Find<Button>(window, "TimelineTab").Classes); // The Timeline tab is the chosen one.
            Assert.True(Find<Border>(window, "TimelineCard").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    // ---- the Week instrument, in both looks

    private static DashboardWindow Open(TimerModel model, bool ink)
    {
        if (ink) model.SetDisplayStyle(DisplayStyle.Ink);
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window); return window;
    }
    private static WeekInstrument Instrument(Window window) => window.GetVisualDescendants().OfType<WeekInstrument>().Single();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheWeekIsOneInstrumentWithADayButtonForEachWorkingDayAndTheTwoViewKeys(bool ink)
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two"), Row("08:00:00", "60", date: "2026-10-06") }));
        var window = Open(model, ink);
        try
        {
            var instrument = Instrument(window);
            Assert.Equal(5, window.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("WeekDay_") == true)); // No weekend.
            var friday = Find<Button>(window, "WeekDay_20261009");
            Assert.Contains("working", Avalonia.Automation.AutomationProperties.GetName(friday)); Assert.NotNull(ToolTip.GetTip(friday));
            Assert.NotNull(Find<Button>(window, "WeekKeyWeek")); Assert.NotNull(Find<Button>(window, "WeekKeySheet"));
            // The old chart, table and tiles are gone, and so are the buttons for Copy and Export on the page (they live in Settings).
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name is "WeekChart" or "WeekTable" or "WeekCopyHours" or "WeekExportCsv");
            Assert.False(instrument.Sheet);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTimesheetKeySwapsTheDayStripsForHoursADayAndTheWeekKeyGoesBack(bool ink)
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two") }));
        var window = Open(model, ink);
        try
        {
            Click(window, "WeekKeySheet"); Assert.True(model.WeekAsTable); Assert.True(Instrument(window).Sheet);
            Assert.Equal(5, window.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("WeekDay_") == true)); // The weekday heads still open their day.
            Click(window, "WeekKeyWeek"); Assert.False(model.WeekAsTable); Assert.False(Instrument(window).Sheet);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccountsWithNoTimeAndNoBudgetFoldIntoOneRowThatShowAndHideBring(bool ink)
    {
        var model = Model(e => e.Sessions.Add(Row("09:00:00", "90")));
        var window = Open(model, ink);
        try
        {
            var instrument = Instrument(window);
            Assert.Equal(1, instrument.Folded); Assert.DoesNotContain("Account two", instrument.ShownAccounts); Assert.Contains("Account three", instrument.ShownAccounts); // A budget keeps its row even with no time.
            Click(window, "WeekShowQuiet"); Assert.Contains("Account two", instrument.ShownAccounts);
            Click(window, "WeekShowQuiet"); Assert.DoesNotContain("Account two", instrument.ShownAccounts);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PressingAnAccountTracesItAndPressingItAgainLetsGo(bool ink)
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two") }));
        var window = Open(model, ink);
        try
        {
            var instrument = Instrument(window); Assert.Null(instrument.Traced);
            Click(window, "WeekAccount_Accountone"); Assert.Equal("Account one", instrument.Traced);
            Click(window, "WeekAccount_Accountone"); Assert.Null(instrument.Traced);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongListOfAccountsScrollsByWholeRowsAndTheRowsOnShowStayInOrder()
    {
        var model = TimerModel.CreateDemo(() => Day.AddHours(12)); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            var instrument = Instrument(window); var all = instrument.RowCount; var capacity = instrument.Capacity;
            var before = instrument.ShownAccounts.ToList(); Assert.NotEmpty(before);
            if (all > capacity) { instrument.ScrollBy(1); Assert.NotEqual(before, instrument.ShownAccounts); instrument.ScrollBy(-1); Assert.Equal(before, instrument.ShownAccounts); }
            instrument.ScrollBy(-5); Assert.Equal(before, instrument.ShownAccounts); // It stops at the top.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABudgetRowSaysItsPaceInItsDetailAndTheHeaderNamesTheWeek()
    {
        var model = Model(e => e.Sessions.AddRange(new[] { Row("07:00:00", "720"), Row("11:00:00", "30", "Account two") }));
        var window = Open(model, false);
        try
        {
            Assert.Contains("Over by 02:00", ToolTip.GetTip(Find<Button>(window, "WeekAccount_Accountone"))!.ToString());
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "5 – 11 Oct");
            var thisWeek = Find<Button>(window, "GoToToday"); Assert.False(thisWeek.IsEnabled);
            model.ChangeWeek(-1); Settle(window); Assert.True(thisWeek.IsEnabled); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "This week");
            Click(window, "GoToToday"); Assert.Equal(model.WeekStart(Day), model.WeekStart(model.SelectedDate)); Assert.False(thisWeek.IsEnabled);
        }
        finally { window.Close(); }
    }
}
