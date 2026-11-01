using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Timer.Desktop;

/// <summary>The figures for a run of frames: how many, how long they took on average and at worst, and how many were late.</summary>
public readonly record struct FrameStats(int Frames, double AverageMs, double P95Ms, double WorstMs, int Slow, int Dropped)
{
    public double Fps => AverageMs > 0 ? 1000 / AverageMs : 0;
    public override string ToString() => Frames == 0 ? "no frames yet" : $"{Fps:0} fps · avg {AverageMs:0.0} ms · p95 {P95Ms:0.0} · worst {WorstMs:0.0} · {Slow} over 20 ms, {Dropped} over 33 ms";
}

/// <summary>
/// Measures how long the frames of a window take. It asks the window for every frame in turn and keeps the gaps between them (the last 900, about fifteen seconds at
/// 60 per second): a gap well over 16.7 milliseconds is a frame that took too long to draw. It is off until it is switched on (Settings, Development), since asking for
/// every frame keeps the window drawing and nothing else in the app does that, and it is never saved.
/// </summary>
public sealed class FrameMonitor
{
    public static FrameMonitor Instance { get; } = new();
    public const int Kept = 900;
    private readonly double[] gaps = new double[Kept];
    private int count, next;
    private TimeSpan last; private bool haveLast;
    private TopLevel? level;
    private DispatcherTimer? tick;
    public bool Running => level is not null;
    /// <summary>Raised about once a second while running, and when it starts or stops, so a readout can follow.</summary>
    public event Action? Changed;

