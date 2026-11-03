using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Timer.Core;
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
        Assert.True(windows.Pill.ShowInTaskbar); Assert.True(windows.Dashboard.ShowInTaskbar); // A taskbar button by default.
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
        Assert.Contains("Ready to track", TrayController.Tooltip(model));
        now = Day.AddHours(10); model.ToggleAccount("Account one"); now = now.AddMinutes(75);
        var text = TrayController.Tooltip(model);
        Assert.Contains("Tracking Account one", text); Assert.True(text.Length <= 127, text);
        Assert.Equal("Pause tracking", TrayController.PauseLabel(model));
        model.TogglePause();
        Assert.Equal("Resume Account one", TrayController.PauseLabel(model)); Assert.Contains("Ready to track", TrayController.Tooltip(model));
        Assert.NotNull(TrayController.Draw(true)); Assert.NotNull(TrayController.Draw(false));
        foreach (var recording in new[] { false, true }) foreach (var due in new[] { false, true }) foreach (var dark in new[] { false, true }) Assert.NotNull(TrayController.Draw(recording, due, dark)); // The pill's two lights and both taskbar themes.
        var windows = new WindowCoordinator(model, () => { });
        using var tray = new TrayController(model, windows); tray.RefreshMenu();
    }

    [AvaloniaFact]
    public void TheTaskbarButtonCanBeSwitchedOffOnlyWhileTheTrayCanBringTheTimerBack()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)));
        var windows = new WindowCoordinator(model, () => { });
        model.SetTaskbarButton(false); // No tray yet, so there would be no way back.
        Assert.True(model.TaskbarButton); Assert.True(windows.Pill.ShowInTaskbar);
        model.TrayAvailable = true; windows.HideToTray = true;
        model.SetTaskbarButton(false);
        Assert.False(model.TaskbarButton); Assert.False(windows.Pill.ShowInTaskbar); Assert.False(windows.Dashboard.ShowInTaskbar); Assert.Contains("tray", model.Notice);
        model.SetTaskbarButton(true);
        Assert.True(windows.Pill.ShowInTaskbar); Assert.True(windows.Dashboard.ShowInTaskbar);
        windows.Quit();
    }

    [AvaloniaFact]
    public void EveryWindowWearsTheAppIconAndItsTitleBarFollowsTheTheme()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Dark");
        var windows = new WindowCoordinator(model, () => { });
        // Pop-ups are dialogs on the dashboard now, so the dashboard is the other window that shows a title bar.
        windows.ShowDashboard(); var dialog = windows.Dashboard;
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(windows.Pill.Icon); Assert.NotNull(windows.Dashboard.Icon); Assert.NotNull(dialog.Icon);
            var dark = WindowChrome.Colours(dialog)!.Value;
            Assert.True(dark.Dark); Assert.Equal(AppThemes.Dark.Colour("Surface").ToUpperInvariant(), dark.Caption.ToUpperInvariant());
            Assert.Equal(AppThemes.Dark.Colour("Ink").ToUpperInvariant(), dark.Text.ToUpperInvariant());
            model.SetTheme("Light"); Dispatcher.UIThread.RunJobs();
            var light = WindowChrome.Colours(dialog)!.Value;
            Assert.False(light.Dark); Assert.Equal(AppThemes.Light.Colour("Surface").ToUpperInvariant(), light.Caption.ToUpperInvariant());
        }
        finally { windows.Quit(); }
    }
}
