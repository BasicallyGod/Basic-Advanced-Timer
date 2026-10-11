using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class MenuAndShellTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TrackingEngine Engine(Func<DateTime>? clock = null) => new(clock: clock ?? (() => Day.AddHours(14)));
    private static SessionRow Row(string start, string minutes, string account = "Account one", string date = "2026-10-09") => new(date, account, start, "added", minutes);

    [AvaloniaFact]
    public void WindowMenuIsOrderedByUseWithCloseTimerAlone()
    {
        var model = new TimerModel(Engine());
        var window = new DashboardWindow(model); window.Show();
        try
        {
            var items = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).ToList();
            var headers = items.Select(i => i is MenuItem m ? (string?)m.Header : "|").ToList();
            Assert.Equal(new[] { "Decimal hours", "Test overtime", "|", BuildInfo.Current, "|", "Close timer" }, headers);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TestOvertimeIsADemoOnlyToggleThatSwitchesOnAndOff()
    {
        var plain = new TimerModel(Engine());
        Assert.Throws<InvalidOperationException>(() => plain.SetOvertimeTest(true)); // Real data is never touched.
        var now = new DateTime(2026, 10, 9, 14, 15, 0);
        var model = TimerModel.CreateDemo(() => now);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            var toggle = menu.Single(m => m.Name == "TestOvertime"); Assert.True(toggle.IsVisible);
            Assert.False(model.IsOvertime); Assert.False(model.OvertimeTest);
            toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(model.OvertimeTest); Assert.True(model.IsOvertime); Assert.Equal("Overtime", model.ThemeState);
            toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(model.OvertimeTest); Assert.False(model.IsOvertime); Assert.Equal("Normal", model.ThemeState); // Back to the usual shift.
            Assert.False(model.Engine.Schedule.Overrides.ContainsKey(now.Date));
        }
        finally { window.Close(); }
        var real = new TimerModel(Engine());
        var realWindow = new DashboardWindow(real); realWindow.Show();
        try
        {
            var items = ((IEnumerable<Control>)realWindow.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            Assert.False(items.Single(m => m.Name == "TestOvertime").IsVisible); // Only the demo has an overtime tester.
        }
        finally { realWindow.Close(); }
    }

    [AvaloniaFact]
    public void RightClickOnATimelineBlockOffersDeleteForItOrTheWholeSelection()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30"), Row("11:00:00", "30") });
        var model = new TimerModel(engine);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            MenuItem Delete(int index)
            {
                var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + index);
                var items = ((IEnumerable<Control>)card.ContextMenu!.ItemsSource!).ToList();
                Assert.IsType<Separator>(items[^2]); return (MenuItem)items[^1]; // Delete sits last, apart from the other actions.
            }
            Assert.Equal("Delete", Delete(1).Header);
            Delete(1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "09:00:00", "11:00:00" }, engine.Sessions.Select(s => s.Start)); // Only the clicked one went.
            Assert.True(model.UndoEdit()); Assert.Equal(3, engine.Sessions.Count); Dispatcher.UIThread.RunJobs();
            model.ToggleSelection(new[] { engine.Sessions[0] }); model.ToggleSelection(new[] { engine.Sessions[2] }); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Delete 2 blocks", Delete(0).Header); // On a selected block it speaks for the whole selection.
            Delete(0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal("10:00:00", Assert.Single(engine.Sessions).Start); Assert.Empty(model.SelectedRows);
            Assert.True(model.UndoEdit()); Assert.Equal(3, engine.Sessions.Count); // One Undo brings them all back.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReorderableAccountRowsShowAGripAndThePillListDoesNot()
    {
        var model = new TimerModel(Engine());
        var dashboard = new DashboardWindow(model); dashboard.Show(); var pill = new PillWindow(model); pill.Show(); pill.ToggleExpansion();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame(); using var pillFrame = pill.CaptureRenderedFrame();
            var grips = dashboard.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Kind == IconKind.Grip).ToList();
            Assert.Equal(model.Accounts.Count - 1, grips.Count); // One for each account row, none for the break.
            Assert.All(grips, g => Assert.Equal("Drag to change the order", ToolTip.GetTip(g)));
            Assert.Empty(pill.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Kind == IconKind.Grip)); // The pill list cannot be reordered.
            // The rest of the row keeps its shape: the account name and today's time are still both visible inside it.
            var row = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account0");
            Assert.All(row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is { Length: > 0 }), t => Assert.True(t.TranslatePoint(default, row)!.Value.X >= 0 && t.TranslatePoint(new Point(t.Bounds.Width, 0), row)!.Value.X <= row.Bounds.Width + 1));
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); frame!.Save(Path.Combine(folder, "account-grips.png")); }
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [Theory]
    [InlineData("0.1.77+aa17cdaf4ceb5540116abd71ca9124e24a6ad33", "Version 0.1.77 (aa17cda)")]
    [InlineData("0.1.77", "Version 0.1.77")]
    [InlineData("0.1.77+abc", "Version 0.1.77")] // A commit too short to be one is left off.
    [InlineData("1.0.0", "Version: development build")]
    [InlineData("1.0.0+aa17cdaf", "Version: development build")]
    [InlineData("", "Version: development build")]
    [InlineData(null, "Version: development build")]
    public void TheBuildNumberReadsAsAVersionAndACommit(string? stamp, string expected) => Assert.Equal(expected, BuildInfo.Describe(stamp));

    [AvaloniaFact]
    public void TheMenuShowsWhichBuildIsRunningAsAGreyedItem()
    {
        var window = new DashboardWindow(new TimerModel(Engine())); window.Show();
        try
        {
            var item = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().Single(m => m.Name == "BuildVersion");
            Assert.False(item.IsEnabled); Assert.StartsWith("Version", (string)item.Header!);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheChosenRailIconKeepsItsTileUnderThePointer()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Light");
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tab = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("rail") && b.Classes.Contains("selected"));
            var presenter = tab.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            var tile = Color.Parse(model.CurrentTheme.Colour("Selected"));
            Assert.Equal(tile, ((ISolidColorBrush)presenter.Background!).Color);
            ((IPseudoClasses)tab.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(tile, ((ISolidColorBrush)presenter.Background!).Color);
            ((IPseudoClasses)tab.Classes).Set(":pressed", true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(tile, ((ISolidColorBrush)presenter.Background!).Color);
        }
        finally { window.Close(); }
    }
}
