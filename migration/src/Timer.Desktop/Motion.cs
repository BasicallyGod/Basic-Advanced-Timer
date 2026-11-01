using System.Runtime.InteropServices;

namespace Timer.Desktop;

/// <summary>
/// Whether the computer asks for less motion ("Show animations in Windows" switched off). When it does, nothing in the app pulses, steams, waves, slides or eases:
/// the reminder light stays steadily blue, the break cup's steam is still, the flag does not ripple and the drawer simply opens. The state is read from Windows
/// and looked at again every few seconds, so changing the setting needs no restart.
/// </summary>
public static class Motion
{
    private const uint GetClientAreaAnimation = 0x1042;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);

    private static bool lastKnown; private static long checkedAt = long.MinValue;
    /// <summary>Set by tests to force the answer on their own thread (so tests running side by side do not disturb each other); null reads the computer's setting.</summary>
    [ThreadStatic] private static bool? forced;
    public static bool? Override { get => forced; set => forced = value; }

    public static bool Reduced
    {
        get
        {
            if (forced is { } answer) return answer;
            if (!OperatingSystem.IsWindows()) return false;
            var now = Environment.TickCount64;
            if (checkedAt != long.MinValue && now >= checkedAt && now - checkedAt < 3000) return lastKnown;
            try { lastKnown = SystemParametersInfo(GetClientAreaAnimation, 0, out var on, 0) && !on; } catch { lastKnown = false; }
            // Publish the cache timestamp only after the corresponding value is available.
            checkedAt = now;
            return lastKnown;
        }
    }

    /// <summary>The length of an ease: as asked, or nothing at all when less motion is wanted.</summary>
    public static TimeSpan Ease(TimeSpan length) => Reduced ? TimeSpan.Zero : length;
    public static TimeSpan Ease(int milliseconds) => Ease(TimeSpan.FromMilliseconds(milliseconds));
}
