using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// The plain clock digits, the compact pill, the alerts card that opens from the sidebar's count (in place of the bell) and the all-red alerts.
public sealed class DisplayOptionsTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Demo(string theme = "Light") { var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); model.SetTheme(theme); return model; }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void EveryAlertIsRedWhateverItsKind()
    {
        var model = Demo(); Assert.NotEmpty(model.Alerts);
        Assert.All(model.Alerts, a => { Assert.Equal("Overtime", a.LightToken); Assert.Equal("Overtime", a.Token); });
    }

    [Fact]
    public void PlainDigitsAndTheCompactPillAreSavedAsAppearanceLinesAndComeBackOff()
    {
        var model = Demo(); Assert.False(model.PlainClock); Assert.False(model.CompactPill);
        model.SetPlainClock(true); model.SetCompactPill(true); Assert.True(model.PlainClock); Assert.True(model.CompactPill);
        model.SetPlainClock(false); model.SetCompactPill(false); Assert.False(model.PlainClock); Assert.False(model.CompactPill);
    }

    [AvaloniaFact]
    public void PlainDigitsKeepTheDisplayExactlyAsWideAndDrawTheClockAndTheCount()
    {
        var model = Demo(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var display = Find<Border>(pill, "PillDisplay"); var width = display.Bounds.Width; var clock = pill.GetVisualDescendants().OfType<SegmentClock>().Single(); var clockWidth = clock.Bounds.Width;
            var count = Find<SegmentRun>(pill, "PillAlertCount");
            Assert.False(count.Plain);
            model.SetPlainClock(true); Settle(pill);
            Assert.True(clock.Plain); Assert.True(count.Plain);
            Assert.Equal(width, display.Bounds.Width, 0.5); Assert.Equal(clockWidth, clock.Bounds.Width, 0.5);
            model.ToggleAccount("Account one"); Settle(pill); Assert.Equal(width, display.Bounds.Width, 0.5); // Ticking on changes nothing about the width.
            model.SetPlainClock(false); Settle(pill); Assert.False(clock.Plain);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheCompactPillIsOnlyItsDisplayAndTheWindowFollows()
    {
        var model = Demo(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.PillWidth, surface.Width);
            model.SetCompactPill(true); Settle(pill);
            Assert.Equal(PillWindow.CompactWidth, surface.Width); Assert.Equal(StatusFace.ShortDisplayWidth + 12, PillWindow.CompactWidth, 1.0);
            Assert.Equal((PillWindow.CompactWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.WindowWidthNow, 0.5);
            Assert.False(Find<Button>(pill, "PillPause").IsVisible); Assert.False(Find<Button>(pill, "PillBreak").IsVisible);
            var display = Find<Border>(pill, "PillDisplay"); Assert.True(display.IsVisible);
            var edge = surface.Bounds.Width - (display.TranslatePoint(default, surface)!.Value.X + display.Bounds.Width);
            Assert.Equal(PillWindow.InsetLeft, edge, 3.0); // The display sits in the card with the same room each side.
            Assert.True(pill.AlertsButton.IsVisible); // The count still opens what is waiting.
            model.SetCompactPill(false); Settle(pill);
            Assert.Equal(PillWindow.PillWidth, surface.Width); Assert.True(Find<Button>(pill, "PillPause").IsVisible);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheCompactPillsMenuCarriesWhatItsKeysDid()
    {
        var model = Demo(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var menu = pill.ContextMenu!; menu.Open(pill); Dispatcher.UIThread.RunJobs();
            var items = menu.Items.OfType<MenuItem>().ToDictionary(i => i.Name ?? "", i => i);
            Assert.False(items["CompactPlay"].IsVisible); Assert.False(items["CompactBreak"].IsVisible); Assert.False(items["CompactTrack"].IsVisible);
            menu.Close(); model.SetCompactPill(true); menu.Open(pill); Dispatcher.UIThread.RunJobs();
            Assert.True(items["CompactPlay"].IsVisible); Assert.True(items["CompactBreak"].IsVisible); Assert.True(items["CompactTrack"].IsVisible);
            Assert.Equal("Take a break", items["CompactBreak"].Header);
            Assert.Equal(model.Accounts.Count, ((IEnumerable<object>)items["CompactTrack"].ItemsSource!).Count());
            menu.Close();
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheBellIsGoneAndTheSidebarsCountOpensTheAlertsCardAndClosesItAgain()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "Notifications");
            var count = Find<Button>(window, "SidebarAlertsButton");
            Assert.False(window.AlertsOpen);
            count.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(window.AlertsOpen); Assert.NotNull(window.AlertsCard);
            count.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(window.AlertsOpen);
        }
        finally { window.Close(); }
    }

    // A monitor being unplugged left the compact pill as a sliver: Windows can report a screen with no width or scale for a moment, and a NaN scale collapsed the pill.
    [Fact]
    public void ThePillsScaleIsAlwaysAUsableNumberWhateverAScreenReports()
    {
        foreach (var (width, scale) in new[] { (double.NaN, double.NaN), (0.0, 0.0), (0.0, 1.5), (1536.0, 0.0), (double.PositiveInfinity, 1.0), (-5.0, -1.0), (1920.0, double.NaN) })
        {
            var result = PillWindow.ScaleFor(width, scale);
            Assert.True(double.IsFinite(result) && result >= PillWindow.SmallestScale && result <= PillWindow.LargestScale, $"{width}, {scale} gave {result}");
        }
        Assert.Equal(PillWindow.ScaleFor(1536, 1.5), PillWindow.ScaleFor(1536.0, 1.5)); // Real figures are unchanged.
    }

    [AvaloniaFact]
    public void RepairingAHealthyPillChangesNothing()
    {
        var model = Demo(); model.SetCompactPill(true); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var before = (pill.Width, pill.Height, pill.Scale); pill.RepairSize(); Settle(pill);
            Assert.Equal(before, (pill.Width, pill.Height, pill.Scale));
        }
        finally { pill.Close(); }
    }
}
