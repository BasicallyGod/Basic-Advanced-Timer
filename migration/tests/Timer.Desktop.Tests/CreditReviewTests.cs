using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
    public void PlacementRejectsExcessOverlapAndStaleEditsWithoutChangingTotals()
    {
        var engine = Engine(); engine.Sessions.Add(Row("00:00:00", "60", "merged")); engine.Sessions.Add(Row("10:00:00", "60"));
        var editor = new LedgerEditor(engine); var original = editor.SessionsCsv;
        editor.Place("Account one", Day.AddHours(9), 30, ""); var position = editor.Positions.Single();
        Assert.Throws<ArgumentException>(() => editor.Place("Account one", Day.AddHours(12), 31, ""));
        Assert.Throws<ArgumentException>(() => editor.Place("Account one", Day.AddHours(10), 20, ""));
        Assert.Throws<ArgumentException>(() => editor.Add("Internal", Day.AddHours(9), Day.AddHours(9.25), ""));
        editor.Place("Account one", Day.AddHours(16), 20, "Future placement"); editor.Undo();
        editor.Place("Account one", Day.AddHours(12), 60, "Moved", position);
        Assert.Equal(0, editor.Unplaced(Day)["Account one"]);
        Assert.Throws<InvalidOperationException>(() => editor.Unplace(position));
        editor.Unplace(editor.Positions.Single()); Assert.Equal(60, editor.Unplaced(Day)["Account one"]);
        Assert.Equal(original, editor.SessionsCsv);
    }
    [Fact]
    public void GroupsPreserveIntervalsNotesAndTotalsAndPruneAfterSourceEdits()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30") });
        var editor = new LedgerEditor(engine); var original = editor.SessionsCsv;
        editor.Group(engine.Sessions.ToArray()); var entry = Assert.Single(editor.Entries(Day, Day.AddDays(1)));
        Assert.Equal("Grouped sessions", entry.Type); Assert.Equal(60, entry.Minutes); Assert.Null(entry.Start);
        Assert.Equal(original, editor.SessionsCsv);
        editor.Ungroup(entry.GroupId!); Assert.Equal(2, editor.Entries(Day, Day.AddDays(1)).Count);
        editor.Undo(); Assert.Single(editor.Entries(Day, Day.AddDays(1)));
        var first = engine.Sessions[0]; editor.Edit(first, first.Account, first.From, first.To.AddMinutes(-5), "note");
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
        Assert.DoesNotContain("\"Lunch\"", export.DetailCsv());
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
        var tidy = new TidyWindow(model); tidy.Show(dashboard);
        Dispatcher.UIThread.RunJobs(); using var tidyFrame = tidy.CaptureRenderedFrame();
        foreach (var box in tidy.GetVisualDescendants().OfType<ComboBox>()) box.SelectedIndex = 1;
        Capture(tidy, "tidy-day-review");
        Find<Button>(tidy, "ApplyTidy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, engine.Sessions.Count);
        var grouping = new GroupWindow(model); grouping.Show(dashboard);
        Dispatcher.UIThread.RunJobs(); using var groupFrame = grouping.CaptureRenderedFrame();
        foreach (var box in grouping.GetVisualDescendants().OfType<CheckBox>()) box.IsChecked = true;
        Capture(grouping, "group-editor");
        Find<Button>(grouping, "SaveGroup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains(model.Editor.Entries(Day, Day.AddDays(1)), e => e.GroupId is not null);
        var export = new ExportWindow(model); export.Show(dashboard); Find<Button>(export, "ExportCsv").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Capture(export, "weekly-export-result");
        dashboard.SelectTab(true); Capture(dashboard, "grouped-credit-week");
        Assert.NotNull(model.ExportPath); Assert.True(File.Exists(Path.Combine(model.ExportPath!, "Detail.csv")));
        Directory.Delete(model.ExportPath!, true); export.Close(); dashboard.Close();
    }

    [Fact]
    public void FutureEntriesCanBeAddedMovedAndPlacedWithinOneDay()
    {
        var engine = Engine(); var editor = new LedgerEditor(engine);
        editor.Add("Account one", Day.AddDays(1).AddHours(9), Day.AddDays(1).AddHours(10), "Tomorrow");
        var row = engine.Sessions.Single(); editor.Edit(row, row.Account, row.From.AddHours(1), row.To.AddHours(1), "Moved");
        Assert.Equal(Day.AddDays(1).AddHours(10), engine.Sessions.Single().From);
        engine.Sessions.Add(new("2026-10-10", "Account one", "00:00:00", "merged", "30"));
        editor.Place("Account one", Day.AddDays(1).AddHours(12), 30, "Future credit");
        Assert.Equal(0, editor.Unplaced(Day.AddDays(1))["Account one"]);
        Assert.Throws<ArgumentException>(() => editor.Place("Account one", Day.AddDays(1).AddHours(10), 30, "Overlap", editor.Positions.Single()));
    }
    [AvaloniaFact]
    public void GroupIsOneTimelineCardAndWeekUngroupRestoresSourceCards()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "27") });
        var model = new TimerModel(engine); model.GroupEntries(engine.Sessions.ToArray());
        var dashboard = new DashboardWindow(model); dashboard.Show(); Capture(dashboard, "combined-group-timeline");
        Assert.Single(dashboard.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is ReviewEntry));
        Assert.Empty(dashboard.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow));
        dashboard.SelectTab(true); Capture(dashboard, "explicit-ungroup-week");
        var ungroup = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name?.StartsWith("Ungroup") == true);
        ungroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dashboard.SelectTab(false); Capture(dashboard, "ungrouped-timeline");
        Assert.Equal(2, dashboard.GetVisualDescendants().OfType<Border>().Count(b => b.Tag is SessionRow));
        Assert.Equal(57, engine.Sessions.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture))); dashboard.Close();
    }
    [AvaloniaFact]
    public void DashboardHasRoundedSurfaceAndSlimScrollbarStillDrags()
    {
        var model = TimerModel.CreateDemo(() => Day.AddHours(12)); var dashboard = new DashboardWindow(model); dashboard.Show();
        Capture(dashboard, "dashboard-rounded-slim-scrollbars");
        var surface = Find<Border>(dashboard, "DashboardSurface"); Assert.Equal(new CornerRadius(20), surface.CornerRadius); Assert.True(surface.ClipToBounds);
        var scroll = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.InRange(bar.Bounds.Width, 6, 8);
        Assert.Empty(bar.GetVisualDescendants().OfType<RepeatButton>());
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), dashboard)!.Value;
        var old = scroll.Offset.Y; dashboard.MouseDown(point, MouseButton.Left); dashboard.MouseMove(point + new Vector(0, 20)); dashboard.MouseUp(point + new Vector(0, 20), MouseButton.Left);
        Assert.True(scroll.Offset.Y > old); dashboard.Close();
    }

    [AvaloniaFact]
    public void CtrlClickSelectsWithoutDraggingAndToolbarAndContextMenuMerge()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("10:00:00", "30") });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        var csv = model.Editor.SessionsCsv;
        void Select(int index)
        {
            Capture(dashboard, "merge-selection");
            var card = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + index);
            var point = card.TranslatePoint(new Point(70, card.Height / 2), dashboard)!.Value;
            dashboard.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
            dashboard.MouseMove(point + new Vector(0, 15), RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
            dashboard.MouseUp(point + new Vector(0, 15), MouseButton.Left, RawInputModifiers.Control);
        }
        Select(0); Select(1); Assert.Equal(2, model.SelectedRows.Count); Assert.True(model.CanMergeSelection);
        Select(1); Assert.Single(model.SelectedRows); Assert.False(model.CanMergeSelection); Select(1);
        Capture(dashboard, "merge-two-selected"); Assert.Equal(csv, model.Editor.SessionsCsv);
        Find<Button>(dashboard, "MergeSelected").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(model.SelectedRows); Assert.Single(model.Editor.Entries(Day, Day.AddDays(1)).Where(e => e.GroupId is not null));
        Capture(dashboard, "merge-combined-selection");
        var groupCard = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is ReviewEntry);
        var p = groupCard.TranslatePoint(new Point(70, 35), dashboard)!.Value;
        dashboard.MouseDown(p, MouseButton.Left, RawInputModifiers.Control); dashboard.MouseUp(p, MouseButton.Left, RawInputModifiers.Control);
        Select(2); Assert.True(model.CanMergeSelection); Capture(dashboard, "merge-group-and-block");
        var last = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry2");
        var menu = last.ContextMenu!.ItemsSource!.Cast<MenuItem>().Single(i => i.Name == "MergeSelectedMenu");
        Assert.True(menu.IsEnabled); menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var combined = Assert.Single(model.Editor.Entries(Day, Day.AddDays(1))); Assert.Equal(90, combined.Minutes); Assert.Equal(3, combined.Sources.Count);
        Assert.Equal(csv, model.Editor.SessionsCsv); dashboard.Close();
    }
    [AvaloniaFact]
    public void MergeContextMenuCaptureLossKeepsBothSelectedBlocksInPlace()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "50"), Row("09:53:00", "55") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        model.ToggleSelection(engine.Sessions.ToArray()); Capture(window, "merge-menu-before");
        var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow).OrderBy(Canvas.GetTop).ToArray();
        Assert.Equal(2, cards.Length); var tops = cards.Select(Canvas.GetTop).ToArray();
        var heights = cards.Select(b => b.Height).ToArray(); var csv = model.Editor.SessionsCsv;
        IPointer? pointer = null; cards[1].PointerPressed += (_, e) => pointer = e.Pointer;
        var point = cards[1].TranslatePoint(new Point(70, 30), window)!.Value;
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right);
        Assert.NotNull(pointer); Assert.True(cards[1].ContextMenu!.IsOpen);
        // Exercise the capture-loss notification produced by native popup transitions.
        pointer!.Capture(cards[1]); pointer.Capture(null);
        Capture(window, "merge-menu-open");
        Assert.Equal(tops, cards.Select(Canvas.GetTop).ToArray()); Assert.Equal(heights, cards.Select(b => b.Height).ToArray());
        Assert.All(cards, b => { Assert.True(b.IsVisible); Assert.Equal(1, b.Opacity); });
        Assert.Equal(2, model.SelectedRows.Count); Assert.Equal(csv, model.Editor.SessionsCsv);
        var menu = cards[1].ContextMenu!.ItemsSource!.Cast<MenuItem>().Single(item => item.Name == "MergeSelectedMenu");
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(108, Assert.Single(model.Editor.Entries(Day, Day.AddDays(1))).Minutes); window.Close();
    }
    [AvaloniaFact]
    public void LosingCaptureDuringActualDragRestoresBlockWithoutEditingTime()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "50"));
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show(); Capture(window, "capture-cancel-before");
        var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is SessionRow);
        var top = Canvas.GetTop(card); var height = card.Height; var csv = model.Editor.SessionsCsv;
        IPointer? pointer = null; card.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel, true);
        var point = card.TranslatePoint(new Point(70, 30), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(0, 20), RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(top, Canvas.GetTop(card)); Assert.NotNull(pointer); pointer!.Capture(null);
        Assert.Equal(top, Canvas.GetTop(card)); Assert.Equal(height, card.Height); Assert.Equal(1, card.Opacity);
        window.MouseUp(point + new Vector(0, 20), MouseButton.Left);
        Assert.Equal(csv, model.Editor.SessionsCsv); window.Close();
    }
    [AvaloniaFact]
    public void DragAndResizeSnapToNeighbourTimesBetweenFiveMinuteMarks()
    {
        void Check(string firstStart, string firstMinutes, string nextStart, string nextMinutes,
            int index, double fraction, double minutes, DateTime expectedFrom, DateTime expectedTo)
        {
            var engine = Engine(); engine.Sessions.AddRange(new[] { Row(firstStart, firstMinutes), Row(nextStart, nextMinutes) });
            var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
            try
            {
                Capture(window, "snap-neighbour-before"); var original = model.Editor.SessionsCsv;
                var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + index);
                var point = card.TranslatePoint(new Point(70, card.Height * fraction), window)!.Value;
                var target = point + new Vector(0, minutes / 60 * 160);
                window.MouseDown(point, MouseButton.Left); window.MouseMove(target); window.MouseUp(target, MouseButton.Left);
                Assert.Null(model.EditError); Assert.Equal(expectedFrom, engine.Sessions[index].From); Assert.Equal(expectedTo, engine.Sessions[index].To);
                Assert.True(model.UndoEdit()); Assert.Equal(original, model.Editor.SessionsCsv);
            }
            finally { window.Close(); }
        }
        // Move upward against a 09:53 end, keeping the dragged block's duration.
        Check("09:00:00", "53", "10:00:00", "30", 1, .5, -7, Day.AddHours(9).AddMinutes(53), Day.AddHours(10).AddMinutes(23));
        // Move downward until the bottom edge meets a 10:03 start.
        Check("09:00:00", "30", "10:03:00", "30", 0, .5, 33, Day.AddHours(9).AddMinutes(33), Day.AddHours(10).AddMinutes(3));
        // Substantial overshoots stop at the first neighbour rather than overlap or jump past it.
        Check("09:00:00", "53", "10:00:00", "30", 1, .5, -60, Day.AddHours(9).AddMinutes(53), Day.AddHours(10).AddMinutes(23));
        Check("09:00:00", "30", "10:03:00", "30", 0, .5, 90, Day.AddHours(9).AddMinutes(33), Day.AddHours(10).AddMinutes(3));
        Check("09:00:00", "53", "10:00:00", "30", 1, .01, -60, Day.AddHours(9).AddMinutes(53), Day.AddHours(10.5));
        Check("09:00:00", "30", "10:03:00", "30", 0, .99, 90, Day.AddHours(9), Day.AddHours(10).AddMinutes(3));
        // Resize either edge to the same exact boundaries.
        Check("09:00:00", "53", "10:00:00", "30", 1, .01, -7, Day.AddHours(9).AddMinutes(53), Day.AddHours(10.5));
        Check("09:00:00", "30", "10:03:00", "30", 0, .99, 33, Day.AddHours(9), Day.AddHours(10).AddMinutes(3));
    }
    [Fact]
    public void MergeFillsGapsButRejectsInterveningAccountsAndPartialGroups()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
        var editor = new LedgerEditor(engine); var csv = editor.SessionsCsv;
        editor.Group(engine.Sessions.ToArray()); Assert.Equal(90, engine.Sessions.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture)));
        editor.Undo(); Assert.Equal(csv, editor.SessionsCsv);
        engine.Sessions[1] = Row("09:30:00", "30"); engine.Sessions.Add(Row("09:15:00", "30", account: "Account two"));
        Assert.Throws<ArgumentException>(() => editor.Group(engine.Sessions.Take(2).ToArray()));
        engine.Sessions.RemoveAt(2); editor.Group(engine.Sessions.ToArray()); engine.Sessions.Add(Row("10:00:00", "30"));
        Assert.Throws<ArgumentException>(() => editor.Group(new[] { engine.Sessions[1], engine.Sessions[2] }));
    }
    [AvaloniaFact]
    public void OldNonconsecutiveGroupsRenderSeparateSectionsAndWeekCtrlClickSelects()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
        var folder = Path.Combine(Path.GetTempPath(), "timer-old-group-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(folder, "Timesheets"));
        File.WriteAllText(Path.Combine(folder, "Timesheets/sessions.csv"), "Date,Account,Start,End,Minutes\n2026-10-09,Account one,09:00:00,added,30\n2026-10-09,Account one,10:00:00,added,30\n");
        File.WriteAllText(Path.Combine(folder, "Timesheets/entry-groups.csv"), "GroupId,Date,Account,Start,End,Minutes\ng,2026-10-09,Account one,09:00:00,added,30\ng,2026-10-09,Account one,10:00:00,added,30\n");
        try
        {
            using var store = new TimerStore(folder); engine = Engine(); var preferences = store.Load(engine); var model = new TimerModel(engine, preferences, store);
            var window = new DashboardWindow(model); window.Show(); Capture(window, "legacy-group-sections");
            Assert.Equal(2, window.GetVisualDescendants().OfType<Border>().Count(b => b.Tag is ReviewEntry));
            window.SelectTab(true); Capture(window, "week-ctrl-selection");
            var entry = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekReviewEntry");
            var point = entry.TranslatePoint(new Point(50, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
            Assert.Equal(2, model.SelectedRows.Count); Assert.False(model.CanMergeSelection); window.Close();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void MergeAddsAllBlankGapsAsOneUndoableLedgerChange()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30"), Row("11:00:00", "30") });
        var editor = new LedgerEditor(engine); var original = editor.SessionsCsv;
        Assert.Equal(60, editor.MergeGapMinutes(engine.Sessions.ToArray())); editor.Group(engine.Sessions.ToArray());
        Assert.Equal(5, engine.Sessions.Count); var group = Assert.Single(editor.Entries(Day, Day.AddDays(1)));
        Assert.Equal(150, group.Minutes); Assert.Equal(5, group.Sources.Count);
        Assert.All(engine.Sessions.Skip(3), s => Assert.Equal("Added by merge", editor.Note(s)));
        var export = new WeekExport(editor, engine, Day.AddDays(-4)); Assert.Equal(250, export.TotalUnits);
        editor.Undo(); Assert.Equal(original, editor.SessionsCsv); Assert.Equal(3, editor.Entries(Day, Day.AddDays(1)).Count);
        editor.Group(engine.Sessions.ToArray()); group = Assert.Single(editor.Entries(Day, Day.AddDays(1)));
        editor.Ungroup(group.GroupId!); Assert.Equal(150, engine.Sessions.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture)));
    }
    [Fact]
    public void GapMergeUndoSurvivesRestartAndRunningOrPositionedTimePreventsIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-gap-merge-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var preferences = store.Load(engine);
                engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
                store.Editor!.Group(engine.Sessions.ToArray()); store.Save(engine, preferences);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var preferences = store.Load(engine); store.Editor!.Undo(); store.Save(engine, preferences);
                Assert.Equal(2, engine.Sessions.Count); Assert.Equal(60, engine.Sessions.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture)));
                engine.Sessions.Add(Row("00:00:00", "30", "merged", "Account two"));
                store.Editor.Place("Account two", Day.AddHours(9.5), 30, "In gap");
                Assert.False(store.Editor.CanCombine(engine.Sessions.Take(2).ToArray()));
            }
            var clock = Day.AddHours(9.5); var running = new TrackingEngine(clock: () => clock);
            running.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") }); running.ToggleAccount("Account two"); clock = Day.AddHours(9.75);
            Assert.False(new LedgerEditor(running).CanCombine(running.Sessions.ToArray()));
        }
        finally { Directory.Delete(folder, true); }
    }
    [AvaloniaFact]
    public void SingleClickBlankTimelineOpensOnePrefilledEditorAndSavesTime()
    {
        var engine = Engine(); engine.Sessions.Add(Row("10:00:00", "30")); var model = new TimerModel(engine);
        var window = new DashboardWindow(model); window.Show(); Capture(window, "click-to-add-timeline");
        var ten = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "10:00");
        var point = ten.TranslatePoint(new Point(180, 168), window)!.Value; // 11:00
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        var editor = Assert.Single(window.OwnedWindows.OfType<EntryWindow>()); Capture(editor, "click-to-add-editor");
        Assert.Equal("11:00:00", Find<TextBox>(editor, "EntryStart").Text); Assert.Equal("11:30:00", Find<TextBox>(editor, "EntryEnd").Text);
        Find<Button>(editor, "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, engine.Sessions.Count); Assert.Equal("added", engine.Sessions[1].End); window.Close();
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
