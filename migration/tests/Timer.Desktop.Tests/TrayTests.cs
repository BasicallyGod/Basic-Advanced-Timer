using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TrayTests
{
    private static DateTime Day => new(2026, 10, 9);

    [AvaloniaFact]
    public void ClosingHidesTheTimerAndKeepsTrackingUntilTheTrayQuits()
    {
        var now = Day.AddHours(10);
        var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var exits = 0; var windows = new WindowCoordinator(model, () => exits++) { HideToTray = true };
        Assert.False(windows.Pill.ShowInTaskbar); Assert.False(windows.Dashboard.ShowInTaskbar);
        windows.Pill.Show(); model.ToggleAccount("Account one");
        windows.Pill.Close(); Dispatcher.UIThread.RunJobs();
        Assert.False(windows.Pill.IsVisible); Assert.False(windows.IsShown); Assert.True(model.IsTracking); Assert.Equal(0, exits);
        windows.ShowTimer(); Assert.True(windows.Pill.IsVisible);
        windows.ShowDashboard(); Assert.True(windows.Dashboard.IsVisible); Assert.False(windows.Pill.IsVisible);
        windows.Dashboard.Close(); Assert.False(windows.IsShown); Assert.True(model.IsTracking);
        windows.ShowTimer(); Assert.True(windows.Dashboard.IsVisible); // It comes back as the view that was hidden.
        windows.Hide(); windows.ResetPosition(); Assert.True(windows.Pill.IsVisible); Assert.False(windows.Dashboard.IsVisible);
        windows.Quit(); Dispatcher.UIThread.RunJobs();
        Assert.False(model.IsTracking); Assert.True(exits >= 1);
    }

    [AvaloniaFact]
    public void WithoutATrayClosingStillClosesAsBefore()
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(10)); var model = new TimerModel(engine);
        var exits = 0; var windows = new WindowCoordinator(model, () => exits++);
        windows.Pill.Show(); windows.Pill.Close(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, exits);
    }

    [AvaloniaFact]
    public void TrayTextAndMenuLabelsFollowTheTimer()
    {
        var now = Day.AddHours(6);
        var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        Assert.Contains("Ready when you are", TrayController.Tooltip(model));
        Assert.Equal("Start day", TrayController.DayLabel(model));
        now = Day.AddHours(10); model.ToggleAccount("Account one"); now = now.AddMinutes(75);
        var text = TrayController.Tooltip(model);
        Assert.Contains("Tracking Account one", text); Assert.True(text.Length <= 127, text);
        Assert.Equal("Pause tracking", TrayController.PauseLabel(model)); Assert.Equal("Finish day", TrayController.DayLabel(model));
        model.TogglePause();
        Assert.Equal("Resume Account one", TrayController.PauseLabel(model)); Assert.Contains("Not tracking", TrayController.Tooltip(model));
        Assert.NotNull(TrayController.Draw(true)); Assert.NotNull(TrayController.Draw(false));
        var windows = new WindowCoordinator(model, () => { });
        using var tray = new TrayController(model, windows); tray.RefreshMenu();
    }
}
