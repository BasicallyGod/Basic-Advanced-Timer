using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class WindowSizeGuardTests
{
    [AvaloniaFact]
    public void RepairsStaleNativeBoundsEvenWhenLogicalSizeAlreadyMatches()
    {
        var window = new Window { SystemDecorations = SystemDecorations.None };
        var actual = new ScreenGuard.Box(-1600, 90, 900, 600);
        var moves = 0;
        var guard = new WindowSizeGuard(window, new Size(1300, 800), _ => actual,
            (_, box) => { actual = box; moves++; return true; });
        window.Show();
        try
        {
            Assert.Equal(1300, window.Width); Assert.Equal(800, window.Height);
            var report = guard.Ensure();
            Assert.Equal(new ScreenGuard.Box(-1600, 90,
                (int)Math.Round(1300 * window.RenderScaling), (int)Math.Round(800 * window.RenderScaling)), actual);
            Assert.Contains("resize accepted", report); Assert.Equal(1, moves);
            guard.Ensure(); Assert.Equal(1, moves); // Correct bounds never cause another native resize.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LatestWorkAreaSizeWinsOverStaleLogicalNotification()
    {
        var window = new Window { SystemDecorations = SystemDecorations.None };
        var actual = new ScreenGuard.Box(20, 30, 1300, 800);
        var guard = new WindowSizeGuard(window, new Size(1300, 800), _ => actual,
            (_, box) => { actual = box; return true; });
        window.Show();
        try
        {
            guard.Request(new Size(1000, 700)); // A smaller monitor's work area.
            window.Width = 900; window.Height = 600; // A late notification from the old native size.
            guard.Ensure();
            Assert.Equal(1000, window.Width); Assert.Equal(700, window.Height);
            Assert.Equal((int)Math.Round(1000 * window.RenderScaling), actual.Width);
            Assert.Equal((int)Math.Round(700 * window.RenderScaling), actual.Height);
            guard.Request(new Size(1300, 800)); guard.Ensure(); // Back on the larger monitor.
            Assert.Equal((int)Math.Round(1300 * window.RenderScaling), actual.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HiddenAndMinimizedWindowsAreNotResizedAndNativeFailureIsReported()
    {
        var window = new Window(); var moves = 0;
        var guard = new WindowSizeGuard(window, new Size(1300, 800), _ => new ScreenGuard.Box(0, 0, 900, 600),
            (_, _) => { moves++; return false; });
        Assert.Contains("skipped", guard.Ensure()); Assert.Equal(0, moves);
        window.Show();
        try
        {
            window.WindowState = WindowState.Minimized;
            Assert.Contains("skipped", guard.Ensure()); Assert.Equal(0, moves);
            window.WindowState = WindowState.Normal;
            Assert.Contains("resize failed, now 900x600 px", guard.Ensure()); Assert.Equal(1, moves);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DashboardReopenRestoresRequestedSizeAndReducedMotionCompletesNormally()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model);
        var reduced = Motion.Override;
        try
        {
            Motion.Override = true;
            window.RequestWindowSize(new Size(1000, 700));
            window.Show(); window.Hide();
            window.Width = 900; window.Height = 600;
            window.GrowFrom(new Rect(12, 12, 406, 72), 20, null, default);
            Assert.Equal(1000, window.Width); Assert.Equal(700, window.Height);
            Assert.False(window.Morphing); Assert.True(window.ContentTakesPresses);
            Assert.Equal(1, window.ContentOpacity);
        }
        finally { Motion.Override = reduced; window.Close(); }
    }
}
