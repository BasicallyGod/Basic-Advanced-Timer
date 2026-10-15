using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>An overnight shift (22:00 to 06:00) belongs to the day it starts and runs on across midnight: the clock, finishing and overtime follow the shift, not the calendar.</summary>
public sealed class OvernightShiftTests
{
    private static readonly DateTime Tue = new(2026, 10, 13), Wed = new(2026, 10, 14);
    private static (TrackingEngine Engine, Action<DateTime> At) Night()
    {
        var now = Tue.AddHours(21);
        var schedule = new WorkSchedule { Default = new Shift(TimeSpan.FromHours(22), TimeSpan.FromHours(6)) };
        var engine = new TrackingEngine(schedule, () => now);
        return (engine, t => now = t);
    }
    private static TimeSpan H(double hours) => TimeSpan.FromHours(hours);

    [Fact]
    public void AnEndBeforeTheStartMakesAShiftThatEndsTheNextMorning()
    {
        var (engine, _) = Night(); var plan = engine.Schedule.Scheduled(Tue);
        Assert.Equal(Tue.AddHours(22), plan.Start); Assert.Equal(Wed.AddHours(6), plan.End); Assert.True(WorkSchedule.IsOvernight(plan));
        var tail = engine.Schedule.Tail(Wed)!; Assert.Equal(Wed, tail.Start); Assert.Equal(Wed.AddHours(6), tail.End);
        Assert.Null(new WorkSchedule().Tail(Wed)); Assert.False(WorkSchedule.IsOvernight(new WorkSchedule().Scheduled(Tue))); // An ordinary day is as it was.
        Assert.Equal(H(4), engine.Schedule.WorkingTimeBetween(Wed.AddHours(1), Wed.AddHours(5))); // Picked up from the shift that began the evening before.
        Assert.Equal(H(14), engine.Schedule.WorkingTimeBetween(Tue, Wed.AddHours(12))); // Monday night's last six hours, then Tuesday night's eight.
    }

    [Fact]
    public void TheWorkingDayStaysWithTheShiftUntilItsOvertimeRunsOutOrTheNextShiftStarts()
    {
        var (engine, at) = Night();
        Assert.Equal(Tue, engine.ShiftKey(Tue.AddHours(23))); Assert.Equal(Tue, engine.ShiftKey(Wed.AddHours(2))); Assert.Equal(Tue, engine.ShiftKey(Wed.AddHours(11)));
        Assert.Equal(Wed, engine.ShiftKey(Wed.AddHours(12.5))); // The tail has run out.
        Assert.Equal(Wed, engine.ShiftKey(Wed.AddHours(22.5))); // The next shift has started.
        at(Wed.AddHours(2)); Assert.Equal(Tue, engine.CurrentKey);
        // With a day schedule nothing changes: the day is its own date all through.
        var plain = new TrackingEngine(new WorkSchedule(), () => Wed.AddHours(1)); Assert.Equal(Wed, plain.ShiftKey(Wed.AddHours(1)));
    }

    [Fact]
    public void TheClockCarriesOnAcrossMidnightAndOvertimeStartsWhenTheShiftEnds()
    {
        var (engine, at) = Night();
        at(Tue.AddHours(23)); Assert.True(engine.DayRunning); Assert.Equal(H(1), engine.ShiftTotals(engine.CurrentKey).Elapsed);
        at(Wed.AddHours(2)); var t = engine.ShiftTotals(engine.CurrentKey); Assert.Equal(H(4), t.Working); Assert.Equal(TimeSpan.Zero, t.Overtime); Assert.True(engine.DayRunning); // Midnight did not reset it.
        at(Wed.AddHours(6)); t = engine.ShiftTotals(engine.CurrentKey); Assert.Equal(H(8), t.Working); Assert.Equal(TimeSpan.Zero, t.Overtime);
        at(Wed.AddHours(7)); t = engine.ShiftTotals(engine.CurrentKey); Assert.Equal(H(8), t.Working); Assert.Equal(H(1), t.Overtime); Assert.Equal(H(9), t.Elapsed);
        at(Wed.AddHours(13)); Assert.Equal(Wed, engine.CurrentKey); Assert.Equal(TimeSpan.Zero, engine.ShiftTotals(engine.CurrentKey).Elapsed); // Overtime stopped at the tail; the next shift is not started.
    }

    [Fact]
    public void FinishingAfterMidnightFinishesThatShiftAndTheNextOneStartsOnItsOwn()
    {
        var (engine, at) = Night();
        at(Wed.AddHours(6.1)); Assert.True(engine.DayRunning); engine.FinishDay();
        Assert.True(engine.Finished); Assert.False(engine.DayRunning);
        at(Wed.AddHours(7)); Assert.True(engine.Finished); Assert.Equal(H(8), engine.ShiftTotals(engine.CurrentKey).Working);
        var overtimeAtFinish = engine.ShiftTotals(Tue).Overtime; at(Wed.AddHours(9)); Assert.Equal(overtimeAtFinish, engine.ShiftTotals(Tue).Overtime); // Finished, so the clock stopped.
        at(Wed.AddHours(22.5)); Assert.Equal(Wed, engine.CurrentKey); Assert.False(engine.Finished); Assert.True(engine.DayRunning); Assert.Equal(H(0.5), engine.ShiftTotals(Wed).Elapsed);
        // Starting the day again before the tail runs out takes up the same shift where it left off.
        var (again, atAgain) = Night(); atAgain(Wed.AddHours(6.1)); again.FinishDay(); atAgain(Wed.AddHours(8)); again.StartDay(); Assert.False(again.Finished); Assert.True(again.DayRunning);
    }

