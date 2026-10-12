using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Dragging a block's end up to the block being recorded, which starts part way through a second.
public sealed class DragToRunningTests
{
    private static readonly DateTime Day = new(2026, 10, 12);

    private static (TimerModel Model, SessionRow Row, DateTime RunningStart) Setup(int startMilliseconds)
    {
        var now = Day.AddHours(11).AddMinutes(21).AddMilliseconds(startMilliseconds);
        var engine = new TrackingEngine(clock: () => now);
        engine.Sessions.Add(new("2026-10-12", "Account one", "10:58:00", "recorded", "14.00"));
        // The block being recorded began at 11:19 and a bit, the way a real one does.
        var started = Day.AddHours(11).AddMinutes(19).AddMilliseconds(startMilliseconds);
        now = started; engine.ToggleAccount("Account two"); now = Day.AddHours(11).AddMinutes(21);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        return (model, engine.Sessions[0], engine.Active!.Start);
    }

    [Theory]
    [InlineData(0)] [InlineData(100)] [InlineData(350)] [InlineData(600)] [InlineData(950)]
    public void TheBottomEdgeSnapsToJustBeforeTheRunningBlockAndTheDragIsAccepted(int milliseconds)
    {
        var (model, row, runningStart) = Setup(milliseconds);
        foreach (var requestedMinutes in new[] { 6.0, 7.0, 7.1, 20.0, 60.0 }) // Right at it, and well past it.
        {
            var delta = TimelineGestures.SnapDelta(model, row, 1, TimeSpan.FromMinutes(requestedMinutes), 120, out _);
            var end = row.To + delta;
            Assert.True(end <= runningStart, $"Snapped to {end:HH:mm:ss.fff}, past the running block's start {runningStart:HH:mm:ss.fff}.");
            Assert.True(runningStart - end < TimeSpan.FromSeconds(2.5), $"Stopped {runningStart - end} short of the running block.");
            Assert.True(model.DragEntry(row, row.From, end, false), model.EditError);
            model.UndoEdit(); row = model.Engine.Sessions[0]; // Undo puts back a new row object, so take that one for the next try.
        }
    }

    [Fact]
    public void MovingTheWholeBlockUpToTheRunningBlockLandsTouchingIt()
    {
        var (model, row, runningStart) = Setup(350);
        var delta = TimelineGestures.SnapDelta(model, row, 0, TimeSpan.FromMinutes(7), 120, out _);
        Assert.True(row.To + delta <= runningStart);
        Assert.True(model.DragEntry(row, row.From + delta, row.To + delta, false), model.EditError);
    }
}
