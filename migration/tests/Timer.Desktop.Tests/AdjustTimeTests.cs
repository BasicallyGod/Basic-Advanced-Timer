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
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AdjustTimeTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TrackingEngine Engine(Func<DateTime>? clock = null) => new(clock: clock ?? (() => Day.AddHours(14)));
    private static SessionRow Row(string start, string minutes, string account = "Account one", string date = "2026-10-09") => new(date, account, start, "added", minutes);

    [Fact]
    public void AdjustingAddsOrRemovesMinutesIsCountedEverywhereAndUndoes()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "60"));
        var editor = new LedgerEditor(engine);
        editor.Adjust("Account one", Day, 15, "Rounded for the call", engine.Now);
        Assert.Equal(TimeSpan.FromMinutes(75), engine.Credited("Account one", Day, Day.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(75), engine.Totals(Day).Allocated);
        var adjustment = engine.Sessions[^1];
        Assert.Equal("adjusted", adjustment.End); Assert.Equal("14:00:00", adjustment.Start); Assert.Equal("15", adjustment.Minutes); Assert.False(adjustment.IsPositioned);
        Assert.Equal("Rounded for the call", editor.Note(adjustment));
        editor.Adjust("Account one", Day, -10.5m, "", engine.Now); // The same second: it moves on a second rather than clashing.
        Assert.Equal("14:00:01", engine.Sessions[^1].Start); Assert.Equal("-10.5", engine.Sessions[^1].Minutes);
        Assert.Equal(TimeSpan.FromMinutes(64.5), engine.Credited("Account one", Day, Day.AddDays(1)));
        var export = new WeekExport(editor, engine, new DateTime(2026, 10, 5)); // The export counts them and still adds up.
        Assert.Equal(export.TotalUnits, export.Accounts["Account one"].Sum());
        Assert.Contains(export.Entries, e => e.Type == "Adjustment" && e.Minutes == 15m);
    }

    [Fact]
    public void AdjustingRefusesNonsenseAndChangesNothing()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "30"));
        var editor = new LedgerEditor(engine); var csv = editor.SessionsCsv;
        Assert.Throws<ArgumentException>(() => editor.Adjust("Account one", Day, 0, "", engine.Now));
        Assert.Throws<ArgumentException>(() => editor.Adjust("Account one", Day, 1441, "", engine.Now));
        Assert.Throws<ArgumentException>(() => editor.Adjust("Account one", Day, 1.234m, "", engine.Now));
        Assert.Throws<ArgumentException>(() => editor.Adjust("Account one", Day, -31, "", engine.Now)); // More than the account has that day.
        Assert.Throws<ArgumentException>(() => editor.Adjust(" ", Day, 5, "", engine.Now));
        Assert.Equal(csv, editor.SessionsCsv); Assert.False(editor.CanUndo);
        editor.Adjust("Account one", Day, -30, "", engine.Now); Assert.Equal(TimeSpan.Zero, engine.Credited("Account one", Day, Day.AddDays(1))); // Exactly to zero is fine.
    }

    [Fact]
    public void APastDayAdjustmentTakesMiddayAndAnAdjustmentCanBeDeletedButMergedCreditCannot()
    {
        var engine = Engine(); var editor = new LedgerEditor(engine);
        engine.Sessions.Add(new("2026-10-06", "Account one", "00:00:00", "merged", "60"));
        var row = editor.Adjust("Account one", new DateTime(2026, 10, 6), 20, "", engine.Now);
        Assert.Equal("12:00:00", row.Start);
        editor.Delete(row); Assert.DoesNotContain(engine.Sessions, s => s.IsAdjustment);
        Assert.True(editor.CanUndo); editor.Undo(); Assert.Contains(engine.Sessions, s => s.IsAdjustment);
        Assert.Throws<ArgumentException>(() => editor.Delete(engine.Sessions[0]));
    }

    [Theory]
    [InlineData("+15", 15)] [InlineData("15", 15)] [InlineData("-10", -10)] [InlineData("1:30", 90)] [InlineData("-0:20", -20)] [InlineData("+ 2:05", 125)] [InlineData("7.5", 7.5)]
    public void MinutesAreUnderstoodInSeveralForms(string text, double expected)
    {
        Assert.True(AdjustWindow.TryParse(text, out var minutes)); Assert.Equal((decimal)expected, minutes);
    }
    [Theory] [InlineData("")] [InlineData("abc")] [InlineData("0")] [InlineData("1:75")] [InlineData("1:2:3")] [InlineData("1.234")] [InlineData("+")]
    public void NonsenseMinutesAreRefused(string text) => Assert.False(AdjustWindow.TryParse(text, out _));

    [AvaloniaFact]
    public void RightClickOnAnAccountAdjustsTimeAndTheAdjustmentShowsOnTheTimelineAndInTotals()
    {
        var now = Day.AddHours(14); var engine = Engine(() => now); engine.Sessions.Add(Row("09:00:00", "60"));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one", 10), new("Account two") }, false, "Light", DayOfWeek.Monday));
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var row = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account0");
            var items = ((IEnumerable<Control>)row.ContextMenu!.ItemsSource!).ToList();
            Assert.Equal("Adjust time today…", ((MenuItem)items[0]).Header); Assert.IsType<Separator>(items[1]); // The everyday action comes first.
            ((MenuItem)items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var dialog = window.OwnedWindows.OfType<AdjustWindow>().Single();
            var minutes = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AdjustMinutes");
            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AdjustPlus15").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AdjustPlus5").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("+20", minutes.Text); Dispatcher.UIThread.RunJobs();
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account one today: 1:00 to 1:20");
            dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AdjustNote").Text = "Call ran over";
            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveAdjustment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsVisible);
            Assert.Equal(TimeSpan.FromMinutes(80), model.Today(model.Accounts[0]));
            var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name?.StartsWith("Adjustment") == true);
            Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account one  +0:20");
            Assert.True(chip.IsEffectivelyVisible); Assert.Contains("Call ran over", ToolTip.GetTip(chip)!.ToString());
            Assert.Equal("Adjust time", model.Editor.UndoLabel);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            using var shot = window.CaptureRenderedFrame(); if (folder is not null) { Directory.CreateDirectory(folder); shot!.Save(Path.Combine(folder, "adjustment-timeline.png")); }
            // A bad entry stays in the dialog with a message.
            model.AdjustTime("Account one", Day, -500, ""); Assert.Contains("more time off", model.EditError);
            // Deleting the chip's adjustment takes it off the timeline and the total.
            var menu = ((IEnumerable<Control>)chip.ContextMenu!.ItemsSource!).OfType<MenuItem>().Single(m => (string?)m.Header == "Delete adjustment");
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromMinutes(60), model.Today(model.Accounts[0]));
            Assert.Empty(window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Adjustment") == true));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WindowMenuIsOrderedByUseWithCloseTimerAlone()
    {
        var model = new TimerModel(Engine());
        var window = new DashboardWindow(model); window.Show();
        try
        {
            var items = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).ToList();
            var headers = items.Select(i => i is MenuItem m ? (string?)m.Header : "|").ToList();
            Assert.Equal(new[] { "Decimal hours", "Accounts and budgets…", "Working hours…", "|", "Appearance", "Window shadows", "Start with Windows", "|", "Open sample-data demo", "Test overtime", "|", BuildInfo.Current, "|", "Close timer" }, headers);
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
            Assert.False(menu.Single(m => (string?)m.Header == "Open sample-data demo").IsVisible); // Already in the demo.
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
            Assert.True(items.Single(m => (string?)m.Header == "Open sample-data demo").IsVisible); Assert.False(items.Single(m => m.Name == "TestOvertime").IsVisible);
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
}
