using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AnimationSchedulingTests
{
    [AvaloniaFact]
    public void SnapshotMorphCapturesBothDirectionsAndRestoresLiveContentOnHideAndReducedMotion()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show();
        var enabled = WindowMorph.Enabled; var reduced = Motion.Override;
        try
        {
            WindowMorph.Enabled = true; Motion.Override = false;
            var image = window.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Single(c => c.Name == "MorphDashboardSnapshot");
            var card = new Rect(12, 12, 406, 72);
            window.ShrinkTo(card, 20, null, default, () => { });
            var body = (Avalonia.Controls.Control)((Avalonia.Controls.Grid)window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "DashboardSurface").Child!).Children[0];
            Assert.Equal(body.Bounds, image.Bounds); // Before any settling/render passes.
            window.SetMorph(.5);
            Assert.NotNull(image.Source); Assert.True(image.IsVisible); Assert.Equal(0, window.ContentOpacity);
            window.Hide(); Assert.Null(image.Source); Assert.Equal(1, window.ContentOpacity);
            window.GrowFrom(card, 20, null, default);
            Assert.Equal(body.Bounds, image.Bounds);
            window.SetMorph(.5);
            Assert.NotNull(image.Source); Assert.True(image.IsVisible);
            window.Hide(); Assert.Null(image.Source);
            Motion.Override = true;
            window.GrowFrom(card, 20, null, default);
            Assert.Null(image.Source); Assert.False(window.Morphing); Assert.Equal(1, window.ContentOpacity);
        }
        finally { WindowMorph.Enabled = enabled; Motion.Override = reduced; window.Close(); }
    }

    [Fact]
    public void DiagnosticsDistinguishNativeCallbacksFromWatchdogAndKeepActualElapsedTime()
    {
        var frames = new Queue<Action<TimeSpan>>(); double now = 0;
        var pump = new AnimationFramePump(frames.Enqueue, () => now);
        var samples = new List<(double, bool)>();
        pump.Start(280, _ => { }, () => { }, (elapsed, fallback) => samples.Add((elapsed, fallback)));
        now = 16; frames.Dequeue()(default);
        now = 100; pump.Watchdog();
        now = 310; frames.Dequeue()(default);
        Assert.Equal(new[] { (16d, false), (100d, true), (310d, false) }, samples);
        Assert.Empty(frames);
    }

    [AvaloniaFact]
    public void TransitionReportIncludesOperationOverlapAndSeparatesCallbackSources()
    {
        var trace = TransitionTimings.Begin("diagnostic-test");
        trace.Sample(10, false); trace.Sample(70, true); trace.Sample(90, false);
        PerformanceTimings.Record("workspace rebuild", 25);
        PerformanceTimings.Record("save", 4);
        trace.Finish("completed"); trace.Finish("cancelled");
        var line = TransitionTimings.Report().Split(Environment.NewLine).Last();
        Assert.Contains("diagnostic-test completed", line);
        Assert.Contains("native 2, watchdog 1", line);
        Assert.Contains("workspace rebuild 1 / 25.0 ms", line);
        Assert.Contains("save 1 / 4.0 ms", line);
        Assert.Contains("worst 80.0 ms", line);
    }

    [AvaloniaFact]
    public void HistoricalWeekIsReusedButCurrentWeekRefreshesOnCheckpoint()
    {
        var now = new DateTime(2026, 11, 9, 14, 15, 0);
        var model = TimerModel.CreateDemo(() => now); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekOverviewTab")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var host = window.GetVisualDescendants().OfType<ContentControl>()
                .Single(c => c.Name == "WorkspaceHost");
            model.GoToDate(model.Engine.Now.AddDays(-7)); var previous = host.Content;
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.Same(previous, host.Content);
            model.ToggleAccount("Account four"); previous = host.Content; // Monthly budget shared by both weeks.
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.NotSame(previous, host.Content);
            model.ShowToday(); previous = host.Content;
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.NotSame(previous, host.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void IdleCheckpointsReuseTimelineAndTotalsBarButTrackingStillRefreshesGeometry()
    {
        var now = new DateTime(2026, 11, 2, 14, 15, 0);
        var model = TimerModel.CreateDemo(() => now); model.CompleteSetup();
        if (model.IsTracking) model.TogglePause();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "TimelineHost");
            var bar = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "DayBar");
            var timeline = host.Content; var parts = bar.Child;
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.Same(timeline, host.Content); Assert.Same(parts, bar.Child);
            model.SetPillScale(90); Assert.Same(timeline, host.Content);
            model.ToggleAccount("Account one"); timeline = host.Content;
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.NotSame(timeline, host.Content); Assert.Same(parts, bar.Child);
            model.GoToDate(model.Engine.Now.AddDays(-2)); timeline = host.Content;
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.Same(timeline, host.Content);
            model.SetDecimalHours(true); Assert.NotSame(timeline, host.Content);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void WatchdogRecoversWithoutMultiplyingPendingFramesAndCompletesOnce()
    {
        var frames = new Queue<Action<TimeSpan>>(); double now = 0;
        var pump = new AnimationFramePump(frames.Enqueue, () => now);
        var updates = new List<double>(); var completed = 0;
        pump.Start(280, updates.Add, () => completed++);
        for (now = 50; now <= 200; now += 50) { pump.Watchdog(); Assert.Single(frames); }
        frames.Dequeue()(default); Assert.Single(frames);
        now = 300; pump.Watchdog();
        Assert.False(pump.Running); Assert.Equal(1, completed); Assert.Equal(280, updates[^1]);
        var count = updates.Count;
        frames.Dequeue()(default); pump.Watchdog();
        Assert.Empty(frames); Assert.Equal(count, updates.Count); Assert.Equal(1, completed);
    }

    [Fact]
    public void RestartReusesOutstandingFrameAndNeverCompletesCancelledRun()
    {
        var frames = new Queue<Action<TimeSpan>>(); double now = 0;
        var pump = new AnimationFramePump(frames.Enqueue, () => now);
        var oldCompleted = 0; var completed = 0; var updates = new List<double>();
        pump.Start(340, _ => { }, () => oldCompleted++);
        pump.Stop(); now = 100;
        pump.Start(280, updates.Add, () => completed++);
        Assert.Single(frames);
        now = 116; frames.Dequeue()(default);
        Assert.Equal(new[] { 16d }, updates); Assert.Single(frames);
        now = 400; frames.Dequeue()(default);
        Assert.Empty(frames); Assert.Equal(0, oldCompleted); Assert.Equal(1, completed);
    }

    [AvaloniaFact]
    public void HiddenDashboardKeepsItsWorkspaceUntilShownThenUsesLatestModel()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "TimelineHost");
            var before = host.Content;
            window.Hide();
            model.ToggleAccount("Account one"); model.TogglePause();
            Assert.Same(before, host.Content);
            window.Show();
            Assert.NotSame(before, host.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChangesDuringCollapseAreDeferredAndHidingCancelsTheCompletion()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show();
        var enabled = WindowMorph.Enabled; var reduced = Motion.Override;
        try
        {
            WindowMorph.Enabled = true; Motion.Override = false;
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "TimelineHost");
            var before = host.Content; var completed = 0;
            window.ShrinkTo(new Rect(12, 12, 406, 72), 20, null, default, () => completed++);
            Assert.True(window.Morphing);
            model.ToggleAccount("Account one"); model.TogglePause();
            Assert.Same(before, host.Content);
            window.Hide(); Assert.False(window.Morphing); Assert.Equal(0, completed);
            window.Show(); Assert.NotSame(before, host.Content); Assert.True(window.ContentTakesPresses);
        }
        finally { WindowMorph.Enabled = enabled; Motion.Override = reduced; window.Close(); }
    }
}
