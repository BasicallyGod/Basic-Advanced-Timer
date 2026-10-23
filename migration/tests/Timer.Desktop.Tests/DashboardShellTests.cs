using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DashboardShellTests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model(Func<DateTime>? clock = null) { var m = new TimerModel(new TrackingEngine(clock: clock ?? (() => Day.AddHours(10)))); m.CompleteSetup(); return m; }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); }
    private static void Click(Control c) => c.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void TheDashboardIsElevenHundredByTwentySevenHundredAndTwentyAndItsRailMatchesThePillsWidth()
    {
        Assert.Equal(1100, DashboardWindow.DashboardWidth); Assert.Equal(720, DashboardWindow.DashboardHeight); Assert.Equal(PillWindow.PillWidth, DashboardWindow.RailWidth);
        var window = new DashboardWindow(Model()); window.Show(); Settle(window);
        try
        {
            Assert.Equal(DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, window.Width); Assert.Equal(DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, window.Height);
            var rail = Find<Border>(window, "DashboardRail");
            Assert.Equal(DashboardWindow.RailWidth, rail.Bounds.Width, 0.5);
            var surface = Find<Border>(window, "DashboardSurface");
            Assert.Equal(surface.Bounds.Height, rail.Bounds.Height, 0.5); // The rail runs the full height of the window.
            Assert.Equal(0, rail.TranslatePoint(default, surface)!.Value.Y, 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRailHoldsTheSameLightsAndClockAsThePillAndNamesThemForTheRail()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal("Not recording", ToolTip.GetTip(Find<StackPanel>(window, "RailLights")));
            Assert.False(Find<Border>(window, "RailReminderLight").IsVisible);
            model.ToggleAccount("Account one"); Settle(window);
            Assert.Equal("Recording", ToolTip.GetTip(Find<StackPanel>(window, "RailLights")));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "AO");
            Assert.StartsWith("Pause", ToolTip.GetTip(Find<Button>(window, "RailPause"))?.ToString());
            Click(Find<Button>(window, "RailPause")); Assert.False(model.IsTracking);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCircleStartsTheChosenAccountAndOpensTheAccountsWhenThereIsNone()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "RailPause")); Assert.True(model.IsTracking); Assert.False(window.AccountsOpen); // The chosen account starts.
            Click(Find<Button>(window, "RailPause")); Assert.False(model.IsTracking);
        }
        finally { window.Close(); }
        var none = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new Timer.Storage.StoreSettings(new List<Timer.Storage.AccountSettings> { new Timer.Storage.AccountSettings("Account one") with { Hidden = true } }, false, "System", DayOfWeek.Monday));
        none.CompleteSetup(); var bare = new DashboardWindow(none); bare.Show(); Settle(bare);
        try { Click(Find<Button>(bare, "RailPause")); Assert.True(bare.AccountsOpen); Assert.False(none.IsTracking); } // Nothing chosen, so it asks which account.
        finally { bare.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsButtonTogglesTheListAndChoosingAnAccountPutsItAway()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.False(window.AccountsOpen);
            Click(Find<Button>(window, "RailAccounts")); Assert.True(window.AccountsOpen);
            Click(Find<Button>(window, "RailAccounts")); Assert.False(window.AccountsOpen);
            Click(Find<Button>(window, "RailAccounts")); Assert.True(window.AccountsOpen);
            Settle(window);
            Click(Find<Button>(window, "Account1")); Settle(window); // Choosing an account records to it and puts the list away.
            Assert.Equal("Account two", model.ActiveAccount); Assert.False(window.AccountsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ManageAccountsOpensTheAccountsDialogAndPutsTheListAway()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            window.ShowAccountsPanel(); Settle(window);
            Click(Find<Button>(window, "RailManageAccounts")); Settle(window);
            Assert.False(window.AccountsOpen); Assert.True(window.Dialogs.Dialogs.OfType<AccountsDialog>().Any());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FinishDayAsksFirstAndStartDayDoesNot()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.ToggleAccount("Account one"); Settle(window);
            Assert.True(model.DayStarted);
            var day = Find<Button>(window, "DashboardDay"); Assert.Equal("Finish day", ToolTip.GetTip(day));
            Click(day); Settle(window);
            Assert.True(window.FinishQuestionOpen); Assert.True(model.DayStarted); // Nothing has happened yet.
            var summary = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FinishDaySummary").Text!;
            Assert.Contains("Working time", summary); Assert.Contains("Allocated", summary); Assert.Contains("Account one stops.", summary); Assert.Contains("Start day undoes it.", summary);
            Click(Find<Button>(window, "FinishDayCancel")); Assert.False(window.FinishQuestionOpen); Assert.True(model.DayStarted);
            Click(day); Settle(window); Click(Find<Button>(window, "FinishDayConfirm")); Settle(window);
            Assert.False(window.FinishQuestionOpen); Assert.False(model.DayStarted); Assert.False(model.IsTracking);
            Assert.Equal("Start day again", ToolTip.GetTip(day));
            Click(day); Settle(window); // Starting asks nothing.
            Assert.False(window.FinishQuestionOpen); Assert.True(model.DayStarted);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSettingsGearIsAtTheTopRightOfTheHeaderAndTheDayTotalsSitUnderTheTimeline()
    {
        var now = Day.AddHours(10); var model = Model(() => now); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var gear = Find<Button>(window, "Settings"); var rail = Find<Border>(window, "DashboardRail");
            Assert.True(gear.TranslatePoint(default, window)!.Value.X > rail.Bounds.Width + 600); Assert.True(gear.TranslatePoint(default, window)!.Value.Y < Ui.ShadowMargin + 80);
            var totals = Find<StackPanel>(window, "DayTotals"); var timeline = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            Assert.True(totals.TranslatePoint(default, window)!.Value.Y >= timeline.TranslatePoint(new Point(0, timeline.Bounds.Height), window)!.Value.Y - 1);
            Assert.True(totals.IsVisible); Assert.False(Find<StackPanel>(window, "TotalsOvertime").IsVisible); // Overtime shows only when there is some.
            window.SelectTab(true); Settle(window); Assert.False(totals.IsVisible); // The week has its own figures.
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheRailAndThePillLineUpTheirCardsWhateverTheScale()
    {
        // The pill is drawn at 70 to 85 per cent, so its room round the card is smaller than the dashboard's, and the windows sit apart by the difference.
        Assert.Equal(0, WindowCoordinator.CardShift(1, 1, 1.5));
        Assert.Equal(5, WindowCoordinator.CardShift(1, .85, 1.0)); // The dashboard opens 4.5 pixels right of and below the pill's window.
        Assert.Equal(-5, WindowCoordinator.CardShift(.85, 1, 1.0)); // And the pill comes back by the same amount.
        Assert.Equal(-(int)Math.Round(Ui.ShadowMargin * .15 * 2), WindowCoordinator.CardShift(.85, 1, 2.0));
    }
}
