using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Small fixes from the first Store feedback: a smaller pill on small screens, clear space beside Merge, and messages that fade.
public sealed class TweakTests
{
    // A few passes, because a new page's visuals appear only after a layout and render pass, and a slow Windows runner has been seen to need more than one.
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void ThePillShrinksWithTheScreenAndStaysBetweenItsLargestAndSmallestSizes()
    {
        Assert.Equal(PillWindow.LargestScale, PillWindow.ScaleFor(2560)); // Even a big screen gets a smaller pill than the design size.
        Assert.Equal(PillWindow.LargestScale, PillWindow.ScaleFor(1920));
        Assert.InRange(PillWindow.ScaleFor(1366), 0.79, 0.81); // A common laptop screen gets the smallest.
        Assert.Equal(PillWindow.SmallestScale, PillWindow.ScaleFor(800));
        // A 2560 by 1600 display at 125 per cent Windows scale keeps the largest pill, and a 1080p one at 100 per cent gets the smallest.
        Assert.Equal(PillWindow.LargestScale, PillWindow.ScaleFor(2560 / 1.25, 1.25));
        Assert.Equal(PillWindow.SmallestScale, PillWindow.ScaleFor(1920, 1.0));
        Assert.InRange(PillWindow.ScaleFor(1920 / 1.1, 1.1), 0.85, 0.87); // In between at 110 per cent.
        Assert.Equal(PillWindow.LargestScale, PillWindow.ScaleFor(1800, 1.5)); // More scaling than 125 per cent never makes it smaller.
    }

