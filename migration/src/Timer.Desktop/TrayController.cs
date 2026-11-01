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
    private readonly NativeMenuItem close = new() { Header = "Quit timer" };
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
    // The app icon's own shapes (Assets/AppIcon, drawn on a 1254 unit grid): the pale moon with the bat's face cut out of it, the wings, and the eyes.
    private const string MoonPath = "M293 489 C340 347 472 244 627 244 C782 244 914 347 961 489 C872 530 804 598 774 670 L774 489 Q774 473 765 481 L670 570 Q627 565 584 570 L489 481 Q480 473 480 489 L480 670 C450 598 382 530 293 489 Z";
    private const string FacePath = "M293 489 C382 530 450 598 480 670 L480 489 Q480 473 489 481 L584 570 Q627 565 670 570 L765 481 Q774 473 774 489 L774 670 C804 598 872 530 961 489 L961 715 C796 688 668 776 627 969 C586 776 458 688 293 715 Z";
    private const string WingPath = "M120 466 C177 458 238 469 293 489 L293 715 C458 688 586 776 627 969 L627 998 C544 895 441 849 318 871 C319 747 265 676 141 676 L113 476 Q111 468 120 466 Z";
    /// <summary>
    /// The bat on its own, without the tile: the pale moon, the purple face, wings that are pale on a dark taskbar and deep purple on a light one, and the pale eyes,
    /// as the two tray files draw it. The eyes are a little larger than the app icon's since the tray is small. A green dot at the bottom right says it is recording
    /// and a blue one at the top right says a reminder is due; with neither, nothing is added, so the tray says what the pill's display says.
    /// </summary>
    public static WindowIcon Draw(bool recording, bool reminderDue = false, bool dark = false) => new(DrawBitmap(recording, reminderDue, dark));
    /// <summary>The picture itself, apart from the icon made of it, so a test can look at it.</summary>
    public static RenderTargetBitmap DrawBitmap(bool recording, bool reminderDue = false, bool dark = false)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        {
            // The art spans 113 to 1141 across and 244 to 998 down; it is scaled to the icon's width and set a little above the middle to leave the dots room.
            var scale = 62.0 / 1028; var left = 1.0; var top = 5.0;
            using (context.PushTransform(Matrix.CreateTranslation(-113, -244) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
            {
                var wings = new SolidColorBrush(dark ? Color.Parse("#F8F2FF") : Color.Parse("#402078"));
                var wing = Geometry.Parse(WingPath);
                context.DrawGeometry(new SolidColorBrush(Color.Parse("#D9C6FB")), null, Geometry.Parse(MoonPath));
                context.DrawGeometry(new SolidColorBrush(Color.Parse("#7136E8")), null, Geometry.Parse(FacePath));
                context.DrawGeometry(wings, null, wing);
                using (context.PushTransform(new Matrix(-1, 0, 0, 1, 1254, 0))) context.DrawGeometry(wings, null, wing);
                var eyes = new SolidColorBrush(Color.Parse("#FBF6FF"));
                context.DrawEllipse(eyes, null, new Point(557.5, 673), 46, 46); context.DrawEllipse(eyes, null, new Point(696.5, 673), 46, 46);
            }
            if (recording) context.DrawEllipse(new SolidColorBrush(Color.Parse("#34D399")), null, new Point(54, 53), 9, 9);
            if (reminderDue) context.DrawEllipse(new SolidColorBrush(Color.Parse("#6096FF")), null, new Point(54, 11), 9, 9);
        }
        return bitmap;
    }
    public TrayIcons Icons => new() { icon };
    public void Dispose() { model.PropertyChanged -= Changed; icon.IsVisible = false; icon.Dispose(); }
}
