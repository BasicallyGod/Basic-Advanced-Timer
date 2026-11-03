using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Timer.Desktop.Tests;

// Dialogs open over the dashboard now, not in windows of their own. These keep the tests short: a dialog is shown over a dashboard
// (the one given, or a new one that goes when the dialog closes), laid out, and then queried like any other control.
internal static class TestDialogs
{
    public static T Open<T>(this T dialog, DashboardWindow dashboard) where T : Dialog
    {
        dashboard.Dialogs.Show(dialog); dialog.Settle(); return dialog;
    }
    public static T Open<T>(this T dialog, TimerModel model) where T : Dialog
    {
        var dashboard = new DashboardWindow(model); dashboard.Show();
        dialog.Closed += (_, _) => dashboard.Close();
        return dialog.Open(dashboard);
    }
    public static void Settle(this Dialog dialog)
    {
        var window = (Window)TopLevel.GetTopLevel(dialog)!;
        for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
        Dispatcher.UIThread.RunJobs();
    }
    public static WriteableBitmap? CaptureRenderedFrame(this Dialog dialog) => ((Window)TopLevel.GetTopLevel(dialog)!).CaptureRenderedFrame();
}
