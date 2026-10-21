using Avalonia;
using Avalonia.Automation;
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

/// <summary>Quick capture keeps what was typed when it loses focus, a snooze survives a restart, and the Notes controls say what they are to a screen reader.</summary>
public sealed class NotesPolishTwoTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "notes-polish-two-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }

    private static TextBox Words(QuickCaptureWindow window) => window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CaptureText");
    private static void Press(TextBox box, Key key) { box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key }); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void ClickingAwayKeepsWhatWasTypedAndEscapeThrowsItAway()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 20, 10, 0, 0))); model.CompleteSetup();
        var first = new QuickCaptureWindow(model); first.Show();
        Words(first).Text = "Call Sam back"; first.ClickAway();
        Assert.False(first.IsVisible); Assert.Equal("Call Sam back", model.QuickCaptureDraft); Assert.Empty(model.AllDayNotes); // Not saved, but not lost.
        var second = new QuickCaptureWindow(model); second.Show();
        Assert.Equal("Call Sam back", Words(second).Text);
        Press(Words(second), Key.Enter);
        Assert.Equal("Call Sam back", Assert.Single(model.AllDayNotes).Text); Assert.Null(model.QuickCaptureDraft); // Saved, so the draft is spent.
        var third = new QuickCaptureWindow(model); third.Show();
        Assert.True(string.IsNullOrEmpty(Words(third).Text));
        Words(third).Text = "Throw this away"; Press(Words(third), Key.Escape);
        Assert.Null(model.QuickCaptureDraft); Assert.Single(model.AllDayNotes); // Escape means it.
        var fourth = new QuickCaptureWindow(model); fourth.Show(); fourth.ClickAway(); // An empty box leaves no draft.
        Assert.Null(model.QuickCaptureDraft);
    }

    [Fact]
    public void ASnoozeSurvivesARestartAndEndsOnTime()
    {
        var clock = new DateTime(2026, 10, 20, 14, 30, 0); long id;
        using (var store = new TimerStore(folder))
        {
            var engine = new TrackingEngine(clock: () => clock); var model = new TimerModel(engine, store.Load(engine), store);
            model.AddDayNote(clock.Date, "14:27 Call back"); id = model.AllDayNotes.Single().Id;
            Assert.Equal(ReminderState.Due, model.ActiveReminder!.State);
            model.SnoozeReminder(id); Assert.Null(model.ActiveReminder);
        }
        using (var store = new TimerStore(folder))
        {
            var engine = new TrackingEngine(clock: () => clock); var again = new TimerModel(engine, store.Load(engine), store);
            Assert.Null(again.ActiveReminder); // Still snoozed after the restart.
            clock = clock.AddMinutes(again.ReminderSnooze + 1);
            Assert.Equal(id, again.ActiveReminder!.Note.Id); // And due again when the snooze is up.
        }
    }

    [AvaloniaFact]
    public void TheNotesControlsHaveNamesForAScreenReader()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        try
        {
            var all = window.GetVisualDescendants().OfType<Control>().ToList();
            bool Named(Control c) => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(c));
            foreach (var prefix in new[] { "NoteTick_", "NoteText_", "NoteRemove_", "NoteDetails_", "ComingTick_", "ComingDone_", "ComingEvent_" })
            {
                var group = all.Where(c => c.Name?.StartsWith(prefix, StringComparison.Ordinal) == true).ToList();
                Assert.All(group, c => Assert.True(Named(c), prefix + " has no name for a screen reader"));
            }
            Assert.NotEmpty(all.Where(c => c.Name?.StartsWith("NoteTick_", StringComparison.Ordinal) == true)); // The demo has notes today, so this is not vacuous.
            foreach (var name in new[] { "NoteInput", "ComingRange" }) Assert.True(Named(all.Single(c => c.Name == name)), name);
            // Each note's controls say which note, since the rows look alike.
            var tick = all.First(c => c.Name?.StartsWith("NoteTick_", StringComparison.Ordinal) == true); Assert.StartsWith("Done: ", AutomationProperties.GetName(tick));
        }
        finally { window.Close(); }
        var capture = new QuickCaptureWindow(model); capture.Show();
        try { Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(Words(capture)))); } finally { capture.Close(); }
    }
}
