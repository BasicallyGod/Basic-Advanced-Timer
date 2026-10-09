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

public sealed class EditingTests
{
    private static TrackingEngine Engine() => new(clock: () => new DateTime(2026, 10, 9, 14, 0, 0));
    private static SessionRow Row(string start = "09:00:00", string end = "10:00:00", string minutes = "60") => new("2026-10-09", "Account one", start, end, minutes);

    [Fact]
    public void EditMovesNoteAndExtraFieldsAndUndoRetainsLaterRecordingAcrossRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(folder, "Timesheets"));
        File.WriteAllText(Path.Combine(folder, "Timesheets/sessions.csv"), "Date,Account,Start,End,Minutes,Extra\n2026-10-09,Account one,09:00:00,10:00:00,60,keep-me\n");
        File.WriteAllText(Path.Combine(folder, "Timesheets/notes.csv"), "Date,Account,Start,Note,Extra\n2026-10-09,Account one,09:00:00,\"Original, note\",note-extra\n");
        File.WriteAllText(Path.Combine(folder, "Timesheets/entry-groups.csv"), "GroupId,Date,Account,Start,End,Minutes,Extra\ng1,2026-10-09,Account one,09:00:00,10:00:00,60,group-extra\n");
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                var old = engine.Sessions.Single();
                editor.Edit(old, "Account two", old.From.AddMinutes(30), old.To.AddMinutes(45), "Revised, note\nSecond line");
                store.Save(engine, prefs);
                Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions.Single().Duration);
                Assert.Equal("Revised, note\nSecond line", editor.Note(engine.Sessions.Single()));
                Assert.Contains("keep-me", File.ReadAllText(Path.Combine(folder, "Timesheets/sessions.csv")));
                Assert.Contains("note-extra", File.ReadAllText(Path.Combine(folder, "Timesheets/notes.csv")));
                Assert.DoesNotContain("group-extra", File.ReadAllText(Path.Combine(folder, "Timesheets/entry-groups.csv")));
                engine.AddSession("Internal", engine.Now.AddMinutes(-10), engine.Now);
                store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                Assert.True(editor.CanUndo); editor.Undo(); store.Save(engine, prefs);
                Assert.Equal(2, engine.Sessions.Count); Assert.Equal(Row(), engine.Sessions[0]);
                Assert.Equal("Internal", engine.Sessions[1].Account);
                Assert.Equal("Original, note", editor.Note(engine.Sessions[0]));
                Assert.Contains("group-extra", File.ReadAllText(Path.Combine(folder, "Timesheets/entry-groups.csv")));
                Assert.Contains("keep-me", File.ReadAllText(Path.Combine(folder, "Timesheets/sessions.csv")));
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
    public void LegacyCreditSupportsNotesWithoutInventingTimeAndAmbiguousKeysAreRejected()
    {
        var engine = Engine(); var merged = Row(end: "merged"); engine.Sessions.Add(merged);
        var editor = new LedgerEditor(engine);
        editor.Edit(merged, merged.Account, merged.From, merged.To, "Legacy note");
        Assert.Equal(merged, engine.Sessions.Single()); Assert.Equal("Legacy note", editor.Note(merged));
        Assert.Throws<ArgumentException>(() => editor.Edit(merged, merged.Account, merged.From.AddMinutes(5), merged.To, ""));
        Assert.Throws<ArgumentException>(() => editor.Delete(merged));
        editor.Undo(); Assert.Equal("", editor.Note(engine.Sessions.Single()));
        var copy = engine.Sessions.Single(); engine.Sessions.Add(copy with { End = "rounded up", Minutes = "5" });
        Assert.Throws<ArgumentException>(() => editor.Edit(copy, copy.Account, copy.From, copy.To, "Ambiguous"));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void InterruptedEditJournalReplaysNotesAndUndoTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-journal-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); engine.Sessions.Add(Row()); store.Save(engine, prefs);
                var row = engine.Sessions.Single(); store.Editor!.Edit(row, row.Account, row.From, row.To.AddMinutes(15), "Checkpoint note");
                Directory.CreateDirectory(Path.Combine(folder, "Timesheets/notes.csv.tmp"));
                var error = Record.Exception(() => store.Save(engine, prefs)); Assert.True(error is IOException or UnauthorizedAccessException);
                Assert.True(File.Exists(Path.Combine(folder, "migration-journal.json")));
                Directory.Delete(Path.Combine(folder, "Timesheets/notes.csv.tmp"));
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
            Directory.CreateDirectory(Path.Combine(folder, "Timesheets/notes.csv.tmp"));
            var row = engine.Sessions.Single();
            var dialog = new EntryWindow(model, null, row.To, row.To.AddMinutes(30)); dialog.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); using var frame = dialog.CaptureRenderedFrame();
                var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(dialog.IsVisible); Assert.Equal(2, engine.Sessions.Count); Assert.NotNull(model.LastError);
                Directory.Delete(Path.Combine(folder, "Timesheets/notes.csv.tmp"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(dialog.IsVisible); Assert.Equal(2, engine.Sessions.Count); Assert.Null(model.LastError);
            }
            finally { dialog.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void EntryDialogSavesNotesAndGapTimeAndWeekExposesLegacyRows()
    {
        var engine = Engine(); engine.Sessions.Add(Row()); engine.Sessions.Add(Row("08:00:00", "merged", "30"));
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
            window.SelectTab(true); Dispatcher.UIThread.RunJobs(); using var week = window.CaptureRenderedFrame();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is not null && t.Text.Contains("Merged credit"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Drafting\nClient review");
            var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath(); Directory.CreateDirectory(output);
            using var settled = window.CaptureRenderedFrame(); settled!.Save(Path.Combine(output, "week-editable-entries.png"));
            var add = new EntryWindow(model, null, row.To.AddMinutes(30), row.To.AddMinutes(45)); add.Show(window);
            Dispatcher.UIThread.RunJobs(); using var addFrame = add.CaptureRenderedFrame(); addFrame!.Save(Path.Combine(output, "entry-editor.png"));
            add.Close();
            Assert.True(model.EditEntry(null, "Account two", row.To.AddMinutes(30), row.To.AddMinutes(45), "Gap note"));
            Assert.Equal(3, engine.Sessions.Count); Assert.True(model.UndoEdit()); Assert.Equal(2, engine.Sessions.Count);
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
        Assert.Equal(TimeSpan.FromSeconds(3) - row.Duration, TimelineGestures.ClampToDay(row, 1, TimeSpan.FromHours(-2)));
        Assert.Equal(row.Duration - TimeSpan.FromSeconds(3), TimelineGestures.ClampToDay(row, -1, TimeSpan.FromHours(2)));
        Assert.Equal(TimeSpan.FromMinutes(5), TimelineGestures.ClampToDay(row, 0, TimeSpan.FromMinutes(5)));
    }
}
