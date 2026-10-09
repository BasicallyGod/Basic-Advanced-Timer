using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class CreditReviewTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TrackingEngine Engine() => new(clock: () => Day.AddHours(15));
    private static SessionRow Row(string start, string minutes, string end = "added", string account = "Account one") => new("2026-10-09", account, start, end, minutes);
    [Fact]
    public void PlacementUsesExistingCreditAndUndoRestoresMetadataAcrossRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-credit-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); engine.Sessions.Add(Row("00:00:00", "90", "merged"));
                var editor = store.Editor!; var original = editor.SessionsCsv;
                editor.Place("Account one", Day.AddHours(9), 30, "Review, note"); store.Save(engine, prefs);
                Assert.Equal(original, editor.SessionsCsv); Assert.Equal(60, editor.Unplaced(Day)["Account one"]);
                Assert.Contains("Review, note", editor.PositionsCsv);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                Assert.Single(editor.Positions); Assert.True(editor.CanUndo); editor.Undo(); store.Save(engine, prefs);
                Assert.Empty(editor.Positions); Assert.Equal(90, editor.Unplaced(Day)["Account one"]); Assert.Single(engine.Sessions);
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void PlacementRejectsExcessOverlapFutureAndStaleEditsWithoutChangingTotals()
    {
        var engine = Engine(); engine.Sessions.Add(Row("00:00:00", "60", "merged")); engine.Sessions.Add(Row("10:00:00", "60"));
        var editor = new LedgerEditor(engine); var original = editor.SessionsCsv;
        editor.Place("Account one", Day.AddHours(9), 30, ""); var position = editor.Positions.Single();
        Assert.Throws<ArgumentException>(() => editor.Place("Account one", Day.AddHours(12), 31, ""));
        Assert.Throws<ArgumentException>(() => editor.Place("Account one", Day.AddHours(10), 20, ""));
        Assert.Throws<ArgumentException>(() => editor.Add("Internal", Day.AddHours(9), Day.AddHours(9.25), ""));
        Assert.Throws<ArgumentException>(() => editor.Place("Account one", Day.AddHours(16), 20, ""));
        editor.Place("Account one", Day.AddHours(12), 60, "Moved", position);
        Assert.Equal(0, editor.Unplaced(Day)["Account one"]);
        Assert.Throws<InvalidOperationException>(() => editor.Unplace(position));
        editor.Unplace(editor.Positions.Single()); Assert.Equal(60, editor.Unplaced(Day)["Account one"]);
        Assert.Equal(original, editor.SessionsCsv);
    }
    [Fact]
    public void GroupsPreserveIntervalsNotesAndTotalsAndPruneAfterSourceEdits()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
        var editor = new LedgerEditor(engine); var original = editor.SessionsCsv;
        editor.Group(engine.Sessions.ToArray()); var entry = Assert.Single(editor.Entries(Day, Day.AddDays(1)));
        Assert.Equal("Grouped sessions", entry.Type); Assert.Equal(60, entry.Minutes); Assert.Null(entry.Start);
        Assert.Equal(original, editor.SessionsCsv);
        editor.Ungroup(entry.GroupId!); Assert.Equal(2, editor.Entries(Day, Day.AddDays(1)).Count);
        editor.Undo(); Assert.Single(editor.Entries(Day, Day.AddDays(1)));
        var first = engine.Sessions[0]; editor.Edit(first, first.Account, first.From, first.To.AddMinutes(5), "note");
        Assert.Equal(2, editor.Entries(Day, Day.AddDays(1)).Count);
        editor.Undo(); Assert.Single(editor.Entries(Day, Day.AddDays(1)));
    }
    [Fact]
    public void ProjectionReconcilesMultipleMergedRowsWithPartialPositionsAndNegativeAdjustments()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("00:00:00", "30", "merged"), Row("00:00:01", "40", "merged"), Row("00:00:02", "-2.33", "adjusted") });
        var editor = new LedgerEditor(engine); editor.Place("Account one", Day.AddHours(9), 50, "placed");
        var entries = editor.Entries(Day, Day.AddDays(1));
        Assert.Equal(67.67m, entries.Sum(e => e.Minutes)); Assert.Equal(20, entries.Single(e => e.Type.Contains("unplaced")).Minutes);
        var export = new WeekExport(editor, engine, Day.AddDays(-4));
        Assert.Equal(113, export.TotalUnits); Assert.Equal(export.TotalUnits, export.EntryUnits.Sum());
    }
    [Fact]
    public void LargestRemainderMakesDetailDailyAccountAndGrandTotalsAgreeIncludingNegatives()
    {
        var engine = Engine(); var random = new Random(123);
        for (var i = 0; i < 200; i++) engine.Sessions.Add(new(Day.AddDays(-(i % 5)).ToString("yyyy-MM-dd"), i % 3 == 0 ? "Lunch" : "Account " + i % 7,
            "00:00:00", "adjusted", ((random.Next(-50, 300)) / 100m).ToString(CultureInfo.InvariantCulture)));
        var editor = new LedgerEditor(engine); var export = new WeekExport(editor, engine, Day.AddDays(-4));
        Assert.Equal(export.TotalUnits, export.Accounts.Where(a => a.Key != "Lunch").Sum(a => a.Value.Sum()));
        Assert.Equal(export.TotalUnits + export.LunchUnits, export.EntryUnits.Sum());
        for (var i = 0; i < export.Entries.Count; i++) Assert.InRange(Math.Abs(export.EntryUnits[i] - export.Entries[i].Minutes / 60m * 100m), 0, 1);
        foreach (var account in export.Accounts)
            for (var d = 0; d < 7; d++) Assert.Equal(account.Value[d], export.Entries.Select((e, i) => (e, i)).Where(p => p.e.Account == account.Key && p.e.Day == Day.AddDays(-4 + d)).Sum(p => export.EntryUnits[p.i]));
        var folder = Path.Combine(Path.GetTempPath(), "timer-export-" + Guid.NewGuid());
        try { var path = export.Write(folder, new[] { "Account one" }); Assert.Equal(2, Directory.GetFiles(path).Length); Assert.Contains("Total (excluding lunch)", File.ReadAllText(Path.Combine(path, "Summary.csv"))); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void TidyPreviewsEligibleGapsAndRechecksEntireBatchBeforeAddingTime()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:33:00", "27"), Row("10:04:00", "26", account: "Account two") });
        var editor = new LedgerEditor(engine); var gaps = editor.TidyCandidates(Day); Assert.Equal(2, gaps.Count);
        var choices = gaps.Select(g => new GapChoice(g, g.Previous)).ToArray();
        engine.Away.Add(new(gaps[1].Start, gaps[1].End));
        Assert.Throws<InvalidOperationException>(() => editor.Tidy(choices)); Assert.Equal(3, engine.Sessions.Count);
        engine.Away.Clear(); editor.Tidy(choices); Assert.Equal(5, engine.Sessions.Count); Assert.Equal(90, engine.Sessions.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture)));
        editor.Undo(); Assert.Equal(3, engine.Sessions.Count);
        engine.Sessions[1] = engine.Sessions[1] with { Account = "Lunch" }; Assert.Empty(editor.TidyCandidates(Day));
    }
    [Fact]
    public void ImportedPositionsKeepUnknownColumnsAndInvalidCreditFailsReview()
    {
        var engine = Engine(); engine.Sessions.Add(Row("00:00:00", "60", "merged"));
        var csv = "Id,Date,Account,Start,Minutes,Extra\np1,2026-10-09,Account one,09:00:00,30,keep\n";
        var editor = new LedgerEditor(engine, positionsCsv: csv); var p = editor.Positions.Single();
        editor.Place(p.Account, p.Start.AddHours(1), p.Minutes, "New note", p);
        Assert.Contains("keep", editor.PositionsCsv); Assert.Contains("New note", editor.PositionsCsv);
        Assert.Throws<InvalidDataException>(() => new LedgerEditor(engine, positionsCsv: csv.Replace(",30,", ",90,")));
    }
    [AvaloniaFact]
    public void NativeReviewControlsPlaceGroupTidyAndExport()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("00:00:00", "60", "merged"), Row("10:00:00", "30"), Row("10:33:00", "27") });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        var place = new PositionWindow(model); place.Show(dashboard);
        Find<TextBox>(place, "PositionStart").Text = "09:00";
        Capture(place, "credit-position-editor");
        Find<Button>(place, "SavePosition").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(model.Editor.Positions); Assert.False(place.IsVisible);
        Capture(dashboard, "positioned-credit-timeline");
        var grouping = new GroupWindow(model); grouping.Show(dashboard);
        Dispatcher.UIThread.RunJobs(); using var groupFrame = grouping.CaptureRenderedFrame();
        foreach (var box in grouping.GetVisualDescendants().OfType<CheckBox>()) box.IsChecked = true;
        Capture(grouping, "group-editor");
        Find<Button>(grouping, "SaveGroup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains(model.Editor.Entries(Day, Day.AddDays(1)), e => e.GroupId is not null);
        var tidy = new TidyWindow(model); tidy.Show(dashboard);
        Dispatcher.UIThread.RunJobs(); using var tidyFrame = tidy.CaptureRenderedFrame();
        foreach (var box in tidy.GetVisualDescendants().OfType<ComboBox>()) box.SelectedIndex = 1;
        Capture(tidy, "tidy-day-review");
        Find<Button>(tidy, "ApplyTidy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, engine.Sessions.Count);
        var export = new ExportWindow(model); export.Show(dashboard); Find<Button>(export, "ExportCsv").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Capture(export, "weekly-export-result");
        dashboard.SelectTab(true); Capture(dashboard, "grouped-credit-week");
        Assert.NotNull(model.ExportPath); Assert.True(File.Exists(Path.Combine(model.ExportPath!, "Detail.csv")));
        Directory.Delete(model.ExportPath!, true); export.Close(); dashboard.Close();
    }
    private static T Find<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Capture(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.Combine(Path.GetTempPath(), "timer-credit-previews");
        Directory.CreateDirectory(folder); Dispatcher.UIThread.RunJobs();
        using var warmup = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame!.Save(Path.Combine(folder, name + ".png"));
    }

}
