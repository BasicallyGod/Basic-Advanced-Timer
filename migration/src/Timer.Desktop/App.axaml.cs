using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Controls;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Colours are registered before any window exists, so every control finds them the first time it looks.
        ThemeEngine.Register(this, AppThemes.BuiltIn);
        // Headings and big figures share the interface typeface (Inter), set heavier, so the window reads as one face. The three keys stay so the classes keep working.
        var inter = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        Resources["ClockFont"] = inter; Resources["FiguresFont"] = inter; Resources["ClockLightFont"] = inter;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var lifetimeEnded = false;
            desktop.Exit += (_, _) => lifetimeEnded = true;
            var smokeMode = desktop.Args?.Contains("--smoke-test") == true;
            TimerStore? store = null;
            TimerModel model;
            try
            {
                using var loadTiming = PerformanceTimings.Measure("startup data and model");
                var args = desktop.Args ?? Array.Empty<string>();
                var index = Array.IndexOf(args, "--data-dir");
                if (args.Contains("--demo") && !BuildInfo.DeveloperFeatures) throw new ArgumentException("Sample-data demo is not available in Store releases.");
                var demoMode = args.Contains("--demo");
                if (demoMode && index >= 0) throw new ArgumentException("Demo mode cannot open a data folder.");
                if (index >= 0 && index + 1 >= args.Length) throw new ArgumentException("--data-dir requires a folder path.");
                var folder = smokeMode ? Path.Combine(Path.GetTempPath(), "timer-smoke-" + Guid.NewGuid())
                    : index >= 0 ? args[index + 1] : TimerDataFolders.DefaultDirectory(
                        AppContext.BaseDirectory,
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        PackageEnvironment.IsPackaged());
                if (demoMode)
                {
                    // The demo runs on a throwaway database in the temporary folder, removed when it closes.
                    folder = Path.Combine(Path.GetTempPath(), "timer-demo-" + Guid.NewGuid());
                    store = new TimerStore(folder);
                    model = TimerModel.CreateDemo(store: store);
                }
                else
                {
                store = new TimerStore(folder);
                var engine = new TrackingEngine();
                var preferences = store.Load(engine);
                model = new TimerModel(engine, preferences, store);
                if (store.AdjustmentsRemoved > 0)
                    model.Notice = $"{store.AdjustmentsRemoved} time adjustment{(store.AdjustmentsRemoved == 1 ? "" : "s")} removed · a copy is in Backups";
                else if (store.TakeOldFilesNotice()) model.Notice = "Old timesheet files left alone · starting empty";
                if (!model.Save()) throw new IOException(model.LastError);
                // Only an explicit data folder is remembered; the default one sits beside the program and needs no argument.
                // A test build started by the updater is offered the setting only when the updater says where to find itself, so the
                // sign-in entry survives updates. A bare --test-build run still leaves the startup entry alone.
                var scriptIndex = Array.IndexOf(args, "--updater-script");
                var updaterScript = scriptIndex >= 0 && scriptIndex + 1 < args.Length ? AutoStartSetting.AcceptUpdaterScript(args[scriptIndex + 1]) : null;
                // The Store package starts through its manifest's startup task; everything else uses the sign-in registry entry.
                if (!smokeMode && !args.Contains("--test-build") && PackageEnvironment.IsPackaged())
                    model.AutoStart = PackagedStartup.TryCreate();
                else if (!smokeMode && (!args.Contains("--test-build") || updaterScript is not null) && WindowsRunKey.Available && !PackageEnvironment.IsPackaged())
                    model.AutoStart = new AutoStartSetting(new WindowsRunKey(), Environment.ProcessPath, index >= 0 ? args[index + 1] : null, updaterScript);
                }
                var openedStore = store;
                var released = false;
                desktop.Exit += (_, _) =>
                {
                    if (released) return;
                    released = true;
                    model.MarkClosed();
                    openedStore?.Dispose();
                    if ((smokeMode || demoMode) && Directory.Exists(folder)) Directory.Delete(folder, true);
                };
            }
            catch (Exception error)
            {
                store?.Dispose();
                if (smokeMode) { desktop.Shutdown(1); return; }
                var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
                panel.Children.Add(new TextBlock { Text = "Could not open timer data", FontSize = 20 });
                panel.Children.Add(new TextBlock { Text = error.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                panel.Children.Add(Ui.Button("Close", () => desktop.Shutdown()));
                desktop.MainWindow = new Window { Title = "Basic’s Advanced Timer", Width = 600, Height = 260, Content = panel };
                base.OnFrameworkInitializationCompleted();
                return;
            }
            // Native Closed callbacks can arrive after lifetime Exit; do not request shutdown again.
            var windows = new WindowCoordinator(model, () => { if (!lifetimeEnded) desktop.Shutdown(); });
            desktop.MainWindow = windows.Pill;
            // A new database opens on the dashboard, where the getting started setup is waiting. After that the pill is the usual start.
            if (!model.SetupDone && !smokeMode) windows.OpenDashboardOnFirstShow();
            if (!smokeMode)
            {
                // The tray icon keeps the timer reachable when its windows are hidden, and closing a window hides it there.
                var tray = new TrayController(model, windows);
                TrayIcon.SetIcons(this, tray.Icons);
                model.TrayAvailable = true; windows.HideToTray = true;
                desktop.Exit += (_, _) => tray.Dispose();
            }
            var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            ticker.Tick += (_, _) => model.Tick();
            ticker.Start();
            desktop.Exit += (_, _) => ticker.Stop();
            if (smokeMode)
            {
                // Exercise the native executable and isolated persistent storage.
                var phase = 0;
                var smoke = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                smoke.Tick += (_, _) =>
                {
                    if (phase == 0)
                    {
                        // The database library has to load and work from the published build, or later steps cannot rely on it.
                        try { Timer.Storage.SqliteProbe.Check(); }
                        catch (Exception error) { Console.Error.WriteLine("SQLite check failed: " + error); smoke.Stop(); desktop.Shutdown(1); return; }
                        if (!windows.Pill.IsVisible || windows.Pill.ClientSize.Width <= 0) { smoke.Stop(); desktop.Shutdown(1); return; }
                        windows.Pill.OpenDashboard!();
                    }
                    else if (phase == 1)
                    {
                        if (!windows.Dashboard.IsVisible || windows.Dashboard.ClientSize.Width > DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin) { smoke.Stop(); desktop.Shutdown(1); return; }
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
