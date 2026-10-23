using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop.Tests;

// A page's controls reach the visual tree only after a layout and render pass, which a slow Windows runner can leave undone after one RunJobs.
internal static class TestPages
{
    /// <summary>Opens the settings page the way a click does, then lays it out and renders it so its controls can be found.</summary>
    public static void OpenSettings(this Window window)
    {
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
        Dispatcher.UIThread.RunJobs();
    }
}
