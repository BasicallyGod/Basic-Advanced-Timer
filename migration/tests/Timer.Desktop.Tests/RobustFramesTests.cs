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

    [AvaloniaFact]
    public void AskingForAFrameAlsoLaysOutWhatWasAddedSinceSoItCanBeQueried()
    {
        var host = new Grid(); var window = new Window { Width = 300, Height = 200, Content = host }; window.Show();
        try
        {
            var late = new Border { Width = 50, Height = 20 }; host.Children.Add(late);
            using var frame = window.CaptureRenderedFrame();
            Assert.True(late.Bounds.Width > 0 && late.IsMeasureValid, "The new control was not laid out."); // It has a size, so it was measured and arranged.
        }
        finally { window.Close(); }
    }
}
