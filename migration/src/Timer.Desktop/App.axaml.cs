using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Timer.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // This shell only uses synthetic in-memory data. Storage is a later migration stage.
            var model = new PreviewModel();
            var windows = new WindowCoordinator(model, () => desktop.Shutdown());
            desktop.MainWindow = windows.Pill;
            var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            ticker.Tick += (_, _) => model.Tick();
            ticker.Start();
            desktop.Exit += (_, _) => ticker.Stop();
            if (desktop.Args?.Contains("--smoke-test") == true)
            {
                // Exercise the actual published native executable, without a data directory.
                var phase = 0;
                var smoke = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                smoke.Tick += (_, _) =>
                {
                    if (phase == 0)
                    {
                        if (!windows.Pill.IsVisible || windows.Pill.ClientSize.Width <= 0) { smoke.Stop(); desktop.Shutdown(1); return; }
                        windows.Pill.OpenDashboard!();
                    }
                    else if (phase == 1)
                    {
                        if (!windows.Dashboard.IsVisible || windows.Dashboard.ClientSize.Width > 960) { smoke.Stop(); desktop.Shutdown(1); return; }
                        windows.Dashboard.OpenPill!();
                    }
                    else { smoke.Stop(); desktop.Shutdown(windows.Pill.IsVisible ? 0 : 1); }
                    phase++;
                };
                windows.Pill.Opened += (_, _) => { if (phase == 0) smoke.Start(); };
                desktop.Exit += (_, _) => smoke.Stop();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
