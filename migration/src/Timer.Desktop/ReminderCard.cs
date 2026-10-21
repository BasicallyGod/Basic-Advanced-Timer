using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Timer.Desktop;

/// <summary>
/// The card for a reminder that is due or overdue (or, when switched on, coming up): what it is, when, and Snooze (a drop-down of times) and Done. After a meeting it asks
/// "Did it take place?" with No and Yes instead. When other reminders are waiting it says "+2 more", which opens the list of them. On the dashboard it floats over the
/// workspace like a message; in the pill it is a plain row inside the card, under the capsule, so it wears the pill's own colours and never changes the pill's width.
/// It is hidden while nothing is waiting. Pressing the words opens Notes.
/// </summary>
public sealed class ReminderCard : Border
{
    private readonly TimerModel model;
    private readonly TextBlock title = new() { FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Name = "ReminderText" };
    private readonly TextBlock sub = new() { FontSize = 12, Name = "ReminderSub", TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private readonly Button snooze, no, done, moreButton;
    private readonly bool inPill;
    private long shownId = -1;
    private bool followup, meeting;
    private bool? minimalApplied;
    private readonly Grid row;
    private readonly StackPanel words, buttons;
    private readonly IconGlyph icon;
    public ReminderCard(TimerModel model, bool inPill)
    {
        this.model = model; this.inPill = inPill; Name = "ReminderCard";
        snooze = ReminderUi.SnoozeButton(model, () => shownId, "ReminderSnooze");
        // After a meeting the same card asks whether it took place: No ticks it off, Yes opens Add time on its hours so the account can be chosen.
        no = Ui.Button("No", () => { if (shownId >= 0) model.DoneReminder(shownId); }, "ReminderNo");
        done = Ui.Button("Done", () => { if (shownId < 0) return; if (followup) model.LogMeeting(shownId); else if (meeting) model.DismissMeetingReminder(shownId); else model.DoneReminder(shownId); }, "ReminderDone");
        foreach (var button in new[] { snooze, no, done }) { button.Classes.Add("toolButton"); button.Classes.Add("quiet"); button.Height = 28; button.Padding = new Thickness(10, 0); button.FontSize = 12; }
        done.Classes.Add("mainAction");
        // "+2 more" opens the whole list, so a busy day is one tap away without the card growing.
        moreButton = Ui.Button("", () => { }, "ReminderMore"); moreButton.Classes.Add("flat"); moreButton.Height = 28; moreButton.Padding = new Thickness(8, 0); moreButton.FontSize = 12; moreButton.Margin = new Thickness(0, 0, 4, 0);
        var listFlyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        listFlyout.Opening += (_, _) => listFlyout.Content = new NotificationsPanel(model, () => listFlyout.Hide());
        moreButton.Flyout = listFlyout; ToolTip.SetTip(moreButton, "See everything waiting");
        words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        words.Children.Add(title); words.Children.Add(sub);
        words.PointerPressed += (_, e) => { if (TopLevel.GetTopLevel(this) is Window window) Ui.CloseMenus(window); model.RequestNotes(); e.Handled = true; };
        icon = new IconGlyph(IconKind.Clock) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        buttons = Ui.Horizontal(snooze, no, done); buttons.Spacing = 6; buttons.Margin = new Thickness(6, 0, 0, 0);
        row = Ui.Grid("Auto,*,Auto,Auto", "Auto");
        Ui.Put(row, icon); Ui.Put(row, words, 1); Ui.Put(row, moreButton, 2); Ui.Put(row, buttons, 3);
        Child = row; IsVisible = false;
        if (inPill) { Padding = new Thickness(16, 6, 14, 12); }
        else
        {
            Classes.Add("messageBar"); CornerRadius = new CornerRadius(12); Padding = new Thickness(14, 8, 8, 8);
            HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(0, 0, 0, 52); MaxWidth = 560;
        }
        model.PropertyChanged += (_, _) => Sync();
        Sync();
    }
    private void LayoutForPill()
    {
        var minimal = inPill && model.PillStyle == "minimal";
        if (minimalApplied == minimal) return;
        minimalApplied = minimal;
        row.Children.Clear();
        row.ColumnDefinitions = new ColumnDefinitions(minimal ? "Auto,*,Auto" : "Auto,*,Auto,Auto");
        row.RowDefinitions = new RowDefinitions(minimal ? "Auto,Auto" : "Auto");
        row.RowSpacing = minimal ? 6 : 0;
        Grid.SetColumnSpan(words, minimal ? 2 : 1);
        Ui.Put(row, icon); Ui.Put(row, words, 1);
        Ui.Put(row, moreButton, minimal ? 1 : 2, row: minimal ? 1 : 0);
        Ui.Put(row, buttons, minimal ? 2 : 3, row: minimal ? 1 : 0);
        title.MaxLines = minimal ? 2 : 1;
        title.TextWrapping = minimal ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
    }

    private void Sync()
    {
        var reminder = inPill ? model.ActiveReminder : model.DashboardReminder;
        IsVisible = reminder is not null;
        if (reminder is null) { shownId = -1; return; }
        shownId = reminder.Note.Id;
        title.Text = Timer.Core.NoteShorthand.Display(reminder.Note, model.AccountNames);
        sub.Text = model.ReminderLine(reminder);
        ToolTip.SetTip(title, title.Text); ToolTip.SetTip(sub, sub.Text);
        ToolTip.SetTip(words, title.Text + "\n" + sub.Text + "\n" + reminder.DueAt.ToString("ddd d MMM HH:mm") + (Timer.Core.NoteShorthand.AccountOf(reminder.Note.Text, model.AccountNames) is { } account ? "\n" + account : ""));
        LayoutForPill();
        followup = reminder.State == ReminderState.Followup;
        meeting = TimerModel.KindOf(reminder.Note) == NoteKind.Meeting;
        snooze.IsVisible = !followup; no.IsVisible = followup; done.Content = followup ? "Yes" : meeting ? "Dismiss" : "Done";
        var others = model.MoreReminders(reminder);
        moreButton.IsVisible = others > 0; moreButton.Content = $"+{others} more";
        var late = reminder.State == ReminderState.Overdue;
        sub.Classes.Set("overtime", late && !inPill); sub.Classes.Set("muted", !late || inPill);
        Classes.Set("late", late && !inPill);
        ToolTip.SetTip(snooze, "Remind me again in a few minutes"); ToolTip.SetTip(no, "It did not take place"); ToolTip.SetTip(done, followup ? "It took place. Add its time" : meeting ? "Dismiss this alert without completing the meeting or recording time" : "Mark this item done");
        Avalonia.Automation.AutomationProperties.SetName(snooze, "Snooze reminder: " + title.Text); Avalonia.Automation.AutomationProperties.SetName(no, "Meeting did not take place: " + title.Text);
        Avalonia.Automation.AutomationProperties.SetName(done, (followup ? "Meeting took place, add its time: " : meeting ? "Dismiss meeting reminder: " : "Mark done: ") + title.Text); Avalonia.Automation.AutomationProperties.SetName(moreButton, $"{others} more waiting");
        Avalonia.Automation.AutomationProperties.SetName(this, "Reminder: " + title.Text);
    }
}

/// <summary>Pieces shared by the reminder card and the notifications list.</summary>
internal static class ReminderUi
{
    /// <summary>A Snooze button that opens a short list of times (the same choices as in Settings) and snoozes the reminder whose id <paramref name="id"/> gives when pressed.</summary>
    public static Button SnoozeButton(TimerModel model, Func<long> id, string name, Action? after = null)
    {
        var button = Ui.Button("Snooze ▾", () => { }, name);
        var choices = new StackPanel { Spacing = 4, Width = 150 }; Flyout? flyout = null;
        foreach (var minutes in ReminderRules.SnoozeChoices)
        {
            var chosen = minutes;
            choices.Children.Add(Ui.DialogButton(chosen + " minutes", () => { flyout?.Hide(); if (id() >= 0) model.SnoozeReminder(id(), chosen); after?.Invoke(); }, name + "_" + chosen));
        }
        flyout = new Flyout { Content = choices, Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight }; button.Flyout = flyout;
        return button;
    }
}
