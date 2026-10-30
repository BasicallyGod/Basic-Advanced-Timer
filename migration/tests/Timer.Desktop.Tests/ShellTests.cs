using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Timer.Desktop.Tests.TestAppBuilder))]

namespace Timer.Desktop.Tests;

public static class TestAppBuilder
{
    // Load production styles, fonts and windows, rather than a corrected test layout.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().WithInterFont().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class ShellTests
{
    [AvaloniaFact]
    public void SwitchingAccountsKeepsEachAccountsTime()
    {
        var now = new DateTime(2026, 10, 9, 9, 0, 0);
        var model = new TimerModel(new TrackingEngine(clock: () => now));
        model.ToggleAccount("Account one");
        now += TimeSpan.FromMinutes(2);
        model.ToggleAccount("Account two");
        Assert.Equal(TimeSpan.FromMinutes(2), model.Today(model.Accounts[0]));
        now += TimeSpan.FromMinutes(3);
        model.TogglePause();
        Assert.False(model.IsTracking);
        Assert.Equal(TimeSpan.FromMinutes(3), model.Today(model.Accounts[1]));
    }

    [AvaloniaFact]
    public void TheIdleStatusFollowsTheThemeAndPastTheEndOfTheDayThereIsNoOvertimeLine()
    {
        var model = Fixture();
        model.Engine.Schedule.Default = new Shift(TimeSpan.FromHours(9), TimeSpan.FromHours(13));
        using var window = new TestWindow(new TrackingLabels(model));
        window.Show();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            model.SetTheme(theme);
            Dispatcher.UIThread.RunJobs();
            using var initialFrame = window.CaptureRenderedFrame();
            var labels = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var idle = labels.Single(x => x.Text == "Ready to track");
            Assert.NotNull(idle.Foreground);
            Assert.DoesNotContain(labels, x => x.Text is not null && x.Text.StartsWith("Overtime")); // The day's hours are over (the shift ends at 13:00), and nothing says so in red.
            Assert.Equal(theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
        }
        model.SetTheme("System");
        Assert.Equal(ThemeEngine.VariantFor(model.CurrentTheme), Application.Current!.RequestedThemeVariant); // System is Light or Dark, by the computer's light or dark.
        Assert.Contains(model.CurrentTheme.Name, new[] { "Light", "Dark" });
    }

    [AvaloniaFact]
    public void RealAccountRowsRespondAtTheirDrawnPositions()
    {
        var model = Fixture();
        var window = new DashboardWindow(model);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout(); // Windows CI can reach the clicks before the first layout pass, leaving every row at 0, 0.
            void Open() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var opened = window.CaptureRenderedFrame(); }
            for (var i = 0; i < model.Accounts.Count - 1; i++) // The break has its own button, not a row.
            {
                Open(); var row = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account" + i);
                var point = row.TranslatePoint(new Point(row.Bounds.Width - 8, row.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); // The pointer arrives over the row before it presses, as a real one does.
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.True(model.Accounts[i].Name == model.ActiveAccount,
                    $"Row {i}: bounds {row.Bounds}, point {point}, hit {window.GetVisualAt(point)?.GetType().Name}, active {model.ActiveAccount ?? "none"}");
                Assert.InRange(row.Bounds.Width, 200, 230);
            }
            model.TogglePause(); // Nothing recording, so the scrolled click below starts an account.
            Open(); var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SidebarAccountScroll");
            scroll.Offset = new Vector(0, 2000);
            Dispatcher.UIThread.RunJobs();
            using var scrolledFrame = window.CaptureRenderedFrame();
            var lower = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account3");
            var lowerPoint = lower.TranslatePoint(new Point(lower.Bounds.Width - 8, lower.Bounds.Height / 2), window)!.Value;
            window.MouseDown(lowerPoint, MouseButton.Left);
            window.MouseUp(lowerPoint, MouseButton.Left);
            Assert.Equal("Internal", model.ActiveAccount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CollapsingTheDashboardLeavesThePillShownAtItsOwnSize()
    {
        var model = Fixture();
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.HideToTray = true;
        coordinator.Pill.Show();
        try
        {
            coordinator.Pill.OpenDashboard!();
            coordinator.Dashboard.OpenPill!();
            Assert.True(coordinator.Pill.IsVisible);
            Assert.Equal(WindowState.Normal, coordinator.Pill.WindowState);
            Assert.InRange(coordinator.Pill.Width, 1, coordinator.Pill.WindowWidthNow + 2);
            Assert.InRange(coordinator.Pill.Height, 1, coordinator.Pill.WindowHeightNow + 2);
            Assert.True(coordinator.IsShown);
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void CollapsingWhileSetupIsPendingDoesNotSendYouBackToTheDashboard()
    {
        var model = Fixture();
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.OpenDashboardOnFirstShow();
        coordinator.Pill.Show();
        try
        {
            Assert.True(coordinator.Dashboard.IsVisible);
            Assert.False(coordinator.Pill.IsVisible);
            coordinator.Dashboard.OpenPill!();
            Assert.True(coordinator.Pill.IsVisible);
            Assert.False(coordinator.Dashboard.IsVisible);
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void ViewSwitchesPreserveTheActiveTimer()
    {
        var model = Fixture();
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            model.ToggleAccount("Account one");
            coordinator.Pill.OpenDashboard!();
            Assert.True(coordinator.Dashboard.IsVisible);
            Assert.False(coordinator.Pill.IsVisible);
            Assert.InRange(coordinator.Dashboard.Width, 1, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin);
            coordinator.Dashboard.OpenPill!();
            Assert.Equal("Account one", model.ActiveAccount);
            Assert.True(coordinator.Pill.IsVisible);
            Assert.False(coordinator.Dashboard.IsVisible);
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void BothViewsRenderWithProductionThemesAndSharedFormats()
    {
        var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.Combine(Path.GetTempPath(), "avalonia-timer-previews");
        Directory.CreateDirectory(output);
        var model = Fixture();
        var dashboard = new DashboardWindow(model);
        var pill = new PillWindow(model);
        dashboard.Show();
        pill.Show();
        try
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                model.SetTheme(theme);
                dashboard.SelectTab(false);
                Save(dashboard, Path.Combine(output, $"dashboard-timeline-{theme}.png"));
                Assert.True(dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll").Offset.X > 500); // Opened part way along the day, on the first block.
                var nine = dashboard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "09:00");
                var ninePoint = nine.TranslatePoint(new Point(0, 0), dashboard)!.Value;
                Assert.InRange(ninePoint.X, 100, 500); Assert.InRange(ninePoint.Y, 100, 400);
                dashboard.SelectTab(true);
                Save(dashboard, Path.Combine(output, $"dashboard-week-{theme}.png"));
                Save(pill, Path.Combine(output, $"pill-{theme}.png"));
            }
            model.ToggleFormat(); dashboard.SelectTab(false); Dispatcher.UIThread.RunJobs();
            var texts = dashboard.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("Allocated", texts); Assert.Contains("4.50h", texts);
            Assert.Contains("Unallocated", texts); Assert.Contains("0.25h", texts);
            // The day's figures sit in one strip under the timeline, and the budgets read in the accounts beside the rail.
            var strip = dashboard.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DayTotals");
            Assert.Contains(strip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "4.50h");
Dispatcher.UIThread.RunJobs(); using var accountsFrame = dashboard.CaptureRenderedFrame();
            texts = dashboard.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            // The budget words are in the row's tooltip now, so the narrow panel keeps the room for the name and the time.
            Assert.Contains(dashboard.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("accountCard")), b => ToolTip.GetTip(b)?.ToString() is { } tip && tip.Contains("9h / 15h")); dashboard.CloseFlyouts();
            model.ToggleAccount("Account three");
            pill.ShowPanel();
            Save(pill, Path.Combine(output, "pill-expanded-Dark.png"));
            Assert.Equal(DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, dashboard.Width);
            Assert.Equal(DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, dashboard.Height);
        }
        finally { dashboard.Close(); pill.Close(); model.SetTheme("System"); }
    }

    private static TimerModel Fixture()
    {
        var now = new DateTime(2026, 10, 9, 14, 15, 0);
        var engine = new TrackingEngine(clock: () => now);
        var prefs = new StoreSettings(new() { new("Account one", 15), new("Account two", 10), new("Account three", 5), new("Internal") }, false, "System", DayOfWeek.Monday);
        var names = new[] { "Account one", "Account two", "Break", "Account three", "Internal" };
        var minutes = new[] { 105, 90, 30, 45, 30 };
        var start = now.Date.AddHours(9);
        for (var i = 0; i < names.Length; i++) { engine.AddSession(names[i], start, start.AddMinutes(minutes[i])); start = start.AddMinutes(minutes[i]); }
        engine.Sessions.Add(new("2026-10-05", "Account one", "09:00:00", "merged", "435"));
        return new TimerModel(engine, prefs);
    }

    [AvaloniaFact]
    public void AppearanceIsAvailableThroughSettingsAndRightClick()
    {
        var model = Fixture();
        var window = new DashboardWindow(model);
        window.Show();
        try
        {
            Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "ThemeDark" or "ThemeLight" or "ThemeSystem")); // Nothing to choose until the settings page is opened.
            Assert.NotNull(window.ContextMenu);
            window.OpenSettings();
            var tiles = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "ThemeSystem" or "ThemeLight" or "ThemeDark").Select(b => b.Name!).ToList();
            Assert.Equal(new[] { "ThemeSystem", "ThemeLight", "ThemeDark" }, tiles); // System, Light and Dark, as three small pictures.
            var elapsed = model.Elapsed;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ThemeDark").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Dark", model.Theme);
            Assert.Equal(elapsed, model.Elapsed);
        }
        finally { window.Close(); model.SetTheme("System"); }
    }

    [AvaloniaFact]
    public void ClosingTheSecondWindowAfterTheStoreIsDisposedDoesNotSaveAgain()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-double-close-" + Guid.NewGuid());
        try
        {
            var store = new TimerStore(folder);
            var engine = new TrackingEngine();
            var model = new TimerModel(engine, store.Load(engine), store);
            Assert.True(model.StopForClose()); // The pill closes and the app shuts down, which disposes the database.
            store.Dispose();
            Assert.True(model.StopForClose()); // The dashboard's close follows and must not touch the closed database.
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void FailedSaveKeepsStoppedTimeAndRetriesWithoutDuplicatingIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-failed-save-" + Guid.NewGuid());
        var now = new DateTime(2026, 10, 9, 9, 0, 0);
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now);
                var prefs = store.Load(engine);
                var model = new TimerModel(engine, prefs, store);
                model.ToggleAccount("Account one");
                now = now.AddMinutes(5);
                StorageFaults.Block(store);
                Assert.False(model.StopForClose());
                Assert.NotNull(model.LastError);
                Assert.Single(engine.Sessions);
                StorageFaults.Unblock(store);
                Assert.True(model.Save());
                Assert.Null(model.LastError);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); store.Load(engine);
                Assert.Single(engine.Sessions);
                Assert.Equal(TimeSpan.FromMinutes(5), engine.Sessions[0].Duration);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void HeadersAcceptBlankAndTextHitsButExcludeButtons()
    {
        var coordinator = new WindowCoordinator(Fixture(), () => { });
        coordinator.Pill.Show(); coordinator.Pill.OpenDashboard!();
        try
        {
            foreach (var window in new Window[] { coordinator.Pill, coordinator.Dashboard })
            {
                window.Show(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                // The pill has one header. The dashboard has one: the empty middle of the header row (the sidebar is dragged by its bare parts).
                var headers = window.GetVisualDescendants().OfType<Panel>().Where(g => g.Name is "DragHeader" or "RailDragHeader").ToList();
                Assert.Single(headers);
                foreach (var header in headers)
                {
                    Assert.NotNull(header.Background); Assert.True(Ui.IsPlainSurface(header));
                    if (header.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !t.GetVisualAncestors().OfType<Button>().Any()) is { } label) Assert.Equal(window is DashboardWindow, Ui.IsPlainSurface(label)); // Text outside a button drags.
                    if (header.GetVisualDescendants().OfType<Button>().FirstOrDefault() is { } button)
                    {
                        Assert.False(Ui.IsPlainSurface(button));
                        Assert.All(button.GetVisualDescendants(), child => Assert.False(Ui.IsPlainSurface(child)));
                    }
                }
            }
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void HiddenDashboardKeepsItsInitialTimelinePositionAndDemoShowsOvertime()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        var model = TimerModel.CreateDemo(() => now);
        Assert.False(model.Persistent);
        Assert.True(model.Allocated > TimeSpan.Zero);
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            for (var i = 0; i < 30; i++) model.Tick();
            Dispatcher.UIThread.RunJobs();
            coordinator.Pill.OpenDashboard!();
            var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath();
            Save(coordinator.Dashboard, Path.Combine(output, "demo-idle.png"));
            var nine = coordinator.Dashboard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "09:00");
            Assert.InRange(nine.TranslatePoint(default, coordinator.Dashboard)!.Value.Y, 100, 400);
            model.ToggleAccount("Account one");
            Save(coordinator.Dashboard, Path.Combine(output, "demo-tracking.png"));
            Assert.Equal("Recording", ToolTip.GetTip(coordinator.Dashboard.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SidebarLights")));
            Save(coordinator.Dashboard, Path.Combine(output, "demo-late.png"));
            coordinator.Dashboard.OpenPill!(); coordinator.Pill.ShowPanel();
            Save(coordinator.Pill, Path.Combine(output, "demo-pill-overtime.png"));
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void EditedShiftsSurviveRestartAndSetHowFarTheDayClockRuns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-shifts-" + Guid.NewGuid());
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now);
                var model = new TimerModel(engine, store.Load(engine), store);
                model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(10), TimeSpan.FromHours(11)) });
                Assert.Equal(TimeSpan.Zero, model.Elapsed); // Nothing is recorded, so the day has not begun, whatever its hours.
                engine.AddSession("Account one", now.Date.AddHours(10), now.Date.AddHours(10.5));
                Assert.Equal(TimeSpan.FromHours(1), model.Elapsed); Assert.Equal(TimeSpan.FromHours(1), model.WorkingOn(now)); // From the first block to the end of the hours: the clock stops there, at eleven, though it is noon.
                engine.AddSession("Account one", now.Date.AddHours(11.5), now.Date.AddHours(12));
                Assert.Equal(TimeSpan.FromHours(2), model.WorkingOn(now)); // Work past the hours is simply working time.
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); store.Load(engine);
                Assert.Equal(TimeSpan.FromHours(10), engine.Plan(now).Start.TimeOfDay);
                Assert.Equal(TimeSpan.FromHours(11), engine.Plan(now).End.TimeOfDay);
                Assert.Single(engine.Schedule.Days);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void AccountEditorUpdatesRowsAndPreservesHiddenCreditAcrossRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-accounts-" + Guid.NewGuid());
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now);
                var model = new TimerModel(engine, store.Load(engine), store);
                var window = new DashboardWindow(model); window.Show();
                try
                {
                    model.ToggleAccount("Account one"); now = now.AddMinutes(5);
                    model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { Hidden = true, BudgetHours = 7.5, BudgetPeriod = "month" } : a).Append(new("New client", 10, "quarter")));
                    Assert.False(model.IsTracking);
                    Assert.DoesNotContain(model.Accounts, a => a.Name == "Account one");
                    Assert.Contains(model.ReviewAccounts, a => a.Name == "Account one");
                    Assert.Equal(TimeSpan.FromMinutes(5), engine.Credited("Account one", now.Date, now.Date.AddDays(1)));
Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "New client"); window.CloseFlyouts();
                    var editor = new AccountsDialog(model).Open((DashboardWindow)window);
                    var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath();
                    Save(window, Path.Combine(output, "accounts-editor.png")); editor.Close();
                    Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings.Append(new("new CLIENT"))));
                }
                finally { window.Close(); }
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); var prefs = store.Load(engine);
                var old = prefs.Accounts.Single(a => a.Name == "Account one");
                Assert.True(old.Hidden); Assert.Equal(7.5, old.BudgetHours); Assert.Equal("month", old.BudgetPeriod);
                var model = new TimerModel(engine, prefs, store);
                model.SetAccounts(model.AccountSettings.Select(a => a with { Hidden = false }));
                Assert.Equal(TimeSpan.FromMinutes(5), model.Today(model.Accounts.Single(a => a.Name == "Account one")));
                Assert.Equal("quarter", prefs.Accounts.Single(a => a.Name == "New client").BudgetPeriod);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void Save(Window window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Dispatcher.UIThread.RunJobs();
        // First frame completes deferred scroll layout; capture the settled production view.
        using var warmup = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        // The preview picture is only for people to look at, so a frame the renderer fails to give is skipped and not a failure.
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) { Console.Error.WriteLine("No preview frame for " + path); return; }
        Assert.True(frame.PixelSize.Width > 0);
        frame.Save(path);
    }

    private sealed class TestWindow(Control control) : Window, IDisposable
    {
        public TestWindow() : this(new Border()) { }
        public void Dispose() => Close();
        protected override void OnOpened(EventArgs e) { Content = control; base.OnOpened(e); }
    }
}
