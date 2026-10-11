using Avalonia;

namespace Timer.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                // Installed MSIX files are read-only, so a packaged build keeps its diagnostics in per-user data. The portable build
                // stays portable and writes beside its program, as before.
                var folder = PackageEnvironment.IsPackaged()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BasicsAdvancedTimer", "Logs")
                    : AppContext.BaseDirectory;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, PackageEnvironment.IsPackaged() ? "startup-error.log" : "migration-startup-error.log"), e.ExceptionObject.ToString());
            }
            catch { /* Preserve the original failure if the directory is not writable. */ }
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}
