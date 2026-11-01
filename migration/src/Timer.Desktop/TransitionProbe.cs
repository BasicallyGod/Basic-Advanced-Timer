using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;

namespace Timer.Desktop;

/// <summary>Short-lived UI-thread probe. It requests no rendered frames and stops with its transition.</summary>
internal sealed class TransitionProbe : IDisposable
{
    private readonly Window window;
    private readonly TransitionTimings.Transition? timing;
    private readonly DispatcherTimer timer;
    private long previous = Stopwatch.GetTimestamp();
    public TransitionProbe(Window window, TransitionTimings.Transition? timing)
    {
        this.window = window; this.timing = timing;
        window.LayoutUpdated += Layout;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            timing?.ProbeDelay(Math.Max(0, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds - 20));
            previous = now;
        });
        timer.Start();
    }
    private void Layout(object? sender, EventArgs e) => timing?.Layout();
    public void Dispose() { timer.Stop(); window.LayoutUpdated -= Layout; }
}
