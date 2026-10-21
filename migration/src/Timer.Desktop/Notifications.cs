using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// Everything waiting for the person, in one list: what needs attention (overdue, due, and a meeting's "did it take place?") and what is coming up, each with the same
/// buttons as its card. It is the content of the flyout opened by the bell on the dashboard and by "+2 more" on a reminder card, and it follows the model while open.
/// </summary>
public sealed class NotificationsPanel : StackPanel
{
    private readonly TimerModel model; private readonly Action? close; private string shown = "";
    public NotificationsPanel(TimerModel model, Action? close = null)
    {
        this.model = model; this.close = close; Name = "NotificationsPanel"; Width = 400; Spacing = 8;
        Avalonia.Automation.AutomationProperties.SetName(this, "Notifications");
        Rebuild();
        AttachedToVisualTree += (_, _) => model.PropertyChanged += OnModel;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
    }
    private string Signature() => string.Join(",", model.Reminders.Select(r => r.Note.Id + ":" + r.State));
    private void OnModel(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName != nameof(TimerModel.Elapsed) && Signature() != shown) Rebuild(); }

    public void Rebuild()
    {
        Children.Clear(); shown = Signature();
        var list = model.Reminders;
        var head = Ui.Grid("*,Auto");
        Ui.Put(head, new TextBlock { Text = "Notifications", FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var count = Ui.Text(list.Count.ToString(), 12, "muted"); count.Name = "NotificationsCount"; count.VerticalAlignment = VerticalAlignment.Center; Ui.Put(head, count, 1);
        Children.Add(head);
        if (list.Count == 0) { Children.Add(Ui.Text("Nothing needs you right now.", 13, "muted")); return; }
        Section("Needs attention", list.Where(r => r.State != ReminderState.Soon).ToList());
        Section("Coming up", list.Where(r => r.State == ReminderState.Soon).ToList());
    }

    private void Section(string name, List<Reminder> items)
    {
        if (items.Count == 0) return;
        var heading = Ui.Text(name + " · " + items.Count, 12, "muted"); heading.FontWeight = FontWeight.SemiBold; heading.Margin = new Thickness(0, 6, 0, 0); Children.Add(heading);
        foreach (var reminder in items) Children.Add(Row(reminder));
    }

    private Control Row(Reminder reminder)
    {
        var id = reminder.Note.Id; var followup = reminder.State == ReminderState.Followup; var late = reminder.State == ReminderState.Overdue;
        var kind = followup ? IconKind.Note : DayNotes.IsDeadline(reminder.Note) ? IconKind.Flag : IconKind.Clock;
        var icon = new IconGlyph(kind) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TintKey = late ? "Overtime" : null };
        var title = new TextBlock { Text = NoteShorthand.Display(reminder.Note, model.AccountNames), FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { Text = model.ReminderLine(reminder), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }; sub.Classes.Add(late ? "overtime" : "muted");
        var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        words.Children.Add(title); words.Children.Add(sub);
        words.PointerPressed += (_, e) => { model.RequestNotes(); close?.Invoke(); e.Handled = true; };
        Button Small(Button button, bool main = false) { button.Classes.Add("toolButton"); button.Classes.Add("quiet"); button.Height = 28; button.Padding = new Thickness(10, 0); button.FontSize = 12; if (main) button.Classes.Add("mainAction"); return button; }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        if (followup)
        {
            actions.Children.Add(Small(Ui.Button("No", () => model.DoneReminder(id), "NotificationNo_" + id)));
            actions.Children.Add(Small(Ui.Button("Yes", () => { close?.Invoke(); model.LogMeeting(id); }, "NotificationYes_" + id), true));
        }
        else
        {
            actions.Children.Add(Small(ReminderUi.SnoozeButton(model, () => id, "NotificationSnooze_" + id)));
            actions.Children.Add(Small(Ui.Button("Done", () => model.DoneReminder(id), "NotificationDone_" + id), true));
        }
        var row = Ui.Grid("Auto,*,Auto", "Auto"); row.Name = "NotificationItem_" + id; row.Margin = new Thickness(0, 2);
        Ui.Put(row, icon); Ui.Put(row, words, 1); Ui.Put(row, actions, 2);
        return row;
    }
}
