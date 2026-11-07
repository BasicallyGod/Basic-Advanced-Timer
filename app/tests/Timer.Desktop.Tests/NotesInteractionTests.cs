using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NotesInteractionTests
{
    private static readonly DateTime Day = new(2026, 11, 5);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(12)),
            new StoreSettings(new() { new("Test account") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }
    private static void Press(Window window, string name)
    {
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
    }

    [AvaloniaFact]
    public void TimelineRowsSelectFromTheirBottomPaddingAndKeepTickAndStarActionsSeparate()
    {
        var model = Model(); model.Notes.AddNote("A short note", null, null);
        var note = model.Notes.All.Single();
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "A meeting", null, RepeatChoice.None);
        var meeting = model.Notes.All.Single(NotesModel.IsMeeting);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            void ClickPadding(long id)
            {
                var row = Find<Button>(window, "NoteEdit_" + id).GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("noteRow"));
                var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height - 3), window)!.Value;
                window.SettledClick(point); Settle(window);
            }
            ClickPadding(note.Id); Assert.Equal(note.Id, model.SelectedNoteId);
            ClickPadding(note.Id); Assert.Null(model.SelectedNoteId);
            void ClickAction(string name)
            {
                var action = Find<Button>(window, name);
                var point = action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.SettledClick(point); Settle(window);
            }
            ClickAction("NoteDone_" + note.Id); Assert.True(model.Notes.Find(note.Id)!.Done); Assert.Null(model.SelectedNoteId);
            ClickAction("NoteImportant_" + note.Id); Assert.True(NotesModel.IsImportant(model.Notes.Find(note.Id)!)); Assert.Null(model.SelectedNoteId);
            Press(window, "CardKindMeetings"); ClickPadding(meeting.Id); Assert.Equal(meeting.Id, model.SelectedMeetingId);
            ClickPadding(meeting.Id); Assert.Null(model.SelectedMeetingId);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void LiteralPunctuationAndExistingStarsSurviveEditsRemindersAndReopening()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-important-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                store.AddDayNote(Day, null, "!! Legacy starred note", null, null, DayNoteKind.Task, false);
                var notes = new NotesModel(store, () => Day, () => Array.Empty<string>());
                var legacy = notes.All.Single(); Assert.True(NotesModel.IsImportant(legacy)); Assert.Equal("Legacy starred note", notes.Words(legacy));
                notes.EditNote(legacy.Id, "Edited legacy note", null, null); Assert.True(NotesModel.IsImportant(notes.Find(legacy.Id)!));
                foreach (var words in new[] { "!! literal text", "!!", "\u2063!! literal separator" })
                {
                    notes.AddNote(words, null, null); var note = notes.All.Last();
                    Assert.False(NotesModel.IsImportant(note)); Assert.Equal(words, notes.Words(note));
                    notes.SetImportant(note.Id, true); Assert.True(NotesModel.IsImportant(notes.Find(note.Id)!)); Assert.Equal(words, notes.Words(notes.Find(note.Id)!));
                    notes.SetReminder(note.Id, Day.AddHours(15)); Assert.Equal(words, notes.Words(notes.Find(note.Id)!));
                    notes.SetImportant(note.Id, false); Assert.Equal(words, notes.Text(notes.Find(note.Id)!));
                }
            }
            using (var store = new TimerStore(folder))
            {
                var notes = new NotesModel(store, () => Day, () => Array.Empty<string>());
                Assert.True(NotesModel.IsImportant(notes.All.First()));
                Assert.Equal(new[] { "!! literal text", "!!", "\u2063!! literal separator" }, notes.All.Skip(1).Select(notes.Words));
                Assert.All(notes.All.Skip(1), note => Assert.False(NotesModel.IsImportant(note)));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void PageStarDoesNotChangeTypedWordsAndSurvivesStashingTheAddDraft()
    {
        var model = Model(); model.Notes.AddNote("Existing note", null, null); var id = model.Notes.All.Single().Id;
        var window = new DashboardWindow(model); window.Show(); window.ShowNotes(); Settle(window);
        try
        {
            Find<TextBox>(window, "PageNoteField").Text = "!! Literal draft";
            Press(window, "PageImportant"); Assert.Equal("!! Literal draft", Find<TextBox>(window, "PageNoteField").Text);
            var page = window.GetVisualDescendants().OfType<NotesPage>().Single(); page.OpenNote(id); Settle(window);
            Press(window, "PageCancel"); Assert.Equal("!! Literal draft", Find<TextBox>(window, "PageNoteField").Text);
            Assert.True(Find<ToggleButton>(window, "PageImportant").IsChecked);
            Press(window, "PageSubmit"); var added = model.Notes.All.Last();
            Assert.True(NotesModel.IsImportant(added)); Assert.Equal("!! Literal draft", model.Notes.Words(added));
            page.OpenNote(added.Id); Settle(window); Press(window, "PageImportant"); Press(window, "PageSubmit");
            Assert.False(NotesModel.IsImportant(model.Notes.Find(added.Id)!)); Assert.Equal("!! Literal draft", model.Notes.Words(model.Notes.Find(added.Id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NoteAndMeetingAddFormsDoNotClipTheKindPickersShadow()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); window.ShowNotes(); Settle(window);
        try
        {
            var page = window.GetVisualDescendants().OfType<NotesPage>().Single();
            foreach (var meeting in new[] { false, true })
            {
                page.SetKind(meeting); Settle(window);
                Assert.False(Find<ContentControl>(window, "PageForm").ClipToBounds);
                Assert.False(Find<Border>(window, "PageKind").ClipToBounds);
            }
        }
        finally { window.Close(); }
    }
}
