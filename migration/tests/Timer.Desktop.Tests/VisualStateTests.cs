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
            model.ToggleAccount("Break"); Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsOnBreak); Assert.NotEqual(normal, Brush(pill, "Surface"));
            Assert.Equal(normal, Brush(dashboard, "Surface")); // The dashboard keeps its own colours; its Break button turns amber instead.
            Assert.Equal(Color.Parse("#B07A12"), Brush(pill, "Break")); // The break colour is a theme token, not hard-coded.
            model.ToggleAccount("Break"); Dispatcher.UIThread.RunJobs();
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
            model.ToggleAccount("Break"); Dispatcher.UIThread.RunJobs();
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
            Assert.NotEmpty(host.Transitions!); // The shadow fades between states, so the edge does not jump when play or pause is pressed.
            host.Transitions = null; // Take the fade out here, so each state can be read as soon as it is set.
            var bar = pill.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "DayProgress");
            Assert.True(bar.IsVisible); Assert.Equal(.5, bar.ColumnDefinitions[0].Width.Value, 2); // 13:00 of a 09:00 to 17:00 day.
            Assert.NotEqual(default, host.BoxShadow); // Not tracking keeps the plain drop shadow, with no glow.
            Assert.Equal(6, host.BoxShadow[0].OffsetY);
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            var dot = pill.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "●");
            Assert.True(Ui.IsPulsing(dot)); Assert.Equal(Brush(pill, "Live").R, host.BoxShadow[0].Color.R); Assert.Equal(0, host.BoxShadow[0].OffsetY); // A recording account gives the pill a glow in the live colour.
            model.SetShadows(false); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brush(pill, "Live").R, host.BoxShadow[0].Color.R); // The glow is a signal, so it stays when plain shadows are off.
            model.TogglePause(); Dispatcher.UIThread.RunJobs(); // Paused: no glow, and no shadow either.
            Assert.Equal(default, host.BoxShadow);
            model.TogglePause(); Dispatcher.UIThread.RunJobs();
            now = Day.AddHours(18); model.Tick(); model.SetShadows(true); Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsOvertime); Assert.Equal(Brush(pill, "Overtime").R, host.BoxShadow[0].Color.R);
            Assert.Equal(1.0, bar.ColumnDefinitions[0].Width.Value, 2);
            model.ToggleAccount("Break"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brush(pill, "Break").R, host.BoxShadow[0].Color.R); // Break outranks overtime.
            model.TogglePause(); Dispatcher.UIThread.RunJobs(); // Resume ends the break and returns to the account.
            Assert.False(model.IsOnBreak); Assert.Equal("Account one", model.ActiveAccount);
            model.TogglePause(); Dispatcher.UIThread.RunJobs();
            Assert.False(Ui.IsPulsing(dot));
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ExpandedPillEndsWithOneProminentDayButtonAndNoDeadSpace()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)));
        var pill = new PillWindow(model); pill.Show();
        try
        {
            pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(pill.GetVisualDescendants().OfType<Button>(), b => b.Name == "Settings"); // Settings are on the right-click menu.
            Assert.NotNull(pill.ContextMenu);
            var day = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillDay");
            Assert.Contains("finishDay", day.Classes); Assert.InRange(day.Bounds.Width, 350, 420); // 10:00 on a working day: finishing is red.
            Assert.DoesNotContain(pill.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(), b => b.IsVisible); // Nothing to scroll, so no scroll bar.
            var bottom = day.TranslatePoint(new Point(0, day.Bounds.Height), pill)!.Value.Y;
            var cardBottom = pill.Height - Ui.ShadowMargin;
            Assert.InRange(cardBottom - bottom, 0, 24); // Only the button's own margin below it, however many accounts there are.
            var accounts = model.Accounts.Count - 1;
            pill.ToggleExpansion(); pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs();
            Assert.InRange(pill.Height - 2 * Ui.ShadowMargin, PillWindow.CollapsedHeight + accounts * 52, PillWindow.CollapsedHeight + accounts * 52 + 130);
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

    [AvaloniaFact]
    public void FinishDayIsRedAndStartDayIsGreenInBothWindows()
    {
        var now = Day.AddHours(6); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var pill = new PillWindow(model); pill.Show(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var pillDay = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillDay");
            var dashDay = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardDay");
            Assert.Equal("Start day", pillDay.Content); Assert.Contains("primary", pillDay.Classes); Assert.DoesNotContain("finishDay", pillDay.Classes);
            Assert.DoesNotContain("finishDay", dashDay.Classes);
            now = Day.AddHours(10); model.ChangeDate(0); Dispatcher.UIThread.RunJobs(); // The scheduled start has passed, so the day is running.
            Assert.Equal("Finish day", pillDay.Content); Assert.Contains("finishDay", pillDay.Classes); Assert.DoesNotContain("primary", pillDay.Classes);
            Assert.Contains("finishDay", dashDay.Classes); Assert.Contains("outlined", dashDay.Classes);
            Assert.Equal(Brush(pill, "Finish"), Color.Parse("#B83B3B")); // The colour is the theme's Finish entry, not hard-coded.
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void UndoButtonStaysPlainAndTheDashboardHasNoSecondClock()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)));
        model.Engine.Sessions.Add(new("2026-10-09", "Account one", "09:00:00", "added", "30"));
        var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            model.Editor.Edit(model.Engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(9.25), "note"); model.ChangeDate(0); Dispatcher.UIThread.RunJobs();
            var undo = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            Assert.Equal("Undo", undo.Content); Assert.Contains("available", ToolTip.GetTip(undo)!.ToString());
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { Length: 8 } x && x[2] == ':' && x[5] == ':' && x != dashboard.GetVisualDescendants().OfType<TextBlock>().First(c => c.FontSize == 40).Text);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheDayProgressLineIsAlwaysShownEvenOnADayOff()
    {
        var now = Day.AddDays(1).AddHours(10); // The Saturday: not a working day.
        var model = new TimerModel(new TrackingEngine(clock: () => now));
        Assert.Null(model.DayProgress);
        var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var bar = pill.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "DayProgress");
            Assert.True(bar.IsVisible); Assert.True(bar.Bounds.Height >= 3); // The row keeps its shape, with an empty track.
            Assert.Equal(0.0, bar.ColumnDefinitions[0].Width.Value); Assert.Contains("No working day", ToolTip.GetTip(bar)!.ToString());
            now = Day.AddDays(3).AddHours(13); model.ChangeDate(0); Dispatcher.UIThread.RunJobs(); // Monday, halfway through the day.
            Assert.True(bar.IsVisible); Assert.Equal(.5, bar.ColumnDefinitions[0].Width.Value, 2);
        }
        finally { pill.Close(); }
    }
}