    public void Start(TopLevel window)
    {
        Stop(); Reset(); ForgetPages(); level = window; layoutWatch = (_, _) => Bump("layout passes"); window.LayoutUpdated += layoutWatch; watched = window;
        window.RequestAnimationFrame(OnFrame);
        tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; tick.Tick += (_, _) => Changed?.Invoke(); tick.Start();
        Changed?.Invoke();
    }
    public void Stop()
    {
        var was = Running; if (watched is not null && layoutWatch is not null) watched.LayoutUpdated -= layoutWatch; watched = null; level = null; tick?.Stop(); tick = null; haveLast = false;
        if (was) Changed?.Invoke();
    }
    private EventHandler? layoutWatch; private TopLevel? watched;
    public void Reset() { count = 0; next = 0; haveLast = false; counters.Clear(); sinceReset.Restart(); }
    // What happened on the page while the frames were taken: how often things were laid out, changed and drawn, per second. A page that is quietly redrawing or
    // laying out something every frame shows up here against one that is not.
    private readonly Dictionary<string, int> counters = new(); private readonly System.Diagnostics.Stopwatch sinceReset = System.Diagnostics.Stopwatch.StartNew();
    /// <summary>Counts one occurrence of <paramref name="what"/> while the monitor runs (nothing otherwise).</summary>
    public static void Bump(string what) { var m = Instance; if (m.level is null) return; m.counters[what] = m.counters.GetValueOrDefault(what) + 1; }
    private string Rates()
    {
        var seconds = Math.Max(1, sinceReset.Elapsed.TotalSeconds);
        return counters.Count == 0 ? "" : " · per second: " + string.Join(", ", counters.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value / seconds:0.#}"));
    }
    /// <summary>Forgets the pages kept from before (a new run of the monitor starts clean).</summary>
    public void ForgetPages() => remembered.Clear();
    private void OnFrame(TimeSpan now)
    {
        if (level is null) return;
        if (haveLast) Record((now - last).TotalMilliseconds);
        last = now; haveLast = true;
        level.RequestAnimationFrame(OnFrame);
    }
    /// <summary>Adds one gap between frames, in milliseconds. Public so a test can feed it.</summary>
    public void Record(double milliseconds) { gaps[next] = milliseconds; next = (next + 1) % Kept; if (count < Kept) count++; }
    public FrameStats Snapshot() => Summarise(gaps.Take(count).ToArray());
    public static FrameStats Summarise(IReadOnlyList<double> milliseconds)
    {
        if (milliseconds.Count == 0) return default;
        var sorted = milliseconds.OrderBy(m => m).ToArray();
        var p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * .95) - 1)];
        return new FrameStats(sorted.Length, sorted.Average(), p95, sorted[^1], sorted.Count(m => m > 20), sorted.Count(m => m > 33));
    }
    /// <summary>A line to paste into a note: when, which page, and the figures.</summary>
    public string Report()
    {
        // The pages visited since the monitor started, each with the figures it had when it was left (a page's figures start again on arriving), then this page's own.
        var lines = remembered.Where(r => r.Key != Page).Select(r => r.Value).Append(Line());
        return string.Join(Environment.NewLine, lines.Append(PerformanceTimings.Report()).Append(TransitionTimings.Report()));
    }
    private readonly Dictionary<string, string> remembered = new();
    /// <summary>Keeps the figures of the page being left, if there are enough frames to mean anything, so Copy report in Settings can still give the Timeline's.</summary>
    public void Remember() { if (Page.Length > 0 && count >= 30) remembered[Page] = Line(); }
    private string Line()
    {
        var all = gaps.Take(count).ToArray();
        // How the frames spread, so a steady 20 ms pace can be told from a smooth run with a few long stalls.
        var spread = $"{all.Length} frames: {all.Count(g => g <= 17.5)} up to 17.5 ms, {all.Count(g => g > 17.5 && g <= 20)} to 20, {all.Count(g => g > 20 && g <= 25)} to 25, {all.Count(g => g > 25 && g <= 33)} to 33, {all.Count(g => g > 33)} over 33";
        var modes = (ThemeEngine.FlatShadows ? " [flat: no raised or sunk shadows]" : "") + (TimerModel.TickPaused ? " [1 s refresh paused]" : "") + (TimerResolution.Requested ? " [1 ms timer requested]" : "");
        var tick = TimerResolution.CurrentMilliseconds is { } resolution ? $" · system timer tick {resolution:0.0} ms" : "";
        return $"{DateTime.Now:yyyy-MM-dd HH:mm} · {Page} · {BuildInfo.Current}{modes} · {Snapshot()} · {spread}{Rates()}{tick}";
    }
    /// <summary>The page the figures were taken on, kept by the readout; the figures start again when it changes, so each report is for one page.</summary>
    public string Page { get; set; } = "";
}

/// <summary>A small readout in the corner of the dashboard while the frame monitor runs: the figures for the last fifteen seconds, and the page they were taken on.</summary>
public sealed class FrameChip : Border
{
    private readonly TextBlock words = new() { FontSize = 11.5, FontWeight = FontWeight.SemiBold, FontFamily = new FontFamily("Consolas, monospace") };
    private readonly Func<string> page;
    public FrameChip(Func<string> page)
    {
        this.page = page; Name = "FrameChip"; IsHitTestVisible = false; IsVisible = false;
        Background = new SolidColorBrush(Color.FromArgb(220, 16, 18, 22)); CornerRadius = new CornerRadius(8); Padding = new Thickness(9, 4);
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(10, 10, 20, 8); // Right, clear of the sidebar's Quit timer.
        words.Foreground = Brushes.White; Child = words;
        AttachedToVisualTree += (_, _) => { FrameMonitor.Instance.Changed += Sync; Sync(); };
        Sync();
        DetachedFromVisualTree += (_, _) => FrameMonitor.Instance.Changed -= Sync;
    }
    private void Sync()
    {
        IsVisible = FrameMonitor.Instance.Running;
        if (!IsVisible) return;
        var now = page(); if (FrameMonitor.Instance.Page != now) { FrameMonitor.Instance.Remember(); FrameMonitor.Instance.Reset(); FrameMonitor.Instance.Page = now; }
        words.Text = now + " · " + FrameMonitor.Instance.Snapshot();
    }
}
