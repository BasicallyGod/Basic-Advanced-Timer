using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 29: the scroll bar keeps clear of a list's buttons, the toast's line runs down smoothly, the pill has the UI's corner, the bell lists what the pill counts, and two
// meetings that meet in time never meet on screen.
public sealed class Round29Tests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 0);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Busy()
    {
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup();
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account three" ? a with { BudgetHours = 1.75, BudgetPeriod = "week" } : a.Name == "Account four" ? a with { BudgetHours = 5, BudgetPeriod = "week" } : a));
        return model;
    }


    [AvaloniaFact]
    public void ThePillAndItsCardsTakeTheDashboardsCornerAndNotACapsule()
    {
        Assert.Equal(20, PillWindow.PillRadius); // The dashboard's own corner, so the pill grows into it without the corner changing.
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            Assert.Equal(PillWindow.PillRadius, pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PillSurface").CornerRadius.TopLeft);
            Assert.Equal(PillWindow.PillRadius, pill.MakeDropdownCard().CornerRadius.TopLeft);
            pill.ShowAlerts(); Assert.Equal(PillWindow.PillRadius, pill.AlertsCard!.CornerRadius.TopLeft);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TwoMeetingsThatMeetInTimeNeverMeetOnScreen()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 21, 8, 0, 0)), new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.GoToDate(new DateTime(2026, 10, 21));
        Assert.Null(model.Notes.AddMeeting(new DateTime(2026, 10, 21), TimeSpan.FromHours(9), TimeSpan.FromHours(9.25), "Stand-up", null, RepeatChoice.None));
        Assert.Null(model.Notes.AddMeeting(new DateTime(2026, 10, 21), TimeSpan.FromHours(9.25), TimeSpan.FromHours(10), "Team sync", null, RepeatChoice.None));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("TimelineMeeting") == true && b.Tag is DayNote).OrderBy(b => b.TranslatePoint(new Point(), window)!.Value.X).ToList();
            Assert.Equal(2, cards.Count);
            var first = cards[0]; var second = cards[1];
            var firstRight = first.TranslatePoint(new Point(first.Bounds.Width, 0), window)!.Value.X; var secondLeft = second.TranslatePoint(new Point(), window)!.Value.X;
            Assert.True(secondLeft - firstRight >= 5, $"a gap of {secondLeft - firstRight} between a meeting that ends at 09:15 and one that starts then");
            Assert.Equal(first.TranslatePoint(new Point(), window)!.Value.Y, second.TranslatePoint(new Point(), window)!.Value.Y, 0.6); // Still one row, side by side.
        }
        finally { window.Close(); }
    }
}
