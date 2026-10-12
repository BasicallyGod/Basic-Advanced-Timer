using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Timer.Desktop.Tests;

// The headless renderer on a slow Windows runner can hand back no frame at first, which made tests that save a preview fail now and then.
// This is a more specific CaptureRenderedFrame for windows, so every call in these tests finds it before Avalonia's own: it waits a little
// and tries again when there is no frame, and only gives up (returning null as before) after about two seconds.
// It also lays the window out first, itself. Tests ask for a frame to make sure a new dialog or page has been laid out and can be queried, and
// when the renderer gives no frame the layout pass that goes with it did not happen either, which left the controls missing from the tree.
internal static class RobustFrames
{
    /// <summary>How many calls had to go round again, so a test can see the retry is in use.</summary>
    public static int Retries, Calls;
    public static WriteableBitmap? CaptureRenderedFrame(this Window window)
    {
        Calls++;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window);
            if (frame is not null) return frame;
            Retries++; Thread.Sleep(50);
        }
        return null;
    }
}
