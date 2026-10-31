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
    public void TheDashboardIsTwelveHundredAndSeventyTwoByTheSevenHundredAndTwentyWithTheSidebarFullHeightAtItsLeft()
    {
        Assert.Equal(1272, DashboardWindow.DashboardWidth); Assert.Equal(760, DashboardWindow.DashboardHeight); Assert.Equal(248, Sidebar.Width);
        Assert.Equal(1100 - 76, DashboardWindow.DashboardWidth - Sidebar.Width); // The page keeps the room it had beside the old rail.
        var window = new DashboardWindow(Model()); window.Show(); Settle(window);
        try
        {
            Assert.Equal(DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, window.Width); Assert.Equal(DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, window.Height);
            var sidebar = Find<Border>(window, "Sidebar"); var surface = Find<Border>(window, "DashboardSurface");
            Assert.Equal(Sidebar.Width, sidebar.Bounds.Width, 0.5);
            Assert.Equal(surface.Bounds.Height, sidebar.Bounds.Height, 0.5); // The sidebar runs the full height of the window.
            Assert.Equal(0, sidebar.TranslatePoint(default, surface)!.Value.X, 0.5); Assert.Equal(0, sidebar.TranslatePoint(default, surface)!.Value.Y, 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSidebarHoldsTheSameLightsAndClockAsThePillAndNamesThemForTheSidebar()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal("Not recording", ToolTip.GetTip(Find<StackPanel>(window, "SidebarLights")));
            Assert.Equal("No reminders waiting", Avalonia.Automation.AutomationProperties.GetName(Find<Border>(window, "SidebarReminderLight"))); // Always there, grey, until one is due.
            model.ToggleAccount("Account one"); Settle(window);
            Assert.Equal("Recording", ToolTip.GetTip(Find<StackPanel>(window, "SidebarLights")));
            Assert.StartsWith("Pause", ToolTip.GetTip(Find<Button>(window, "SidebarPause"))?.ToString());
            Assert.Equal("Account one", Find<TextBlock>(window, "SidebarWhoName").Text); Assert.StartsWith("Tracking · ", Find<TextBlock>(window, "SidebarWhoStatus").Text);
            Click(Find<Button>(window, "SidebarPause")); Assert.False(model.IsTracking);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePlayTileStartsTheChosenAccountAndSaysSoWhenThereIsNoneToStart()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "SidebarPause")); Assert.True(model.IsTracking); // The chosen account starts.
            Click(Find<Button>(window, "SidebarPause")); Assert.False(model.IsTracking);
        }
        finally { window.Close(); }
        var none = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new Timer.Storage.StoreSettings(new List<Timer.Storage.AccountSettings> { new Timer.Storage.AccountSettings("Account one") with { Hidden = true } }, false, "System", DayOfWeek.Monday));
        none.CompleteSetup(); var bare = new DashboardWindow(none); bare.Show(); Settle(bare);
        try { Click(Find<Button>(bare, "SidebarPause")); Assert.False(none.IsTracking); Assert.Equal("Choose an account", Find<TextBlock>(bare, "SidebarWhoName").Text); } // Nothing chosen: nothing starts, and the card says what to do.
        finally { bare.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsAreAlwaysInTheSidebarAndPressingOneStartsItAndManageAccountsOpensTheDialog()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "Account1")); Settle(window); Assert.Equal("Account two", model.ActiveAccount);
            Click(Find<Button>(window, "Account0")); Settle(window); Assert.Equal("Account one", model.ActiveAccount);
            var manage = Find<Button>(window, "SidebarManageAccounts"); var sidebar = Find<Border>(window, "Sidebar");
            Assert.Equal(IconKind.Sliders, ((IconGlyph)manage.Content!).Kind); Assert.Equal("Manage accounts", ToolTip.GetTip(manage)?.ToString()); // One icon, no words.
            var at = manage.TranslatePoint(default, sidebar)!.Value; Assert.InRange(sidebar.Bounds.Width - (at.X + manage.Bounds.Width), 6, 24); // At the right of the accounts heading.
            Assert.Equal("ACCOUNTS", Find<TextBlock>(window, "SidebarAccountsHeading").Text);
            Click(manage); Settle(window); Assert.True(window.Dialogs.Dialogs.OfType<AccountsDialog>().Any());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheViewsAreTabsAtTheLeftOfTheHeaderAndTheSettingsPageHasItsNameThereInstead()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var tabs = new[] { "TimelineTab", "NotesTab", "WeekOverviewTab" }.Select(n => Find<Button>(window, n)).ToList();
            Assert.Equal(new[] { "Timeline", "Notes", "Week overview" }, tabs.Select(t => ((StackPanel)t.Content!).Children.OfType<TextBlock>().Single().Text)); // In this order, with their names.
            Assert.True(tabs[0].TranslatePoint(default, window)!.Value.X < tabs[1].TranslatePoint(default, window)!.Value.X && tabs[1].TranslatePoint(default, window)!.Value.X < tabs[2].TranslatePoint(default, window)!.Value.X);
            Assert.Contains("selected", tabs[0].Classes); Assert.DoesNotContain("selected", tabs[1].Classes);
            Click(tabs[1]); Settle(window); Assert.Contains("selected", tabs[1].Classes); Assert.DoesNotContain("selected", tabs[0].Classes);
            Click(tabs[2]); Settle(window); Assert.Contains("selected", tabs[2].Classes);
            Click(Find<Button>(window, "Settings")); Settle(window);
            Assert.False(Find<Border>(window, "ViewTabs").IsEffectivelyVisible); Assert.Equal("Settings", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Settings" && t.IsEffectivelyVisible).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThereIsNoStartDayOrFinishDayAndCloseTimerHasTheFoot()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "DashboardDay"); // The day begins with its first block.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "FinishDayCard" or "FinishDayConfirm" or "FinishDayCancel");
            var quit = Find<Button>(window, "QuitTimer"); Assert.True(quit.Bounds.Width > 150); // On its own it takes the foot's width.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSettingsGearIsAtTheTopRightOfTheHeaderAndTheDayTotalsSitUnderTheTimeline()
    {
        var now = Day.AddHours(10); var model = Model(() => now); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var gear = Find<Button>(window, "Settings"); var rail = Find<Border>(window, "Sidebar");
            Assert.True(gear.TranslatePoint(default, window)!.Value.X > rail.Bounds.Width + 600); Assert.True(gear.TranslatePoint(default, window)!.Value.Y < Ui.ShadowMargin + 80);
            var totals = Find<StackPanel>(window, "DayTotals"); var timeline = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            Assert.True(totals.TranslatePoint(default, window)!.Value.Y >= timeline.TranslatePoint(new Point(0, timeline.Bounds.Height), window)!.Value.Y - 1);
            Assert.True(totals.IsVisible); Assert.DoesNotContain(window.GetVisualDescendants().OfType<StackPanel>(), c => c.Name == "TotalsOvertime"); // There is no overtime figure.
            window.SelectTab(true); Settle(window); Assert.False(totals.IsEffectivelyVisible); // The week has its own figures.
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
