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

    private static string? BadgeText(Window dashboard) => dashboard.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NotificationsBadge").GetVisualDescendants().OfType<TextBlock>().First().Text;

    [AvaloniaFact]
    public void TheBellsListLeavesRoomForItsScrollBarOnlyWhileItScrolls()
    {
        var model = Busy(); var flyout = new NotificationsFlyout(model) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 460, Height = 1100, Content = flyout, SystemDecorations = SystemDecorations.None }; window.Show(); Settle(window);
        try
        {
            var scroll = flyout.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NotificationsScroll"); var column = (Control)scroll.Content!;
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + .5, $"extent {scroll.Extent.Height} viewport {scroll.Viewport.Height}"); Assert.Equal(0, column.Margin.Right); // It fits, so the rows use the whole width.
            flyout.LimitHeight(220); Settle(window);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the list now scrolls"); Assert.True(column.Margin.Right >= 12, "room for the 10 pixel track and a gap");
            foreach (var button in column.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible))
            {
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), scroll)!.Value.X;
                Assert.True(right <= scroll.Bounds.Width - 12, $"{button.Name} ends at {right} of {scroll.Bounds.Width}, clear of the track");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBellListsWhatThePillCounts_SoTheTwoNumbersAgree()
    {
        var model = Busy(); var dashboard = new DashboardWindow(model); var pill = new PillWindow(model); dashboard.Show(); pill.Show(); Settle(dashboard); Settle(pill);
        try
        {
            var alerts = model.Alerts; Assert.Contains(alerts, a => a.Kind is AlertKind.Budget or AlertKind.BudgetSpent); Assert.Contains(alerts, a => a.Kind == AlertKind.Reminder);
            var badge = BadgeText(dashboard);
            Assert.Equal(alerts.Count.ToString(), badge);
            var flyout = new NotificationsFlyout(model); var host = new Window { Width = 460, Height = 900, Content = flyout }; host.Show(); Settle(host);
            try
            {
                var rows = flyout.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("NotificationAlert_") == true || b.Name?.StartsWith("Notification_") == true && b.Classes.Contains("lateRow"));
                Assert.Equal(alerts.Count, rows);
                var budget = alerts.First(a => a.Kind is AlertKind.Budget or AlertKind.BudgetSpent);
                flyout.GetVisualDescendants().OfType<Button>().First(b => b.Name == "NotificationOk_" + budget.Key).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(host);
                Assert.Equal(alerts.Count - 1, model.Alerts.Count);
                Assert.Equal((alerts.Count - 1).ToString(), BadgeText(dashboard));
            }
            finally { host.Close(); }
        }
        finally { dashboard.Close(); pill.Close(); }
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
