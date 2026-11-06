using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class MeetingReliabilityTests
{
    private static readonly DateTime Day = new(2026, 11, 6);
    private static TimerModel Model(TimerStore? store = null)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddDays(1).AddHours(3)),
            new StoreSettings(new() { new("Account one") }, false, "Dark", DayOfWeek.Monday), store);
        model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }
    private static void Click(Window window, string name)
    {
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
    }
    private static void Commit(Window window, string name, string value)
    {
        var box = Find<TextBox>(window, name); box.Focus(); box.Text = value;
        box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Settle(window);
    }

    [Fact]
    public void BlocksOnOneNightShiftCanBeCopiedAcrossMidnight()
    {
        var model = Model(); model.SetDayStart(TimeSpan.FromHours(18)); model.GoToDate(Day);
        Assert.True(model.AddEntry("Account one", Day.AddHours(22), Day.AddHours(23), "Before midnight"));
        Assert.True(model.AddEntry("Account one", Day.AddDays(1).AddHours(1), Day.AddDays(1).AddHours(2), "After midnight"));
        Assert.All(model.Engine.Sessions, row => Assert.True(model.CountsOn(row, Day)));
        Assert.True(model.CopyBlocks(model.Engine.Sessions.ToArray()));
        Assert.Null(model.EditError);
        Assert.True(model.PasteBlocks(Day.AddDays(1).AddHours(22))); Assert.False(model.CanPaste);
        Assert.Equal(2, model.Engine.Sessions.Count(row => model.CountsOn(row, Day.AddDays(1))));
        Assert.True(model.AddEntry("Account one", Day.AddHours(10), Day.AddHours(11), "Another workday"));
        Assert.False(model.CopyBlocks(model.Engine.Sessions.Take(2).Append(model.Engine.Sessions.Last()).ToArray()));
        Assert.Equal("One day at a time", model.EditError);
    }

    [AvaloniaFact]
    public void UnsavedSeriesDraftRefusesSelectionUntilSaveOrDiscard()
    {
        var model = Model(); model.GoToDate(Day);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original meeting", null, RepeatChoice.Day);
        model.Notes.AddNote("Other item", null, null);
        var meeting = model.Notes.MeetingsOn(Day).Single(); var note = model.Notes.All.Single(n => !NotesModel.IsMeeting(n));
        var window = new DashboardWindow(model); window.Show(); model.SelectMeeting(meeting.Id); Settle(window);
        try
        {
            Commit(window, "DetailsMeetingName", "Unsaved replacement");
            Assert.Contains("Unsaved changes", Find<TextBlock>(window, "DetailsSaveState").Text);
            model.SelectNote(note.Id); Settle(window); Assert.Null(model.SelectedNoteId); Assert.Equal(meeting.Id, model.SelectedMeetingId); Assert.Contains("Save or discard", model.EditError);
            model.SelectMeeting(meeting.Id); Settle(window);
            Assert.Equal("Unsaved replacement", Find<TextBox>(window, "DetailsMeetingName").Text);
            Assert.Equal("Original meeting", model.Notes.Words(model.Notes.Find(meeting.Id)!));
            model.ChangeDate(-1); Settle(window); Assert.Equal(Day, model.SelectedDate);
            Click(window, "DetailsSave"); Assert.Equal("Unsaved replacement", model.Notes.Words(model.Notes.Find(meeting.Id)!));
            Assert.Null(model.EditError); model.SelectNote(note.Id); Settle(window); Assert.Equal(note.Id, model.SelectedNoteId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AfterMidnightTimelineRequestKeepsItsCalendarDate()
    {
        var model = Model(); model.SetDayStart(TimeSpan.FromHours(18)); model.GoToDate(Day);
        var target = Day.AddDays(1).AddHours(2);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.RequestNewMeeting(target); Settle(window);
            Assert.Equal("02:00", Find<TextBox>(window, "MeetingStart").Text);
            Find<TextBox>(window, "NoteField").Text = "Night meeting"; Click(window, "SubmitNote");
            var saved = Assert.Single(model.Notes.All);
            Assert.Equal(target, saved.Day + saved.At!.Value);
            Assert.Equal(Day.AddDays(1), saved.Day);
            Assert.Single(model.Notes.MeetingsOn(Day));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetailsReportsQueuedMeetingWritesAndRecovery(bool repeating)
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-review-saved-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder); var model = Model(store); model.GoToDate(Day);
            model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original", null, repeating ? RepeatChoice.Day : RepeatChoice.None);
            var meeting = model.Notes.MeetingsOn(Day).Single();
            store.Database.Execute("CREATE TRIGGER fail_meeting BEFORE UPDATE ON day_notes BEGIN SELECT RAISE(ABORT, 'review write failure'); END");
            var window = new DashboardWindow(model); window.Show(); model.SelectMeeting(meeting.Id); Settle(window);
            try
            {
                Commit(window, "DetailsMeetingName", "Queued change");
                if (repeating) Click(window, "DetailsSave");
                Assert.True(model.Notes.Unsaved); Assert.NotNull(model.SaveError);
                Assert.Equal("Not saved · retrying", Find<TextBlock>(window, "DetailsSaveState").Text);
                Assert.Equal("Original", store.LoadDayNotes().Single(n => n.Id == meeting.Id).Text);
                store.Database.Execute("DROP TRIGGER fail_meeting"); model.Notes.RetryUnsaved(); Settle(window);
                Assert.Equal("Saved", Find<TextBlock>(window, "DetailsSaveState").Text); Assert.False(model.Notes.Unsaved);
                Assert.Equal("Queued change", store.LoadDayNotes().Single(n => n.Id == meeting.Id).Text);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void ChangingSeriesStartRepeatedlyKeepsThePendingDuration()
    {
        var model = Model(); model.GoToDate(Day);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Meeting", null, RepeatChoice.Day);
        var meeting = model.Notes.MeetingsOn(Day).Single();
        var window = new DashboardWindow(model); window.Show(); model.SelectMeeting(meeting.Id); Settle(window);
        try
        {
            Commit(window, "DetailsMeetingStart", "10:00"); Assert.Equal("11:00", Find<TextBox>(window, "DetailsMeetingEnd").Text);
            Commit(window, "DetailsMeetingStart", "11:00"); Assert.Equal("12:00", Find<TextBox>(window, "DetailsMeetingEnd").Text);
            Click(window, "DetailsSave"); Assert.Null(model.EditError);
            Assert.Equal(TimeSpan.FromHours(11), model.Notes.Find(meeting.Id)!.At); Assert.Equal(TimeSpan.FromHours(12), model.Notes.Find(meeting.Id)!.End);
        }
        finally { window.Close(); }
    }
}

