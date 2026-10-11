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
    [InlineData("Time Deck", "timedeck-light")]
    [InlineData("Time Deck Dark", "timedeck-dark")]
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
}
