using Avalonia;

namespace Timer.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "migration-startup-error.log"), e.ExceptionObject.ToString()); }
            catch { /* Preserve the original failure if the directory is not writable. */ }
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}
