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
        }
        base.OnFrameworkInitializationCompleted();
    }
}
