using System.Diagnostics;

namespace Timer.Desktop;

/// <summary>Last twenty transitions, measured passively on the UI thread. No account names or other user data.</summary>
public static class TransitionTimings
{
    private static readonly Queue<string> recent = new();
    private static Transition? active;
    private static int sequence;
    private static readonly object gate = new();
    public static Transition Begin(string direction)
    {
        lock (gate)
        {
            active?.Finish("cancelled");
            return active = new Transition(++sequence, direction);
        }
    }
    public static void Operation(string operation, double ms)
    {
        if (operation is not ("save" or "workspace rebuild" or "expand preparation" or "collapse preparation")) return;
        lock (gate)
        {
            if (active is null) return;
            var prior = active.operations.GetValueOrDefault(operation);
            active.operations[operation] = (prior.Count + 1, prior.Ms + ms);
        }
    }
    public static string Report()
    {
        lock (gate) return "Recent transitions (native callbacks are not presented frames; preparation included in total):"
            + Environment.NewLine + string.Join(Environment.NewLine, recent);
    }
    public sealed class Transition(int id, string direction)
    {
        private readonly long started = Stopwatch.GetTimestamp();
        internal readonly Dictionary<string, (int Count, double Ms)> operations = new();
        private int native, watchdog;
        private double firstNative, lastNative, worstGap, totalGaps;
        private bool finished;
        public void Sample(double elapsed, bool fallback)
        {
            lock (gate)
            {
                if (finished) return;
                if (fallback) { watchdog++; return; }
                if (native++ == 0) firstNative = elapsed;
                else { var gap = elapsed - lastNative; totalGaps += gap; worstGap = Math.Max(worstGap, gap); }
                lastNative = elapsed;
            }
        }
        public void Finish(string outcome)
        {
            lock (gate)
            {
                if (finished) return;
                finished = true;
                var work = operations.Count == 0 ? "none" : string.Join(", ", operations.Select(p => $"{p.Key} {p.Value.Count} / {p.Value.Ms:0.0} ms"));
                var first = native == 0 ? "none" : $"{firstNative:0.0} ms";
                recent.Enqueue($"#{id} {direction} {outcome}: total {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms; native {native}, watchdog {watchdog}; first native after scheduling {first}; native gaps avg {(native > 1 ? totalGaps / (native - 1) : 0):0.0} ms, worst {worstGap:0.0} ms; work during transition: {work}");
                if (recent.Count > 20) recent.Dequeue();
                if (ReferenceEquals(active, this)) active = null;
            }
        }
    }
}
