using System.Diagnostics;

namespace Timer.Desktop;

/// <summary>One-shot launch milestones. Callback milestones are UI delivery times, not screen presentation times.</summary>
public static class StartupTimings
{
    private static long? started;
    private static readonly HashSet<string> recorded = new();
    private static readonly object gate = new();
    public static void Begin() { lock (gate) { started = Stopwatch.GetTimestamp(); recorded.Clear(); } }
    public static void Milestone(string name)
    {
        lock (gate)
            if (started is { } time && recorded.Add(name)) PerformanceTimings.Record(name, Stopwatch.GetElapsedTime(time).TotalMilliseconds);
    }
    public static IDisposable? MeasureOnce(string name)
    {
        lock (gate) return recorded.Add(name) ? PerformanceTimings.Measure(name) : null;
    }
}
