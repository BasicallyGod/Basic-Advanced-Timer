namespace Timer.Desktop;

/// <summary>
/// The motion of the dashboard's account panel as numbers, so the screen, the tests and the mock all read one thing. Two eased channels carry everything:
/// <see cref="P"/> (0 shut to 1 fully out) and <see cref="Pin"/> (0 floating over the page to 1 docked, with the page making room for it), and
/// <see cref="Room"/> is the two together, the share of the panel's width the page gives up. Each channel eases on a cubic Bézier, the way the board draws it
/// (opening 280 ms easing out quickly, closing 220 ms ease in and out, pinning 260 ms), and turning round half way simply sets off again from wherever the
/// value is, with the new direction's length, so nothing jumps. Nothing here touches a control: the window asks for <see cref="Advance"/> from a timer and
/// repaints on <see cref="Changed"/>.
/// </summary>
public sealed class DockMotion
{
    public const int OpenMilliseconds = 280, CloseMilliseconds = 220, PinMilliseconds = 260;
    /// <summary>The panel's width when fully out, and the width its contents are laid out at (clipped while it is narrower, so nothing in it reflows).</summary>
    public const double PanelWidth = 272;
    /// <summary>Off in tests, where no timer ticks and the end state is wanted at once, like <see cref="PillWindow.SlideEnabled"/>.</summary>
    public static bool Enabled { get; set; } = true;

    private static readonly Bezier OpenCurve = new(.22, .8, .24, 1), CloseCurve = new(.4, 0, .2, 1), PinCurve = new(.25, .1, .25, 1);
    private readonly Channel open = new(), pin = new();

    public DockMotion(bool isOpen = false, bool pinned = true) { open.Snap(isOpen ? 1 : 0); pin.Snap(pinned ? 1 : 0); }

    /// <summary>Raised whenever either value moved.</summary>
    public event Action? Changed;
    /// <summary>How far out the panel is, 0 to 1.</summary>
    public double P => open.Value;
    /// <summary>How docked the panel is, 0 (floating) to 1 (the page makes room).</summary>
    public double Pin => pin.Value;
    /// <summary>The share of the panel's width the page gives up: the panel's progress times its docked-ness.</summary>
    public double Room => open.Value * pin.Value;
    /// <summary>Where each channel is heading.</summary>
    public bool WantsOpen => open.To > .5;
    public bool WantsPinned => pin.To > .5;
    /// <summary>Whether anything is still moving.</summary>
    public bool Moving => open.Moving || pin.Moving;

    /// <summary>Opens or shuts the panel. Asked again for where it is already heading, nothing changes.</summary>
    public void SetOpen(bool on)
    {
        var target = on ? 1.0 : 0.0;
        if (open.To == target) return;
        if (!Enabled || Motion.Reduced) open.Snap(target);
        else open.Start(target, on ? OpenMilliseconds : CloseMilliseconds, on ? OpenCurve : CloseCurve);
        Changed?.Invoke();
    }

    /// <summary>Docks the panel into the page's room, or floats it over the page.</summary>
    public void SetPinned(bool on)
    {
        var target = on ? 1.0 : 0.0;
        if (pin.To == target) return;
        if (!Enabled || Motion.Reduced) pin.Snap(target);
        else pin.Start(target, PinMilliseconds, PinCurve);
        Changed?.Invoke();
    }

    /// <summary>Moves both channels on by this much time. Returns whether anything moved.</summary>
    public bool Advance(TimeSpan elapsed)
    {
        var moved = open.Advance(elapsed.TotalMilliseconds) | pin.Advance(elapsed.TotalMilliseconds);
        if (moved) Changed?.Invoke();
        return moved;
    }

    /// <summary>Jumps both to where they are heading (the window being hidden, less motion asked for, tests).</summary>
    public void Finish()
    {
        var moved = open.Moving || pin.Moving;
        open.Snap(open.To); pin.Snap(pin.To);
        if (moved) Changed?.Invoke();
    }

    /// <summary>A part of the page that moves over only a stretch of the panel's progress (the board's windows): 0 before <paramref name="from"/>, 1 after <paramref name="to"/>, linear between.</summary>
    public static double Window(double value, double from, double to) => Math.Clamp((value - from) / (to - from), 0, 1);

    private sealed class Channel
    {
        public double Value, To, From, Elapsed, Length;
        private Bezier curve = OpenCurve;
        public bool Moving => Length > 0;
        public void Snap(double v) { Value = From = To = v; Length = 0; Elapsed = 0; }
        public void Start(double target, double milliseconds, Bezier shape) { From = Value; To = target; Elapsed = 0; Length = milliseconds; curve = shape; }
        public bool Advance(double milliseconds)
        {
            if (Length <= 0) return false;
            Elapsed += milliseconds;
            if (Elapsed >= Length) { Snap(To); return true; }
            Value = From + (To - From) * curve.At(Elapsed / Length);
            return true;
        }
    }

    /// <summary>A CSS-style cubic Bézier easing: the curve from (0,0) to (1,1) with two handles, read as progress against time.</summary>
    internal readonly struct Bezier(double x1, double y1, double x2, double y2)
    {
        private double X(double t) => 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
        private double Y(double t) => 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
        /// <summary>The eased share for a share of the time, found by bisection on the curve's x.</summary>
        public double At(double time)
        {
            if (time <= 0) return 0; if (time >= 1) return 1;
            double low = 0, high = 1, t = time;
            for (var i = 0; i < 28; i++)
            {
                var x = X(t);
                if (Math.Abs(x - time) < 1e-7) break;
                if (x < time) low = t; else high = t;
                t = (low + high) / 2;
            }
            return Y(t);
        }
    }
}
