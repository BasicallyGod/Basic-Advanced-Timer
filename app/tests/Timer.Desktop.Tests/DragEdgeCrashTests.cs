using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Dragging a selected block's edge crashed the app (Windows report, 12 November): the magnet offered every block in the history as a place to snap to, and for a block
// days away it asked the ledger for a block longer than a day, which throws.
public sealed class DragEdgeCrashTests
{
    private static readonly DateTime Day = new(2026, 11, 10);
    private static DateTime T(double hours) => Day.AddHours(hours);

    [Fact]
    public void DraggingAnEdgeNeverAsksForABlockLongerThanADay()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => T(15)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup();
        // Blocks on other days, a week before and after, and the day on show packed tight, as in the report.
        model.Engine.AddSession("Account two", T(-7 * 24 + 9), T(-7 * 24 + 10)); model.Engine.AddSession("Account two", T(3 * 24 + 9), T(3 * 24 + 10));
        model.Engine.AddSession("Account two", T(8.5), T(9.25)); model.Engine.AddSession("Account one", T(9.25), T(10)); model.Engine.AddSession("Account two", T(10), T(10.75));
        model.GoToDate(Day);
        var row = model.Engine.Sessions.First(s => s.From == T(9.25));
        foreach (var edge in new[] { -1, 0, 1 })
            for (var minutes = -180; minutes <= 180; minutes += 5)
            {
                var delta = TimelineGestures.Magnet(model, row, edge, TimeSpan.FromMinutes(minutes), 90, free: false, out _);
                var from = row.From + (edge <= 0 ? delta : TimeSpan.Zero); var until = row.To + (edge >= 0 ? delta : TimeSpan.Zero);
                Assert.True(until - from <= LedgerEditor.MaxBlock);
            }
    }
}
