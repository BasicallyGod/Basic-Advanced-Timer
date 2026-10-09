using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;
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
    [Fact]
    public void SwitchingAccountsPreservesElapsedAndFinishStopsRecording()
    {
        var now = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        var model = new PreviewModel(() => now);
        model.ToggleAccount("Account one");
        now += TimeSpan.FromMinutes(2);
        model.ToggleAccount("Account two");
        Assert.Equal(TimeSpan.FromMinutes(107), model.Today(model.Accounts[0]));
        now += TimeSpan.FromMinutes(3);
        model.ToggleDay();
        Assert.False(model.IsTracking);
        Assert.False(model.DayStarted);
        Assert.Equal(TimeSpan.FromMinutes(93), model.Today(model.Accounts[1]));
    }

    [AvaloniaFact]
    public void IdleAndOvertimeUseIndependentThemeResources()
    {
        var model = new PreviewModel();
        model.ToggleOvertime();
        using var window = new TestWindow(new TrackingLabels(model));
        window.Show();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            model.SetTheme(theme);
            Dispatcher.UIThread.RunJobs();
            var labels = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var idle = labels.Single(x => x.Text == "● Not tracking");
            var overtime = labels.Single(x => x.Text == "Overtime");
            var idleColour = ((ISolidColorBrush)idle.Foreground!).Color;
            Assert.InRange(Math.Abs(idleColour.R - idleColour.G), 0, 15);
            Assert.NotEqual(idle.Foreground, overtime.Foreground);
            Assert.True(overtime.IsVisible);
            Assert.Equal(theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
        }
        model.SetTheme("System");
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void RealAccountRowsRespondAtTheirDrawnPositions()
    {
        var model = new PreviewModel();
        var window = new DashboardWindow(model);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < model.Accounts.Count; i++)
            {
                var row = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account" + i);
                var point = row.TranslatePoint(new Point(row.Bounds.Width - 8, row.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.Equal(model.Accounts[i].Name, model.ActiveAccount);
                Assert.InRange(row.Bounds.Width, 280, 300);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ViewSwitchesPreserveTheActiveTimer()
    {
        var model = new PreviewModel();
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            model.ToggleAccount("Account one");
            coordinator.Pill.OpenDashboard!();
            Assert.True(coordinator.Dashboard.IsVisible);
            Assert.False(coordinator.Pill.IsVisible);
            Assert.InRange(coordinator.Dashboard.Width, 1, 960);
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
        var model = new PreviewModel();
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
                dashboard.SelectTab(true);
                Save(dashboard, Path.Combine(output, $"dashboard-week-{theme}.png"));
                Save(pill, Path.Combine(output, $"pill-{theme}.png"));
            }
            model.ToggleFormat();
            var texts = dashboard.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(texts, t => t is not null && t.Contains("Today allocated") && t.Contains("4.50h"));
            Assert.Contains(texts, t => t is not null && t.Contains("Unallocated") && t.Contains("0.25h"));
            Assert.Contains(texts, t => t is not null && t.Contains("9.00h of 15.00h"));
            model.ToggleAccount("Account three");
            pill.ToggleExpansion();
            Save(pill, Path.Combine(output, "pill-expanded-Dark.png"));
            Assert.Equal(960, dashboard.Width);
            Assert.Equal(640, dashboard.Height);
        }
        finally { dashboard.Close(); pill.Close(); model.SetTheme("System"); }
    }

    private static void Save(Window window, string path)
    {
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
