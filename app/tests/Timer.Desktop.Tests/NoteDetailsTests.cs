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

// A note is selected from the Notes card or its pin, and Details edits it: the one rule for the page is select something, edit it in Details.
public sealed class NoteDetailsTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); return model;
    }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static void Commit(Window window, string name, string text) { var box = Find<TextBox>(window, name); box.Text = text; box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle(window); }

    [AvaloniaFact]
    public void SelectingANoteShowsItInDetailsAndSelectingABlockOrMeetingOrNothingLetsItGo()
    {
        var model = Model(); model.Notes.AddNote("Ring the bank", "Account one", Day.AddHours(15)); model.Notes.AddMeeting(Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Review", null, RepeatChoice.None);
        var id = model.Notes.All.Single(n => !NotesModel.IsMeeting(n)).Id; var meeting = model.Notes.MeetingsOn(Day).Single().Id;
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NoteEdit_" + id); Assert.Equal(id, model.SelectedNoteId); Assert.Contains("rowSelected", Find<TextBlock>(window, "NoteText_" + id).GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("noteRow")).Classes);
            model.SelectMeeting(meeting); Settle(window); Assert.Null(model.SelectedNoteId); Assert.Equal(meeting, model.SelectedMeetingId); Assert.NotNull(Find<TextBox>(window, "DetailsMeetingName"));
            model.SelectNote(id); Settle(window); Assert.Null(model.SelectedMeetingId); Assert.NotNull(Find<TextBox>(window, "DetailsNoteWords"));
            model.ChangeDate(1); Settle(window); Assert.Null(model.SelectedNoteId); // Another day lets it go.
            model.SelectNote(id); model.ClearSelection(); Assert.Null(model.SelectedNoteId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetailsEditsAnAccountImportantAndDoneAndDeleteHasUndo()
    {
        var model = Model(); model.Notes.AddNote("Ring the bank", null, null); var id = model.Notes.All.Single().Id;
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectNote(id); Settle(window);
            Assert.Contains(Find<Button>(window, "DetailsNoteReminder").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No reminder");
            Find<ComboBox>(window, "DetailsNoteAccount").SelectedItem = "Account two"; Settle(window); Assert.Equal("Account two", model.Notes.AccountOf(model.Notes.Find(id)!));
            Click(window, "DetailsNoteImportant"); Assert.True(NotesModel.IsImportant(model.Notes.Find(id)!)); Assert.Equal(id, model.SelectedNoteId); // Still selected, the box follows.
            Assert.Equal("!! Ring the bank", Find<TextBox>(window, "DetailsNoteWords").Text);
            Click(window, "DetailsNoteDone"); Assert.True(model.Notes.Find(id)!.Done);
            Commit(window, "DetailsNoteWords", ""); Assert.Equal("Write a note first", model.EditError); Assert.Equal("!! Ring the bank", Find<TextBox>(window, "DetailsNoteWords").Text); // A refused edit puts the saved words back.
            Click(window, "DeleteNote"); Assert.Empty(model.Notes.All); Assert.Null(model.SelectedNoteId); Assert.True(model.NoticeHasUndo);
            model.UndoNotice(); Assert.Single(model.Notes.All); // The toast's Undo brings it back.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCardHasNoAddButtonsAndItsLineFollowsTheSwitchWithAStartAndAnEndForAMeeting()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name is "AddNote" or "AddMeeting");
            Assert.NotNull(Find<TextBox>(window, "NoteField")); Assert.NotNull(Find<Button>(window, "FormBell")); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), t => t.Name is "MeetingStart");
            Click(window, "CardKindMeetings"); Assert.NotNull(Find<TextBox>(window, "MeetingStart")); Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "FormBell");
            Find<TextBox>(window, "NoteField").Text = "Plan the week"; Click(window, "SubmitNote");
            Assert.Equal("Plan the week", model.Notes.Text(model.Notes.MeetingsOn(Day).Single())); Assert.Equal("", Find<TextBox>(window, "NoteField").Text ?? ""); Assert.Equal("Meeting added", model.Notice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARowsActionsShowOnHoverAndTheChipsAreCompact()
    {
        var model = Model(); model.Notes.AddNote("Ring the bank", "Account one", Day.AddHours(15)); var id = model.Notes.All.Single().Id;
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var bin = Find<Button>(window, "NoteRemove_" + id); Assert.Equal(0, bin.Opacity); Assert.False(bin.IsHitTestVisible);
            var row = bin.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("noteRow")); var at = row.TranslatePoint(new Point(40, 15), window)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, at); Settle(window); Assert.Equal(1, bin.Opacity);
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "15:00"); // The reminder is the time alone today.
            Assert.DoesNotContain(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "#Accountone"); // The account is a dot, its name in a tooltip.
        }
        finally { window.Close(); }
    }
}
