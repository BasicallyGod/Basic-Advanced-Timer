using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NativeAcceptanceFindingsTests
{
    private static readonly DateTime Day = new(2026, 11, 15);
    private static void Settle(Window window) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Model(TimerStore? store = null)
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(14));
        return new TimerModel(engine, store?.Load(engine) ?? new StoreSettings(new() { new("NASA R&D") }, false, "Light", DayOfWeek.Monday), store);
    }

    [AvaloniaFact]
    public void FinalSetupActionSpeaksItsCurrentCaptionAndIconPanelsKeepTheirName()
    {
        var model = Model(); var setup = new OnboardingView(model); var window = new Window { Width = 1000, Height = 800, Content = setup }; window.Show(); Settle(window);
        try
        {
            var next = setup.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SetupNext");
            var peer = ControlAutomationPeer.CreatePeerForElement(next)!;
            Assert.Equal("Next", peer.GetName());
            setup.Advance(); setup.Advance(); setup.Advance(); Settle(window);
            Assert.Equal("Open timer", next.Content); Assert.Equal("Open timer", peer.GetName());
            next.Content = new StackPanel { Children = { new TextBlock { Text = "Open timer" } } };
            Assert.Equal("Open timer", peer.GetName());
            AutomationProperties.SetName(next, "Explicit action description"); next.Content = "Different caption";
            Assert.Equal("Explicit action description", peer.GetName());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCreationsShowRetryingBeforeAnyEditorIsOpenedAndClearAfterRecovery(bool meeting)
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-native-badge-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder); var model = Model(store); model.CompleteSetup();
            store.Database.Execute("CREATE TRIGGER badge_fail BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'fixture failure'); END");
            if (meeting) model.Notes.AddMeeting(Day, TimeSpan.FromHours(15), TimeSpan.FromHours(16), "Queued meeting", null, RepeatChoice.None);
            else model.Notes.AddNote("Queued note", null, null);
            var note = Assert.Single(model.Notes.All); Assert.True(note.Id < 0);
            var page = new NotesPage(model); var window = new Window { Width = 1000, Height = 700, Content = page }; window.Show(); Settle(window);
            try
            {
                var badge = window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "DraftStatus_" + note.Id);
                Assert.True(badge.IsVisible); Assert.Equal("Retrying", badge.Text); Assert.Equal(0, model.Drafts.TrackedCount);
                store.Database.Execute("DROP TRIGGER badge_fail"); model.Notes.RetryUnsaved(); Settle(window);
                Assert.True(Assert.Single(model.Notes.All).Id > 0); Assert.False(model.Notes.Unsaved);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), c => c.Name?.StartsWith("DraftStatus_") == true && c.IsVisible);
                Assert.Equal(0, model.Drafts.TrackedCount);
            }
            finally { window.Close(); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void UnopenedSavedRowShowsFailedUpdatesWithoutLeasingADraft()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-native-update-badge-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder); var model = Model(store); model.CompleteSetup(); model.Notes.AddNote("Saved note", null, null);
            var id = Assert.Single(model.Notes.All).Id;
            var page = new NotesPage(model); var window = new Window { Width = 1000, Height = 700, Content = page }; window.Show(); Settle(window);
            TextBlock Badge() => window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "DraftStatus_" + id);
            try
            {
                Assert.False(Badge().IsVisible);
                store.Database.Execute("CREATE TRIGGER badge_update_fail BEFORE UPDATE ON day_notes BEGIN SELECT RAISE(ABORT, 'fixture failure'); END");
                model.Notes.Toggle(id); Settle(window);
                Assert.Equal("Retrying", Badge().Text); Assert.True(Badge().IsVisible); Assert.Equal(0, model.Drafts.TrackedCount);
                store.Database.Execute("DROP TRIGGER badge_update_fail"); model.Notes.RetryUnsaved(); Settle(window);
                Assert.False(Badge().IsVisible); Assert.True(Assert.Single(store.LoadDayNotes()).Done);
            }
            finally { window.Close(); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(18)]
    public void ReminderAutomationAdjustsAnExactMinuteOnItsWorkingDayAndCommitsExplicitly(int dayStart)
    {
        var model = Model(); model.SetDayStart(TimeSpan.FromHours(dayStart)); DateTime? committed = null;
        var strip = new RescheduleStrip(model, due => committed = due); var selected = Day.AddDays(1);
        strip.Show(selected, selected.AddHours(23).AddMinutes(59));
        var window = new Window { Width = 400, Height = 200, Content = strip }; window.Show(); Settle(window);
        try
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(strip)!; var value = Assert.IsAssignableFrom<IValueProvider>(peer);
            Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType()); Assert.False(value.IsReadOnly);
            Assert.Equal("23:59", value.Value); Assert.Contains("Reminder time", peer.GetName());
            value.SetValue("02:17"); Assert.Null(committed); Assert.Equal("02:17", value.Value);
            var expected = selected.AddDays(dayStart == 18 ? 1 : 0).AddHours(2).AddMinutes(17);
            Assert.Equal(expected, strip.Pin);
            Assert.Throws<ArgumentException>(() => value.SetValue("24:00")); Assert.Equal(expected, strip.Pin);
            Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke(); Assert.Equal(expected, committed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReminderAutomationCannotCommitAPinThatBecamePastOrEditADisabledStrip()
    {
        var now = Day.AddHours(14); var model = new TimerModel(new TrackingEngine(clock: () => now)); DateTime? committed = null;
        var strip = new RescheduleStrip(model, due => committed = due); strip.Show(Day, now.AddMinutes(10));
        var window = new Window { Content = strip }; window.Show(); Settle(window);
        try
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(strip)!; var value = Assert.IsAssignableFrom<IValueProvider>(peer);
            Assert.Throws<ArgumentOutOfRangeException>(() => value.SetValue("13:59"));
            Assert.Throws<ArgumentException>(() => value.SetValue("unreadable"));
            now = now.AddMinutes(11); Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke(); Assert.Null(committed); Assert.True(strip.Pin >= now);
            strip.IsEnabled = false; Assert.ThrowsAny<Exception>(() => value.SetValue("15:00")); Assert.Null(committed);
        }
        finally { window.Close(); }
    }
}
