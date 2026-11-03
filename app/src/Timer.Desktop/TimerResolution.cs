using System.Runtime.InteropServices;

namespace Timer.Desktop;

/// <summary>
/// Windows' system timer, which paces every timer-driven frame. By default it ticks every 15.6 ms, so a frame loop that wants 60 per second lands on 15.6 or 31.2 ms
/// unless some other program has asked for a finer tick. The frame report names the current tick, and a developer switch can ask for 1 ms to see what that does. Neither is
/// used outside Windows, and neither changes what the app does unless the switch is pressed.
/// </summary>
public static class TimerResolution
{
    [DllImport("ntdll.dll")] private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

    /// <summary>The system's current timer tick in milliseconds, or null where it cannot be read.</summary>
    public static double? CurrentMilliseconds
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            try { return NtQueryTimerResolution(out _, out _, out var current) == 0 ? current / 10000.0 : null; }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
        }
    }

    /// <summary>Whether this app is asking Windows for a 1 ms tick now.</summary>
    public static bool Requested { get; private set; }

    /// <summary>Asks for (or gives back) a 1 ms system tick for as long as the app is running. A developer switch; never saved.</summary>
    public static void Request(bool on)
    {
        if (!OperatingSystem.IsWindows() || on == Requested) return;
        try { if (on) timeBeginPeriod(1); else timeEndPeriod(1); Requested = on; }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
