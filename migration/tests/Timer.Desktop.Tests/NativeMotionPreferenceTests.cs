using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NativeMotionPreferenceTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint flags);

    [AvaloniaFact]
    public void UnforcedPreferenceMatchesWindowsClientAreaAnimationSetting()
    {
        if (!OperatingSystem.IsWindows()) return;
        var previous = Motion.Override;
        try
        {
            Motion.Override = null;
            var reduced = SystemParametersInfo(0x1042, 0, out var enabled, 0) && !enabled;
            Assert.Equal(reduced, Motion.Reduced);
            Assert.Equal(reduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(340), Motion.Ease(340));
        }
        finally { Motion.Override = previous; }
    }
}
