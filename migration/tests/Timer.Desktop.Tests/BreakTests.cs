using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class BreakTests
{
    private static DateTime Day => new(2026, 10, 9);

    [AvaloniaFact]
    public void BreakStopsTheAccountAndEndingItGoesBackToIt()
    {
        var now = Day.AddHours(10); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        model.ToggleAccount("Account one"); now = now.AddMinutes(30);
        model.ToggleBreak(); now = now.AddMinutes(20);
        Assert.True(model.IsOnBreak); Assert.Equal("On break", model.Status); Assert.Equal("On break", model.AccountLabel); Assert.Equal("Account one", model.ResumeAccount);
        Assert.Equal("Account one", model.SelectedAccount); // The break is never the selected account.
        model.TogglePause(); now = now.AddMinutes(15); // Resume on the pill ends the break too.
        Assert.False(model.IsOnBreak); Assert.Equal("Account one", model.ActiveAccount);
        model.ToggleBreak(); now = now.AddMinutes(5); model.ToggleBreak(); // Take a second break; the same button ends it.
        Assert.Equal("Account one", model.ActiveAccount);
        now = now.AddMinutes(5); model.TogglePause();
        Assert.Equal(new[] { "Account one", "Break", "Account one", "Break", "Account one" }, engine.Sessions.Select(s => s.Account));
        Assert.Equal(TimeSpan.FromMinutes(25), engine.Credited("Break", Day, Day.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(30 + 15 + 5), engine.Credited("Account one", Day, Day.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(50), engine.Totals(now).Allocated); // The break is not allocated to any account.
    }

    [AvaloniaFact]
    public void BreakWithNothingRunningEndsWithNothingRunning()
    {
        var now = Day.AddHours(10); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        model.ToggleBreak(); now = now.AddMinutes(10); Assert.True(model.IsOnBreak); Assert.Null(model.ResumeAccount);
        model.ToggleBreak(); Assert.False(model.IsTracking); Assert.False(model.IsOnBreak);
    }

    [AvaloniaFact]
    public void TheAccountToReturnToIsFoundFromTheRecordedTime()
    {
        var now = Day.AddHours(10); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        model.ToggleAccount("Account two"); now = now.AddMinutes(30);
        engine.ToggleAccount("Break"); // Started by the engine, so the model remembers nothing about it.
        Assert.Equal("Account two", model.ResumeAccount);
        model.TogglePause(); Assert.Equal("Account two", model.ActiveAccount);
    }

    [AvaloniaFact]
    public void BreakHasItsOwnButtonsAndIsNotAnAccountRow()
    {
        var now = Day.AddHours(10); var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine);
        var dashboard = new DashboardWindow(model); dashboard.Show(); var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            model.ToggleAccount("Account one"); now = now.AddMinutes(10); Dispatcher.UIThread.RunJobs();
            var rows = dashboard.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("Account") == true && char.IsDigit(b.Name[^1])).ToList();
            Assert.Equal(model.Accounts.Count - 1, rows.Count);
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Break");
            var dashboardBreak = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardBreak");
            var pillBreak = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillBreak");
            Assert.Contains("breakButton", dashboardBreak.Classes); Assert.DoesNotContain("onBreak", dashboardBreak.Classes);
            Assert.Contains(dashboardBreak.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Start break");
            var point = dashboardBreak.TranslatePoint(new Point(dashboardBreak.Bounds.Width / 2, dashboardBreak.Bounds.Height / 2), dashboard)!.Value;
            dashboard.MouseDown(point, MouseButton.Left); dashboard.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            now = now.AddMinutes(30); model.ChangeDate(0); Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsOnBreak); Assert.Contains("onBreak", dashboardBreak.Classes); Assert.Contains("onBreak", pillBreak.Classes);
            Assert.Contains(dashboardBreak.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Finish break");
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "On break");
            Assert.True(pill.GetVisualDescendants().OfType<BreakGlyph>().Single().Steaming); // The steam moves while the break is on.
            Assert.True(dashboard.GetVisualDescendants().OfType<BreakGlyph>().Single().Steaming);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                using var dashboardFrame = dashboard.CaptureRenderedFrame(); dashboardFrame!.Save(Path.Combine(folder, "break-dashboard.png"));
                using var pillFrame = pill.CaptureRenderedFrame(); pillFrame!.Save(Path.Combine(folder, "break-pill.png"));
            }
            var timeline = dashboard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is "Break" or "● Break").ToList(); // A short recording block puts its dot in front of the name.
            Assert.NotEmpty(timeline); // The running break block is labelled Break.
            pillBreak.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsOnBreak); Assert.Equal("Account one", model.ActiveAccount); Assert.DoesNotContain("onBreak", pillBreak.Classes);
            Assert.False(pill.GetVisualDescendants().OfType<BreakGlyph>().Single().Steaming); // And it is still when the break ends.
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheEntryEditorOffersBreakAndSavesItAsTheBreakAccount()
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(14)); var row = new SessionRow("2026-10-09", "Account one", "09:00:00", "added", "30"); engine.Sessions.Add(row);
        var model = new TimerModel(engine);
        var editor = new EntryWindow(model, row, row.From, row.To); editor.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = editor.CaptureRenderedFrame();
            var accounts = editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EntryAccount");
            var items = accounts.ItemsSource!.Cast<string>().ToList();
            Assert.Contains("Break", items); Assert.DoesNotContain("Lunch", items);
            accounts.SelectedItem = "Break";
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Break", engine.Sessions.Single().Account);
        }
        finally { editor.Close(); }
    }

    [AvaloniaFact]
    public void BreakIsAReservedName()
    {
        var model = new TimerModel(new TrackingEngine());
        Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings.Append(new("break"))));
        Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings.Append(new("Break"))));
    }

    [Fact]
    public void ExportsCallItBreak()
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(15));
        engine.Sessions.Add(new("2026-10-09", "Account one", "09:00:00", "added", "60")); engine.Sessions.Add(new("2026-10-09", "Break", "10:00:00", "added", "30"));
        var export = new WeekExport(new LedgerEditor(engine), engine, new DateTime(2026, 10, 5));
        var summary = export.SummaryCsv(new[] { "Account one" });
        Assert.Contains("\"Break\"", summary); Assert.Contains("Total (excluding breaks)", summary); Assert.DoesNotContain("Lunch", summary);
    }

    [Fact]
    public void ABreakIsStoredAsItsOwnKindWithTheAccountToReturnTo_AndLunchIsAnOrdinaryName()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-break-kind-" + Guid.NewGuid());
        var now = Day.AddHours(10);
        try
        {
            using var store = new TimerStore(folder);
            var engine = new TrackingEngine(clock: () => now); var prefs = store.Load(engine);
            engine.ToggleAccount("Account one"); now = now.AddMinutes(30); engine.ToggleAccount(BreakAccount.Name);
            Assert.Equal("Account one", engine.ResumeAfterBreak); store.Save(engine, prefs);
            Assert.Equal("break", store.Database.Scalar("SELECT kind FROM running")); Assert.Equal("Account one", store.Database.Scalar("SELECT name FROM accounts WHERE id = (SELECT resume_account_id FROM running)"));
            now = now.AddMinutes(15); engine.ToggleAccount("Account one"); Assert.Null(engine.ResumeAfterBreak); store.Save(engine, prefs);
            Assert.Equal(1L, store.Database.Scalar("SELECT COUNT(*) FROM entries WHERE kind = 'break' AND account_id IS NULL"));
            Assert.Equal(0L, store.Database.Scalar("SELECT COUNT(*) FROM accounts WHERE name IN ('Break', 'Lunch')"));
            engine.AddSession("Lunch", now, now.AddMinutes(5)); // Now just a name, like any other.
            Assert.Equal(TimeSpan.FromMinutes(5), engine.Totals(now.Date).Allocated - TimeSpan.FromMinutes(30));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AnExportIsCheckedAgainstTheSavedRecordsAndStoppedWhenTheyDisagree()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-export-check-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = new TrackingEngine(clock: () => Day.AddHours(18)); var prefs = store.Load(engine);
            engine.AddSession("Account one", Day.AddHours(9), Day.AddHours(10)); engine.AddSession(BreakAccount.Name, Day.AddHours(12), Day.AddHours(12.5));
            store.Save(engine, prefs);
            var export = new WeekExport(store.Editor!, engine, new DateTime(2026, 10, 5));
            export.VerifyAgainst(store.Database); // Matches, including the break.
            engine.AddSession("Account two", Day.AddHours(13), Day.AddHours(14)); // Not saved yet.
            Assert.Throws<InvalidDataException>(() => new WeekExport(store.Editor!, engine, new DateTime(2026, 10, 5)).VerifyAgainst(store.Database));
            store.Save(engine, prefs); new WeekExport(store.Editor!, engine, new DateTime(2026, 10, 5)).VerifyAgainst(store.Database);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
