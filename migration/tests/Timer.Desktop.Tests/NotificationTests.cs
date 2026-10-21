using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The notification list, the bell, "+2 more" on a card, the Snooze drop-down and a meeting's question on the timeline.</summary>
public sealed class NotificationTests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private DateTime now = Day.AddHours(14).AddMinutes(20);
    private TimerModel Model() { var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup(); model.SetReminderSoundOn(false); return model; }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    // Three things waiting at 14:20: a call that was due at 14:00 (overdue), one due at 14:18 (due) and one due at 14:25 (a heads-up).
    private TimerModel Busy() { var model = Model(); model.AddDayNote(Day, "14:00 Call A"); model.AddDayNote(Day, "14:18 Call B"); model.AddDayNote(Day, "14:25 Call C"); return model; }

    [Fact]
    public void WhatNeedsAttentionComesFirstAndTheCardCountsTheRest()
    {
        var model = Busy();
        Assert.Equal(new[] { ReminderState.Overdue, ReminderState.Due, ReminderState.Soon }, model.Reminders.Select(r => r.State).ToArray());
        var active = model.ActiveReminder!; Assert.Equal("Call A", active.Note.Text); Assert.Equal(2, model.MoreReminders(active));
        Assert.Equal(0, model.MoreReminders(null));
    }

    [Fact]
    public void AHeadsUpShowsOnThePillOnlyWhenTheSettingIsOn()
    {
        var model = Model(); model.AddDayNote(Day, "14:25 Call C");
        Assert.Null(model.ActiveReminder); Assert.False(model.ShowUpcoming);          // Only a heads-up: the pill stays as it is.
        model.SetShowUpcoming(true); Assert.Equal("Call C", model.ActiveReminder!.Note.Text); Assert.Equal(ReminderState.Soon, model.ActiveReminder.State);
    }

    [Fact]
    public void SnoozingForAChosenTimeHidesItForThatLongAndAMeetingsQuestionCanBePutOffToo()
    {
        var model = Model(); model.AddDayNote(Day, "14:18 Call B"); var id = model.AllDayNotes.Single().Id;
        model.SnoozeReminder(id, 30); Assert.Null(model.ActiveReminder);
        now = now.AddMinutes(29); Assert.Null(model.ActiveReminder);
        now = now.AddMinutes(2); Assert.Equal(id, model.ActiveReminder!.Note.Id);
        var meeting = Model(); meeting.AddDayNote(Day, "Review 14:00 - 15:00"); var meetingId = meeting.AllDayNotes.Single().Id;
        now = Day.AddHours(15).AddMinutes(10); Assert.Equal(ReminderState.Followup, meeting.ActiveReminder!.State);
        meeting.SnoozeReminder(meetingId, 30); Assert.Null(meeting.ActiveReminder);
        now = now.AddMinutes(31); Assert.Equal(ReminderState.Followup, meeting.ActiveReminder!.State); // Asked again.
    }

    [AvaloniaFact]
    public void TheListGroupsWhatIsWaitingAndItsButtonsWork()
    {
        var model = Busy(); var window = new Window { Content = new NotificationsPanel(model), Width = 500, Height = 600 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var ids = model.AllDayNotes.OrderBy(n => n.At).Select(n => n.Id).ToArray();
            Assert.Equal("3", Find<TextBlock>(window, "NotificationsCount").Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Needs attention · 2"); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Coming up · 1");
            Assert.NotNull(Find<Button>(window, "NotificationSnooze_" + ids[0]).Flyout);
            Click(window, "NotificationDone_" + ids[0]); // Acknowledge the meeting alert without completing the scheduled note.
            Assert.False(model.AllDayNotes.Single(n => n.Id == ids[0]).Done); Assert.Equal("2", Find<TextBlock>(window, "NotificationsCount").Text);
            Assert.Empty(window.GetVisualDescendants().OfType<Control>().Where(c => c.Name == "NotificationItem_" + ids[0]));
            Click(window, "NotificationDone_" + ids[1]); Click(window, "NotificationDone_" + ids[2]);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Nothing needs you right now.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheListAsksAMeetingsQuestionWithNoAndYes()
    {
        var model = Model(); model.AddDayNote(Day, "Review 14:00 - 14:10"); var id = model.AllDayNotes.Single().Id;
        var window = new Window { Content = new NotificationsPanel(model), Width = 500, Height = 400 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal("Yes", Find<Button>(window, "NotificationYes_" + id).Content?.ToString());
            Click(window, "NotificationNo_" + id); Assert.True(model.AllDayNotes.Single().Done);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBellShowsHowManyAreWaitingAndTheCardSaysHowManyMoreAndSnoozesFromADropDown()
    {
        var model = Busy(); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            var bell = Find<Button>(window, "NotificationsBell"); Assert.NotNull(bell.Flyout);
            Assert.True(Find<Border>(window, "NotificationsBadge").IsVisible); Assert.True(Find<Border>(window, "NotificationsBadge").Child is TextBlock { Text: "2" });
            var card = window.GetVisualDescendants().OfType<ReminderCard>().Single(); Assert.True(card.IsVisible);
            var more = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReminderMore"); Assert.True(more.IsVisible); Assert.Equal("+2 more", more.Content?.ToString()); Assert.NotNull(more.Flyout);
            // The Snooze button opens a list of times, and a time chosen snoozes just that reminder for that long.
            var snooze = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReminderSnooze");
            var choices = (StackPanel)((Flyout)snooze.Flyout!).Content!; var thirty = choices.Children.OfType<Button>().Single(b => b.Name == "ReminderSnooze_30");
            var first = model.ActiveReminder!.Note.Id; thirty.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(first, model.ActiveReminder!.Note.Id); now = now.AddMinutes(31); Assert.Contains(model.Reminders, r => r.Note.Id == first);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AMeetingThatHasEndedAsksUnderItsBlockOnTheTimelineAndAnswersClearIt()
    {
        var model = Model(); model.AddDayNote(Day, "Review 14:00 - 14:10"); var id = model.AllDayNotes.Single().Id;
        var window = new DashboardWindow(model); window.Show();
        try
        {
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            var prompt = Find<Border>(window, "TimelinePrompt_" + id); Assert.Contains("take place", Avalonia.Automation.AutomationProperties.GetName(prompt));
            Assert.False(window.GetVisualDescendants().OfType<ReminderCard>().Single().IsVisible); // Not asked twice.
            Assert.NotNull(Find<Button>(window, "TimelinePromptLater_" + id).Flyout);
            Click(window, "TimelinePromptNo_" + id);
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            Assert.Empty(window.GetVisualDescendants().OfType<Control>().Where(c => c.Name == "TimelinePrompt_" + id)); Assert.True(model.AllDayNotes.Single().Done);
        }
        finally { window.Close(); }
    }
}
