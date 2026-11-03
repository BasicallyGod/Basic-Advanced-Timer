using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TimelineSpineTests
{
    private static DashboardWindow Open(TimerModel model)
    {
        var window = new DashboardWindow(model); window.Show();
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TheTimelineHasAnAxisWithHourTicksMoreOfThemAsYouZoomAndNoDotsOnIt()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "TimelineAxis");
            int Ticks() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Tag is "Tick");
            model.SetTimelineScale(80, 200, 0); Dispatcher.UIThread.RunJobs(); using var f0 = window.CaptureRenderedFrame();
            Assert.Equal(48, Ticks()); // An hour and a half hour each, even when zoomed out, so the half hours can always be read.
            model.SetTimelineScale(160, 200, 0); Dispatcher.UIThread.RunJobs(); using var f1 = window.CaptureRenderedFrame();
            Assert.Equal(48, Ticks());
            model.SetTimelineScale(400, 200, 0); Dispatcher.UIThread.RunJobs(); using var f2 = window.CaptureRenderedFrame();
            Assert.Equal(96, Ticks()); // And quarters when zoomed in.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Ellipse>(), e => e.Tag is "SpineDot"); // The block's own edge strip already carries the account colour.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TodaysTimelineShowsANowMarkerAndAnotherDayDoesNot()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NowChip");
            var label = (TextBlock)chip.Child!; Assert.Equal(model.Engine.Now.ToString("HH:mm"), label.Text);
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "NowMarker");
            model.ChangeDate(-1); Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "NowMarker");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheZoomButtonsStepByAFifthAndStopAtTheEnds()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Button Zoom(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var start = model.TimelinePixelsPerHour;
            Zoom("TimelineZoomIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame();
            Assert.Equal(start * 1.2, model.TimelinePixelsPerHour, 3);
            Zoom("TimelineZoomOut").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(start, model.TimelinePixelsPerHour, 3);
            for (var i = 0; i < 30; i++) model.ZoomTimeline(1, 0, 0);
            Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame();
            Assert.Equal(640, model.TimelinePixelsPerHour, 0.5); Assert.False(Zoom("TimelineZoomIn").IsEnabled);
            for (var i = 0; i < 40; i++) model.ZoomTimeline(-1, 0, 0);
            Dispatcher.UIThread.RunJobs(); Assert.Equal(60, model.TimelinePixelsPerHour, 0.5); Assert.False(Zoom("TimelineZoomOut").IsEnabled);
        }
        finally { window.Close(); }
    }
}
