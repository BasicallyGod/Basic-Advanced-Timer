using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// There is no Start day or Finish day: the day begins with its first block, recorded or added, and its clock counts up from there.
public sealed class DayClockTests
{
    private static readonly DateTime Friday = new(2026, 10, 9);

    private static (TimerModel Model, TrackingEngine Engine, Action<DateTime> Set) Setup(DateTime now)
    {
        var current = now;
        var engine = new TrackingEngine(clock: () => current);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(9), TimeSpan.FromHours(17.5)) });
        return (model, engine, t => current = t);
    }

    [Fact]
    public void ADayWithNothingOnItHasNoClock()
    {
        var (model, _, _) = Setup(Friday.AddHours(10));
        Assert.Equal(TimeSpan.Zero, model.WorkingOn(Friday)); Assert.Equal(TimeSpan.Zero, model.Elapsed); Assert.Null(model.DaySpan(Friday));
    }

    [Fact]
    public void TheClockStartsAtTheFirstBlockNotTheScheduledStartAndTicksUpThroughQuietTime()
    {
        var (model, engine, set) = Setup(Friday.AddHours(10));
        engine.AddSession("Account one", Friday.AddHours(9.5), Friday.AddHours(10)); // Half past nine, not the scheduled nine.
        Assert.Equal(TimeSpan.FromMinutes(30), model.WorkingOn(Friday)); Assert.Equal(TimeSpan.FromMinutes(30), model.Elapsed);
        set(Friday.AddHours(11)); // An hour with nothing recorded: the clock keeps going and the hour is Unallocated.
        Assert.Equal(TimeSpan.FromMinutes(90), model.Elapsed); Assert.Equal(TimeSpan.FromMinutes(30), engine.Totals(Friday).Allocated); Assert.Equal(TimeSpan.FromMinutes(60), model.UnallocatedOn(Friday));
    }

    [Fact]
    public void ARunningBlockStartsTheClockAndBreaksAreLeftOut()
    {
        var (model, engine, set) = Setup(Friday.AddHours(10));
        model.ToggleAccount("Account one"); set(Friday.AddHours(10.5));
        Assert.Equal(TimeSpan.FromMinutes(30), model.Elapsed); // The clock began with the block that is being recorded.
        model.ToggleBreak(); set(Friday.AddHours(10.75)); Assert.Equal(TimeSpan.FromMinutes(30), model.Elapsed); // A break running now is not counted.
        model.ToggleBreak(); set(Friday.AddHours(11)); Assert.Equal(TimeSpan.FromMinutes(45), model.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(15), model.BreakTimeOn(Friday));
    }

    [Fact]
    public void ABlockAddedLaterInTheDayExtendsTheClockAndTheGapBeforeItIsUnallocated()
    {
        var (model, engine, set) = Setup(Friday.AddHours(16.5)); // Half past four: the late block has just ended.
        engine.AddSession("Account one", Friday.AddHours(9), Friday.AddHours(10.5)); engine.AddSession(BreakAccount.Name, Friday.AddHours(12), Friday.AddHours(12.5));
        engine.AddSession("Account one", Friday.AddHours(12.5), Friday.AddHours(15)); engine.AddSession("Account two", Friday.AddHours(15 + 1.0 / 3), Friday.AddHours(16.5));
        Assert.Equal(new TimeSpan(7, 0, 0), model.WorkingOn(Friday)); // Nine to half past four, less the half hour break.
        Assert.Equal(new TimeSpan(6, 40, 0), engine.Totals(Friday).Allocated); Assert.Equal(new TimeSpan(0, 20, 0), model.UnallocatedOn(Friday));
    }

    [Fact]
    public void AfterTheWorkingDayTheClockStopsAtTheLaterOfItsEndAndTheLastBlock()
    {
        var (model, engine, set) = Setup(Friday.AddHours(23));
        engine.AddSession("Account one", Friday.AddHours(9.5), Friday.AddHours(12));
        Assert.Equal(new TimeSpan(8, 0, 0), model.WorkingOn(Friday)); // To the scheduled 17:30, not on through the evening.
        engine.AddSession("Account one", Friday.AddHours(17.5), Friday.AddHours(18.25)); // Later work than the schedule.
        Assert.Equal(new TimeSpan(8, 45, 0), model.WorkingOn(Friday));
    }

    [Fact]
    public void APastDayIsMeasuredTheSameWayAndADayOffByItsBlocksAlone()
    {
        var (model, engine, set) = Setup(Friday.AddDays(3).AddHours(9)); // The Monday after.
        engine.AddSession("Account one", Friday.AddHours(9), Friday.AddHours(15));
        Assert.Equal(new TimeSpan(8, 30, 0), model.WorkingOn(Friday)); // Nine to the scheduled half past five, as it was on the day.
        var saturday = Friday.AddDays(1); engine.AddSession("Account one", saturday.AddHours(10), saturday.AddHours(11)); engine.AddSession("Account two", saturday.AddHours(12), saturday.AddHours(13));
        Assert.Equal(TimeSpan.FromHours(3), model.WorkingOn(saturday)); // No schedule: from the first block to the last.
        Assert.Equal(TimeSpan.FromHours(1), model.UnallocatedOn(saturday));
    }

    [Fact]
    public void AnOlderFinishedDayStillEndsWhereItWasFinished()
    {
        var (model, engine, set) = Setup(Friday.AddHours(20));
        engine.AddSession("Account one", Friday.AddHours(9), Friday.AddHours(15)); engine.Away.Add(new AwayInterval(Friday.AddHours(15), null)); // What the old Finish day left behind.
        Assert.Equal(TimeSpan.FromHours(6), model.WorkingOn(Friday));
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheTimelineHasNoFinishFlagAndTheDayHasNoStartOrFinishButton()
    {
        var (model, engine, set) = Setup(Friday.AddHours(16));
        engine.AddSession("Account one", Friday.AddHours(9), Friday.AddHours(15)); engine.Away.Add(new AwayInterval(Friday.AddHours(15), null));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "TimelineFinishFlag" or "FinishMarker" or "DashboardDay");
        }
        finally { window.Close(); }
    }
}
