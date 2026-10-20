using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Timer.Desktop;

/// <summary>
/// The card for a reminder that is due or overdue: what it is, when, and Snooze and Done. On the dashboard it floats over the workspace like a message;
/// in the pill it is a plain row inside the card, under the capsule, so it wears the pill's own colours and never changes the pill's width.
/// It is hidden while nothing is due. Pressing the words opens Notes.
/// </summary>
public sealed class ReminderCard : Border
{
    private readonly TimerModel model;
    private readonly TextBlock title = new() { FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Name = "ReminderText" };
    private readonly TextBlock sub = new() { FontSize = 12, Name = "ReminderSub", TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private readonly Button snooze, done;
    private readonly bool inPill;
    private long shownId = -1;
    public ReminderCard(TimerModel model, bool inPill)
    {
        this.model = model; this.inPill = inPill; Name = "ReminderCard";
        snooze = Ui.Button("Snooze", () => { if (shownId >= 0) model.SnoozeReminder(shownId); }, "ReminderSnooze");
        done = Ui.Button("Done", () => { if (shownId >= 0) model.DoneReminder(shownId); }, "ReminderDone");
        foreach (var button in new[] { snooze, done }) { button.Classes.Add("toolButton"); button.Classes.Add("quiet"); button.Height = 28; button.Padding = new Thickness(10, 0); button.FontSize = 12; }
        done.Classes.Add("mainAction");
        var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        words.Children.Add(title); words.Children.Add(sub);
        words.PointerPressed += (_, e) => { if (TopLevel.GetTopLevel(this) is Window window) Ui.CloseMenus(window); model.RequestNotes(); e.Handled = true; };
        var icon = new IconGlyph(IconKind.Clock) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var buttons = Ui.Horizontal(snooze, done); buttons.Spacing = 6; buttons.Margin = new Thickness(10, 0, 0, 0);
        var row = Ui.Grid("Auto,*,Auto", "Auto");
        Ui.Put(row, icon); Ui.Put(row, words, 1); Ui.Put(row, buttons, 2);
        Child = row; IsVisible = false;
        if (inPill) { Padding = new Thickness(16, 6, 14, 12); }
        else
        {
            Classes.Add("messageBar"); CornerRadius = new CornerRadius(12); Padding = new Thickness(14, 8, 8, 8);
            HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(0, 0, 0, 52); MaxWidth = 520;
        }
        model.PropertyChanged += (_, _) => Sync();
        Sync();
    }
    private void Sync()
    {
        var reminder = model.ActiveReminder;
        IsVisible = reminder is not null;
        if (reminder is null) { shownId = -1; return; }
        shownId = reminder.Note.Id;
        title.Text = Timer.Core.NoteShorthand.Display(reminder.Note, model.AccountNames);
        sub.Text = model.ReminderLine(reminder);
        var late = reminder.State == ReminderState.Overdue;
        sub.Classes.Set("overtime", late && !inPill); sub.Classes.Set("muted", !late || inPill);
        Classes.Set("late", late && !inPill);
        ToolTip.SetTip(snooze, $"Remind me again in {model.ReminderSnooze} minutes");
        Avalonia.Automation.AutomationProperties.SetName(snooze, "Snooze reminder: " + title.Text); Avalonia.Automation.AutomationProperties.SetName(done, "Mark done: " + title.Text); Avalonia.Automation.AutomationProperties.SetName(this, "Reminder: " + title.Text + ", " + sub.Text);
    }
}
