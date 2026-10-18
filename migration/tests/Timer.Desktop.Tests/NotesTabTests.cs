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

public sealed class NotesTabTests
{
    private static (TimerModel Model, DashboardWindow Window) Open()
    {
        var model = new TimerModel(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        return (model, window);
    }
    private static T Find<T>(DashboardWindow window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Type(DashboardWindow window, string words)
    {
        var box = Find<TextBox>(window, "NoteInput"); box.Text = words;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheNotesRailButtonOpensAPageHeadedNotesWithTheDaysNotesAndComingUp()
    {
        var (model, window) = Open();
        try
        {
            Assert.Single(window.GetVisualDescendants().OfType<NotesPage>());
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Notes");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No notes for this day.");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Nothing coming up");
            Assert.Empty(window.GetVisualDescendants().OfType<Control>().Where(c => c.Name == "TimelineZoom" && c.IsEffectivelyVisible)); // No timeline controls over a notes page.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNotesPageLaysItsPartsOutInOrderWithTheBoxBelowTheListAndAShortHint()
    {
        var (model, window) = Open();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var page = window.GetVisualDescendants().OfType<NotesPage>().Single(); var input = Find<TextBox>(window, "NoteInput");
            var heading = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == model.SelectedDate.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")));
            Assert.True(input.Bounds.Height < 80); // A box, not a column that fills the card.
            var headingBottom = heading.TranslatePoint(new Avalonia.Point(0, heading.Bounds.Height), page)!.Value.Y; var inputTop = input.TranslatePoint(new Avalonia.Point(0, 0), page)!.Value.Y;
            Assert.True(headingBottom <= inputTop); // The heading is above the box, not under it.
            Assert.True(input.Watermark!.Length < 50); // Short enough to show in full in the box.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteTypedInTheShorthandKeepsItsTimeAndAccountAndShowsThemAsChips()
    {
        var (model, window) = Open();
        try
        {
            Type(window, "14:30 call Sam #AccountOne");
            var note = Assert.Single(model.NotesOn(model.SelectedDate));
            Assert.Equal(new TimeSpan(14, 30, 0), note.At); Assert.Equal("call Sam #AccountOne", note.Text);
            Assert.Equal("", Find<TextBox>(window, "NoteInput").Text); // The box is empty for the next one.
            Find<Border>(window, "NoteTimeChip_" + note.Id); Find<Border>(window, "NoteAccountChip_" + note.Id);
            Assert.Equal("Account one", NoteShorthand.AccountOf(note.Text, model.AccountNames));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TakingARecognisedPartBackOutPutsItsWordsBackIntoTheNote()
    {
        var (model, window) = Open();
        try
        {
            Type(window, "! 09:15 send invoice #AccountTwo"); var id = Assert.Single(model.NotesOn(model.SelectedDate)).Id;
            Find<Button>(window, "NoteTimeClear_" + id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var note = model.NotesOn(model.SelectedDate).Single();
            Assert.Null(note.At); Assert.StartsWith("! 09:15", note.Text);
            Find<Button>(window, "NoteDeadlineClear_" + id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(DayNotes.IsDeadline(model.NotesOn(model.SelectedDate).Single()));
            Find<Button>(window, "NoteAccountClear_" + id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("#", model.NotesOn(model.SelectedDate).Single().Text); Assert.Contains("AccountTwo", model.NotesOn(model.SelectedDate).Single().Text); // Still reads as a sentence.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TickingMovingAndRemovingANoteWork()
    {
        var (model, window) = Open();
        try
        {
            Type(window, "ring the bank"); var id = Assert.Single(model.NotesOn(model.SelectedDate)).Id;
            Find<CheckBox>(window, "NoteTick_" + id).RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(model.AllDayNotes.Single().Done);
            var tomorrow = model.SelectedDate.AddDays(1);
            ((Calendar)((Flyout)Find<Button>(window, "NoteMove_" + id).Flyout!).Content!).SelectedDate = tomorrow; // The calendar lives in the button's flyout. Dispatcher.UIThread.RunJobs();
            Assert.Empty(model.NotesOn(model.SelectedDate)); Assert.Equal(tomorrow, Assert.Single(model.NotesOn(tomorrow)).Day);
            Assert.Contains("Moved to", model.Notice); // The note left the day showing, so it says where it went.
            model.MoveDayNote(id, model.SelectedDate); Dispatcher.UIThread.RunJobs();
            Find<Button>(window, "NoteRemove_" + id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(model.AllDayNotes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADayNamedInTheWordsWinsAndComingUpListsTheDatedNote()
    {
        var (model, window) = Open();
        try
        {
            Type(window, "board pack @tomorrow");
            Assert.Empty(model.NotesOn(model.SelectedDate)); var note = Assert.Single(model.NotesOn(model.SelectedDate.AddDays(1)));
            Assert.Contains("Added to", model.Notice);
            Assert.NotEmpty(window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name == "ComingTick_" + note.Id)); // A note with no time is a task, ticked from Coming up.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "board pack");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheClockTickingNeverRebuildsThePageUnderThePointer()
    {
        var (model, window) = Open();
        try
        {
            Type(window, "keep this"); var page = window.GetVisualDescendants().OfType<NotesPage>().Single(); var row = Find<TextBox>(window, "NoteText_" + model.AllDayNotes[0].Id);
            for (var i = 0; i < 40; i++) model.Tick(); Dispatcher.UIThread.RunJobs();
            Assert.Same(page, window.GetVisualDescendants().OfType<NotesPage>().Single()); Assert.Same(row, Find<TextBox>(window, "NoteText_" + model.AllDayNotes[0].Id));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnotherTabAndBackStillShowsTheNotes()
    {
        var (model, window) = Open();
        try
        {
            Type(window, "still here");
            window.SelectTab(false); Dispatcher.UIThread.RunJobs(); Assert.Empty(window.GetVisualDescendants().OfType<NotesPage>());
            window.ShowNotes(); Dispatcher.UIThread.RunJobs(); Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "still here");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void RenamingAnAccountRewritesItsTagAndDeletingItLeavesTheNameAsAWord()
    {
        var model = new TimerModel(); model.AddDayNote(model.SelectedDate, "call #AccountOne about it"); model.AddDayNote(model.SelectedDate, "chase #Internal");
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { Name = "Client Alpha" } : a), new Dictionary<string, string> { ["Account one"] = "Client Alpha" });
        Assert.Contains(model.AllDayNotes, n => n.Text == "call #ClientAlpha about it");
        model.SetAccounts(model.AccountSettings.Where(a => a.Name != "Internal"), null, new[] { "Internal" });
        Assert.Contains(model.AllDayNotes, n => n.Text == "chase Internal");
    }

    [Fact]
    public void NotesSurviveARestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "notes-tab-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); model.AddDayNote(model.SelectedDate, "09:00 stand-up #Internal"); model.Save(); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); var note = Assert.Single(model.AllDayNotes); Assert.Equal(new TimeSpan(9, 0, 0), note.At); Assert.Equal("stand-up #Internal", note.Text); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
