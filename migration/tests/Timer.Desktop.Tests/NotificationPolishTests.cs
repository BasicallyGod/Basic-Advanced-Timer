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

public sealed class NotificationPolishTests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void DismissingAMeetingLeavesItPlannedAndItsEndCanStillAskForConfirmation()
    {
        var now = Day.AddHours(14);
        var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup();
        model.AddDayNote(Day, "Review 14:00 - 15:00"); var id = model.AllDayNotes.Single().Id;
        model.DismissMeetingReminder(id);
        Assert.Empty(model.Reminders); Assert.False(model.AllDayNotes.Single().Done);
        Assert.Single(model.MeetingsOn(Day)); Assert.Empty(model.Engine.Sessions);
        now = Day.AddHours(15);
        Assert.Equal(ReminderState.Followup, Assert.Single(model.Reminders).State);
        Assert.False(model.AllDayNotes.Single().Done);
    }

    [Fact]
    public void DismissalSurvivesARestartAndMovingTheMeetingClearsIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "notification-dismiss-" + Guid.NewGuid());
        var now = Day.AddHours(14);
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
                model.AddDayNote(Day, "Review 14:00 - 15:00"); model.DismissMeetingReminder(model.AllDayNotes.Single().Id); Assert.True(model.Save());
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.Empty(model.Reminders); var note = Assert.Single(model.AllDayNotes); Assert.False(note.Done);
                Assert.Null(model.SaveNote(note.Id, new NoteEdit(NoteKind.Meeting, "Review moved", Day, new TimeSpan(14, 5, 0), new TimeSpan(15, 5, 0), null, null, false)));
                Assert.Equal(ReminderState.Soon, Assert.Single(model.Reminders).State);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void UpcomingOnlyItemsHaveNoAttentionBadgeAndAnOpenListUpdatesTitlesAndCountdowns()
    {
        var now = Day.AddHours(14); var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup();
        model.AddDayNote(Day, "14:05 Review #AccountOne"); var id = model.AllDayNotes.Single().Id;
        var dashboard = new DashboardWindow(model); dashboard.Show();
        var list = new Window { Content = new NotificationsPanel(model), Width = 440, Height = 500 }; list.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, model.AttentionReminderCount); Assert.False(Find<Border>(dashboard, "NotificationsBadge").IsVisible);
            model.SetDayNoteWords(id, "Updated review"); Dispatcher.UIThread.RunJobs();
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Updated review");
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account one");
            now = now.AddMinutes(1); model.Tick(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "In 4 minutes · 14:05");
            now = Day.AddHours(14).AddMinutes(5); model.Tick(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, model.AttentionReminderCount); Assert.True(Find<Border>(dashboard, "NotificationsBadge").IsVisible);
        }
        finally { list.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void MinimalPillGivesTheTitleRoomAndListScrollsWithoutGrowingPastItsLimit()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(14))); model.CompleteSetup(); model.SetPillStyle("minimal");
        var title = "Send the revised budget and outstanding supporting materials to the account team";
        model.AddDayNote(Day, "! 14:00 " + title);
        for (var i = 0; i < 12; i++) model.AddDayNote(Day, "! 14:00 Follow up " + i);
        var pill = new PillWindow(model); pill.Show();
        var list = new Window { Content = new NotificationsPanel(model), Width = 440, Height = 600 }; list.Show();
        try
        {
            using var frame = pill.CaptureRenderedFrame(); using var other = list.CaptureRenderedFrame();
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } previews)
            {
                Directory.CreateDirectory(previews); frame?.Save(Path.Combine(previews, "minimal-pill-notifications.png")); other?.Save(Path.Combine(previews, "notifications-list.png"));
            }
            var words = Find<TextBlock>(pill, "ReminderText"); var action = Find<Button>(pill, "ReminderDone");
            Assert.Equal(title, ToolTip.GetTip(words)); Assert.Equal(2, words.MaxLines);
            Assert.True(words.Bounds.Width > 220);
            Assert.True(action.TranslatePoint(default, pill)!.Value.Y >= words.TranslatePoint(new Point(0, words.Bounds.Height), pill)!.Value.Y);
            var card = pill.GetVisualDescendants().OfType<ReminderCard>().Single(); Assert.True(card.Bounds.Width <= PillLayouts.For("minimal").Width);
            var scroll = Find<ScrollViewer>(list, "NotificationsScroll"); Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(list.GetVisualDescendants().OfType<NotificationsPanel>().Single().Bounds.Height <= 480);
            Click(pill, "ReminderDone"); Assert.Equal(12, model.Reminders.Count);
        }
        finally { list.Close(); pill.Close(); }
    }
}
