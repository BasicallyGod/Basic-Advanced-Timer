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
        if (operation is not ("save" or "workspace rebuild" or "expand preparation" or "collapse preparation" or "dashboard capture" or "animation update" or "snapshot release" or "deferred refresh" or "pill show" or "dashboard hide" or "pill activate")) return;
        lock (gate)
        {
            if (active is null || active.OwnerThread != Environment.CurrentManagedThreadId) return;
            var prior = active.operations.GetValueOrDefault(operation);
            active.operations[operation] = (prior.Count + 1, prior.Ms + ms);
            if (operation == "animation update") active.worstUpdate = Math.Max(active.worstUpdate, ms);
        }
    }
    public static string Report()
    {
        lock (gate) return "Recent transitions (native callbacks are not presented frames; preparation included in total):"
            + Environment.NewLine + string.Join(Environment.NewLine, recent);
    }
    public sealed class Transition(int id, string direction)
    {
        internal int OwnerThread { get; } = Environment.CurrentManagedThreadId;
        private readonly long started = Stopwatch.GetTimestamp();
        internal readonly Dictionary<string, (int Count, double Ms)> operations = new();
        private int native, watchdog;
        private double firstNative, lastNative, worstGap, totalGaps;
        private bool finished;
        private string mode = "live";
        private int layouts, probes;
        private double worstDelay;
        internal double worstUpdate;
        public void SetMode(string value) { lock (gate) mode = value; }
        public void Layout() { lock (gate) layouts++; }
        public void ProbeDelay(double ms) { lock (gate) { probes++; worstDelay = Math.Max(worstDelay, ms); } }
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
                work += $"; mode {mode}; layout events {layouts}; update worst {worstUpdate:0.0} ms; dispatcher probes {probes}, worst lateness {worstDelay:0.0} ms";
                recent.Enqueue($"#{id} {direction} {outcome}: total {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms; native {native}, watchdog {watchdog}; first native after scheduling {first}; native gaps avg {(native > 1 ? totalGaps / (native - 1) : 0):0.0} ms, worst {worstGap:0.0} ms; work during transition: {work}");
                if (recent.Count > 20) recent.Dequeue();
                if (ReferenceEquals(active, this)) active = null;
            }
        }
    }
}