    [Fact]
    public void TimeOnAccountsCountsAcrossMidnightForTheShiftAndByCalendarDayForReports()
    {
        var (engine, at) = Night();
        at(Tue.AddHours(23)); engine.ToggleAccount("Account one"); at(Wed.AddHours(1)); // Recording since 23:00.
        var shift = engine.ShiftTotals(Tue); Assert.Equal(H(2), shift.Allocated); Assert.Equal(H(3), shift.Working); Assert.Equal(H(1), shift.Unallocated);
        Assert.Equal(H(1), engine.Totals(Tue).Allocated); Assert.Equal(H(1), engine.Totals(Wed).Allocated); // Reports stay by calendar day.
        Assert.Equal(H(8), engine.Totals(Tue).Working); Assert.Equal(H(1), engine.Totals(Wed).Working); // Tuesday holds Monday night's tail (six hours) and the first two of its own; Wednesday so far the tail's first hour.
        engine.StopAccount(); at(Wed.AddHours(1.5));
        var window = engine.ShiftWindow(Tue); Assert.Equal(H(2), engine.CreditedIn("Account one", window.From, window.Until));
        Assert.Equal(H(1), engine.CreditedIn("Account one", Tue, Tue.AddDays(1))); Assert.Equal(H(1), engine.CreditedIn("Account one", Wed, Wed.AddDays(1)));
    }

    [Fact]
    public void ABreakAcrossMidnightIsLeftOutOfTheShiftsWorkingTime()
    {
        var (engine, at) = Night();
        at(Tue.AddHours(23.5)); engine.ToggleAccount(BreakAccount.Name); at(Wed.AddHours(0.5)); engine.StopAccount(); at(Wed.AddHours(2));
        var t = engine.ShiftTotals(Tue); Assert.Equal(H(3), t.Working); // Four hours in, less the hour's break.
        Assert.Equal(TimeSpan.Zero, t.Allocated);
    }

    [Fact]
    public void AShiftThatIsOverKeepsItsOwnTotalsAndTheNightBeforeDoesNotLeakIntoTheNext()
    {
        var (engine, at) = Night();
        engine.AddSession("Account one", Tue.AddHours(22), Wed.AddHours(5.5)); at(Wed.AddHours(20));
        var tue = engine.ShiftTotals(Tue); Assert.Equal(H(8), tue.Working); Assert.Equal(TimeSpan.Zero, tue.Overtime); Assert.Equal(H(7.5), tue.Allocated); Assert.Equal(H(0.5), tue.Unallocated);
        at(Wed.AddHours(22.5)); var wed = engine.ShiftTotals(Wed); Assert.Equal(H(0.5), wed.Working); Assert.Equal(TimeSpan.Zero, wed.Allocated); // Last night's hours are not tonight's.
    }

    [Fact]
    public void ADayShiftAfterANightShiftStartsItsOwnClockWhenItBegins()
    {
        var schedule = new WorkSchedule { Days = new() { DayOfWeek.Tuesday, DayOfWeek.Wednesday }, Default = new Shift(TimeSpan.FromHours(9), TimeSpan.FromHours(17)) };
        schedule.Shifts[DayOfWeek.Tuesday] = new Shift(TimeSpan.FromHours(22), TimeSpan.FromHours(6));
        var now = Wed.AddHours(10); var engine = new TrackingEngine(schedule, () => now);
        Assert.Equal(Wed, engine.CurrentKey); Assert.Equal(H(1), engine.ShiftTotals(Wed).Elapsed); // Only Wednesday's own hours, not last night's tail.
        Assert.Equal(H(7), engine.Totals(Wed).Working); // By calendar day Wednesday holds the night shift's tail (six hours) and its own first hour.
    }

    [Fact]
    public void TheModelsClockTodayAndTheDayTileFollowTheShiftAcrossMidnight()
    {
        var now = Tue.AddHours(22); var schedule = new WorkSchedule { Default = new Shift(TimeSpan.FromHours(22), TimeSpan.FromHours(6)) };
        var engine = new TrackingEngine(schedule, () => now);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one", null, null) }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.ToggleAccount("Account one"); now = Wed.AddHours(2);
        Assert.Equal("04:00:00", model.ClockText); Assert.Equal(TimeSpan.FromHours(4), model.Today(model.Accounts[0])); // Neither started again at midnight.
        Assert.False(model.IsOvertime); Assert.Equal(0.5, model.DayProgress!.Value, 3);
        model.ShowToday(); var figures = DayFigures.For(model); Assert.Equal(TimeSpan.FromHours(4), figures.Allocated); Assert.Equal(TimeSpan.FromHours(8), figures.Target);
        now = Wed.AddHours(7); Assert.True(model.IsOvertime); Assert.Equal("09:00:00", model.ClockText);
    }

    [Fact]
    public void AnOvernightShiftIsSavedAndReadBackAndOnlyEqualTimesAreRefused()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-night-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                model.SetShifts(new() { [DayOfWeek.Tuesday] = new Shift(TimeSpan.FromHours(22), TimeSpan.FromHours(6)) });
                Assert.Throws<ArgumentException>(() => model.SetShifts(new() { [DayOfWeek.Tuesday] = new Shift(TimeSpan.FromHours(9), TimeSpan.FromHours(9)) }));
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); store.Load(engine);
                var plan = engine.Schedule.Scheduled(Tue); Assert.Equal(Tue.AddHours(22), plan.Start); Assert.Equal(Wed.AddHours(6), plan.End);
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
