using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Timer.Core;

namespace Timer.Desktop;

// The tray icon is the timer's only handle once its windows are hidden: it shows the state at a glance, brings the
// timer back, and offers the few actions worth reaching without opening anything.
public sealed class TrayController : IDisposable
{
    private readonly TimerModel model;
    private readonly TrayIcon icon;
    private readonly NativeMenuItem show = new() { Header = "Show timer" };
    private readonly NativeMenuItem dashboard = new() { Header = "Dashboard" };
    private readonly NativeMenuItem pause = new();
    private readonly NativeMenuItem reset = new() { Header = "Reset position" };
    private readonly NativeMenuItem close = new() { Header = "Close timer" };
    private readonly Dictionary<(bool, bool, bool), WindowIcon> icons = new();
    private (bool Recording, bool Reminder, bool Dark)? shown;

    public TrayController(TimerModel model, WindowCoordinator windows)
    {
        this.model = model;
        show.Click += (_, _) => windows.ShowTimer();
        dashboard.Click += (_, _) => windows.ShowDashboard();
        pause.Click += (_, _) => model.TogglePause();
        reset.Click += (_, _) => windows.ResetPosition();
        close.Click += (_, _) => windows.Quit();
        var menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[] { show, dashboard, new NativeMenuItemSeparator(), pause, new NativeMenuItemSeparator(), reset, close })
            menu.Items.Add(item);
        menu.Opening += (_, _) => RefreshMenu();
        icon = new TrayIcon { Menu = menu, IsVisible = true };
        icon.Clicked += (_, _) => windows.ToggleFromTray();
        model.PropertyChanged += Changed;
        // The two middle items take their words from the day's state. They are filled in now, not only when the menu opens, because the menu can
        // be drawn before that event arrives, which left them as blank lines.
        RefreshMenu();
        Refresh();
    }
    public static string Tooltip(TimerModel model)
    {
        // Windows shows at most 127 characters, so keep it short.
        var text = model.Elapsed == TimeSpan.Zero && !model.IsTracking ? model.Status
            : (model.ActiveAccount is { } name ? (BreakAccount.Is(name) ? "On break" : "Tracking " + name) : model.Status) + ", day " + model.Format(model.Elapsed);
        return "Basic’s Advanced Timer\n" + text;
    }
    public static string PauseLabel(TimerModel model) => model.IsOnBreak ? (model.ResumeAccount is { } back ? "End break and resume " + back : "End break")
        : model.IsTracking ? "Pause tracking"
        : model.SelectedAccount is { } name && model.Accounts.Any(a => a.Name == name) ? "Resume " + name : "Start tracking";
    public void RefreshMenu()
    {
        pause.Header = PauseLabel(model);
        pause.IsEnabled = model.IsTracking || model.SelectedAccount is not null;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { RefreshMenu(); Refresh(); }
    private void Refresh()
    {
        var text = Tooltip(model);
        if (icon.ToolTipText != text) icon.ToolTipText = text;
        var now = (model.IsTracking, model.ReminderDue, TaskbarIsDark());
        if (shown != now)
        {
            shown = now;
            if (!icons.TryGetValue(now, out var drawn)) icons[now] = drawn = Draw(now.Item1, now.Item2, now.Item3);
            icon.Icon = drawn;
        }
    }
    // The taskbar follows the computer's light or dark setting, not the timer's own theme.
    private static bool TaskbarIsDark() => Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
    /// <summary>
    /// The pill's outline with its two lights and the bars for the time, in one colour (white on a dark taskbar, near black on a light one), as the app icon draws it
    /// at small sizes. The state light turns green while recording and the reminder light blue while one is due, so the tray says what the pill's lights say.
    /// </summary>
    public static WindowIcon Draw(bool recording, bool reminderDue = false, bool dark = false)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(.25, .25)))
        {
            var line = dark ? Colors.White : Color.Parse("#141824");
            var ink = new SolidColorBrush(line);
            context.DrawRectangle(null, new Pen(ink, 18), new Rect(79, 33, 98, 190), 49, 49);
            context.DrawRectangle(new SolidColorBrush(recording ? Color.Parse("#34D399") : line), null, new Rect(106, 68, 20, 38), 10, 10);
            context.DrawRectangle(new SolidColorBrush(reminderDue ? Color.Parse("#6096FF") : line), null, new Rect(130, 68, 20, 38), 10, 10);
            context.DrawRectangle(ink, null, new Rect(98, 134, 60, 22), 11, 11);
            context.DrawRectangle(ink, null, new Rect(98, 166, 60, 22), 11, 11);
        }
        return new WindowIcon(bitmap);
    }
    public TrayIcons Icons => new() { icon };
    public void Dispose() { model.PropertyChanged -= Changed; icon.IsVisible = false; icon.Dispose(); }
}