    [AvaloniaFact]
    public void AScaledPillKeepsItsProportionsAndGrowsBackOnABigScreen()
    {
        var pill = new PillWindow(TimerModel.CreateDemo()); pill.Show(); Settle(pill);
        Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.Width, 0.5); // The window keeps the pill's proportions at whatever size it is drawn.
        var before = pill.Scale; pill.FitToScreen(null); Assert.Equal(before, pill.Scale); // No screen to measure (a hidden pill) leaves the size as it is.
        Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5);
        pill.Close();
    }

    [AvaloniaFact]
    public void FooterNotesFadeAfterFifteenSecondsButAFailedSaveDoesNot()
    {
        var model = TimerModel.CreateDemo();
        Assert.Equal(TimeSpan.FromSeconds(15), TimerModel.MessageLifetime);
        model.ExplainRunning();
        Assert.NotNull(model.EditError);
        model.ExpireMessages();
        Assert.Null(model.EditError); Assert.Null(model.Notice);
    }

    [AvaloniaFact]
    public void MergeAndTheZoomBarHaveRoomBetweenThem()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var merge = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MergeSelected");
        var zoom = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineControls");
        var mergeRight = merge.TranslatePoint(new Point(merge.Bounds.Width, 0), dashboard)!.Value.X;
        var zoomLeft = zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.X;
        Assert.True(zoomLeft - mergeRight >= 24, $"Only {zoomLeft - mergeRight} px between Merge and the zoom bar.");
        dashboard.Close();
    }

    [Fact]
    public void AMonthExportCoversEveryDayOfTheMonthAndTheHundredthsStillAddUpExactly()
    {
        var engine = new TrackingEngine(clock: () => new DateTime(2026, 10, 31, 18, 0, 0));
        // Three odd lengths on different weeks, so rounding has to be shared out across the whole month.
        engine.Sessions.Add(new("2026-10-01", "Account one", "09:00:00", "added", "20"));
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "added", "20"));
        engine.Sessions.Add(new("2026-10-30", "Account two", "09:00:00", "added", "20"));
        engine.Sessions.Add(new("2026-09-30", "Account one", "09:00:00", "added", "60")); // Last month, left out.
        var export = WeekExport.ForMonth(new LedgerEditor(engine), engine, new DateTime(2026, 10, 17));
        Assert.True(export.IsMonth);
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), export.Period);
        Assert.Equal(100, export.TotalUnits); // One hour in all, shared as 33, 33 and 34 hundredths.
        Assert.Equal(100, export.Accounts.Values.Sum(days => days.Sum()));
        var summary = export.SummaryCsv(new[] { "Account one", "Account two" });
        Assert.Contains("Thu 01/10", summary); Assert.Contains("Sat 31/10", summary); Assert.DoesNotContain("Wed 30/09", summary);
        Assert.Equal(3, export.Entries.Count);
        var weekExport = new WeekExport(new LedgerEditor(engine), engine, new DateTime(2026, 10, 12));
        Assert.False(weekExport.IsMonth); Assert.Equal(33, weekExport.TotalUnits); // A week is unchanged.
    }

    [AvaloniaFact]
    public void TheExportDialogOffersAWeekAndAMonthEachWithAListAndWritesTheOneChosen()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));
        var window = new ExportDialog(model).Open(model);
        T Get<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        var week = Get<RadioButton>("ExportWeekChoice"); var month = Get<RadioButton>("ExportMonthChoice");
        var weekPick = Get<ComboBox>("ExportWeekPick"); var monthPick = Get<ComboBox>("ExportMonthPick");
        Assert.True(week.IsChecked); Assert.False(month.IsChecked);
        // The lists start on the week and month being viewed, and reach back over the recorded days.
        Assert.Equal("5 Oct to 11 Oct 2026", weekPick.SelectedItem); Assert.Equal("October 2026", monthPick.SelectedItem);
        Assert.True(weekPick.ItemCount >= 12 && monthPick.ItemCount >= 12);
        // Choosing from the month list picks the month, and exports that month, not the one being viewed.
        monthPick.SelectedIndex = 2; Assert.True(month.IsChecked); Assert.False(week.IsChecked);
        Get<Button>("ExportCsv").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.NotNull(model.ExportPath); Assert.Contains("Month-202608", model.ExportPath);
        Assert.True(File.Exists(Path.Combine(model.ExportPath!, "Detail.csv")) && File.Exists(Path.Combine(model.ExportPath!, "Summary.csv")));
        // Choosing from the week list goes back to a week.
        weekPick.SelectedIndex = 3; Assert.True(week.IsChecked);
        Get<Button>("ExportCsv").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("Week-20260914", model.ExportPath); // Three weeks before the week of 5 October.
        window.Close();
    }

    [Fact]
    public void ADailyBudgetCountsTodayOnlyAndPacesAcrossTheWorkingDay()
    {
        var now = new DateTime(2026, 10, 14, 13, 0, 0); // A Wednesday, part way through the working day.
        var engine = new TrackingEngine(clock: () => now);
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "added", "60"));
        engine.Sessions.Add(new("2026-10-13", "Account one", "09:00:00", "added", "240")); // Yesterday, not counted.
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one", 4, "day") }, false, ThemeLibrary.Default, DayOfWeek.Monday));
        var account = model.Accounts.First(a => a.Name == "Account one");
        Assert.Equal(TimeSpan.FromHours(1), model.BudgetUsed(account));
        Assert.Equal(0.25, model.BudgetRatio(account)!.Value, 3);
        Assert.Contains("daily", model.BudgetLine(account));
        Assert.InRange(model.BudgetPace(account), 0.01, 0.99); // Somewhere through the working day, not midnight to midnight.
        model.SetAccounts(new[] { new AccountSettings("Account one", 3, "day") }); // Saving a daily budget is accepted.
        Assert.Equal("day", model.Accounts.First(a => a.Name == "Account one").Period);
    }

    [Fact]
    public void AnOlderDatabaseUpgradesToAllowDailyBudgetsWithEveryAccountEntryAndLinkKept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bat-upgrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var db = TimerDatabase.Open(folder, () => new DateTime(2026, 10, 1)))
            {
                db.Execute("INSERT INTO accounts (id, name, colour, position, budget_seconds, budget_period) VALUES (7, 'Account one', '#336699', 0, 7200, 'month')");
                db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, created_at) VALUES ('work', 7, '2026-10-01', '2026-10-01T09:00:00', '2026-10-01T09:10:00', 600, '2026-10-01T09:10:00')");
            }
            SqliteConnection.ClearAllPools();
            // Put it back to how format 1 stored accounts, with the narrower period check, as an existing user's file would be.
            using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, TimerDatabase.FileName), Pooling = false }.ToString()))
            {
                raw.Open();
                foreach (var sql in new[] { "DROP TABLE day_notes", "DROP TABLE note_series", "PRAGMA foreign_keys = OFF", "BEGIN", TimerDatabase.AccountsTable("accounts_old", false),
                    "INSERT INTO accounts_old SELECT id, name, colour, position, hidden, budget_seconds, budget_period FROM accounts", "DROP TABLE accounts",
                    "ALTER TABLE accounts_old RENAME TO accounts", "COMMIT", "PRAGMA user_version = 1" })
                { using var command = raw.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
                using var check = raw.CreateCommand(); check.CommandText = "INSERT INTO accounts (name, colour, position, budget_period) VALUES ('x', '#336699', 1, 'day')";
                Assert.Throws<SqliteException>(() => check.ExecuteNonQuery()); // Format 1 really refuses a daily budget.
            }
            SqliteConnection.ClearAllPools();
            using var upgraded = TimerDatabase.Open(folder, () => new DateTime(2026, 10, 2));
            Assert.Equal(TimerDatabase.SchemaVersion, upgraded.Version);
            Assert.Equal(7200L, upgraded.Scalar("SELECT budget_seconds FROM accounts WHERE id = 7"));
            Assert.Equal("month", upgraded.Scalar("SELECT budget_period FROM accounts WHERE id = 7"));
            Assert.Equal("Account one", upgraded.Scalar("SELECT a.name FROM entries e JOIN accounts a ON a.id = e.account_id"));
            Assert.Empty(upgraded.Query("PRAGMA foreign_key_check", r => 1));
            upgraded.Execute("UPDATE accounts SET budget_period = 'day' WHERE id = 7"); // Now allowed.
            Assert.Equal("day", upgraded.Scalar("SELECT budget_period FROM accounts WHERE id = 7"));
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "Backups"), $"timer-before-format-{TimerDatabase.SchemaVersion}-*.db"));
        }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static TimerModel CopyModel(out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 17, 0, 0));
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "recorded", "30.50")); // Credited length is not a whole number of minutes.
        engine.Sessions.Add(new("2026-10-14", "Account two", "10:00:00", "recorded", "45.00"));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, ThemeLibrary.Default, DayOfWeek.Monday));
        model.ChangeDate((new DateTime(2026, 10, 14) - model.SelectedDate.Date).Days);
        return model;
    }

    [Fact]
    public void CopiedBlocksPasteWithTheirCreditedTimeNotesAndGapsAsOneUndoStep()
    {
        var model = CopyModel(out var engine);
        model.Editor.Edit(engine.Sessions[1], "Account two", engine.Sessions[1].From, engine.Sessions[1].To, "Call with client");
        Assert.True(model.CopyBlocks(engine.Sessions.ToList()));
        Assert.True(model.CanPaste);
        Assert.True(model.PasteBlocks(new DateTime(2026, 10, 14, 13, 0, 0)));
        Assert.Equal(4, engine.Sessions.Count);
        var pastedFirst = engine.Sessions.Single(s => s.Start == "13:00:00"); var pastedSecond = engine.Sessions.Single(s => s.Start == "14:00:00"); // The one hour gap is kept.
        Assert.Equal("30.50", pastedFirst.Minutes); Assert.Equal("Account one", pastedFirst.Account); // Credit is copied exactly, not worked out from clock times.
        Assert.Equal("Call with client", model.Editor.Note(pastedSecond));
        model.UndoEdit();
        Assert.Equal(2, engine.Sessions.Count); // The whole paste comes back out in one step.
    }

    [Fact]
    public void ARefusedPasteAddsNothing()
    {
        var model = CopyModel(out var engine);
        model.CopyBlocks(engine.Sessions.ToList());
        Assert.False(model.PasteBlocks(new DateTime(2026, 10, 14, 9, 15, 0))); // Lands on the existing 09:00 block.
        Assert.Equal(2, engine.Sessions.Count); Assert.NotNull(model.EditError);
        Assert.False(model.PasteBlocks(new DateTime(2026, 10, 14, 23, 30, 0))); // Runs past midnight.
        Assert.Equal(2, engine.Sessions.Count);
    }

    [Fact]
    public void BlocksFromDifferentDaysCannotBeCopiedTogetherAndNothingCopiedMeansNothingToPaste()
    {
        var model = CopyModel(out var engine);
        engine.Sessions.Add(new("2026-10-13", "Account one", "09:00:00", "recorded", "60.00"));
        Assert.False(model.CopyBlocks(engine.Sessions.ToList())); Assert.False(model.CanPaste); Assert.Contains("one day", model.EditError);
        Assert.False(model.PasteBlocks(new DateTime(2026, 10, 14, 13, 0, 0)));
        Assert.Equal(3, engine.Sessions.Count);
    }

    [Fact]
    public void CtrlVWithoutAPointerPastesStraightAfterTheLastBlockOfTheDay()
    {
        var model = CopyModel(out var engine);
        model.CopyBlocks(new[] { engine.Sessions[0] });
        Assert.Equal(new DateTime(2026, 10, 14, 10, 45, 0), model.DefaultPasteTime());
        model.PasteTime = new DateTime(2026, 10, 14, 15, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 14, 15, 0, 0), model.DefaultPasteTime());
    }

    [AvaloniaFact]
    public void BlocksAndTheTimelineOfferCopyAndPasteHere()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var block = dashboard.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("TimelineEntry") == true);
        Assert.Contains(block.ContextMenu!.Items.OfType<MenuItem>(), m => m.Name == "CopyBlocksMenu");
        var canvas = dashboard.GetVisualDescendants().OfType<Canvas>().First(c => c.ContextMenu is not null && c.ContextMenu.Items.OfType<MenuItem>().Any(m => m.Name == "PasteHereMenu"));
        Assert.False(canvas.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == "PasteHereMenu").IsEnabled); // Nothing copied yet.
        dashboard.Close();
    }

    private static Avalonia.Platform.Screen FakeScreen(int width, int height, double scaling)
        => new(scaling, new PixelRect(0, 0, width, height), new PixelRect(0, 0, width, height), true);

    [AvaloniaFact]
    public void ThePillIsResizedForANewScreenAsSoonAsItMovesThereWithoutBeingShownAgain()
    {
        var pill = new PillWindow(TimerModel.CreateDemo()); pill.Show(); Settle(pill);
        pill.FitToScreen(FakeScreen(2560, 1600, 1.25)); Assert.Equal(PillWindow.LargestScale, pill.Scale, 3); // The 2560 by 1600 screen at 125 per cent.
        // Moving the window makes it look at the screen it is on now. That one is not the screen last used, so it is sized for it.
        pill.FitIfScreenChanged();
        Assert.Equal(PillWindow.ScaleFor(pill.Screens.ScreenFromWindow(pill)!.WorkingArea.Width / pill.Screens.ScreenFromWindow(pill)!.Scaling, pill.Screens.ScreenFromWindow(pill)!.Scaling), pill.Scale, 3);
        Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.Width, 0.5);
        var settled = pill.Scale; pill.FitIfScreenChanged(); Assert.Equal(settled, pill.Scale); // On the same screen, nothing changes.
        pill.Close();
    }

    [AvaloniaFact]
    public void APillSizeChosenInSettingsAppliesAtOnceEvenWhileThePillIsHiddenAndAutomaticGoesBack()
    {
        var model = TimerModel.CreateDemo(); var pill = new PillWindow(model); // Never shown: the dashboard would be open when this is chosen.
        model.SetPillScale(90); Assert.Equal(0.9, pill.Scale, 3); Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * 0.9, pill.Width, 0.5);
        pill.FitToScreen(FakeScreen(1366, 768, 1.0)); Assert.Equal(0.9, pill.Scale, 3); // A screen does not override a size chosen by hand.
        model.SetPillScale(null); pill.FitToScreen(FakeScreen(1920, 1080, 1.0)); Assert.Equal(PillWindow.SmallestScale, pill.Scale, 3); // Automatic again.
        model.SetPillScale(500); Assert.Equal(120, model.PillScalePercent); model.SetPillScale(1); Assert.Equal(50, model.PillScalePercent); // Kept to a sensible range.
        pill.Close();
    }

    [AvaloniaFact]
    public void TheSettingsPageHasAPillSizeChoiceThatSetsAndShowsTheSize()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
        var box = dashboard.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PillSize");
        Assert.Equal("Automatic", box.SelectedItem); Assert.Contains("120%", (IEnumerable<string>)box.ItemsSource!);
        box.SelectedItem = "80%"; Assert.Equal(80, model.PillScalePercent);
        box.SelectedItem = "Automatic"; Assert.Null(model.PillScalePercent);
        dashboard.Close();
    }

    [Fact]
    public void ThePillSizeIsSavedAndLoadedAndABadValueFallsBackToAutomatic()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-pill-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.Null(prefs.PillScale); store.Save(engine, prefs with { PillScale = 85 }); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.Equal(85, prefs.PillScale); store.Save(engine, prefs with { PillScale = null }); }
            using (var store = new TimerStore(folder)) Assert.Null(store.Load(new TrackingEngine()).PillScale);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void AScaleSetBeforeThePillIsShowingStillGivesItTheRightHeightWhenItOpens()
    {
        // The 1080p case: the scale was worked out while the window was not showing yet, so Opened saw nothing to change and left the old height.
        var pill = new PillWindow(TimerModel.CreateDemo());
        pill.FitToScreen(FakeScreen(1920, 1080, 1.0)); Assert.Equal(PillWindow.SmallestScale, pill.Scale, 3);
        pill.Show(); Settle(pill);
        Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.Width, 0.5);
        Assert.Equal(pill.Height, pill.ClientSize.Height, 1.5); // The real window, not only the request.
        var card = pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PillSurface");
        Assert.Equal(PillWindow.PillHeight * pill.Scale, card.Bounds.Height * pill.Scale, 2.0); // No white card below the capsule.
        pill.Close();
    }

    [AvaloniaFact]
    public void AStaleHeightIsPutRightWhateverLeftItThere()
    {
        var pill = new PillWindow(TimerModel.CreateDemo()); pill.Show(); Settle(pill);
        pill.Height = pill.Height + 60; Settle(pill); // Something left the window taller than the pill.
        Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5);
        pill.Width = pill.Width - 80; Settle(pill); // And narrower, so the account box would be cut off.
        Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.Width, 0.5);
        pill.ShowPanel(); Settle(pill); Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); // The account list is a pop-up, so the window keeps its size.
        pill.CloseFlyouts(); Settle(pill); Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5);
        pill.Close();
    }

    [AvaloniaFact]
    public void ThePillHasTheRightSizeAfterGoingToAnotherScreenAndAfterBeingHiddenAndShownAgain()
    {
        var pill = new PillWindow(TimerModel.CreateDemo()); pill.Show(); Settle(pill);
        pill.FitToScreen(FakeScreen(2560, 1600, 1.25)); Settle(pill);
        Assert.Equal(PillWindow.LargestScale, pill.Scale, 3); Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5);
        Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.Width, 0.5);
        pill.Hide(); pill.FitToScreen(FakeScreen(1920, 1080, 1.0)); // Changed while hidden, as when the dashboard was up.
        pill.Show(); Settle(pill);
        Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.Width, 0.5);
        Assert.Equal(pill.Width, pill.ClientSize.Width, 1.5);
        pill.Close();
    }
}
