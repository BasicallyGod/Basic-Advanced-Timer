using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class RobustFramesTests
{
    [AvaloniaFact]
    public void CaptureRenderedFrameOnAWindowGoesThroughTheRetryingVersion()
    {
        var window = new Window { Width = 200, Height = 100, Content = new TextBlock { Text = "x" } }; window.Show();
        try
        {
            // This call is bound to the more specific method in this namespace, not Avalonia's, which is what the counter shows.
            var before = RobustFrames.Calls;
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(before + 1, RobustFrames.Calls); Assert.NotNull(frame);
        }
        finally { window.Close(); }
    }
}
