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
        Assert.Equal("2:30", table.Rows[0].Total); Assert.Equal("0:30", table.Rows[1].Total); Assert.Equal("–", table.Rows[2].Total); // Short forms, and a dash for nothing.
        Assert.Equal("2h 30m", table.Rows[0].CopyTotal); Assert.Equal("0h 00m", table.Rows[2].CopyTotal); // Copying keeps the full wording.
        Assert.Equal("3:00", table.Rows[3].Total); Assert.Equal("0:30", table.Rows[4].Total); // The break stays out of the total.
        Assert.Equal("1:00", table.Rows[3].Cells[1]); Assert.Equal("2:00", table.Rows[3].Cells[4]); Assert.Equal("–", table.Rows[3].Cells[0]);
        var budget = Assert.Single(table.Budgets);
        Assert.Equal("Account one", budget.Account); Assert.Equal("10:00 per week", budget.Budget); Assert.Equal("2:30", budget.Used);
        Assert.Equal("7:30", budget.Remaining); Assert.Equal(.25, budget.Fraction, 3);
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
        decimal Value(string text) => text == "–" ? 0m : decimal.Parse(text.TrimEnd('h'), System.Globalization.CultureInfo.InvariantCulture);
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
            window.SelectTab(true); model.SetWeekSection("entries"); Dispatcher.UIThread.RunJobs(); using var weekFrame = window.CaptureRenderedFrame();
            var entry = window.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("WeekEntry") == true);
            Assert.Contains(((IEnumerable<Control>)entry.ContextMenu!.ItemsSource!).OfType<MenuItem>(), m => m.Name == "MoveToAccountMenu");
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); weekFrame!.Save(Path.Combine(folder, "week-dashboard.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WeekIsSplitIntoOverviewHoursAndEntries()
    {
        var model = Model(false, out var engine, e => e.Sessions.AddRange(new[] { Row("09:00:00", "90"), Row("11:00:00", "30", "Account two"), Row("08:00:00", "60", date: "2026-10-06") }));
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.SelectTab(true); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Assert.Equal("overview", model.WeekSection);
            var bars = window.GetVisualDescendants().OfType<WeekBars>().Single();
            Assert.Equal(5, bars.Bars.Count); // A bar per working day, with no working-day line.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "By account");
            Assert.Equal(3, bars.Bars[4].Segments.Count);
            Assert.Equal(1.5, bars.Bars[4].Segments[0].Hours, 3); Assert.Equal("2:00", bars.Bars[4].TotalText); Assert.Equal("–", bars.Bars[0].TotalText);
            Assert.Contains("Account one  1:30", bars.Bars[4].Tooltip);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Worked this week", texts); Assert.Contains("Unallocated", texts);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "CopyHours"); // No wall of numbers on the overview.
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekHoursTab").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("hours", model.WeekSection); Assert.Empty(window.GetVisualDescendants().OfType<WeekBars>());
            Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "CopyHours");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekEntriesTab").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("entries", model.WeekSection); Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name?.StartsWith("WeekEntry") == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WeekTableFitsTheWidthInEveryFormat()
    {
        foreach (var decimalHours in new[] { false, true })
        {
            var model = Model(decimalHours, out _, e => { for (var d = 0; d < 7; d++) e.Sessions.Add(Row("09:00:00", "475.5", date: $"2026-10-{5 + d:00}")); });
            var window = new DashboardWindow(model); window.Show();
            try
            {
                window.SelectTab(true); model.SetWeekSection("hours"); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Content is StackPanel);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"decimal {decimalHours}: table {scroll.Extent.Width} wider than {scroll.Viewport.Width}"); // No sideways scrolling, even with seven days.
                var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
                if (folder is not null) { Directory.CreateDirectory(folder); frame!.Save(Path.Combine(folder, $"week-hours-{(decimalHours ? "decimal" : "hm")}.png")); }
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void DateTextHasAFixedWidthAndArrowKeysStepThePeriod()
    {
        var model = Model(false, out _);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Button Arrow(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var before = Arrow("NextPeriod").TranslatePoint(default, window)!.Value.X;
            window.SelectTab(true); Dispatcher.UIThread.RunJobs();
            model.ChangeWeek(-1); Dispatcher.UIThread.RunJobs(); using var second = window.CaptureRenderedFrame();
            var after = Arrow("NextPeriod").TranslatePoint(default, window)!.Value.X;
            var one = Arrow("PreviousPeriod").TranslatePoint(default, window)!.Value.X; model.ChangeWeek(-1); Dispatcher.UIThread.RunJobs();
            Assert.Equal(one, Arrow("PreviousPeriod").TranslatePoint(default, window)!.Value.X, 1); // The left arrow does not move with the text.
            Assert.Equal(after, Arrow("NextPeriod").TranslatePoint(default, window)!.Value.X, 1); Assert.True(Math.Abs(before - after) < 40);
            var start = model.SelectedDate;
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowRight, Avalonia.Input.RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(start.AddDays(7), model.SelectedDate); // A week in the Week tab.
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowLeft, Avalonia.Input.RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(start, model.SelectedDate);
            window.SelectTab(false); Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowLeft, Avalonia.Input.RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(start.AddDays(-1), model.SelectedDate); // A day in Timeline.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ArrowKeysDoNothingWhileTypingInABox()
    {
        var model = Model(false, out _);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); var start = model.SelectedDate;
            var box = new TextBox(); var holder = new Window { Content = box }; holder.Show(); box.Focus();
            holder.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowLeft, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(start, model.SelectedDate); holder.Close();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DemoHasSevenAccountsWithSensibleBudgetsAndOverviewLinesUpWithEachAccount()
    {
        var model = TimerModel.CreateDemo(() => Day.AddHours(12)); // A Friday, so the week holds Monday to Thursday history.
        var accounts = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        Assert.Equal(7, accounts.Count);
        Assert.Equal(5, accounts.Count(a => a.Budget is not null));
        Assert.Equal(new[] { "week", "week", "week", "month", "quarter" }, accounts.Where(a => a.Budget is not null).Select(a => a.Period));
        var fractions = accounts.Where(a => a.Budget is not null).Select(a => model.BudgetUsed(a).TotalSeconds / a.Budget!.Value.TotalSeconds).ToList();
        Assert.All(fractions, f => Assert.True(f > .05)); // Every budget has something used, so no meter is empty.
        Assert.Contains(fractions, f => f is >= .8 and < 1); Assert.Contains(fractions, f => f >= 1); Assert.Contains(fractions, f => f < .8); // Comfortable, nearly used and over.
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.SelectTab(true); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs();
            var lines = window.GetVisualDescendants().OfType<Grid>().Where(g => g.ColumnDefinitions.Count == 5 && g.Children.Count >= 3 && g.MinHeight == 34).ToList();
            Assert.Equal(7, lines.Count);
            double Centre(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
            foreach (var line in lines)
            {
                var name = line.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 1);
                foreach (var other in line.Children.OfType<Control>().Where(c => Grid.GetColumn(c) is 0 or 2 or 3 or 4))
                    Assert.True(Math.Abs(Centre(other) - Centre(name)) < 1.5, $"{name.Text}: column {Grid.GetColumn(other)} is {Centre(other) - Centre(name):0.0}px off");
            }
            Assert.Equal(1, lines.Select(l => Math.Round(l.Bounds.Height)).Distinct().Count()); // Rows are the same height with or without a budget.
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); frame!.Save(Path.Combine(folder, "demo-overview.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TimelineHasZoomAndFitIconsThatDoWhatTheirTooltipsSay()
    {
        var model = Model(false, out var engine, e => { e.Sessions.Add(Row("09:00:00", "60")); e.Sessions.Add(Row("10:30:00", "30")); });
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Button Find(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            Assert.Equal("Zoom out", ToolTip.GetTip(Find("TimelineZoomOut"))); Assert.Equal("Fit the day to the window", ToolTip.GetTip(Find("TimelineFit")));
            Assert.Contains("Zoom in", ToolTip.GetTip(Find("TimelineZoomIn"))!.ToString());
            Assert.All(new[] { "TimelineZoomOut", "TimelineZoomIn", "TimelineFit" }, n => Assert.Null(Find(n).Content as string)); // Icons only, no words.
            var before = model.TimelinePixelsPerHour;
            Find("TimelineZoomIn").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(before * 1.2, model.TimelinePixelsPerHour, 3);
            Find("TimelineZoomOut").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, model.TimelinePixelsPerHour, 3);
            for (var i = 0; i < 20; i++) model.ZoomTimeline(1, 0, 0);
            Dispatcher.UIThread.RunJobs(); Assert.False(Find("TimelineZoomIn").IsEnabled); Assert.True(Find("TimelineZoomOut").IsEnabled); // At the limit it says so.
            var viewport = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll").Viewport.Height;
            Find("TimelineFit").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            // 09:00 to 11:00 plus a quarter hour each side is 2.5 hours across the whole viewport.
            Assert.Equal(viewport / 2.5, model.TimelinePixelsPerHour, 1); Assert.Equal(8.75 * model.TimelinePixelsPerHour, model.TimelineOffset!.Value, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FitUsesTheWorkingDayWhenNothingIsRecordedAndNeverLeavesTheLimits()
    {
        var model = Model(false, out _);
        model.FitTimeline(700); // 09:00 to 17:00 with padding is 8.5 hours.
        Assert.Equal(700 / 8.5, model.TimelinePixelsPerHour, 3);
        model.FitTimeline(100000); Assert.Equal(640, model.TimelinePixelsPerHour);
        model.FitTimeline(10); Assert.Equal(60, model.TimelinePixelsPerHour);
        model.FitTimeline(0); Assert.True(model.TimelinePixelsPerHour >= 60); // No viewport yet: a sensible size, not a crash.
    }
}
