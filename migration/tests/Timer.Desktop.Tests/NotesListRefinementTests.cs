using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NotesListRefinementTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0);
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static DashboardWindow Open(TimerModel model)
    {
        model.CompleteSetup(); var window = new DashboardWindow(model); window.Show(); window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "SidebarSlot").Transitions = null;
        Click(window, "NotesTab"); return window;
    }

    [AvaloniaFact]
    public void ALongTitleOpensItsFullEditorAndHoverToolsNeverStealItsWidth()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now));
        var words = "Prepare the briefing materials for the financial review with the entire account team and include every outstanding question";
        model.AddDayNote(Now.Date, words); var id = model.AllDayNotes.Single().Id;
        var window = Open(model);
        try
        {
            using var first = window.CaptureRenderedFrame();
            var title = Find<Button>(window, "NoteText_" + id);
            var label = Assert.IsType<TextBlock>(title.Content);
            Assert.Equal(2, label.MaxLines); Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(words, ToolTip.GetTip(title)); Assert.True(title.Bounds.Width > 0);
            var before = title.Bounds;
            Find<Grid>(window, "NoteRow_" + id).Classes.Add("revealing");
            using var hover = window.CaptureRenderedFrame(); Assert.Equal(before, title.Bounds);
            Click(window, title.Name!);
            var dialog = Assert.IsType<NoteEditorDialog>(Assert.Single(window.Dialogs.Dialogs)); dialog.Settle();
            Assert.Equal(words, Find<TextBox>(window, "NoteDetailsWords").Text);
            Find<TextBox>(window, "NoteDetailsWords").Text = "Updated briefing";
            Click(window, "NoteDetailsSave");
            Assert.Equal("Updated briefing", model.AllDayNotes.Single().Text);
            Assert.Equal("Updated briefing", ((TextBlock)Find<Button>(window, "NoteText_" + id).Content!).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MeetingsHaveNoCompletionCheckboxAndDeadlineFlagsUpdateComingUp()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now));
        model.AddDayNote(Now.Date, "14:00 Team meeting"); var meeting = model.AllDayNotes.Single();
        model.AddDayNote(Now.Date, "! Send invoice"); var deadline = model.AllDayNotes.Last();
        var window = Open(model);
        try
        {
            Find<Border>(window, "NoteMeetingMark_" + meeting.Id);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "NoteTick_" + meeting.Id);
            Assert.Equal("Meetings", Find<Button>(window, "NotesFilter_events").Content);
            Find<Button>(window, "ComingDone_" + deadline.Id);
            Click(window, "NoteDeadlineMark_" + deadline.Id);
            Assert.True(model.AllDayNotes.Single(n => n.Id == deadline.Id).Done);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), c => c.Name == "ComingDone_" + deadline.Id);
            Click(window, "NoteDeadlineMark_" + deadline.Id);
            Assert.False(model.AllDayNotes.Single(n => n.Id == deadline.Id).Done);
            Find<Button>(window, "ComingDone_" + deadline.Id);
            Assert.Empty(model.Engine.Sessions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComingUpStillLooksForwardWhenTheDayListMovesIntoThePast()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now));
        model.AddDayNote(Now.Date.AddDays(1), "Tomorrow's task"); var tomorrow = model.AllDayNotes.Single();
        model.AddDayNote(Now.Date.AddDays(-2), "Historical task"); var past = model.AllDayNotes.Last();
        var window = Open(model);
        try
        {
            model.GoToDate(Now.Date.AddDays(-2)); Dispatcher.UIThread.RunJobs();
            Find<Button>(window, "NoteText_" + past.Id);
            Find<CheckBox>(window, "ComingTick_" + tomorrow.Id);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "ComingTick_" + past.Id);
        }
        finally { window.Close(); }
    }
}
