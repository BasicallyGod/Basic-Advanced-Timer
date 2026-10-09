using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Timer.Storage;

namespace Timer.Desktop;

// The tray icon is the timer's only handle once its windows are hidden: it shows the state at a glance, brings the
// timer back, and offers the few actions worth reaching without opening anything.
public sealed class TrayController : IDisposable
{
    private readonly TimerModel model;
    private readonly WindowCoordinator windows;
    private readonly TrayIcon icon;
    private readonly NativeMenuItem show = new() { Header = "Show timer" };
    private readonly NativeMenuItem dashboard = new() { Header = "Dashboard" };
    private readonly NativeMenuItem pause = new();
    private readonly NativeMenuItem day = new();
    private readonly NativeMenuItem reset = new() { Header = "Reset position" };
    private readonly NativeMenuItem close = new() { Header = "Close timer" };
    private WindowIcon? recordingIcon, idleIcon;
    private bool? shownRecording;

    public TrayController(TimerModel model, WindowCoordinator windows)
    {
        this.model = model; this.windows = windows;
        show.Click += (_, _) => windows.ShowTimer();
        dashboard.Click += (_, _) => windows.ShowDashboard();
        pause.Click += (_, _) => model.TogglePause();
        day.Click += (_, _) => model.ToggleDay();
        reset.Click += (_, _) => windows.ResetPosition();
        close.Click += (_, _) => windows.Quit();
        var menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[] { show, dashboard, new NativeMenuItemSeparator(), pause, day, new NativeMenuItemSeparator(), reset, close })
            menu.Items.Add(item);
        menu.Opening += (_, _) => RefreshMenu();
        icon = new TrayIcon { Menu = menu, IsVisible = true };
        icon.Clicked += (_, _) => windows.ToggleFromTray();
        model.PropertyChanged += Changed;
        Refresh();
    }
    public static string Tooltip(TimerModel model)
    {
        // Windows shows at most 127 characters, so keep it short.
        var overtime = model.IsOvertime ? ", overtime " + model.Format(model.Engine.Totals(model.Engine.Now).Overtime) : "";
        var text = !model.DayStarted && !model.IsTracking ? "Ready when you are"
            : (model.ActiveAccount is { } name ? (name == "Lunch" ? "On lunch" : "Tracking " + name) : "Not tracking") + ", day " + model.Format(model.Elapsed) + overtime;
        return "Basic’s Advanced Timer\n" + text;
    }
    public static string PauseLabel(TimerModel model) => model.IsTracking ? "Pause tracking"
        : model.SelectedAccount is { } name && model.Accounts.Any(a => a.Name == name) ? "Resume " + name : "Start tracking";
    public static string DayLabel(TimerModel model) => model.DayStarted ? "Finish day" : "Start day";
    public void RefreshMenu()
    {
        pause.Header = PauseLabel(model);
        pause.IsEnabled = model.IsTracking || model.SelectedAccount is not null;
        day.Header = DayLabel(model);
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        var text = Tooltip(model);
        if (icon.ToolTipText != text) icon.ToolTipText = text;
        if (shownRecording != model.IsTracking)
        {
            shownRecording = model.IsTracking;
            icon.Icon = model.IsTracking ? recordingIcon ??= Draw(true) : idleIcon ??= Draw(false);
        }
    }
    // A clock face, green while recording and slate when not, so the state shows without opening anything.
    public static WindowIcon Draw(bool recording)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        {
            var face = new SolidColorBrush(Color.Parse(recording ? "#337A50" : "#4A5860"));
            context.DrawEllipse(face, null, new Point(32, 32), 29, 29);
            var pen = new Pen(Brushes.White, 5, lineCap: PenLineCap.Round);
            context.DrawLine(pen, new Point(32, 32), new Point(32, 14));
            context.DrawLine(pen, new Point(32, 32), new Point(44, 38));
        }
        return new WindowIcon(bitmap);
    }
    public TrayIcons Icons => new() { icon };
    public void Dispose() { model.PropertyChanged -= Changed; icon.IsVisible = false; icon.Dispose(); }
}
