using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;
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
    public void TheTimelineHasASpineWithHourTicksMoreOfThemAsYouZoomAndNoDotsOnIt()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "TimelineSpine");
            int Ticks() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Tag is "Tick");
            model.SetTimelineScale(80, 200, 0); Dispatcher.UIThread.RunJobs(); using var f0 = window.CaptureRenderedFrame();
            Assert.Equal(24, Ticks()); // One for each hour when zoomed out.
            model.SetTimelineScale(160, 200, 0); Dispatcher.UIThread.RunJobs(); using var f1 = window.CaptureRenderedFrame();
            Assert.Equal(48, Ticks()); // Halves appear.
            model.SetTimelineScale(400, 200, 0); Dispatcher.UIThread.RunJobs(); using var f2 = window.CaptureRenderedFrame();
            Assert.Equal(96, Ticks()); // And quarters.
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
    public void TheZoomSliderFollowsTheButtonsAndTheButtonsFollowTheSlider()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            var slider = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "TimelineZoom");
            Assert.Equal(model.TimelineZoomFraction, slider.Value, 3);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineZoomIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(model.TimelineZoomFraction, slider.Value, 3);
            slider.Value = 1; Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame();
            Assert.Equal(640, model.TimelinePixelsPerHour, 0.5); Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineZoomIn").IsEnabled);
            slider.Value = 0; Dispatcher.UIThread.RunJobs();
            Assert.Equal(60, model.TimelinePixelsPerHour, 0.5);
            Assert.Equal(60, TimerModel.ScaleAt(0), 0.001); Assert.Equal(640, TimerModel.ScaleAt(1), 0.001);
        }
        finally { window.Close(); }
    }
}
