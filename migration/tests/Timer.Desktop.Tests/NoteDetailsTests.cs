using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The panel that adds or edits a note with controls, so a meeting, a deadline or a repeat never has to be typed in the shorthand.</summary>
public sealed class NoteDetailsTests
{
    private static readonly DateTime Wednesday = new(2026, 10, 21);
    private readonly DateTime now = Wednesday.AddHours(8);
    private TimerModel Model() { var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup(); return model; }
    private static List<DayNote> Series(TimerModel model) => model.AllDayNotes.Where(n => n.SeriesId is not null).OrderBy(n => n.Day).ToList();

    // The panel is a control built for a flyout, so it is shown in a plain window here to find its parts.
    private static Window Host(Control panel) { var window = new Window { Content = panel, Width = 500, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs(); return window; }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void AMeetingWithARepeatIsAddedFromTheEditAndAnEndBeforeTheStartIsRefused()
    {
        var model = Model();
        Assert.Contains("start", model.AddNote(new NoteEdit(NoteKind.Meeting, "Standup", Wednesday, null, null, null, null, false))!);
        Assert.Contains("after the start", model.AddNote(new NoteEdit(NoteKind.Meeting, "Standup", Wednesday, new TimeSpan(10, 0, 0), new TimeSpan(9, 0, 0), null, null, false))!);
        Assert.Empty(model.AllDayNotes); // Nothing half-added.
        Assert.Null(model.AddNote(new NoteEdit(NoteKind.Meeting, "Standup", Wednesday, new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), model.AccountNames[1], new Recurrence(1, Recurrence.Weekdays), false)));
        var notes = Series(model);
        Assert.True(notes.Count > 30); Assert.All(notes, n => { Assert.Equal(new TimeSpan(9, 30, 0), n.At); Assert.Equal(new TimeSpan(10, 0, 0), n.End); Assert.Equal(model.AccountNames[1], NoteShorthand.AccountOf(n.Text, model.AccountNames)); });
        Assert.Equal(Wednesday, notes[0].Day);
    }

