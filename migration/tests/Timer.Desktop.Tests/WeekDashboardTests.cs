using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class WeekDashboardTests
{
    private static DateTime Day => new(2026, 10, 9); // A Friday; the week starts on Monday 5 October.
    private static SessionRow Row(string start, string minutes, string account = "Account one", string date = "2026-10-09") => new(date, account, start, "added", minutes);
    private static TimerModel Model(bool decimalHours, out TrackingEngine engine, Action<TrackingEngine>? seed = null)
    {
        var e = new TrackingEngine(clock: () => Day.AddHours(16)); engine = e; seed?.Invoke(e);
        var prefs = new StoreSettings(new() { new("Account one", 10), new("Account two"), new("Internal") }, decimalHours, "Light", DayOfWeek.Monday);
        return new TimerModel(e, prefs);
    }

    [AvaloniaFact]
    public void WeekTableHasBoldTotalFaintBreakAndUnallocatedAndBudgets()
    {
        var model = Model(false, out var engine, e =>
        {
            e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two"), Row("12:00:00", "30", "Lunch"), Row("08:00:00", "60", date: "2026-10-06") });
        });
        var week = new DateTime(2026, 10, 5);
        var table = WeekTable.Build(model, new WeekExport(model.Editor, engine, week), week, new[] { 0, 1, 2, 3, 4 });
        Assert.Equal(new[] { "Account one", "Account two", "Internal", "Total (excluding breaks)", "Break", "Unallocated" }, table.Rows.Select(r => r.Label));
        Assert.Equal(new[] { "normal", "normal", "normal", "bold", "faint", "faint" }, table.Rows.Select(r => r.Style));
        Assert.Equal("2h 30m", table.Rows[0].Total); Assert.Equal("0h 30m", table.Rows[1].Total);
        Assert.Equal("3h 00m", table.Rows[3].Total); Assert.Equal("0h 30m", table.Rows[4].Total); // The break stays out of the total.
        Assert.Equal("1h 00m", table.Rows[3].Cells[1]); Assert.Equal("2h 00m", table.Rows[3].Cells[4]);
        var budget = Assert.Single(table.Budgets);
        Assert.Equal("Account one", budget.Account); Assert.Equal("10h 00m per week", budget.Budget); Assert.Equal("2h 30m", budget.Used);
        Assert.Equal("7h 30m", budget.Remaining); Assert.Equal(.25, budget.Fraction, 3);
    }

    [AvaloniaFact]
    public void DecimalTableAddsUpExactlyAcrossRowsAndColumns()
    {
        var model = Model(true, out var engine, e =>
        {
            for (var i = 0; i < 7; i++) { e.Sessions.Add(Row($"{9 + i:00}:00:00", "13.34", i % 2 == 0 ? "Account one" : "Account two")); e.Sessions.Add(Row($"{9 + i:00}:30:00", "7.11", "Internal", "2026-10-07")); }
        });
        var week = new DateTime(2026, 10, 5);
        var table = WeekTable.Build(model, new WeekExport(model.Editor, engine, week), week, new[] { 0, 1, 2, 3, 4 });
        decimal Value(string text) => decimal.Parse(text.TrimEnd('h'), System.Globalization.CultureInfo.InvariantCulture);
        var accounts = table.Rows.Take(3).ToList(); var total = table.Rows[3];
        for (var d = 0; d < 5; d++) Assert.Equal(accounts.Sum(r => Value(r.Cells[d])), Value(total.Cells[d]));
        Assert.Equal(accounts.Sum(r => Value(r.Total)), Value(total.Total));
        foreach (var row in table.Rows.Take(4)) Assert.Equal(row.Cells.Sum(Value), Value(row.Total));
    }

    [AvaloniaFact]
    public void CopyTextIsATabSeparatedTableWithTheSameFigures()
    {
        var model = Model(false, out var engine, e => e.Sessions.Add(Row("09:00:00", "90")));
        var week = new DateTime(2026, 10, 5);
        var table = WeekTable.Build(model, new WeekExport(model.Editor, engine, week), week, new[] { 0, 1, 2, 3, 4 });
        var lines = table.ToTsv().Split("\r\n");
        Assert.Equal("Account\tMon 05/10\tTue 06/10\tWed 07/10\tThu 08/10\tFri 09/10\tTotal", lines[0]);
        Assert.Equal("Account one\t0h 00m\t0h 00m\t0h 00m\t0h 00m\t1h 30m\t1h 30m", lines[1]);
        Assert.Equal(table.Rows.Count + 1, lines.Length); Assert.All(lines, l => Assert.Equal(6, l.Count(c => c == '\t')));
    }

    [AvaloniaFact]
    public void WeekTabStepsByWholeWeeksAndStopsAtThisWeek()
    {
        var model = Model(false, out _);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.SelectTab(true); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var next = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NextPeriod");
            var previous = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PreviousPeriod");
            Assert.False(next.IsEnabled);
            model.ChangeWeek(1); Assert.Equal(new DateTime(2026, 10, 9), model.SelectedDate); // Not past this week.
            previous.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new DateTime(2026, 9, 28), model.SelectedDate); Assert.True(next.IsEnabled);
            var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Week of " + new DateTime(2026, 9, 28).ToString("d MMM", british));
            next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new DateTime(2026, 10, 5), model.SelectedDate); Assert.False(next.IsEnabled);
            window.SelectTab(false); Dispatcher.UIThread.RunJobs(); // Timeline steps by days.
            previous.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal(new DateTime(2026, 10, 4), model.SelectedDate);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void MovingEntriesChangesAccountKeepsNotesAndIsOneUndoableEdit()
    {
        var model = Model(false, out var engine, e => e.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30"), Row("10:30:00", "30") }));
        model.Editor.Edit(engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(9.5), "keep me");
        model.Editor.Group(new[] { engine.Sessions[1], engine.Sessions[2] });
        var csv = model.Editor.SessionsCsv; var notes = model.Editor.NotesCsv; var groups = model.Editor.GroupsCsv;
        Assert.True(model.MoveEntries(new[] { engine.Sessions[0], engine.Sessions[1] }, "Account two"));
        Assert.Equal(new[] { "Account two", "Account two", "Account one" }, engine.Sessions.Select(s => s.Account));
        Assert.Equal("keep me", model.Editor.Note(engine.Sessions[0]));
        Assert.DoesNotContain("GroupId", string.Join("", model.Editor.GroupsCsv!.Split("\r\n").Skip(1))); // The split group is gone.
        Assert.Equal("Move 2 entries to Account two", model.Editor.UndoLabel);
        Assert.True(model.UndoEdit());
        Assert.Equal(csv, model.Editor.SessionsCsv); Assert.Equal(notes, model.Editor.NotesCsv); Assert.Equal(groups, model.Editor.GroupsCsv);
        Assert.True(model.MoveEntries(new[] { engine.Sessions[0] }, "Lunch")); Assert.Equal("Move to Break", model.Editor.UndoLabel);
    }

    [Fact]
    public void MovingRefusesConflictsMergedCreditAndUnknownAccountsWithoutChangingAnything()
    {
        var model = Model(false, out var engine, e => e.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:00:00", "30", "Account two"), new("2026-10-09", "Account one", "00:00:00", "merged", "60") }));
        var csv = model.Editor.SessionsCsv;
        Assert.False(model.MoveEntries(new[] { engine.Sessions[0] }, "Account two")); Assert.Contains("same time", model.EditError);
        Assert.False(model.MoveEntries(new[] { engine.Sessions[2] }, "Internal")); Assert.Contains("Merged", model.EditError);
        Assert.False(model.MoveEntries(new[] { engine.Sessions[0] }, "No such account"));
        Assert.Equal(csv, model.Editor.SessionsCsv); Assert.False(model.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void RightClickOffersMoveToAccountOnTimelineAndWeekEntries()
    {
        var model = Model(false, out var engine, e => e.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") }));
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry0");
            var move = ((IEnumerable<Control>)card.ContextMenu!.ItemsSource!).OfType<MenuItem>().Single(m => m.Name == "MoveToAccountMenu");
            var targets = ((IEnumerable<MenuItem>)move.ItemsSource!).Select(m => (string)m.Header!).ToList();
            Assert.DoesNotContain("Account one", targets); Assert.Contains("Account two", targets); Assert.Contains("Break", targets);
            ((IEnumerable<MenuItem>)move.ItemsSource!).Single(m => (string)m.Header! == "Account two").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("Account two", engine.Sessions[0].Account);
            window.SelectTab(true); Dispatcher.UIThread.RunJobs(); using var weekFrame = window.CaptureRenderedFrame();
            var entry = window.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("WeekEntry") == true);
            Assert.Contains(((IEnumerable<Control>)entry.ContextMenu!.ItemsSource!).OfType<MenuItem>(), m => m.Name == "MoveToAccountMenu");
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); weekFrame!.Save(Path.Combine(folder, "week-dashboard.png")); }
        }
        finally { window.Close(); }
    }
}
