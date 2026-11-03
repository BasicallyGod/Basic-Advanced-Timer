using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class FrameMonitorSessionTests
{
    [AvaloniaFact]
    public void OldCallbacksCannotSampleOrContinueAfterRestartOrWindowSwitch()
    {
        foreach (var switchWindow in new[] { false, true })
        {
            var first = new Window(); var second = new Window();
            var pending = new Queue<(TopLevel Owner, Action<TimeSpan> Callback)>();
            var monitor = new FrameMonitor((owner, callback) => pending.Enqueue((owner, callback)));
            try
            {
                monitor.Start(first); var stale = pending.Dequeue(); monitor.Stop();
                var target = switchWindow ? second : first;
                monitor.Start(target); var current = pending.Dequeue(); Assert.Same(target, current.Owner);
                stale.Callback(TimeSpan.FromMilliseconds(1));
                Assert.Empty(pending); Assert.Equal(0, monitor.Snapshot().Frames);
                current.Callback(TimeSpan.FromMilliseconds(100)); Assert.Single(pending);
                pending.Dequeue().Callback(TimeSpan.FromMilliseconds(116.7));
                Assert.Equal(1, monitor.Snapshot().Frames); Assert.Equal(16.7, monitor.Snapshot().AverageMs, 3);
                var last = pending.Dequeue(); monitor.Stop(); last.Callback(TimeSpan.FromMilliseconds(133.4));
                Assert.Empty(pending); Assert.Equal(1, monitor.Snapshot().Frames);
            }
            finally { monitor.Stop(); first.Close(); second.Close(); }
        }
    }
}
