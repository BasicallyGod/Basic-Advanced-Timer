using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;

namespace Timer.Desktop.Tests;

internal static class SettingsTestHelpers
{
    /// <summary>Opens the settings dialog from the rail's gear if it is not already open, and shows one category.</summary>
    public static void OpenCategory(this DashboardWindow window, string key)
    {
        if (!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SettingsCategory_general"))
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Opens the Accounts tile in place on the desk, so its rows are all on show and at known places.</summary>
    public static void ExpandAccounts(this DashboardWindow window)
    {
        window.GetVisualDescendants().OfType<DeskView>().Single().Lively = false; // Nothing ticks headless, so tiles must move straight to their places.
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TileExpand_accounts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }
}
