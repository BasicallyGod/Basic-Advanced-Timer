using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PolishTests
{
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Plain() => new(new TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0)));

    [AvaloniaFact]
    public void DeveloperFeaturesAreHiddenUntilTenQuickClicksOnAbout()
    {
        var model = Plain(); var window = new DashboardWindow(model); window.Show();
        try
        {
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            Assert.False(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); Settle(window);
            Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible); // No sample-data demo yet.
            window.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "SettingsPage").Offset = new Vector(0, 5000); Settle(window); // About is at the foot of the page.
            var about = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "AboutSection");
            var point = about.TranslatePoint(new Point(20, 20), window)!.Value;
            for (var i = 0; i < SettingsView.UnlockClicks - 1; i++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
            Assert.False(model.DeveloperUnlocked); // Nine is not enough.
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(model.DeveloperUnlocked);
            Assert.True(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible);
            window.ContextMenu!.Close(); window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            Assert.True(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible); // The tester is for the demo only.
            // The switch in the Development section hides them again.
            model.SetDeveloper(false); Dispatcher.UIThread.RunJobs();
            Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADemoAlwaysShowsTheDeveloperMenuItems()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            Assert.True(menu.Single(m => m.Name == "TestOvertime").IsVisible); Assert.True(menu.Single(m => m.Name == "BuildVersion").IsVisible);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ThePrivateSettingSurvivesSavingAndLoading()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-dev-" + Guid.NewGuid());
        try
        {
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                Assert.False(prefs.Developer);
                store.Save(engine, prefs with { Developer = true, Tooltips = false });
            }
            using (var store = new Timer.Storage.TimerStore(folder))
            { var prefs = store.Load(new TrackingEngine()); Assert.True(prefs.Developer); Assert.False(prefs.Tooltips); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void BudgetsReadAsCompactFiguresWithTheirPeriodAndTodayIsLabelled()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var lines = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t is not null).ToList();
            Assert.Contains(lines, t => t!.EndsWith(" / 20h monthly")); Assert.Contains(lines, t => t!.EndsWith(" weekly")); Assert.Contains(lines, t => t!.EndsWith(" quarterly"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.Contains(" of ") && x.Contains("h ") && t.Classes.Contains("muted") && x.Contains("m") && x.Length > 0 && x.Contains("00m"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "TodayHeading" && t.Text == "Today");
            Assert.Equal("10h35", model.FormatCompact(TimeSpan.FromMinutes(635))); Assert.Equal("20h", model.FormatCompact(TimeSpan.FromHours(20)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheStatusDotAndTheAccountDotShareACentreLine()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("dayCard"));
            var dots = card.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().ToList();
            Assert.Equal(2, dots.Count);
            double Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.Equal(Centre(dots[0]), Centre(dots[1]), 0.6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OnABreakThePillsSecondLineSaysSomethingNew()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var pill = new PillWindow(model); pill.Show();
        try
        {
            if (model.IsTracking) model.TogglePause();
            model.ToggleBreak(); Settle(pill);
            var texts = pill.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
            Assert.Equal(1, texts.Count(t => t!.Contains("On break", StringComparison.OrdinalIgnoreCase) || t == "On a break"));
            Assert.Contains(texts, t => t!.StartsWith("Since ") || t!.StartsWith("Returns to "));
            Assert.Contains("onBreak", pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillBreak").Classes);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void EveryAccountRowsPlayCircleSitsInTheSameColumn()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var xs = window.GetVisualDescendants().OfType<AccountIndicator>().Select(i => Math.Round(i.TranslatePoint(default, window)!.Value.X, 1)).Distinct().ToList();
            Assert.Single(xs); // A long name or a wide percentage never pushes a row's button aside.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheZoomBarsIconsAndSliderAreAllCentredOnItsMiddle()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineControls");
            double Mid(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
            var thumb = bar.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single(); var track = bar.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Track>().Single();
            Assert.Equal(Mid(bar), Mid(thumb), 0.6); Assert.Equal(Mid(bar), Mid(track), 0.6);
            foreach (var icon in bar.GetVisualDescendants().OfType<IconGlyph>()) Assert.Equal(Mid(bar), Mid(icon), 0.6);
        }
        finally { window.Close(); }
    }
}
