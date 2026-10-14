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

// Makes the database refuse new entries, to stand in for a full disk or a locked file.
internal static class StorageFaults
{
    public static void Block(TimerStore store) => store.Database.Execute("CREATE TRIGGER block_entries BEFORE INSERT ON entries BEGIN SELECT RAISE(ABORT, 'disk full'); END");
    public static void Unblock(TimerStore store) => store.Database.Execute("DROP TRIGGER block_entries");
    public static List<SessionRow> Saved(TimerStore store)
    {
        var engine = new TrackingEngine();
        engine.Sessions.AddRange(store.Database.Query("SELECT day, start_at, seconds FROM entries ORDER BY id",
            r => new SessionRow(r.GetString(0), "x", r.GetString(1)[11..], "y", (r.GetDouble(2) / 60).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return engine.Sessions;
    }
}

public sealed class EditingTests
{
    private static TrackingEngine Engine() => new(clock: () => new DateTime(2026, 10, 9, 14, 0, 0));
    private static SessionRow Row(string start = "09:00:00", string end = "10:00:00", string minutes = "60") => new("2026-10-09", "Account one", start, end, minutes);

    [Fact]
    public void EditMovesNoteAndUndoRetainsLaterRecordingAcrossRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                engine.Sessions.Add(Row()); store.Save(engine, prefs);
                var old = engine.Sessions.Single();
                editor.Edit(old, "Account two", old.From.AddMinutes(30), old.To.AddMinutes(45), "Revised, note\nSecond line");
                store.Save(engine, prefs);
                Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions.Single().Duration);
                Assert.Equal("Revised, note\nSecond line", editor.Note(engine.Sessions.Single()));
                engine.AddSession("Internal", engine.Now.AddMinutes(-10), engine.Now);
                store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                Assert.Equal("Revised, note\nSecond line", editor.Note(engine.Sessions[0]));
                Assert.True(editor.CanUndo); editor.Undo(); store.Save(engine, prefs);
                Assert.Equal(2, engine.Sessions.Count); Assert.Equal(Row(), engine.Sessions[0]);
                Assert.Equal("Internal", engine.Sessions[1].Account);
                Assert.Equal("", editor.Note(engine.Sessions[0]));
                Assert.False(editor.CanUndo);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AddDeleteAndUndoAreReversibleAndRejectOverlapAndStaleEntries()
    {
        var engine = Engine(); var row = Row(); engine.Sessions.Add(row); var editor = new LedgerEditor(engine);
        editor.Add("Account two", row.To, row.To.AddMinutes(30), "Added note");
        Assert.Equal(2, engine.Sessions.Count); editor.Undo(); Assert.Single(engine.Sessions);
        row = engine.Sessions.Single(); editor.Delete(row); Assert.Empty(engine.Sessions); editor.Undo();
        Assert.Equal(Row(), engine.Sessions.Single());
        var current = engine.Sessions.Single();
        Assert.Throws<ArgumentException>(() => editor.Add("Account two", current.From.AddMinutes(20), current.To, ""));
        Assert.Throws<ArgumentException>(() => editor.Edit(current, current.Account, current.From, current.From, ""));
        Assert.Throws<InvalidOperationException>(() => editor.Delete(row)); // Undo replaced this reference.
        Assert.Single(engine.Sessions);
        engine.ToggleAccount("Internal");
        editor.Add("Account two", engine.Now.AddHours(1), engine.Now.AddHours(2), "Future entry");
        Assert.Equal(2, engine.Sessions.Count);
    }

    [Fact]
    public void AFailedSaveChangesNothingAndTheRetrySavesEditNotesAndUndoTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-failed-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); engine.Sessions.Add(Row()); store.Save(engine, prefs);
                var row = engine.Sessions.Single(); store.Editor!.Edit(row, row.Account, row.From, row.To.AddMinutes(15), "Checkpoint note");
                StorageFaults.Block(store);
                Assert.NotNull(Record.Exception(() => store.Save(engine, prefs)));
                Assert.Equal(TimeSpan.FromHours(1), StorageFaults.Saved(store).Single().Duration); // The failed save left the old records whole.
                StorageFaults.Unblock(store); store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine);
                Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions.Single().Duration);
                Assert.Equal("Checkpoint note", store.Editor!.Note(engine.Sessions.Single()));
                Assert.True(store.Editor.CanUndo); store.Editor.Undo(); store.Save(engine, prefs);
                Assert.Equal(TimeSpan.FromHours(1), engine.Sessions.Single().Duration);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void TimelineBodyAndEdgesEditRealRowsAndUndoRestoresThem()
    {
        var engine = Engine(); engine.Sessions.Add(Row());
        engine.Sessions.Add(Row("11:00:00", "12:00:00") with { Account = "Account two" });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show(); window.SelectTab(false);
        try
        {
            void Layout() { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var settled = window.CaptureRenderedFrame(); }
            void Drag(double fraction, double pixels)
            {
                Layout();
                var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry0");
                var point = card.TranslatePoint(new Point(80, card.Height * fraction), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(0, pixels)); window.MouseUp(point + new Vector(0, pixels), MouseButton.Left);
            }
            Drag(.5, 80); Assert.Equal(9, engine.Sessions[0].From.Hour); Assert.Equal(30, engine.Sessions[0].From.Minute);
            Assert.Equal(TimeSpan.FromHours(1), engine.Sessions[0].Duration); Assert.True(model.UndoEdit());
            Drag(.99, 80); Assert.Equal(TimeSpan.FromMinutes(90), engine.Sessions[0].Duration); Assert.True(model.UndoEdit());
            Drag(.01, 40); Assert.Equal(15, engine.Sessions[0].From.Minute); Assert.Equal(TimeSpan.FromMinutes(45), engine.Sessions[0].Duration);
            Assert.True(model.UndoEdit());
            Drag(.5, 168); Assert.Equal(10, engine.Sessions[0].From.Hour); Assert.Equal(11, engine.Sessions[0].To.Hour); Assert.Null(model.EditError); // Stops against account two.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NoteOnlyDialogPreservesFractionalCreditAndEditingTheSelectionRequestsAnEditor()
    {
        var engine = Engine(); var row = Row(end: "09:01:01", minutes: "1.01") with { Account = "ACCOUNT ONE" }; engine.Sessions.Add(row);
        var model = new TimerModel(engine);
        var editor = new EntryDialog(model, row, row.From, row.To).Open(model);
        try
        {
            editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EntryNote").Text = "Short entry note";
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(row, engine.Sessions.Single()); Assert.Equal("1.01", engine.Sessions.Single().Minutes);
        }
        finally { editor.Close(); }
        var requests = 0; DateTime? gap = null;
        model.EntryRequested += (entry, from, _) => { if (entry is not null) requests++; else gap = from; };
        var window = new Window { Width = 600, Height = 500, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var settled = window.CaptureRenderedFrame();
            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry0");
            var point = card.TranslatePoint(new Point(80, 1), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Equal(0, requests); Assert.Empty(model.SelectedRows); // Two clicks select and deselect. There is no double-click edit any more.
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.True(model.CanEditSelection); model.EditSelection();
            Assert.Equal(1, requests); model.ClearSelection(); Dispatcher.UIThread.RunJobs();
            var nine = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "09:00");
            var empty = nine.TranslatePoint(new Point(200, 60), window)!.Value; // The label sits five pixels above its hour's tick.
            window.MouseDown(empty, MouseButton.Left); window.MouseUp(empty, MouseButton.Left);
            Assert.Null(gap); // One click adds nothing.
            window.MouseDown(empty, MouseButton.Left); window.MouseUp(empty, MouseButton.Left);
            Assert.Equal(row.Day.AddHours(9).AddMinutes(20), gap);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RetryingFailedDialogSaveDoesNotAddTheEntryTwice()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-dialog-retry-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = Engine(); var prefs = store.Load(engine); engine.Sessions.Add(Row());
            var model = new TimerModel(engine, prefs, store); Assert.True(model.Save());
            StorageFaults.Block(store);
            var row = engine.Sessions.Single();
            var dialog = new EntryDialog(model, null, row.To, row.To.AddMinutes(30)).Open(model);
            try
            {
                var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(dialog.IsVisible); Assert.Equal(2, engine.Sessions.Count); Assert.NotNull(model.LastError);
                StorageFaults.Unblock(store);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(dialog.IsVisible); Assert.Equal(2, engine.Sessions.Count); Assert.Null(model.LastError);
            }
            finally { dialog.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void EntryDialogSavesNotesAndGapTimeAndWeekShowsTheEditedEntry()
    {
        var engine = Engine(); engine.Sessions.Add(Row());
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            var row = engine.Sessions[0]; var editor = new EntryDialog(model, row, row.From, row.To).Open(window);
            var end = editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EntryEnd"); end.Text = "10:15";
            editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EntryNote").Text = "Drafting\nClient review";
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(editor.IsVisible); Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions[0].Duration);
            Assert.Equal("Drafting\nClient review", model.Editor.Note(engine.Sessions[0]));
            window.SelectTab(true); model.SetWeekSection("entries"); Dispatcher.UIThread.RunJobs(); using var week = window.CaptureRenderedFrame();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Drafting\nClient review");
            var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath(); Directory.CreateDirectory(output);
            using var settled = window.CaptureRenderedFrame(); settled?.Save(Path.Combine(output, "week-editable-entries.png"));
            var add = new EntryDialog(model, null, row.To.AddMinutes(30), row.To.AddMinutes(45)).Open(window);
            Dispatcher.UIThread.RunJobs(); using var addFrame = add.CaptureRenderedFrame(); addFrame?.Save(Path.Combine(output, "entry-editor.png"));
            add.Close();
            Assert.True(model.EditEntry(null, "Account two", row.To.AddMinutes(30), row.To.AddMinutes(45), "Gap note"));
            Assert.Equal(2, engine.Sessions.Count); Assert.True(model.UndoEdit()); Assert.Single(engine.Sessions);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(100, 0, -1)] [InlineData(100, 5, -1)] [InlineData(100, 95, 1)] [InlineData(100, 50, 0)]
    [InlineData(18, 2, -1)] [InlineData(18, 9, 0)] [InlineData(18, 16, 1)]
    [InlineData(6, 1, -1)] [InlineData(6, 5, 1)] [InlineData(3, 0.5, -1)] [InlineData(3, 2.5, 1)]
    public void ShortCardsStillHaveBothEdgeHandles(double height, double y, int expected) => Assert.Equal(expected, TimelineGestures.EdgeAt(y, height));

    [Fact]
    public void DragsAreClampedToTheDayAndMinimumLength()
    {
        var row = Row("23:00:00", "added", "30");
        Assert.Equal(row.Day.AddDays(1) - row.To, TimelineGestures.ClampToDay(row, 0, TimeSpan.FromHours(5)));
        Assert.Equal(row.Day - row.From, TimelineGestures.ClampToDay(row, 0, TimeSpan.FromHours(-30)));
        Assert.Equal(row.Day.AddDays(1) - row.To, TimelineGestures.ClampToDay(row, 1, TimeSpan.FromHours(5)));
        Assert.Equal(TimeSpan.FromMinutes(1) - row.Duration, TimelineGestures.ClampToDay(row, 1, TimeSpan.FromHours(-2)));
        Assert.Equal(row.Duration - TimeSpan.FromMinutes(1), TimelineGestures.ClampToDay(row, -1, TimeSpan.FromHours(2)));
        Assert.Equal(TimeSpan.FromMinutes(5), TimelineGestures.ClampToDay(row, 0, TimeSpan.FromMinutes(5)));
    }

    [AvaloniaFact]
    public void BackspaceDeletesSelectedBlocksAsOneUndoableEdit()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "added", "30"), Row("10:00:00", "added", "30"), Row("11:00:00", "added", "30") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None); Assert.Equal(3, engine.Sessions.Count); // Nothing selected.
            model.ToggleSelection(new[] { engine.Sessions[0] }); model.ToggleSelection(new[] { engine.Sessions[2] });
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Assert.Equal("10:00:00", Assert.Single(engine.Sessions).Start); Assert.Empty(model.SelectedRows);
            Assert.True(model.UndoEdit()); Assert.Equal(3, engine.Sessions.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void VeryShortBlocksAreStillBigEnoughToSeeAndSelectAndSelectedBlocksAddNothingToTheirText()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "09:01:00", "1"), Row("10:00:00", "10:20:00", "20") });
        var model = new TimerModel(engine); model.SetAccounts(model.AccountSettings.Select(x => x.Name == "Account one" ? x with { Colour = "#102030" } : x));
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow).OrderBy(b => Canvas.GetTop(b)).ToList();
            Assert.NotEqual(Avalonia.Media.Color.Parse("#102030"), ((Avalonia.Media.ISolidColorBrush)cards[0].Background!).Color); // A very dark colour is lifted a little so it shows.
            Assert.True(cards[0].Height >= ReviewViews.MinimumBlockHeight); // One minute is only a few pixels at this zoom, but it can be clicked.
            model.ToggleSelection(new[] { engine.Sessions[1] }); Dispatcher.UIThread.RunJobs(); using var again = window.CaptureRenderedFrame();
            var selected = window.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is SessionRow row && ReferenceEquals(row, engine.Sessions[1]));
            var lines = selected.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.DoesNotContain(lines, t => t is not null && (t.Contains("✓") || t.Contains("Selected"))); // Selection is the border alone; the text is untouched.
            Assert.Contains("mergeSelected", selected.Classes);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void OnlyVeryDarkColoursAreLiftedForDisplay()
    {
        Assert.Equal("#91B7D5", Ui.Visible("#91B7D5")); Assert.Equal("#FFFFFF", Ui.Visible("#FFFFFF")); Assert.Equal("nope", Ui.Visible("nope"));
        foreach (var colour in TimerModel.Palette) Assert.Equal(colour, Ui.Visible(colour));
        var lifted = Ui.Visible("#0F1F30"); Assert.NotEqual("#0F1F30", lifted);
        Assert.True(Convert.ToInt32(lifted[1..3], 16) > 0x0F); // The same colour, lighter.
        Assert.Equal("#FFFFFF", Ui.InkOn(lifted)); // Still takes white text.
    }

    [AvaloniaFact]
    public void TheBottomLineAppearsOnlyForAMessageAndTheHeaderUsesIcons()
    {
        var engine = Engine(); var model = TimerModel.CreateDemo();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible), t => t.Text is not null && (t.Text.Contains("Sample data") || t.Text.Contains("saved locally") || t.Text.Contains("Test session")));
            var settings = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings"); var back = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BackToPill");
            Assert.IsType<IconGlyph>(settings.Content); Assert.IsType<IconGlyph>(back.Content); // A gear and a minimise bar, not words.
            Assert.Equal(IconKind.Gear, ((IconGlyph)settings.Content!).Kind); Assert.Equal(IconKind.Minimise, ((IconGlyph)back.Content!).Kind);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBarMarksTheChosenViewAndTheDeskIsTheOneItOpensOn()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var desk = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab");
            Assert.Contains("rail", desk.Classes); Assert.Contains("selected", desk.Classes); // It opens on the desk.
            dashboard.SelectTab(false); Dispatcher.UIThread.RunJobs();
            var tab = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab");
            Assert.Contains("selected", tab.Classes); Assert.DoesNotContain("selected", desk.Classes);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void ZoomAndFitSitInABarUnderTheTimelineTheHintLineIsGoneAndADisabledButtonStaysOutlined()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show(); dashboard.SelectTab(false);
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "ReviewTools"); // The Tools menu is gone.
            var undoButton = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            var zoom = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineControls");
            var scrollAbove = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "TimelineScroll");
            // One bar under the timeline: Undo (and Add time) on the left, the zoom controls on the right, on the same line.
            Assert.True(undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y >= scrollAbove.TranslatePoint(new Point(0, scrollAbove.Bounds.Height - 1), dashboard)!.Value.Y);
            Assert.InRange(Math.Abs(undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y + undoButton.Bounds.Height / 2 - (zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y + zoom.Bounds.Height / 2)), 0, 3);
            Assert.True(zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.X > undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.X);
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible), t => t.Text is not null && t.Text.Contains("Click blocks to select"));
            var undo = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            Assert.False(undo.IsEnabled); Assert.True(undo.Opacity < 1); // Dimmed by the style, keeping its outline, not a solid grey fill.
            model.SetWeekSection("overview"); dashboard.SelectTab(true); Dispatcher.UIThread.RunJobs();
            Assert.False(zoom.IsVisible); // Zoom belongs to the timeline only.
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "ExportWeek"); // Export lives only on the Overview tab.
            model.SetWeekSection("hours"); Dispatcher.UIThread.RunJobs();
            var export = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportWeek"); var copy = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CopyHours");
            Assert.Equal(IconKind.Export, ((IconGlyph)export.Content!).Kind); Assert.Equal(IconKind.Copy, ((IconGlyph)copy.Content!).Kind); Assert.Equal("Export hours…", ToolTip.GetTip(export));
            Assert.InRange(Math.Abs(export.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y - copy.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y), 0, 2); // Same line.
            Assert.Contains(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "WeekOverviewTab" && ToolTip.GetTip(b) as string == "Week overview");
            Assert.Contains(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "WeekHoursTab" && ToolTip.GetTip(b) as string == "Hours table");
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TidyDayIsOfferedWhenYouRightClickEmptyTimelineSpace()
    {
        var model = TimerModel.CreateDemo(); var raised = 0; model.TidyRequested += () => raised++;
        var window = new Window { Width = 700, Height = 600, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var canvas = window.GetVisualDescendants().OfType<Canvas>().First(c => c.ContextMenu is not null);
            var tidy = canvas.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == "TidyDayMenu");
            tidy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal(1, raised);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MessagesFloatOverTheWorkspaceWhereverItIsShowing()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show(); dashboard.SelectTab(false);
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            model.Notice = "A message for the footer"; model.ToggleFormat(); model.ToggleFormat(); Dispatcher.UIThread.RunJobs(); using var again = dashboard.CaptureRenderedFrame();
            var message = dashboard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "A message for the footer" && t.IsVisible);
            Assert.True(message.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y > dashboard.Bounds.Height / 2); // Over the lowest part of the workspace.
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheTopOfTheWindowDragsItButNoButtonOrTheTimelineDoes()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        var model = TimerModel.CreateDemo(() => now); var dashboard = new DashboardWindow(model); dashboard.Show(); dashboard.SelectTab(false);
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var tab = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab");
            var tabAt = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), dashboard)!.Value;
            Assert.True(dashboard.InDragZone(new Point(tabAt.X + 400, tabAt.Y))); // Empty space beside the buttons in the bar.
            var timelineScroll = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            Assert.False(dashboard.InDragZone(timelineScroll.TranslatePoint(new Point(100, 100), dashboard)!.Value));
            Assert.False(Ui.IsPlainSurface(tab));
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheDashboardStaysOnTopAndThePillStaysTheOvertimeColourEvenWhileRecording()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); var pill = new PillWindow(model); dashboard.Show(); pill.Show();
        try
        {
            Assert.True(dashboard.Topmost); Assert.True(pill.Topmost);
            model.SetOvertimeTest(true); model.ToggleAccount("Account three"); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            Assert.True(model.IsOvertime); Assert.True(model.IsTracking);
            // The whole capsule is the overtime colour while recording, and its line says how much overtime there is.
            Assert.Equal("Overtime", model.ThemeState); Assert.Equal(ThemeEngine.StateVariant(model.CurrentTheme, ThemeDerivation.Overtime), pill.RequestedThemeVariant);
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is not null && t.Text.Contains("· OT "));
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); Assert.True(model.IsOnBreak);
        }
        finally { dashboard.Close(); pill.Close(); model.SetOvertimeTest(false); }
    }
}
