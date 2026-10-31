using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class CreditReviewTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TrackingEngine Engine() => new(clock: () => Day.AddHours(15));
    private static SessionRow Row(string start, string minutes, string end = "added", string account = "Account one") => new("2026-10-09", account, start, end, minutes);
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
    public void LargestRemainderMakesDetailDailyAccountAndGrandTotalsAgreeIncludingNegatives()
    {
        var engine = Engine(); var random = new Random(123);
        for (var i = 0; i < 200; i++) engine.Sessions.Add(new(Day.AddDays(-(i % 5)).ToString("yyyy-MM-dd"), i % 3 == 0 ? "Break" : "Account " + i % 7,
            "00:00:00", "adjusted", ((random.Next(-50, 300)) / 100m).ToString(CultureInfo.InvariantCulture)));
        var editor = new LedgerEditor(engine); var export = new WeekExport(editor, engine, Day.AddDays(-4));
        Assert.Equal(export.TotalUnits, export.Accounts.Where(a => a.Key != "Break").Sum(a => a.Value.Sum()));
        Assert.Equal(export.TotalUnits + export.LunchUnits, export.EntryUnits.Sum());
        for (var i = 0; i < export.Entries.Count; i++) Assert.InRange(Math.Abs(export.EntryUnits[i] - export.Entries[i].Minutes / 60m * 100m), 0, 1);
        foreach (var account in export.Accounts)
            for (var d = 0; d < 7; d++) Assert.Equal(account.Value[d], export.Entries.Select((e, i) => (e, i)).Where(p => p.e.Account == account.Key && p.e.Day == Day.AddDays(-4 + d)).Sum(p => export.EntryUnits[p.i]));
        Assert.DoesNotContain("\"Lunch\"", export.DetailCsv());
        var folder = Path.Combine(Path.GetTempPath(), "timer-export-" + Guid.NewGuid());
        try { var path = export.Write(folder, new[] { "Account one" }); Assert.Equal(2, Directory.GetFiles(path).Length); Assert.Contains("Total (excluding breaks)", File.ReadAllText(Path.Combine(path, "Summary.csv"))); }
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
        engine.Sessions[1] = engine.Sessions[1] with { Account = "Break" }; Assert.Empty(editor.TidyCandidates(Day));
    }
    // What the Details card says it is showing: the chip beside its title (nothing when nothing is selected).
    private static string Kind(Window w) => w.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "DetailsKind")?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "";

    [AvaloniaFact]
    public void MergedBlocksCanBeExportedAndTheWeekOverviewShowsThem()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("10:00:00", "30"), Row("10:33:00", "27") });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        model.ToggleSelection(engine.Sessions.ToArray()); Assert.True(model.MergeSelection());
        Assert.Single(model.Editor.Entries(Day, Day.AddDays(1)), e => e.GroupId is not null);
        Assert.True(model.ExportWeek()); // The export itself is in Settings, Export.
        dashboard.SelectTab(true); Capture(dashboard, "grouped-credit-week");
        Assert.NotNull(model.ExportPath); Assert.True(File.Exists(Path.Combine(model.ExportPath!, "Detail.csv")));
        Directory.Delete(model.ExportPath!, true); dashboard.Close();
    }

    [Fact]
    public void FutureEntriesCanBeAddedAndMovedWithinOneDay()
    {
        var engine = Engine(); var editor = new LedgerEditor(engine);
        editor.Add("Account one", Day.AddDays(1).AddHours(9), Day.AddDays(1).AddHours(10), "Tomorrow");
        var row = engine.Sessions.Single(); editor.Edit(row, row.Account, row.From.AddHours(1), row.To.AddHours(1), "Moved");
        Assert.Equal(Day.AddDays(1).AddHours(10), engine.Sessions.Single().From);
    }
    [AvaloniaFact]
    public void GroupIsOneTimelineCardAndUngroupRestoresSourceCards()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "27") });
        var model = new TimerModel(engine); model.Editor.Group(engine.Sessions.ToArray());
        var dashboard = new DashboardWindow(model); dashboard.Show(); Capture(dashboard, "combined-group-timeline");
        Assert.Single(dashboard.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is ReviewEntry));
        Assert.Empty(dashboard.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow));
        model.UngroupEntries(model.Editor.Entries(Day, Day.AddDays(1)).Single(e => e.GroupId is not null).GroupId!); Capture(dashboard, "ungrouped-timeline");
        Assert.Equal(2, dashboard.GetVisualDescendants().OfType<Border>().Count(b => b.Tag is SessionRow));
        Assert.Equal(57, engine.Sessions.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture))); dashboard.Close();
    }
    [AvaloniaFact]
    public void DashboardHasRoundedSurfaceAndTheTimelineScrollsAlongTheDayWithNoScrollBar()
    {
        var model = TimerModel.CreateDemo(() => Day.AddHours(12)); var dashboard = new DashboardWindow(model); dashboard.Show();
        Capture(dashboard, "dashboard-rounded");
        var surface = Find<Border>(dashboard, "DashboardSurface"); Assert.Equal(new CornerRadius(20), surface.CornerRadius); Assert.True(surface.ClipToBounds);
        var scroll = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
        Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility); // Scrolling is implied: the wheel slides along the day.
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), dashboard)!.Value;
        var old = scroll.Offset.X; dashboard.MouseWheel(point, new Vector(0, -3)); Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.X > old); dashboard.Close();
    }

    [AvaloniaFact]
    public void TheSelectedBlockCardSaysWhatIsSelectedAndNamesTheSpecificMergeProblem()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
        engine.Sessions.Add(Row("11:00:00", "30") with { Account = "Other account" });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            string Title() => Find<TextBlock>(dashboard, "SelectedTitle").Text + "|" + Kind(dashboard);
            Assert.Equal("Details|", Title()); Assert.Contains("Nothing is selected", Find<TextBlock>(dashboard, "SelectedEmpty").Text);
            model.ToggleSelection(new[] { engine.Sessions[0] }); dashboard.UpdateLayout();
            Assert.Equal("Details|Block", Title()); Assert.Equal("09:00:00", Find<TextBox>(dashboard, "SelectedStart").Text);
            model.ToggleSelection(new[] { engine.Sessions[1] }); dashboard.UpdateLayout();
            Assert.Equal("Details|2 blocks selected", Title()); Assert.Contains("Joins them", model.MergeHint);
            Assert.Contains("primary", Find<Button>(dashboard, "MergeSelected").Classes); Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SelectedNote"); // No note under the card.
            model.ToggleSelection(new[] { engine.Sessions[2] }); dashboard.UpdateLayout();
            Assert.Equal("Details|3 blocks selected", Title()); Assert.Contains("Same account", model.MergeHint);
            // Merge can still be pressed: it says why not in the toast.
            Assert.DoesNotContain("primary", Find<Button>(dashboard, "MergeSelected").Classes); Assert.True(Find<Button>(dashboard, "MergeSelected").IsEnabled);
            Find<Button>(dashboard, "MergeSelected").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Contains("Same account", model.EditError);
            Find<Button>(dashboard, "ClearSelection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dashboard.UpdateLayout();
            Assert.Empty(model.SelectedRows); Assert.Equal("Details|", Title());
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheBarUnderTheTimelineHasAddTimeAndUndoThenTheFiguresThenZoomInOneRow()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Capture(dashboard, "timeline-bar");
            Rect Box(Control c) => new(c.TranslatePoint(default, dashboard)!.Value, c.Bounds.Size);
            var add = Box(Find<Button>(dashboard, "AddTime")); var undo = Box(Find<Button>(dashboard, "UndoEdit"));
            var figures = Box(Find<StackPanel>(dashboard, "DayTotals")); var zoom = Box(Find<StackPanel>(dashboard, "TimelineControls"));
            Assert.True(add.Right <= undo.Left + .5 && undo.Right <= figures.Left + .5 && figures.Right <= zoom.Left + .5, "Add time, Undo, the figures and the zoom run left to right without overlapping.");
            foreach (var box in new[] { undo, figures, zoom }) Assert.True(Math.Abs(box.Center.Y - add.Center.Y) < 12, "They share one row.");
            Assert.Equal(2, zoom.Width > 0 ? Find<StackPanel>(dashboard, "TimelineControls").Children.Count : 0); // Zoom out and zoom in, as in the mock.
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void AClickSelectsOneBlockAndShiftClickAddsAnotherOrTakesOneOut()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30"), Row("11:00:00", "30") });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Capture(dashboard, "click-select");
            void Click(int index, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                var card = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + index);
                var point = card.TranslatePoint(new Point(card.Width / 2, card.Height / 2), dashboard)!.Value;
                dashboard.MouseDown(point, MouseButton.Left, modifiers); dashboard.MouseUp(point, MouseButton.Left, modifiers);
            }
            Click(0); Assert.Same(engine.Sessions[0], Assert.Single(model.SelectedRows));
            Click(1); Assert.Same(engine.Sessions[1], Assert.Single(model.SelectedRows)); // A plain click replaces the selection.
            Click(2, RawInputModifiers.Shift); Assert.Equal(2, model.SelectedRows.Count); // Shift adds.
            Click(1, RawInputModifiers.Shift); Assert.Same(engine.Sessions[2], Assert.Single(model.SelectedRows)); // And takes one out.
            Click(2); Assert.Empty(model.SelectedRows); // A plain click on the only selected block clears it.
            Click(0); Click(1, RawInputModifiers.Shift); Click(2); Assert.Same(engine.Sessions[2], Assert.Single(model.SelectedRows)); // A plain click on one of several leaves just that one.
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void ShiftClickSelectsAPairThatTheCardAndTheContextMenuMerge()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("10:00:00", "30") });
        var model = new TimerModel(engine); var dashboard = new DashboardWindow(model); dashboard.Show();
        var csv = model.Editor.SessionsCsv;
        void Select(int index, bool shift)
        {
            Capture(dashboard, "merge-selection");
            var card = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + index);
            var point = card.TranslatePoint(new Point(card.Width / 2, card.Height / 2), dashboard)!.Value;
            var keys = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
            dashboard.MouseDown(point, MouseButton.Left, keys);
            dashboard.MouseUp(point, MouseButton.Left, keys);
        }
        Select(0, false); Select(1, true); Assert.Equal(2, model.SelectedRows.Count); Assert.True(model.CanMergeSelection);
        model.ToggleSelection(new[] { engine.Sessions[1] }); Assert.Single(model.SelectedRows); Assert.False(model.CanMergeSelection); model.ToggleSelection(new[] { engine.Sessions[1] });
        Capture(dashboard, "merge-two-selected"); Assert.Equal(csv, model.Editor.SessionsCsv);
        Find<Button>(dashboard, "MergeSelected").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(model.SelectedRows); Assert.Single(model.Editor.Entries(Day, Day.AddDays(1)).Where(e => e.GroupId is not null));
        Assert.Equal("Merged", model.Notice); Assert.True(model.NoticeHasUndo);
        Capture(dashboard, "merge-combined-selection");
        var groupCard = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is ReviewEntry);
        var p = groupCard.TranslatePoint(new Point(groupCard.Width / 2, 35), dashboard)!.Value;
        dashboard.MouseDown(p, MouseButton.Left); dashboard.MouseUp(p, MouseButton.Left);
        Assert.Equal("Merged · 2 blocks", Kind(dashboard));
        Select(2, true); Assert.True(model.CanMergeSelection); Capture(dashboard, "merge-group-and-block");
        var last = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry2");
        var menu = last.ContextMenu!.ItemsSource!.OfType<MenuItem>().Single(i => i.Name == "MergeSelectedMenu");
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
        var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow).OrderBy(Canvas.GetLeft).ToArray();
        Assert.Equal(2, cards.Length); var tops = cards.Select(Canvas.GetLeft).ToArray();
        var heights = cards.Select(b => b.Width).ToArray(); var csv = model.Editor.SessionsCsv;
        IPointer? pointer = null; cards[1].PointerPressed += (_, e) => pointer = e.Pointer;
        var point = cards[1].TranslatePoint(new Point(cards[1].Width / 2, 30), window)!.Value;
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right);
        Assert.NotNull(pointer); Assert.True(cards[1].ContextMenu!.IsOpen);
        // Exercise the capture-loss notification produced by native popup transitions.
        pointer!.Capture(cards[1]); pointer.Capture(null);
        Capture(window, "merge-menu-open");
        Assert.Equal(tops, cards.Select(Canvas.GetLeft).ToArray()); Assert.Equal(heights, cards.Select(b => b.Width).ToArray());
        Assert.All(cards, b => { Assert.True(b.IsVisible); Assert.Equal(1, b.Opacity); });
        Assert.Equal(2, model.SelectedRows.Count); Assert.Equal(csv, model.Editor.SessionsCsv);
        var menu = cards[1].ContextMenu!.ItemsSource!.OfType<MenuItem>().Single(item => item.Name == "MergeSelectedMenu");
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(108, Assert.Single(model.Editor.Entries(Day, Day.AddDays(1))).Minutes); window.Close();
    }
    [AvaloniaFact]
    public void LosingCaptureDuringActualDragRestoresBlockWithoutEditingTime()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "50"));
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show(); Capture(window, "capture-cancel-before");
        var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is SessionRow);
        var left = Canvas.GetLeft(card); var width = card.Width; var csv = model.Editor.SessionsCsv;
        IPointer? pointer = null; card.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel, true);
        var point = card.TranslatePoint(new Point(card.Width / 2, card.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(left, Canvas.GetLeft(card)); Assert.NotNull(pointer); pointer!.Capture(null);
        Assert.Equal(left, Canvas.GetLeft(card)); Assert.Equal(width, card.Width); Assert.Equal(1, card.Opacity);
        window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
        Assert.Equal(csv, model.Editor.SessionsCsv); window.Close();
    }
    [AvaloniaFact]
    public void DragAndResizeSnapToNeighbourTimesBetweenFiveMinuteMarks()
    {
        void Check(string firstStart, string firstMinutes, string nextStart, string nextMinutes,
            int index, double fraction, double minutes, DateTime expectedFrom, DateTime expectedTo)
        {
            var engine = Engine(); engine.Sessions.AddRange(new[] { Row(firstStart, firstMinutes), Row(nextStart, nextMinutes) });
            var model = new TimerModel(engine); model.SetTimelineScale(160, 0, 0); model.TimelineOffset = null; var window = new DashboardWindow(model); window.Show(); // 160 pixels an hour.
            try
            {
                Capture(window, "snap-neighbour-before"); var original = model.Editor.SessionsCsv;
                var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + index);
                var point = card.TranslatePoint(new Point(card.Width * fraction, card.Height / 2), window)!.Value;
                var target = point + new Vector(minutes / 60 * 160, 0);
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
        // Small overshoots stop at the first neighbour rather than overlap or jump past it.
        Check("09:00:00", "53", "10:00:00", "30", 1, .5, -12, Day.AddHours(9).AddMinutes(53), Day.AddHours(10).AddMinutes(23));
        Check("09:00:00", "30", "10:03:00", "30", 0, .5, 40, Day.AddHours(9).AddMinutes(33), Day.AddHours(10).AddMinutes(3));
        Check("09:00:00", "53", "10:00:00", "30", 1, .01, -12, Day.AddHours(9).AddMinutes(53), Day.AddHours(10.5));
        Check("09:00:00", "30", "10:03:00", "30", 0, .99, 40, Day.AddHours(9), Day.AddHours(10).AddMinutes(3));
        // Resize either edge to the same exact boundaries.
        Check("09:00:00", "53", "10:00:00", "30", 1, .01, -7, Day.AddHours(9).AddMinutes(53), Day.AddHours(10.5));
        Check("09:00:00", "30", "10:03:00", "30", 0, .99, 33, Day.AddHours(9), Day.AddHours(10).AddMinutes(3));
    }
    [Fact]
    public void BlockedInsertionDoesNotChangeLedgerOrUndo()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("00:00:00", "30"), Row("00:30:00", "30") });
        var editor = new LedgerEditor(engine); var csv = editor.SessionsCsv;
        Assert.Throws<ArgumentException>(() => editor.EditTimeline(engine.Sessions[1], Day.AddMinutes(-10), Day.AddMinutes(20), true));
        Assert.Equal(csv, editor.SessionsCsv); Assert.False(editor.CanUndo);
        engine.Sessions.Clear(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("10:15:00", "30") });
        engine.ToggleAccount("Account two"); csv = editor.SessionsCsv; var undo = System.Text.Json.JsonSerializer.Serialize(editor.Steps); // Running from 15:00.
        Assert.Throws<ArgumentException>(() => editor.EditTimeline(engine.Sessions[2], Day.AddHours(14).AddMinutes(50), Day.AddHours(15).AddMinutes(20), true));
        Assert.Equal(csv, editor.SessionsCsv); Assert.Equal(undo, System.Text.Json.JsonSerializer.Serialize(editor.Steps));
    }
    [Fact]
    public void InsertionReordersBothWaysAndRepeatedUndoSurvivesRestartWithLaterRecording()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("10:00:00", "30") });
        var editor = new LedgerEditor(engine); var original = editor.SessionsCsv;
        editor.Edit(engine.Sessions[1], "Account one", Day.AddHours(9.5), Day.AddHours(10), "Keep note");
        var annotated = editor.SessionsCsv; var annotatedNotes = editor.NotesCsv;
        editor.EditTimeline(engine.Sessions[2], Day.AddHours(9).AddMinutes(40), Day.AddHours(10).AddMinutes(10), true);
        Assert.Equal(Day.AddHours(9.5), engine.Sessions[2].From);
        Assert.Equal(Day.AddHours(10), engine.Sessions[1].From);
        Assert.Equal("Keep note", editor.Note(engine.Sessions[1]));
        var firstMove = editor.SessionsCsv;
        editor.EditTimeline(engine.Sessions[0], Day.AddHours(9).AddMinutes(20), Day.AddHours(9).AddMinutes(50), true);
        Assert.Equal(Day.AddHours(9.5), engine.Sessions[0].From);
        Assert.Equal(Day.AddHours(9), engine.Sessions[2].From);
        var later = Row("11:00:00", "15"); engine.Sessions.Add(later);
        editor = new LedgerEditor(engine, notesCsv: editor.NotesCsv, steps: editor.Steps.ToList());
        Assert.Equal(3, editor.UndoCount);
        editor.Undo(); Assert.Equal(firstMove, new LedgerEditorPrefix(engine, 3).Csv);
        editor.Undo(); Assert.Equal(annotatedNotes, editor.NotesCsv);
        Assert.Equal(annotated, new LedgerEditorPrefix(engine, 3).Csv);
        editor.Undo(); Assert.Equal(original, new LedgerEditorPrefix(engine, 3).Csv);
        Assert.Same(later, engine.Sessions[^1]); Assert.False(editor.CanUndo);
    }
    private sealed class LedgerEditorPrefix
    {
        public string Csv { get; }
        public LedgerEditorPrefix(TrackingEngine source, int count)
        { var copy = Engine(); copy.Sessions.AddRange(source.Sessions.Take(count)); Csv = new LedgerEditor(copy).SessionsCsv; }
    }
    [AvaloniaFact]
    public void DashboardZoomRetainsPointerAnchorAfterRefresh()
    {
        var model = new TimerModel(Engine()); var window = new DashboardWindow(model); window.Show();
        try
        {
            Capture(window, "zoom-before");
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            var oldOffset = scroll.Offset.X;
            model.ZoomTimeline(1, 120, oldOffset);
            Capture(window, "zoom-after");
            scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            Assert.Equal(ReviewViews.TimelineGutter + (oldOffset + 120 - ReviewViews.TimelineGutter) * 1.2 - 120, scroll.Offset.X, 4);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void TimelineZoomKeepsPointerTimeAndClampsScale()
    {
        var model = new TimerModel(Engine());
        var gutter = ReviewViews.TimelineGutter;
        model.ZoomTimeline(1, 120, 800);
        Assert.Equal(108, model.TimelinePixelsPerHour);
        Assert.Equal(gutter + (800 + 120 - gutter) * 1.2 - 120, model.TimelineOffset!.Value, 5);
        model.ZoomTimeline(-1, 120, model.TimelineOffset.Value);
        Assert.Equal(90, model.TimelinePixelsPerHour, 5);
        Assert.Equal(800, model.TimelineOffset!.Value, 5);
        model.ZoomTimeline(100, 0, 0); Assert.Equal(640, model.TimelinePixelsPerHour);
        model.ZoomTimeline(-100, 0, 0); Assert.Equal(60, model.TimelinePixelsPerHour);
        model.ChangeDate(1); Assert.Null(model.TimelineOffset);
    }
    [AvaloniaFact]
    public void DragBeyondNestleThresholdInsertsAndPreviewsNeighbours()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("10:00:00", "30") });
        var model = new TimerModel(engine); model.SetTimelineScale(160, 0, 0); model.TimelineOffset = null; var window = new DashboardWindow(model); window.Show(); Capture(window, "nudge-before");
        try
        {
            var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow).OrderBy(Canvas.GetLeft).ToArray();
            var csv = model.Editor.SessionsCsv; var oldLeft = Canvas.GetLeft(cards[1]);
            var point = cards[0].TranslatePoint(new Point(cards[0].Width / 2, cards[0].Height / 2), window)!.Value; var target = point + new Vector(20.0 / 60 * 160, 0);
            window.MouseDown(point, MouseButton.Left); window.MouseMove(target);
            Assert.True(Canvas.GetLeft(cards[1]) < oldLeft); Assert.Equal(csv, model.Editor.SessionsCsv);
            window.MouseUp(target, MouseButton.Left); Assert.Null(model.EditError); Assert.Empty(model.SelectedRows);
            Assert.Equal(Day.AddHours(9), engine.Sessions[1].From); Assert.Equal(Day.AddHours(9.5), engine.Sessions[0].From); Assert.True(model.UndoEdit()); Assert.Equal(csv, model.Editor.SessionsCsv);
        }
        finally { window.Close(); }
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
    public void OldNonconsecutiveGroupsRenderAsOneMergedBlockAndClickSelectsAllItsEntries()
    {
        var engine = Engine();
        var folder = Path.Combine(Path.GetTempPath(), "timer-old-group-" + Guid.NewGuid());
        using (var seed = new TimerStore(folder))
        {
            var prefs = seed.Load(engine); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") }); seed.Save(engine, prefs);
            seed.Database.Execute("INSERT INTO meta (key, value) VALUES ('groups', $g) ON CONFLICT(key) DO UPDATE SET value = $g",
                ("$g", "GroupId,Date,Account,Start,End,Minutes\ng,2026-10-09,Account one,09:00:00,added,30\ng,2026-10-09,Account one,10:00:00,added,30\n"));
        }
        try
        {
            using var store = new TimerStore(folder); engine = Engine(); var preferences = store.Load(engine); var model = new TimerModel(engine, preferences, store);
            model.CompleteSetup(); // A new database opens with the getting started setup over the dashboard, which would catch these clicks.
            var window = new DashboardWindow(model); window.Show(); Capture(window, "legacy-group-sections");
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Tag is ReviewEntry); // One block from the first entry to the last, the gap drawn lighter.
            Assert.NotEmpty(window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is "Seam"));
            // A merged card on the timeline selects all of its entries, and they cannot be merged again.
            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Tag is ReviewEntry);
            var point = card.TranslatePoint(new Point(card.Width / 4, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
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
    public void GapMergeUndoSurvivesRestartAndRunningOrRecordedTimePreventsIt()
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
                engine.Sessions.Add(Row("09:30:00", "30", "added", "Account two")); // Fills the gap between the two blocks.
                Assert.False(store.Editor.CanCombine(engine.Sessions.Take(2).ToArray()));
            }
            var clock = Day.AddHours(9.5); var running = new TrackingEngine(clock: () => clock);
            running.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") }); running.ToggleAccount("Account two"); clock = Day.AddHours(9.75);
            Assert.False(new LedgerEditor(running).CanCombine(running.Sessions.ToArray()));
        }
        finally { Directory.Delete(folder, true); }
    }
    [AvaloniaFact]
    public void DoubleClickBlankTimelineOpensOnePrefilledEditorAndSavesTime()
    {
        var engine = Engine(); engine.Sessions.Add(Row("10:00:00", "30")); var model = new TimerModel(engine);
        var window = new DashboardWindow(model); window.Show(); Capture(window, "click-to-add-timeline");
        var canvas = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "TimelineCanvas");
        var point = canvas.TranslatePoint(new Point(ReviewViews.TimelineGutter + 11 * model.TimelinePixelsPerHour, 150), window)!.Value; // 11:00, in the blank band
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Assert.Empty(window.Dialogs.Dialogs.OfType<EntryDialog>()); // A single click adds nothing.
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        var editor = Assert.Single(window.Dialogs.Dialogs.OfType<EntryDialog>()); Capture(editor, "click-to-add-editor");
        Assert.Equal("11:00:00", Find<TextBox>(editor, "EntryStart").Text); Assert.Equal("11:30:00", Find<TextBox>(editor, "EntryEnd").Text);
        Find<Button>(editor, "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, engine.Sessions.Count); Assert.Equal("added", engine.Sessions[1].End); window.Close();
    }
    private static T Find<T>(Control window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Capture(Dialog dialog, string name) => Capture((Window)TopLevel.GetTopLevel(dialog)!, name);
    private static void Capture(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.Combine(Path.GetTempPath(), "timer-credit-previews");
        Directory.CreateDirectory(folder); Dispatcher.UIThread.RunJobs();
        using var warmup = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs();
        // The preview picture is only for people to look at. The renderer on a slow Windows runner sometimes gives no frame, most often with a menu open,
        // and what these tests check is the blocks and the selection, so a missing picture is skipped and not a failure.
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) { Console.Error.WriteLine("No preview frame for " + name); return; }
        frame.Save(Path.Combine(folder, name + ".png"));
    }

}
