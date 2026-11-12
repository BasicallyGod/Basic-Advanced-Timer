using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

// The display look for the timeline (the board "The timeline as a display", option A): a setting, lit blocks with segment words, a note dot, the recording ring
// inside its block, segment hours and one honeycomb over the trough.
public sealed class VfdTimelineTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Demo(string theme = "Dark") { var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); model.SetTheme(theme); return model; }

    [Fact]
    public void TheDisplayLookIsASettingThatIsOffUntilChosen()
    {
        var model = Demo(); Assert.False(model.DisplayTimeline);
        model.SetDisplayTimeline(true); Assert.True(model.DisplayTimeline);
        model.SetDisplayTimeline(false); Assert.False(model.DisplayTimeline);
    }

    [AvaloniaFact]
    public void OnTheDisplayTheTimelineIsDrawnInSegmentsUnderOneMesh()
    {
        var model = Demo(); model.SetDisplayTimeline(true); if (!model.IsTracking) model.ToggleAccount(model.Accounts[0].Name);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "TimelineCanvas");
            Assert.Single(canvas.Children.OfType<HoneycombOverlay>());
            Assert.IsType<SegmentText>(canvas.Children.OfType<Control>().First(c => c.Name == "TimelineHour_9"));
            Assert.Contains(canvas.GetVisualDescendants().OfType<SegmentText>(), s => s.Name == "BlockWords");
            // The block being recorded wears its ring inside its own edge.
            var recording = canvas.Children.OfType<Border>().Single(b => b.Name == "TimelineRecording");
            Assert.True(recording.BoxShadow[0].IsInset);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "vfd-timeline-dark.png")); }
        }
        finally { window.Close(); }
    }
}
