using System.Diagnostics;

namespace Timer.Desktop;

/// <summary>Bounded, passive operation timings. No timer, render requests, file writes or user data.</summary>
public static class PerformanceTimings
{
    private static readonly Dictionary<string, (long Count, double Total, double Worst, double Last)> samples = new();
    private static readonly object gate = new();
    public static IDisposable Measure(string operation) => new Measurement(operation);
    public static T Measure<T>(string operation, Func<T> action)
    {
        using var timing = Measure(operation);
        return action();
    }
    public static void Record(string operation, double milliseconds)
    {
        TransitionTimings.Operation(operation, milliseconds);
        lock (gate)
        {
            if (!samples.ContainsKey(operation) && samples.Count >= 64) return;
            var previous = samples.GetValueOrDefault(operation);
            samples[operation] = (previous.Count + 1, previous.Total + milliseconds, Math.Max(previous.Worst, milliseconds), milliseconds);
        }
    }
    public static string Report()
    {
        lock (gate) return "Passive timings since launch (UI callbacks, not presentation times):" + Environment.NewLine
            + string.Join(Environment.NewLine, samples.Select(s => $"{s.Key}: {s.Value.Count} calls, avg {s.Value.Total / s.Value.Count:0.0} ms, worst {s.Value.Worst:0.0} ms, last {s.Value.Last:0.0} ms"));
    }
    private sealed class Measurement(string operation) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        public void Dispose() => Record(operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
