using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AnimationSchedulingTests
{
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
