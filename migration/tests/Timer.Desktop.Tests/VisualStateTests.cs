using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
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
                Assert.Equal("Collapse to tray", ((MenuItem)items[^1]).Header); Assert.IsType<Separator>(items[^2]);
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
                Assert.True(prefs.TaskbarButton); store.Save(engine, prefs with { Shadows = false, Theme = "Dark", TaskbarButton = false });
            }
            using (var store = new TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine()); Assert.False(prefs.Shadows); Assert.Equal("Dark", prefs.Theme); Assert.False(prefs.TaskbarButton);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void FinishDayIsRedAndStartDayIsGreenOnTheDashboard()
    {
        var now = Day.AddHours(6); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var dashDay = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardDay");
            Assert.Equal("Start", dashDay.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Kind == IconKind.Sunrise).TintKey); Assert.Equal("Finish", dashDay.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Kind == IconKind.Flag).TintKey);
            foreach (var theme in AppThemes.BuiltIn) { var (r, g, b) = Timer.Core.ThemeColours.Rgb(theme.Colour("Start")); Assert.True(g > r + 20 && g > b + 20, theme.Name + " Start day is green"); }
            Assert.DoesNotContain("finishDay", dashDay.Classes);
            now = Day.AddHours(10); model.ChangeDate(0); Dispatcher.UIThread.RunJobs(); // The scheduled start has passed, so the day is running.
            Assert.Contains("finishDay", dashDay.Classes);
            Assert.Equal(Brush(dashboard, "Finish"), Color.Parse(model.CurrentTheme.Colour("Finish"))); // The colour is the theme's Finish entry, not hard-coded.
        }
        finally { dashboard.Close(); }
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
            Assert.Equal("Undo", undo.GetVisualDescendants().OfType<TextBlock>().Single().Text); Assert.Contains("toolButton", undo.Classes); Assert.Contains("available", ToolTip.GetTip(undo)!.ToString());
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { Length: 8 } x && x[2] == ':' && x[5] == ':' && x != dashboard.GetVisualDescendants().OfType<TextBlock>().First(c => c.FontSize == 46 && c.Classes.Contains("clock")).Text);
        }
        finally { dashboard.Close(); }
    }

}
