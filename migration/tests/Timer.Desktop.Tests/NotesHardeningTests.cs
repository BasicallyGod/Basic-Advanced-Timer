using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>What a sense check of the code found in Notes: writes that fail, a removal with no way back, words lost to the shorthand, reminders that could be missed, and tasks left on past days.</summary>
public sealed class NotesHardeningTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "notes-hardening-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }

    private static TimerModel Model() { var model = new TimerModel(new TrackingEngine(clock: () => Now)); model.CompleteSetup(); return model; }
    private static void Click(Visual root, string name) { root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static DashboardWindow Open(TimerModel model) { var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); Click(window, "NotesTab"); return window; }
    private static int Rows(Visual root) => root.GetVisualDescendants().OfType<TextBox>().Count(t => t.Name?.StartsWith("NoteText_") == true);
    private static void Enter(TextBox box) { box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void ANoteTheDatabaseRefusesStaysOnScreenAndIsWrittenWhenItWorksAgain()
    {
        using var store = new TimerStore(folder);
        var engine = new TrackingEngine(clock: () => Now); var model = new TimerModel(engine, store.Load(engine), store);
        model.AddDayNote(Now.Date, "First"); var first = Assert.Single(model.AllDayNotes); Assert.True(first.Id > 0);
        model.AddDayNote(Now.Date, "Doomed"); var doomed = model.AllDayNotes.Single(n => n.Text == "Doomed");
        // The database now refuses every note write, as a full disk or a locked file would.
        foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE" }) store.Database.Execute($"CREATE TRIGGER block_notes_{verb} BEFORE {verb} ON day_notes BEGIN SELECT RAISE(ABORT, 'disk full'); END");
        model.AddDayNote(Now.Date, "Second");
        model.SetDayNoteText(first.Id, "First edited");
        model.RemoveDayNote(doomed.Id);
        Assert.Equal(new[] { "First edited", "Second" }, model.AllDayNotes.Select(n => n.Text).OrderBy(t => t).ToArray()); // All of it still shows.
        Assert.True(model.NotesUnsaved); Assert.Contains("Could not save", model.Notice);
        Assert.Contains(model.AllDayNotes, n => n.Id < 0 && n.Text == "Second");
        foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE" }) store.Database.Execute($"DROP TRIGGER block_notes_{verb}");
        Assert.True(model.Save()); // Every save tries the refused writes again.
        Assert.False(model.NotesUnsaved); Assert.DoesNotContain(model.AllDayNotes, n => n.Id < 0);
        Assert.Equal(new[] { "First edited", "Second" }, store.LoadDayNotes().Select(n => n.Text).OrderBy(t => t).ToArray());
    }

    [Fact]
    public void OnlyATimeOrADayIsNotANoteAndTheWordsAreKept()
    {
        var model = Model();
        Assert.False(model.HasNoteWords("14:30")); Assert.False(model.HasNoteWords("@fri"));
        Assert.True(model.HasNoteWords("Call back 14:30"));
        Assert.False(model.AddDayNote(Now.Date, "14:30")); Assert.Empty(model.AllDayNotes); Assert.Contains("words", model.Notice);
        Assert.True(model.AddDayNote(Now.Date, "Call back 14:30")); Assert.Single(model.AllDayNotes);
    }

    [AvaloniaFact]
    public void TheNotesBoxAndQuickCaptureKeepATimeTypedWithNoWords()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NoteInput");
            box.Text = "14:30"; Enter(box);
            Assert.Equal("14:30", box.Text); Assert.Empty(model.AllDayNotes); // Still there to finish.
            box.Text = "14:30 Call back"; Enter(box);
            Assert.Equal("", box.Text); Assert.Single(model.AllDayNotes);
        }
        finally { window.Close(); }
        var capture = new QuickCaptureWindow(model); capture.Show();
        try
        {
            var text = capture.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CaptureText"); text.Text = "@fri"; Enter(text);
            Assert.True(capture.IsVisible); Assert.Equal("@fri", text.Text); Assert.Single(model.AllDayNotes);
            Assert.Contains("words", capture.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CaptureReading").Text);
        }
        finally { capture.Close(); }
    }

    [Fact]
    public void ARemovedNoteCanBePutBackWithItsDayAndTick()
    {
        var model = Model();
        model.AddDayNote(Now.Date.AddDays(2), "Plan the trip"); var note = Assert.Single(model.AllDayNotes); model.ToggleDayNote(note.Id);
        Assert.Null(model.LastRemovedNote);
        model.RemoveDayNote(note.Id); Assert.Empty(model.AllDayNotes); Assert.Equal("Plan the trip", model.LastRemovedNote!.Text);
        model.RestoreRemovedNote();
        var back = Assert.Single(model.AllDayNotes); Assert.Equal("Plan the trip", back.Text); Assert.Equal(Now.Date.AddDays(2), back.Day); Assert.True(back.Done);
        Assert.Null(model.LastRemovedNote);
    }

    [AvaloniaFact]
    public void TheNotesPageOffersUndoRemoveAfterARemoval()
    {
        var model = Model(); model.AddDayNote(Now.Date, "Keep me"); var window = Open(model);
        try
        {
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoteUndoRemove").IsVisible);
            Click(window, "NoteRemove_" + model.AllDayNotes.Single().Id); Assert.Equal(0, Rows(window));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoteUndoRemove").IsVisible);
            Click(window, "NoteUndoRemove"); Assert.Equal(1, Rows(window));
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoteUndoRemove").IsVisible);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void MissedNotesAreUnfinishedTasksAndDeadlinesFromEarlierDaysAndCanMoveToToday()
    {
        var model = Model();
        model.AddDayNote(Now.Date.AddDays(-1), "Send the invoice");          // A task from yesterday: missed.
        model.AddDayNote(Now.Date.AddDays(-3), "! Pay the supplier");        // A deadline from three days ago: missed.
        model.AddDayNote(Now.Date.AddDays(-1), "10:00 Team call");           // An event just passes.
        model.AddDayNote(Now.Date.AddDays(-2), "Already done"); model.ToggleDayNote(model.AllDayNotes.Single(n => n.Text == "Already done").Id);
        model.AddDayNote(Now.Date.AddDays(-200), "Far too old");
        model.AddDayNote(Now.Date, "Today's own task");                       // Not yet missed.
        Assert.Equal(2, model.MissedNotes().Count); Assert.Equal("Send the invoice", model.MissedNotes()[0].Text); Assert.Contains("Pay the supplier", model.MissedNotes()[1].Text); // The latest day first.
        model.MoveMissedToToday();
        Assert.Empty(model.MissedNotes());
        Assert.Equal(3, model.NotesOn(Now.Date).Count); Assert.Contains("Moved 2 notes to today", model.Notice);
        // The event stayed where it was.
        Assert.Equal(Now.Date.AddDays(-1), model.AllDayNotes.Single(n => n.Text.Contains("Team call")).Day);
    }

    [AvaloniaFact]
    public void TheMissedTabListsThemByDayAndCarriesThemToToday()
    {
        var model = Model(); model.AddDayNote(Now.Date.AddDays(-1), "Send the invoice"); model.AddDayNote(Now.Date.AddDays(-3), "Book the room");
        var window = Open(model);
        try
        {
            Assert.Contains("Missed 2", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesFilter_missed").Content?.ToString());
            Click(window, "NotesFilter_missed"); Assert.Equal(2, Rows(window));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Missed"); // The page's heading.
            Click(window, "NotesMissedToToday");
            Assert.Equal(0, Rows(window)); Assert.Equal(2, model.NotesOn(Now.Date).Count);
            Click(window, "NotesFilter_all"); Assert.Equal(2, Rows(window));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AHeadsUpOfOneMinuteIsNotPlural()
    {
        var note = new DayNote(1, Now.Date, new TimeSpan(12, 1, 0), "Call");
        var reminder = Assert.Single(ReminderRules.For(new[] { note }, Now, 10));
        Assert.Equal("In 1 minute · 12:01", ReminderRules.Describe(reminder, Now));
        Assert.Equal("In 2 minutes · 12:01", ReminderRules.Describe(reminder, Now.AddSeconds(-30)));
    }

    [Fact]
    public void TheReminderSoundIsOnUnlessSwitchedOff()
    {
        var model = Model();
        Assert.True(model.ReminderSoundOn); model.SetReminderSoundOn(false); Assert.False(model.ReminderSoundOn); model.SetReminderSoundOn(true); Assert.True(model.ReminderSoundOn);
    }

    [AvaloniaFact]
    public void ADueReminderRestoresATimerThatWasOnlyMinimised()
    {
        var offset = TimeSpan.Zero; var start = DateTime.Now;
        var model = TimerModel.CreateDemo(() => start + offset); model.SetReminderSoundOn(false);
        var windows = new WindowCoordinator(model, () => { });
        try
        {
            windows.Pill.Show(); windows.Pill.WindowState = WindowState.Minimized; Assert.True(windows.IsShown); // Minimised still counts as visible.
            model.Tick(); offset += TimeSpan.FromMinutes(11); model.Tick(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, windows.Pill.WindowState);
        }
        finally { windows.Pill.Close(); windows.Dashboard.Close(); }
    }
}
