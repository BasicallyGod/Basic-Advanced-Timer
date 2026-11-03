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
    // A double-click is two presses within the platform's double-click time, which the headless input measures in real time. Settling a frame before every press
    // (above) can take longer than that on a slow runner, so the second press was a second single click and the test failed now and then. A pair that must be a double-click
    // settles once, before the first press, and sends the rest straight after it, since nothing has changed the layout in between.
    /// <summary>A click after one frame has been rendered for the layout.</summary>
    public static void SettledClick(this TopLevel topLevel, Point point)
    { Settle(topLevel); HeadlessWindowExtensions.MouseDown(topLevel, point, MouseButton.Left, RawInputModifiers.None); HeadlessWindowExtensions.MouseUp(topLevel, point, MouseButton.Left, RawInputModifiers.None); }
    /// <summary>A click sent straight away, with no frame rendered first, for the second click of a double-click.</summary>
    public static void QuickClick(this TopLevel topLevel, Point point)
    { HeadlessWindowExtensions.MouseDown(topLevel, point, MouseButton.Left, RawInputModifiers.None); HeadlessWindowExtensions.MouseUp(topLevel, point, MouseButton.Left, RawInputModifiers.None); }
    /// <summary>
    /// Moves the pointer onto a point and, if the control there has not yet reacted (a slow runner can hit-test a frame older than the layout), nudges it by a pixel and
    /// tries again, up to a few times. For tests of what happens under the pointer, never of a move that is meant to be missed.
    /// </summary>
    public static void MouseMoveUntil(this TopLevel topLevel, Point point, Func<bool> reacted)
    {
        for (var attempt = 0; attempt < 5 && !reacted(); attempt++) MouseMove(topLevel, new Point(point.X + attempt % 2, point.Y));
    }
}