    [Fact]
    public void ChangingAKindMovesTheTimeAndTheDeadlineMarkerAndTheAccountTagWithIt()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Send the invoice");
        var id = model.AllDayNotes.Single().Id; var account = model.AccountNames[0];
        Assert.Null(model.SaveNote(id, new NoteEdit(NoteKind.Deadline, "Send the invoice", Wednesday, new TimeSpan(16, 0, 0), null, account, null, false)));
        var deadline = model.AllDayNotes.Single(); Assert.True(DayNotes.IsDeadline(deadline)); Assert.Equal(new TimeSpan(16, 0, 0), deadline.At); Assert.Equal(account, NoteShorthand.AccountOf(deadline.Text, model.AccountNames));
        Assert.Equal(NoteKind.Deadline, TimerModel.KindOf(deadline));
        Assert.Null(model.SaveNote(id, new NoteEdit(NoteKind.Task, "Send the invoice", Wednesday.AddDays(1), null, null, null, null, false)));
        var task = model.AllDayNotes.Single(); Assert.True(DayNotes.IsTask(task)); Assert.Equal("Send the invoice", task.Text); Assert.Equal(Wednesday.AddDays(1), task.Day);
    }

    [Fact]
    public void MakingANoteRepeatFromItsDetailsStartsASeriesAndThisAndAllLaterCanChangeOrEndIt()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup 09:30 - 10:00"); var id = model.AllDayNotes.Single().Id;
        Assert.Null(model.SaveNote(id, new NoteEdit(NoteKind.Meeting, "Standup", Wednesday, new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), null, new Recurrence(1, Recurrence.Weekdays), false)));
        var count = Series(model).Count; Assert.True(count > 30); Assert.NotNull(model.SeriesOf(Series(model)[0]));
        // This one only: just its words change.
        var mid = Series(model).First(n => n.Day == Wednesday.AddDays(2));
        Assert.Null(model.SaveNote(mid.Id, new NoteEdit(NoteKind.Meeting, "Standup (moved)", mid.Day, new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0), null, null, false)));
        Assert.Equal(count, Series(model).Count); Assert.Equal("Standup (moved)", model.AllDayNotes.Single(n => n.Id == mid.Id).Text); Assert.Equal("Standup", Series(model).First(n => n.Day == Wednesday.AddDays(5)).Text);
        // This and all later, with a new repeat: later notes are made again, on Mondays only.
        var monday = Series(model).First(n => n.Day == new DateTime(2026, 10, 26));
        Assert.Null(model.SaveNote(monday.Id, new NoteEdit(NoteKind.Meeting, "Weekly standup", monday.Day, new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), null, new Recurrence(1, 0), true)));
        Assert.All(Series(model).Where(n => n.Day > monday.Day), n => { Assert.Equal(DayOfWeek.Monday, n.Day.DayOfWeek); Assert.Equal("Weekly standup", n.Text); });
        // This and all later, never: the later ones go and this one stands alone.
        Assert.Null(model.SaveNote(monday.Id, new NoteEdit(NoteKind.Meeting, "Weekly standup", monday.Day, new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), null, null, true)));
        Assert.DoesNotContain(model.AllDayNotes, n => n.Day > monday.Day); Assert.Null(model.SeriesOf(model.AllDayNotes.Single(n => n.Id == monday.Id)));
    }

    [Fact]
    public void WordsEditedInARowKeepTheDeadlineMarkerAndTheAccountTag()
    {
        var model = Model(); model.AddDayNote(Wednesday, "! send invoice #" + model.AccountNames[0].Replace(" ", ""));
        var id = model.AllDayNotes.Single().Id;
        model.SetDayNoteWords(id, "send the final invoice");
        var note = model.AllDayNotes.Single(); Assert.True(DayNotes.IsDeadline(note)); Assert.Equal(model.AccountNames[0], NoteShorthand.AccountOf(note.Text, model.AccountNames)); Assert.Contains("send the final invoice", note.Text);
    }

    [AvaloniaFact]
    public void ThePanelAddsAMeetingThatRepeatsFromItsControls()
    {
        var model = Model(); var close = 0;
        var window = Host(NoteDetails.Build(model, null, Wednesday, () => close++));
        try
        {
            Click(window, "NoteKind_Meeting");
            Find<TextBox>(window, "NoteDetailsWords").Text = "Standup";
            Find<TextBox>(window, "NoteDetailsStart").Text = "09:30"; Find<TextBox>(window, "NoteDetailsEnd").Text = "10:00";
            Find<ComboBox>(window, "NoteDetailsRepeat").SelectedIndex = 2; Dispatcher.UIThread.RunJobs(); // Every weekday.
            Click(window, "NoteDetailsAdd");
            Assert.Equal(1, close); Assert.Equal("", Find<TextBlock>(window, "NoteDetailsError").Text ?? "");
            var notes = Series(model); Assert.True(notes.Count > 30); Assert.Equal(new TimeSpan(10, 0, 0), notes[0].End);
            Assert.Equal("Every weekday", model.SeriesOf(notes[0])!.Rule.Words());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePanelSaysWhatIsWrongAndKeepsItselfOpen()
    {
        var model = Model(); var close = 0;
        var window = Host(NoteDetails.Build(model, null, Wednesday, () => close++));
        try
        {
            Click(window, "NoteKind_Meeting");
            Find<TextBox>(window, "NoteDetailsWords").Text = "Call"; Find<TextBox>(window, "NoteDetailsStart").Text = "10:00"; Find<TextBox>(window, "NoteDetailsEnd").Text = "9:00";
            Click(window, "NoteDetailsAdd");
            Assert.Equal(0, close); Assert.Contains("after the start", Find<TextBlock>(window, "NoteDetailsError").Text); Assert.Empty(model.AllDayNotes);
            Find<TextBox>(window, "NoteDetailsEnd").Text = "ten"; Click(window, "NoteDetailsAdd");
            Assert.Contains("HH:mm", Find<TextBlock>(window, "NoteDetailsError").Text); Assert.Empty(model.AllDayNotes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnExistingRepeatingNoteOpensWithItsRepeatAndOnlyChangesItWhenAllLaterIsChosen()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup 09:30 - 10:00 every weekday");
        var note = Series(model)[1];
        var window = Host(NoteDetails.Build(model, note, Wednesday, () => { }));
        try
        {
            Assert.Equal(2, Find<ComboBox>(window, "NoteDetailsRepeat").SelectedIndex); Assert.Equal("Standup", Find<TextBox>(window, "NoteDetailsWords").Text);
            Assert.False(Find<ComboBox>(window, "NoteDetailsRepeat").IsEffectivelyEnabled); // Repeat is greyed until all later is chosen.
            Find<RadioButton>(window, "NoteDetailsScopeLater").IsChecked = true; Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "NoteDetailsRepeat").SelectedIndex = 0; Click(window, "NoteDetailsSave"); // Never, for this and all later.
            Assert.DoesNotContain(model.AllDayNotes, n => n.Day > note.Day);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EachRowHasADetailsButtonAndAddNoteOpensTheSamePanel()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Book the room");
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            var id = model.AllDayNotes.Single().Id;
            Assert.NotNull(Find<Button>(window, "NoteDetails_" + id).Flyout); Assert.NotNull(Find<Button>(window, "NoteAddButton").Flyout);
            Assert.Equal("Book the room", Find<TextBox>(window, "NoteText_" + id).Text);
        }
        finally { window.Close(); }
    }
}
