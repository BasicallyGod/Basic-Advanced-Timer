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
    public void AdjustmentsSupportNotesWithoutInventingTimeAndAmbiguousKeysAreRejected()
    {
        var engine = Engine(); var merged = Row(end: "adjusted"); engine.Sessions.Add(merged);
        var editor = new LedgerEditor(engine);
        editor.Edit(merged, merged.Account, merged.From, merged.To, "Legacy note");
        Assert.Equal(merged, engine.Sessions.Single()); Assert.Equal("Legacy note", editor.Note(merged));
        Assert.Throws<ArgumentException>(() => editor.Edit(merged, merged.Account, merged.From.AddMinutes(5), merged.To, ""));
        editor.Undo(); Assert.Equal("", editor.Note(engine.Sessions.Single()));
        var copy = engine.Sessions.Single(); engine.Sessions.Add(copy with { End = "adjusted", Minutes = "5" });
        Assert.Throws<ArgumentException>(() => editor.Edit(copy, copy.Account, copy.From, copy.To, "Ambiguous"));
        Assert.False(editor.CanUndo);
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
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
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
    public void NoteOnlyDialogPreservesFractionalCreditAndDoubleClickRequestsAnEditor()
    {
        var engine = Engine(); var row = Row(end: "09:01:01", minutes: "1.01") with { Account = "ACCOUNT ONE" }; engine.Sessions.Add(row);
        var model = new TimerModel(engine);
        var editor = new EntryWindow(model, row, row.From, row.To); editor.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = editor.CaptureRenderedFrame();
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
            Assert.Equal(1, requests); Assert.Empty(model.SelectedRows); // A double-click edits without leaving the block selected.
            var nine = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "09:00");
            var empty = nine.TranslatePoint(new Point(200, 55), window)!.Value;
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
            var dialog = new EntryWindow(model, null, row.To, row.To.AddMinutes(30)); dialog.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); using var frame = dialog.CaptureRenderedFrame();
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
            var row = engine.Sessions[0]; var editor = new EntryWindow(model, row, row.From, row.To); editor.Show(window);
            Dispatcher.UIThread.RunJobs(); using var frame = editor.CaptureRenderedFrame();
            var end = editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EntryEnd"); end.Text = "10:15";
            editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EntryNote").Text = "Drafting\nClient review";
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(editor.IsVisible); Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions[0].Duration);
            Assert.Equal("Drafting\nClient review", model.Editor.Note(engine.Sessions[0]));
            window.SelectTab(true); model.SetWeekSection("entries"); Dispatcher.UIThread.RunJobs(); using var week = window.CaptureRenderedFrame();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Drafting\nClient review");
            var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath(); Directory.CreateDirectory(output);
            using var settled = window.CaptureRenderedFrame(); settled!.Save(Path.Combine(output, "week-editable-entries.png"));
            var add = new EntryWindow(model, null, row.To.AddMinutes(30), row.To.AddMinutes(45)); add.Show(window);
            Dispatcher.UIThread.RunJobs(); using var addFrame = add.CaptureRenderedFrame(); addFrame!.Save(Path.Combine(output, "entry-editor.png"));
            add.Close();
            Assert.True(model.EditEntry(null, "Account two", row.To.AddMinutes(30), row.To.AddMinutes(45), "Gap note"));
            Assert.Equal(2, engine.Sessions.Count); Assert.True(model.UndoEdit()); Assert.Single(engine.Sessions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DoubleClickRevertsOnlyTheToggleFromItsOwnFirstClick()
    {
        var engine = Engine(); var row = Row("09:00:00", "added", "30"); var other = Row("10:00:00", "added", "30"); engine.Sessions.AddRange(new[] { row, other });
        var model = new TimerModel(engine); var opened = 0; model.EntryRequested += (_, _, _) => opened++;
        model.ToggleSelection(new[] { other });
        model.ToggleSelection(new[] { row }); model.OpenEntryFromDoubleClick(row);
        Assert.Equal(1, opened); Assert.Same(other, Assert.Single(model.SelectedRows));
        model.ToggleSelection(new[] { row }); model.ToggleSelection(new[] { row }); model.OpenEntryFromDoubleClick(row); // Selected before, so the first click deselected it and the double-click restores it.
        Assert.Equal(2, opened); Assert.Contains(row, model.SelectedRows);
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
    public void AccountRowsAreSlimSoMoreFitInTheDashboardAndPillAndTabsAreUnderlined()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var rows = dashboard.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("account")).ToList();
            Assert.NotEmpty(rows); Assert.All(rows, r => Assert.Equal(52, r.Bounds.Height, 0.5)); // The pill's rows match.
            var tab = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab");
            Assert.Equal(new Avalonia.Thickness(0, 0, 0, 3), tab.BorderThickness); Assert.Contains("selected", tab.Classes);
            Assert.Equal(Avalonia.Media.Brushes.Transparent, tab.Background);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void ZoomAndFitSitInTheActionRowTheHintLineIsGoneAndADisabledButtonStaysOutlined()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "ReviewTools"); // The Tools menu is gone.
            var undoButton = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            var zoom = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineControls");
            Assert.InRange(Math.Abs(undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y + undoButton.Bounds.Height / 2 - (zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y + zoom.Bounds.Height / 2)), 0, 3); // Same row.
            Assert.True(zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.X > undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.X); // On the right of the row.
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible), t => t.Text is not null && t.Text.Contains("Click blocks to select"));
            var undo = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            Assert.False(undo.IsEnabled); Assert.True(undo.Opacity < 1); // Dimmed by the style, keeping its outline, not a solid grey fill.
            model.SetWeekSection("overview"); dashboard.SelectTab(true); Dispatcher.UIThread.RunJobs();
            Assert.False(zoom.IsVisible); // Zoom belongs to the timeline only.
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "ExportWeek"); // Export lives only on the Overview tab.
            model.SetWeekSection("hours"); Dispatcher.UIThread.RunJobs();
            var export = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportWeek"); var copy = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CopyHours");
            Assert.Equal(IconKind.Export, ((IconGlyph)export.Content!).Kind); Assert.Equal(IconKind.Copy, ((IconGlyph)copy.Content!).Kind); Assert.Equal("Export week…", ToolTip.GetTip(export));
            Assert.InRange(Math.Abs(export.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y - copy.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y), 0, 2); // Same line.
            Assert.Contains(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "WeekOverviewTab" && b.Content as string == "Week view");
            Assert.Contains(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "WeekHoursTab" && b.Content as string == "Overview");
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
}
