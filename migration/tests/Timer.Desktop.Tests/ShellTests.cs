using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;
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
    public void SwitchingAccountsPreservesElapsedAndFinishStopsRecording()
    {
        var now = new DateTime(2026, 10, 9, 9, 0, 0);
        var model = new TimerModel(new TrackingEngine(clock: () => now));
        model.ToggleAccount("Account one");
        now += TimeSpan.FromMinutes(2);
        model.ToggleAccount("Account two");
        Assert.Equal(TimeSpan.FromMinutes(2), model.Today(model.Accounts[0]));
        now += TimeSpan.FromMinutes(3);
        model.ToggleDay();
        Assert.False(model.IsTracking);
        Assert.False(model.DayStarted);
        Assert.Equal(TimeSpan.FromMinutes(3), model.Today(model.Accounts[1]));
    }

    [AvaloniaFact]
    public void IdleAndOvertimeUseIndependentThemeResources()
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
            var idle = labels.Single(x => x.Text == "● Not tracking");
            var overtime = labels.Single(x => x.Text is not null && x.Text.StartsWith("Overtime"));
            Assert.NotEqual(idle.Foreground, overtime.Foreground);
            var overtimeColour = ((ISolidColorBrush)overtime.Foreground!).Color;
            Assert.True(overtimeColour.R > overtimeColour.G + 30);
            Assert.True(overtime.IsVisible);
            Assert.Equal(theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
        }
        model.SetTheme("System");
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
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
            for (var i = 0; i < model.Accounts.Count - 1; i++) // The break has its own button, not a row.
            {
                var row = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account" + i);
                var point = row.TranslatePoint(new Point(row.Bounds.Width - 8, row.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.True(model.Accounts[i].Name == model.ActiveAccount,
                    $"Row {i}: bounds {row.Bounds}, point {point}, hit {window.GetVisualAt(point)?.GetType().Name}, active {model.ActiveAccount ?? "none"}");
                Assert.InRange(row.Bounds.Width, 280, 300);
            }
            model.TogglePause(); // Nothing recording, so the scrolled click below starts an account.
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "AccountScroll");
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
            Assert.InRange(coordinator.Dashboard.Width, 1, 960 + 2 * Ui.ShadowMargin);
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
                Assert.True(dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll").Offset.Y > 1000);
                var nine = dashboard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "09:00");
                var ninePoint = nine.TranslatePoint(new Point(0, 0), dashboard)!.Value;
                Assert.InRange(ninePoint.Y, 250, 400);
                dashboard.SelectTab(true);
                Save(dashboard, Path.Combine(output, $"dashboard-week-{theme}.png"));
                Save(pill, Path.Combine(output, $"pill-{theme}.png"));
            }
            model.ToggleFormat();
            var texts = dashboard.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(texts, t => t is not null && t.Contains("Today allocated") && t.Contains("4.50h"));
            Assert.Contains(texts, t => t is not null && t.Contains("Unallocated") && t.Contains("0.25h"));
            Assert.Contains(texts, t => t is not null && t.Contains("9.00h / 15.00h"));
            model.ToggleAccount("Account three");
            pill.ToggleExpansion();
            Save(pill, Path.Combine(output, "pill-expanded-Dark.png"));
            Assert.Equal(960 + 2 * Ui.ShadowMargin, dashboard.Width);
            Assert.Equal(640 + 2 * Ui.ShadowMargin, dashboard.Height);
        }
        finally { dashboard.Close(); pill.Close(); model.SetTheme("System"); }
    }

    private static TimerModel Fixture()
    {
        var now = new DateTime(2026, 10, 9, 14, 15, 0);
        var engine = new TrackingEngine(clock: () => now);
        var prefs = new StoreSettings(new() { new("Account one", 15), new("Account two", 10), new("Account three", 5), new("Internal") }, false, "System", DayOfWeek.Monday);
        var names = new[] { "Account one", "Account two", "Lunch", "Account three", "Internal" };
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
            Assert.Empty(window.GetVisualDescendants().OfType<ComboBox>());
            Assert.NotNull(window.ContextMenu);
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings");
            var appearance = ((IEnumerable<Control>)button.ContextMenu!.ItemsSource!).OfType<MenuItem>().Single(m => (string?)m.Header == "Appearance");
            var dark = ((IEnumerable<MenuItem>)appearance.ItemsSource!).Single(item => (string?)item.Header == "Dark");
            var elapsed = model.Elapsed;
            dark.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("Dark", model.Theme);
            Assert.Equal(elapsed, model.Elapsed);
        }
        finally { window.Close(); model.SetTheme("System"); }
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
                var header = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "DragHeader");
                Assert.NotNull(header.Background);
                Assert.True(Ui.IsDragSurface(header));
                var label = header.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.Equal(window is DashboardWindow, Ui.IsDragSurface(label));
                var button = header.GetVisualDescendants().OfType<Button>().First();
                Assert.False(Ui.IsDragSurface(button));
                Assert.All(button.GetVisualDescendants(), child => Assert.False(Ui.IsDragSurface(child)));
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
            Assert.InRange(nine.TranslatePoint(default, coordinator.Dashboard)!.Value.Y, 250, 400);
            model.ToggleAccount("Account one");
            Save(coordinator.Dashboard, Path.Combine(output, "demo-tracking.png"));
            Assert.Contains(coordinator.Dashboard.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("live") && t.Text == model.ClockText);
            model.ForceOvertime(); Assert.True(model.IsOvertime);
            Save(coordinator.Dashboard, Path.Combine(output, "demo-overtime.png"));
            coordinator.Dashboard.OpenPill!(); coordinator.Pill.ToggleExpansion();
            Save(coordinator.Pill, Path.Combine(output, "demo-pill-overtime.png"));
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void EditedShiftsSurviveRestartAndControlDayClockAndOvertime()
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
                Assert.Equal(TimeSpan.FromHours(2), model.Elapsed); Assert.True(model.IsOvertime);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); store.Load(engine);
                Assert.Equal(TimeSpan.FromHours(10), engine.Plan(now).Start.TimeOfDay);
                Assert.Equal(TimeSpan.FromHours(11), engine.Plan(now).End.TimeOfDay);
                Assert.Single(engine.Schedule.Days); Assert.Equal(TimeSpan.FromHours(1), engine.Totals(now).Overtime);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void PillAccountAndTimerClicksOpenTheirRespectiveViews()
    {
        var model = Fixture();
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            model.ToggleAccount("Account one");
            Dispatcher.UIThread.RunJobs();
            using var frame = coordinator.Pill.CaptureRenderedFrame();
            void Click(string name)
            {
                var button = coordinator.Pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), coordinator.Pill)!.Value;
                coordinator.Pill.MouseDown(point, MouseButton.Left);
                coordinator.Pill.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            Click("PillAccounts");
            Assert.True(coordinator.Pill.Height > PillWindow.CollapsedWindowHeight);
            Assert.False(coordinator.Dashboard.IsVisible);
            Click("PillAccounts");
            Assert.Equal(PillWindow.CollapsedWindowHeight, coordinator.Pill.Height);
            Click("PillDashboard");
            Assert.True(coordinator.Dashboard.IsVisible);
            Assert.False(coordinator.Pill.IsVisible);
            Assert.Equal("Account one", model.ActiveAccount);
            coordinator.Dashboard.OpenPill!();
            Assert.Equal(PillWindow.CollapsedWindowHeight, coordinator.Pill.Height);
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void DraggingEachPillTargetSuppressesClicksAndPauseResumesTheSelectedAccount()
    {
        var model = Fixture();
        var coordinator = new WindowCoordinator(model, () => { }); coordinator.Pill.Show();
        try
        {
            model.ToggleAccount("Account one");
            Dispatcher.UIThread.RunJobs(); using var initial = coordinator.Pill.CaptureRenderedFrame();
            foreach (var name in new[] { "PillDashboard", "PillAccounts", "PillPause" })
            {
                var button = coordinator.Pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), coordinator.Pill)!.Value;
                coordinator.Pill.MouseDown(point, MouseButton.Left);
                coordinator.Pill.MouseMove(point + new Vector(20, 10));
                coordinator.Pill.MouseUp(point + new Vector(20, 10), MouseButton.Left);
                Assert.False(coordinator.Dashboard.IsVisible);
                Assert.Equal(PillWindow.CollapsedWindowHeight, coordinator.Pill.Height);
                Assert.Equal("Account one", model.ActiveAccount);
            }
            var pause = coordinator.Pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillPause");
            var pausePoint = pause.TranslatePoint(new Point(18, 18), coordinator.Pill)!.Value;
            coordinator.Pill.MouseDown(pausePoint, MouseButton.Left); coordinator.Pill.MouseUp(pausePoint, MouseButton.Left);
            Assert.False(model.IsTracking); Assert.Equal("Account one", model.SelectedAccount);
            coordinator.Pill.MouseDown(pausePoint, MouseButton.Left); coordinator.Pill.MouseUp(pausePoint, MouseButton.Left);
            Assert.Equal("Account one", model.ActiveAccount);
            var timerButton = coordinator.Pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillDashboard");
            var accountButton = coordinator.Pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillAccounts");
            Assert.True(timerButton.Bounds.X < accountButton.Bounds.X && accountButton.Bounds.X < pause.Bounds.X);
            var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath();
            Save(coordinator.Pill, Path.Combine(output, "pill-classic-tracking.png"));
            model.TogglePause(); Save(coordinator.Pill, Path.Combine(output, "pill-classic-paused.png"));
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
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
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "New client");
                    var editor = new AccountsWindow(model); editor.Show(window);
                    var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath();
                    Save(editor, Path.Combine(output, "accounts-editor.png")); editor.Close();
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
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame!.PixelSize.Width > 0);
        frame.Save(path);
    }

    private sealed class TestWindow(Control control) : Window, IDisposable
    {
        public TestWindow() : this(new Border()) { }
        public void Dispose() => Close();
        protected override void OnOpened(EventArgs e) { Content = control; base.OnOpened(e); }
    }
}
