using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class VisualStateTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static Color Brush(Window window, string key) => ((ISolidColorBrush)(window.TryFindResource(key, window.ActualThemeVariant, out var v) ? v! : throw new Exception("missing " + key))).Color;

    [AvaloniaFact]
    public void DigitsHaveTheSameWidthWhateverTheNumbers()
    {
        var narrow = new TextBlock { Text = "11:11:11", FontSize = 40 }; var wide = new TextBlock { Text = "00:00:00", FontSize = 40 };
        var window = new Window { Content = new StackPanel { Children = { narrow, wide } } }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Assert.Equal(wide.Bounds.Width, narrow.Bounds.Width, 3);
            // Control: with the feature switched off the digits differ, so the check above is not passing by accident.
            narrow.FontFeatures = new FontFeatureCollection(); wide.FontFeatures = new FontFeatureCollection();
            narrow.Measure(Size.Infinity); wide.Measure(Size.Infinity);
            Assert.NotEqual(wide.DesiredSize.Width, narrow.DesiredSize.Width, 3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BreakTurnsOnlyThePillAmberAndBackAgain()
    {
        var now = Day.AddHours(10); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var dashboard = new DashboardWindow(model); dashboard.Show(); var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); var normal = Brush(pill, "Surface");
            model.ToggleAccount("Lunch"); Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsOnBreak); Assert.NotEqual(normal, Brush(pill, "Surface"));
            Assert.Equal(normal, Brush(dashboard, "Surface")); // The dashboard keeps its own colours; its Break button turns amber instead.
            Assert.Equal(Color.Parse("#B07A12"), Brush(pill, "Break")); // The break colour is a theme token, not hard-coded.
            model.ToggleAccount("Lunch"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(normal, Brush(pill, "Surface"));
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void OvertimeTurnsOnlyThePillRedAndABreakOutranksIt()
    {
        var now = Day.AddHours(10); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var dashboard = new DashboardWindow(model); dashboard.Show(); var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); var normal = Brush(dashboard, "Surface"); Assert.Equal("Normal", model.ThemeState);
            Assert.Equal(normal, Brush(pill, "Surface"));
            now = Day.AddHours(18); model.Tick(); model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Overtime", model.ThemeState);
            var red = Brush(pill, "Surface"); Assert.NotEqual(normal, red);
            Assert.Equal(normal, Brush(dashboard, "Surface")); // The dashboard stays calm.
            model.ToggleAccount("Lunch"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Break", model.ThemeState);
            Assert.NotEqual(red, Brush(pill, "Surface")); Assert.NotEqual(normal, Brush(pill, "Surface"));
            Assert.Equal(normal, Brush(dashboard, "Surface")); // A break tints the pill only as well.
            model.TogglePause(); model.TogglePause(); Dispatcher.UIThread.RunJobs(); // Back on the account, then paused: still in overtime, not tracking.
            Assert.Equal("Overtime", model.ThemeState); Assert.Equal(red, Brush(pill, "Surface")); Assert.Equal(normal, Brush(dashboard, "Surface"));
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void PillShowsDayProgressGlowsForItsStatesAndPulsesItsDot()
    {
        var now = Day.AddHours(13); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var host = pill.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ShadowHost");
            var bar = pill.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "DayProgress");
            Assert.True(bar.IsVisible); Assert.Equal(.5, bar.ColumnDefinitions[0].Width.Value, 2); // 13:00 of a 09:00 to 17:00 day.
            Assert.NotEqual(default, host.BoxShadow); // Not tracking glows in the idle colour.
            Assert.Equal(Brush(pill, "Idle").R, host.BoxShadow[0].Color.R);
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            var dot = pill.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "●");
            Assert.True(Ui.IsPulsing(dot)); Assert.Equal(0, host.BoxShadow[0].OffsetX); Assert.Equal(6, host.BoxShadow[0].OffsetY); // Plain drop shadow.
            model.SetShadows(false); Dispatcher.UIThread.RunJobs();
            Assert.Equal(default, host.BoxShadow);
            now = Day.AddHours(18); model.Tick(); model.SetShadows(true); Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsOvertime); Assert.Equal(Brush(pill, "Overtime").R, host.BoxShadow[0].Color.R);
            Assert.Equal(1.0, bar.ColumnDefinitions[0].Width.Value, 2);
            model.ToggleAccount("Lunch"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brush(pill, "Break").R, host.BoxShadow[0].Color.R); // Break outranks overtime.
            model.TogglePause(); Dispatcher.UIThread.RunJobs(); // Resume ends the break and returns to the account.
            Assert.False(model.IsOnBreak); Assert.Equal("Account one", model.ActiveAccount);
            model.TogglePause(); Dispatcher.UIThread.RunJobs();
            Assert.False(Ui.IsPulsing(dot));
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ExpandedPillKeepsSettingsAndDayButtonsSideBySideAlongTheBottom()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)));
        var pill = new PillWindow(model); pill.Show();
        try
        {
            pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var settings = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings");
            var day = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillDay");
            Assert.Equal(settings.TranslatePoint(default, pill)!.Value.Y, day.TranslatePoint(default, pill)!.Value.Y, 1);
            Assert.True(day.TranslatePoint(default, pill)!.Value.X > settings.TranslatePoint(new Point(settings.Bounds.Width, 0), pill)!.Value.X - 1);
            Assert.InRange(settings.Bounds.Width, 150, 230); Assert.Equal(settings.Bounds.Width, day.Bounds.Width, 1);
            Assert.True(day.TranslatePoint(new Point(0, day.Bounds.Height), pill)!.Value.Y <= pill.Height - Ui.ShadowMargin); // Inside the card, not clipped.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void NeitherWindowHasACloseButtonAndCloseTimerSitsAlone()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)));
        foreach (Window window in new Window[] { new PillWindow(model), new DashboardWindow(model) })
        {
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "×");
                var items = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).ToList();
                Assert.Equal("Close timer", ((MenuItem)items[^1]).Header); Assert.IsType<Separator>(items[^2]);
            }
            finally { window.Close(); }
        }
    }

    [Fact]
    public void ShadowChoiceSurvivesARestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-shadow-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.True(prefs.Shadows);
                store.Save(engine, prefs with { Shadows = false, Theme = "Dark" });
            }
            using (var store = new TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine()); Assert.False(prefs.Shadows); Assert.Equal("Dark", prefs.Theme);
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
