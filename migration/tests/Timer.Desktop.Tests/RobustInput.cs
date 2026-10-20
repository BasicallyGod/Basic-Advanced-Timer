using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace Timer.Desktop.Tests;

// Headless pointer input is hit-tested against the last rendered frame. On a slow Windows runner the renderer can lag, so a click made straight
// after a layout change landed on nothing and a test failed now and then (a right-click that never reached its block). These are more specific
// MouseDown, MouseMove and MouseUp for tests, found before Avalonia's own like RobustFrames, that make sure a frame has been rendered for the
// current layout before the input is sent.
internal static class RobustInput
{
    private static void Settle(TopLevel topLevel)
    {
        Dispatcher.UIThread.RunJobs();
        if (topLevel is Window window) { using var frame = window.CaptureRenderedFrame(); }
        Dispatcher.UIThread.RunJobs();
    }
    public static void MouseDown(this TopLevel topLevel, Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
    { Settle(topLevel); HeadlessWindowExtensions.MouseDown(topLevel, point, button, modifiers); }
    public static void MouseMove(this TopLevel topLevel, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    { Settle(topLevel); HeadlessWindowExtensions.MouseMove(topLevel, point, modifiers); }
    public static void MouseUp(this TopLevel topLevel, Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
    { Settle(topLevel); HeadlessWindowExtensions.MouseUp(topLevel, point, button, modifiers); }
}
