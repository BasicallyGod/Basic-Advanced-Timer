using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

// Renders the dashboard in both Time Deck themes, for looking at when TIMER_PREVIEW_DIR is set.
public sealed class TimeDeckPreviewTests
{
    [AvaloniaTheory]
    [InlineData("Light", "timedeck-light")]
    [InlineData("Dark", "timedeck-dark")]
    public void TheDashboardDrawsInEachTimeDeckTheme(string theme, string file)
    {
        var model = TimerModel.CreateDemo(); model.SetTheme(theme);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Assert.Equal(theme, model.CurrentTheme.Name);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var shot = window.CaptureRenderedFrame(); shot!.Save(Path.Combine(folder, file + ".png")); }
        }
        finally { window.Close(); model.SetTheme("System"); }
    }

    [AvaloniaTheory]
    [InlineData("Light", "pill-light")]
    [InlineData("Dark", "pill-dark")]
    public void ThePillDrawsInEachState(string theme, string file)
    {
        var model = TimerModel.CreateDemo(); model.SetTheme(theme);
        var pill = new PillWindow(model); pill.Show();
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
        // Animations do not tick in the headless renderer, so the eased colour change would never arrive. Switch the easing off to see each state as it settles.
        Dispatcher.UIThread.RunJobs();
        foreach (var border in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(pill).OfType<Avalonia.Controls.Border>()) if (border.Name == "PillSurface") border.Transitions = null;
        void Shot(string state)
        {
            Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            if (folder is not null) { Directory.CreateDirectory(folder); using var shot = pill.CaptureRenderedFrame(); shot!.Save(Path.Combine(folder, $"{file}-{state}.png")); }
        }
        try
        {
            Shot("idle");
            model.ToggleAccount("Account three"); Shot("recording");
            model.SetOvertimeTest(true); Shot("overtime"); model.SetOvertimeTest(false);
            model.ToggleBreak(); Shot("break"); model.ToggleBreak();
            pill.ToggleExpansion(); Shot("expanded");
        }
        finally { pill.Close(); model.SetTheme("System"); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheSettingsPageOvertimeAndWeekOverviewDraw(string theme)
    {
        var model = TimerModel.CreateDemo(); model.SetTheme(theme);
        var window = new DashboardWindow(model); window.Show();
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
        void Shot(string name)
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            if (folder is not null) { Directory.CreateDirectory(folder); using var shot = window.CaptureRenderedFrame(); shot!.Save(Path.Combine(folder, $"{name}-{theme}.png")); }
        }
        try
        {
            model.SetOvertimeTest(true); model.ToggleAccount("Account one"); Shot("dash-overtime"); model.SetOvertimeTest(false);
            model.ToggleBreak(); Shot("dash-break"); model.ToggleBreak();
            window.SelectTab(true); Shot("week");
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>().Single(b => b.Name == "Settings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Shot("settings");
        }
        finally { window.Close(); model.SetTheme("System"); }
    }
}
