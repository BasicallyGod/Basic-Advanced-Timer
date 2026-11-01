using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

// The frame monitor: its figures, and that its switch and readout live where the developer features do.
public sealed class FrameMonitorTests
{
    [Fact]
    public void TheFiguresAreTheAverageTheNinetyFifthPercentileTheWorstAndTheLateOnes()
    {
        var gaps = Enumerable.Repeat(16.7, 95).Concat(new[] { 25.0, 25.0, 40.0, 40.0, 90.0 }).ToArray();
        var stats = FrameMonitor.Summarise(gaps);
        Assert.Equal(100, stats.Frames); Assert.Equal(90, stats.WorstMs); Assert.Equal(5, stats.Slow); Assert.Equal(3, stats.Dropped);
        Assert.InRange(stats.AverageMs, 17.9, 18.3); Assert.InRange(stats.P95Ms, 16.7, 25.1); Assert.True(stats.Fps < 60);
        Assert.Equal(default, FrameMonitor.Summarise(Array.Empty<double>()));
    }

    [Fact]
    public void OnlyTheLastNineHundredFramesAreKept()
    {
        var monitor = new FrameMonitor();
        for (var i = 0; i < FrameMonitor.Kept + 50; i++) monitor.Record(i < 50 ? 500 : 16);
        Assert.Equal(FrameMonitor.Kept, monitor.Snapshot().Frames); Assert.Equal(16, monitor.Snapshot().WorstMs); // The slow early ones have gone.
    }

    [AvaloniaFact]
    public void TheSwitchStartsAndStopsItAndTheChipShowsOnlyWhileItRuns()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var chip = window.GetVisualDescendants().OfType<FrameChip>().Single(); Assert.False(chip.IsVisible);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var toggle = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FrameTimingToggle");
            toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(FrameMonitor.Instance.Running); Assert.True(chip.IsVisible);
            toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(FrameMonitor.Instance.Running); Assert.False(chip.IsVisible);
        }
        finally { FrameMonitor.Instance.Stop(); window.Close(); }
    }

    [Fact]
    public void TheReportSaysHowTheFramesSpreadAndWhichSwitchesWereOn()
    {
        var monitor = new FrameMonitor();
        foreach (var gap in new[] { 16.6, 16.7, 19.0, 22.0, 30.0, 40.0 }) monitor.Record(gap);
        var plain = monitor.Report();
        Assert.Contains("6 frames: 2 up to 17.5 ms, 1 to 20, 1 to 25, 1 to 33, 1 over 33", plain); Assert.DoesNotContain("flat", plain); Assert.DoesNotContain("paused", plain);
        try
        {
            ThemeEngine.FlatShadows = true; TimerModel.TickPaused = true;
            var marked = monitor.Report(); Assert.Contains("[flat", marked); Assert.Contains("[1 s refresh paused]", marked);
        }
        finally { ThemeEngine.FlatShadows = false; TimerModel.TickPaused = false; }
    }

    [Fact]
    public void ThePagesLeftBehindKeepTheirFiguresSoOneCopyGivesThemAll()
    {
        var monitor = new FrameMonitor();
        monitor.Page = "Timeline"; for (var i = 0; i < 40; i++) monitor.Record(20);
        monitor.Remember(); monitor.Reset(); monitor.Page = "Settings"; for (var i = 0; i < 40; i++) monitor.Record(16.7);
        var report = monitor.Report().Split(Environment.NewLine);
        Assert.Equal(2, report.Length); Assert.Contains("Timeline", report[0]); Assert.Contains("40 frames: 0 up to 17.5 ms, 40 to 20", report[0]);
        Assert.Contains("Settings", report[1]); Assert.Contains("40 frames: 40 up to 17.5 ms", report[1]);
        monitor.ForgetPages(); Assert.Single(monitor.Report().Split(Environment.NewLine));
    }

    [AvaloniaFact]
    public void WhatHappenedOnThePageIsCountedPerSecondWhileTheMonitorRuns()
    {
        FrameMonitor.Bump("before"); // Nothing counts while it is off.
        var window = new Window(); window.Show();
        try
        {
            FrameMonitor.Instance.Start(window); FrameMonitor.Instance.Page = "Probe";
            FrameMonitor.Bump("model changes"); FrameMonitor.Bump("model changes");
            var report = FrameMonitor.Instance.Report();
            Assert.Contains("per second: model changes", report); Assert.DoesNotContain("before", report);
        }
        finally { FrameMonitor.Instance.Stop(); window.Close(); }
    }

    [Fact]
    public void TheTimerTickIsReadOnWindowsAndLeftAloneElsewhere()
    {
        var tick = TimerResolution.CurrentMilliseconds;
        if (OperatingSystem.IsWindows()) { Assert.NotNull(tick); Assert.InRange(tick!.Value, 0.4, 20); }
        else { Assert.Null(tick); TimerResolution.Request(true); Assert.False(TimerResolution.Requested); } // Nothing to ask for off Windows.
    }
}
