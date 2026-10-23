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
            var manage = Find<Button>(window, "RailManageAccounts"); var drawer = Find<Border>(window, "AccountsDrawer");
            Assert.Equal(IconKind.Sliders, ((IconGlyph)manage.Content!).Kind); Assert.Equal("Manage accounts", ToolTip.GetTip(manage)?.ToString()); // One icon, no words.
            var at = manage.TranslatePoint(default, drawer)!.Value;
            Assert.True(at.Y < 40, $"icon top {at.Y}"); Assert.InRange(drawer.Bounds.Width - (at.X + manage.Bounds.Width), 10, 18); // Top right of the drawer.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Manage accounts");
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
            Assert.Matches(@"^Working time \d\d:\d\d\.$", summary); // Just the working time: the card is small.
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

    [AvaloniaFact]
    public void TheRailsTopIsDrawnAtThePillsSizeAndPlaceSoNothingJumps()
    {
        var model = Model(); model.SetPillScale(90);
        var pill = new PillWindow(model); pill.Show(); var dash = new DashboardWindow(model); dash.Show();
        dash.RailScaleSource = () => pill.Scale; dash.SetRailScale(pill.Scale); Settle(pill); Settle(dash);
        try
        {
            Assert.Equal(.9, pill.Scale, 3); Assert.Equal(DashboardWindow.RailWidth * .9, dash.RailWidthNow, 0.01);
            Assert.Equal(dash.RailWidthNow, Find<Border>(dash, "DashboardRail").Bounds.Width, 0.5); // The rail is as wide as the pill is drawn.
            var surface = Find<Border>(pill, "PillSurface"); var rail = Find<Border>(dash, "DashboardRail");
            foreach (var name in new[] { "Dashboard", "Pause", "Accounts" })
            {
                var inPill = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Pill" + name); var inRail = dash.GetVisualDescendants().OfType<Button>().Single(b => b.Name == (name == "Dashboard" ? "RailDashboard" : "Rail" + name));
                var fromPill = inPill.TranslatePoint(default, surface)!.Value.Y * pill.Scale; // The pill's own units, drawn at its scale.
                var fromRail = inRail.TranslatePoint(default, rail)!.Value.Y;
                Assert.True(Math.Abs(fromPill - fromRail) <= 1.0, $"{name}: pill {fromPill} rail {fromRail}");
                Assert.Equal(inPill.Bounds.Height * pill.Scale, inRail.Bounds.Height * dash.RailWidthNow / DashboardWindow.RailWidth, 1.0); // And at the same size.
            }
        }
        finally { dash.Close(); pill.Close(); model.SetPillScale(null); }
    }

    [AvaloniaFact]
    public void TheRailsAccountsUnfoldFromItsEdgeFullHeightAndEscapeOrTheDimmedPageFoldsThemAway()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var layer = Find<Grid>(window, "AccountsLayer"); var drawer = Find<Border>(window, "AccountsDrawer"); var rail = Find<Border>(window, "DashboardRail"); var surface = Find<Border>(window, "DashboardSurface");
            Assert.False(layer.IsVisible);
            Click(Find<Button>(window, "RailAccounts")); Settle(window);
            Assert.True(layer.IsVisible); Assert.True(window.AccountsOpen);
            Assert.Equal(surface.Bounds.Height, drawer.Bounds.Height, 0.5); // Full height.
            Assert.Equal(rail.TranslatePoint(new Point(rail.Bounds.Width, 0), window)!.Value.X, drawer.TranslatePoint(default, window)!.Value.X, 0.5); // It starts at the rail's edge.
            Assert.Equal(DashboardWindow.RailWidth, rail.Bounds.Width, 0.5); // The rail itself does not move.
            Assert.Equal("Close the accounts", ToolTip.GetTip(Find<Button>(window, "RailAccounts")));
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Assert.False(window.AccountsOpen);
            Click(Find<Button>(window, "RailAccounts")); Settle(window); Assert.True(window.AccountsOpen);
            var scrim = Find<Border>(window, "AccountsScrim"); var at = scrim.TranslatePoint(new Point(scrim.Bounds.Width - 30, scrim.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Assert.False(window.AccountsOpen); // A click on the dimmed page.
            Assert.Equal("Accounts", ToolTip.GetTip(Find<Button>(window, "RailAccounts")));
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
