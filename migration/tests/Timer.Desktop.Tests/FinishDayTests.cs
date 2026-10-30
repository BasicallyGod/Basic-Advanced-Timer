using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Time logged after Finish day still counts as working time: the flag stays where it was pressed and later blocks are ordinary time worked.
public sealed class FinishDayTests
{
    private static readonly DateTime Day = new(2026, 10, 9); // A Friday.

    private static (TimerModel Model, TrackingEngine Engine, Func<DateTime> At, Action<DateTime> Set) Setup()
    {
        var now = Day.AddHours(15);
        var engine = new TrackingEngine(clock: () => now);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two"), new("Account three") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(9), TimeSpan.FromHours(17.5)) });
        return (model, engine, () => now, t => now = t);
    }

    [Fact]
    public void BlocksLoggedAfterFinishDayCountAsWorkingTimeAndNothingIsLeftUnallocated()
    {
        var (model, engine, _, set) = Setup();
        engine.AddSession("Account one", Day.AddHours(9), Day.AddHours(10.5)); engine.AddSession("Account two", Day.AddHours(10.5), Day.AddHours(12));
        engine.AddSession(BreakAccount.Name, Day.AddHours(12), Day.AddHours(12.5)); engine.AddSession("Account one", Day.AddHours(12.5), Day.AddHours(15));
        engine.FinishDay(); Assert.True(engine.Finished);
        set(Day.AddHours(16.75)); engine.AddSession("Account three", Day.AddHours(15 + 1.0 / 3), Day.AddHours(16.5));
        Assert.Equal(TimeSpan.FromMinutes(5 * 60 + 30), engine.Totals(Day).Working + engine.Totals(Day).Overtime); // The engine alone stops at the flag...
        Assert.Equal(new TimeSpan(6, 40, 0), model.WorkingOn(Day)); // ...and the app adds what was logged after it.
        Assert.Equal(new TimeSpan(6, 40, 0), engine.Totals(Day).Allocated); Assert.Equal(TimeSpan.Zero, model.UnallocatedOn(Day));
    }

    [Fact]
    public void TheGapBeforeTheLateBlockIsNotWorkingTimeAndEarlierUnallocatedTimeStays()
    {
        var (model, engine, _, set) = Setup();
        engine.AddSession("Account one", Day.AddHours(9), Day.AddHours(11)); // Two hours on an account in a clock span of six.
        engine.FinishDay(); set(Day.AddHours(16.5));
        engine.AddSession("Account two", Day.AddHours(15 + 1.0 / 3), Day.AddHours(16 + 1.0 / 3)); // An hour, twenty minutes after the flag.
        Assert.Equal(TimeSpan.FromHours(7), model.WorkingOn(Day)); // Six on the clock to the flag, plus the hour after it.
        Assert.Equal(TimeSpan.FromHours(4), model.UnallocatedOn(Day)); // The six hours less the two on an account; the hour after counts for itself.
    }

    [Fact]
    public void ADayWithoutTimeAfterTheFlagIsUnchanged()
    {
        var (model, engine, _, _) = Setup();
        engine.AddSession("Account one", Day.AddHours(9), Day.AddHours(12)); engine.AddSession("Account two", Day.AddHours(12.5), Day.AddHours(15)); engine.FinishDay();
        Assert.Equal(engine.Totals(Day).Working + engine.Totals(Day).Overtime, model.WorkingOn(Day));
        Assert.Equal(engine.Totals(Day).Unallocated, model.UnallocatedOn(Day));
    }

    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheTimelineMarksWhereTheDayWasFinishedAndNothingElse()
    {
        var (model, engine, _, set) = Setup();
        engine.AddSession("Account one", Day.AddHours(9), Day.AddHours(15));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "FinishMarker"); // Nothing before the day is finished.
            engine.FinishDay(); set(Day.AddHours(16.75)); engine.AddSession("Account two", Day.AddHours(15 + 1.0 / 3), Day.AddHours(16.5)); model.ShowToday(); model.SetTheme("Light"); Settle(window);
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "FinishMarker");
            var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "FinishChip");
            Assert.Contains("Finished 15:00", chip.GetVisualDescendants().OfType<TextBlock>().Single().Text);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name?.Contains("AfterFinish") == true); // No bracket or label for the late block.
        }
        finally { window.Close(); }
    }
}
