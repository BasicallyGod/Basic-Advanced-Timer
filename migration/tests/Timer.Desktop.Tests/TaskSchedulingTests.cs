using Avalonia;
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

public sealed class TaskSchedulingTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private readonly string folder = Path.Combine(Path.GetTempPath(), "task-scheduling-" + Guid.NewGuid());
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    private static TimerModel Model() { var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(14))); model.CompleteSetup(); return model; }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Quick(Visual root, string words)
    {
        var input = Find<TextBox>(root, "NoteInput"); input.Text = words;
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void RecurringTimedTasksKeepTheirTypeAccountAndReminderAfterReopening()
    {
        using (var store = new TimerStore(folder))
        {
            var engine = new TrackingEngine(clock: () => Day.AddHours(14)); var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
            Assert.Null(model.AddNote(new NoteEdit(NoteKind.Task, "Send update", Day, TimeSpan.FromHours(14), null, model.AccountNames[0], new Recurrence(1, Recurrence.Weekdays), false)));
            Assert.True(model.Save());
        }
        using (var store = new TimerStore(folder))
        {
            var engine = new TrackingEngine(clock: () => Day.AddHours(14)); var model = new TimerModel(engine, store.Load(engine), store);
            Assert.True(model.AllDayNotes.Count > 20); Assert.All(model.AllDayNotes, note =>
            {
                Assert.Equal(NoteKind.Task, TimerModel.KindOf(note)); Assert.Equal(DayNoteKind.Task, note.Kind);
                Assert.Equal(model.AccountNames[0], NoteShorthand.AccountOf(note.Text, model.AccountNames));
            });
            Assert.Equal(ReminderState.Due, Assert.Single(model.Reminders).State); Assert.Empty(model.MeetingsOn(Day)); Assert.Empty(model.Engine.Sessions);
            var first = model.NotesOn(Day).Single(); model.DoneReminder(first.Id); Assert.True(model.NotesOn(Day).Single().Done);
        }
    }

    [Fact]
    public void UndatedTasksSurviveUndoRemoveAndDoNotAppearOnCalendarOrReminderLists()
    {
        using var store = new TimerStore(folder);
        var engine = new TrackingEngine(clock: () => Day.AddHours(14)); var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
        Assert.Null(model.AddNote(new NoteEdit(NoteKind.Task, "Capture idea", Day.AddDays(-5), null, null, null, null, false, HasDate: false)));
        var note = Assert.Single(model.UndatedNotes); model.RemoveDayNote(note.Id); model.RestoreRemovedNote();
        Assert.False(Assert.Single(store.LoadDayNotes()).HasDate); Assert.Equal(DayNoteKind.Task, store.LoadDayNotes().Single().Kind);
        Assert.Empty(model.NotesOn(Day.AddDays(-5))); Assert.Empty(model.ComingUp()); Assert.Empty(model.MissedNotes()); Assert.Empty(model.Reminders);
    }

    [Fact]
    public void RetryingAFailedInsertPreservesKindDateAndEndTime()
    {
        using var store = new TimerStore(folder);
        var engine = new TrackingEngine(clock: () => Day.AddHours(14)); var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
        store.Database.Execute("CREATE TRIGGER refuse_notes BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'disk full'); END");
        Assert.Null(model.AddNote(new NoteEdit(NoteKind.Task, "Undated", Day, null, null, null, null, false, HasDate: false)));
        Assert.Null(model.AddNote(new NoteEdit(NoteKind.Meeting, "Review", Day, TimeSpan.FromHours(15), TimeSpan.FromHours(16), null, null, false)));
        Assert.True(model.NotesUnsaved);
        store.Database.Execute("DROP TRIGGER refuse_notes"); Assert.True(model.Save());
        var notes = store.LoadDayNotes(); Assert.Equal(2, notes.Count);
        Assert.False(notes.Single(n => n.Text == "Undated").HasDate);
        var meeting = notes.Single(n => n.Text == "Review"); Assert.Equal(DayNoteKind.Meeting, meeting.Kind); Assert.Equal(TimeSpan.FromHours(16), meeting.End);
    }

    [AvaloniaFact]
    public void TheEditorCanCaptureAnUndatedTaskThenScheduleItWithoutTurningItIntoAMeeting()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Click(window, "NotesTab");
        try
        {
            Click(window, "NoteAddButton"); Find<CheckBox>(window, "NoteDetailsAddDate").IsChecked = false;
            Find<TextBox>(window, "NoteDetailsWords").Text = "Capture for later";
            Assert.False(Find<Control>(window, "NoteDetailsDateField").IsVisible);
            Assert.False(Find<ComboBox>(window, "NoteDetailsRepeat").IsEffectivelyEnabled);
            Click(window, "NoteDetailsAdd"); var note = Assert.Single(model.UndatedNotes);
            Click(window, "NotesFilter_undated"); Find<Button>(window, "NoteText_" + note.Id);
            Quick(window, "Another undated task"); Assert.Equal(2, model.UndatedNotes.Count);
            Click(window, "NoteText_" + note.Id);
            Find<CheckBox>(window, "NoteDetailsAddDate").IsChecked = true;
            Find<CheckBox>(window, "NoteDetailsAddTime").IsChecked = true;
            Click(window, "NoteDetailsSave"); Assert.True(window.Dialogs.Any); // An enabled empty time is invalid.
            Find<TextBox>(window, "NoteDetailsStart").Text = "14:25";
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } previews)
            {
                window.Dialogs.Dialogs.Single().Settle();
                Directory.CreateDirectory(previews); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(previews, "task-scheduling-editor.png"));
            }
            Click(window, "NoteDetailsSave"); Assert.False(window.Dialogs.Any);
            var task = model.NotesOn(Day).Single(); Assert.Equal(new TimeSpan(14, 25, 0), task.At); Assert.Equal(NoteKind.Task, TimerModel.KindOf(task));
            Click(window, "NotesFilter_tasks"); Find<CheckBox>(window, "NoteTick_" + task.Id); Find<CheckBox>(window, "ComingTick_" + task.Id);
            Click(window, "NotesFilter_events"); Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), c => c.Name == "NoteText_" + task.Id);
            Assert.Empty(model.Engine.Sessions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void QuickAddWithASingleTimeMakesATaskAndItsNotificationOffersDone()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Click(window, "NotesTab");
        try
        {
            Quick(window, "14:00 Send report #AccountOne"); var note = Assert.Single(model.AllDayNotes);
            Assert.Equal(NoteKind.Task, TimerModel.KindOf(note)); Assert.Equal(TimeSpan.FromHours(14), note.At);
            Assert.Equal("Done", Find<Button>(window, "ReminderDone").Content);
            Click(window, "ReminderDone"); Assert.True(model.AllDayNotes.Single().Done);
        }
        finally { window.Close(); }
    }
}
