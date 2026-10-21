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
public sealed class NotificationsPanel : Grid
{
    private readonly StackPanel rows = new() { Spacing = 8 };
    private readonly ScrollViewer scroll;
    private readonly TimerModel model; private readonly Action? close; private string shown = "";
    public NotificationsPanel(TimerModel model, Action? close = null)
    {
        this.model = model; this.close = close; Name = "NotificationsPanel"; Width = 400; MaxHeight = 480;
        RowDefinitions = new RowDefinitions("Auto,*"); RowSpacing = 8;
        Avalonia.Automation.AutomationProperties.SetName(this, "Notifications");
        scroll = new ScrollViewer { Name = "NotificationsScroll", Content = rows, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Rebuild();
        AttachedToVisualTree += (_, _) => model.PropertyChanged += OnModel;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
    }
    private string Signature() => string.Join(",", model.Reminders.Select(r => r.Note.Id + ":" + r.State + ":" + r.Note.Text + ":" + r.Note.Day + ":" + r.Note.At + ":" + r.Note.End + ":" + model.ReminderLine(r)));
    private void OnModel(object? sender, PropertyChangedEventArgs e) { if (Signature() != shown) Rebuild(); }

    public void Rebuild()
    {
        Children.Clear(); rows.Children.Clear(); shown = Signature();
        var list = model.Reminders;
        var head = Ui.Grid("*,Auto");
        Ui.Put(head, new TextBlock { Text = "Notifications", FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var count = Ui.Text(list.Count.ToString(), 12, "muted"); count.Name = "NotificationsCount"; count.VerticalAlignment = VerticalAlignment.Center; Ui.Put(head, count, 1);
        Children.Add(head);
        Ui.Put(this, scroll, row: 1);
        if (list.Count == 0) { rows.Children.Add(Ui.Text("Nothing needs you right now.", 13, "muted")); return; }
        Section("Needs attention", list.Where(r => r.State != ReminderState.Soon).ToList());
        Section("Coming up", list.Where(r => r.State == ReminderState.Soon).ToList());
    }

    private void Section(string name, List<Reminder> items)
    {
        if (items.Count == 0) return;
        var heading = Ui.Text(name + " · " + items.Count, 12, "muted"); heading.FontWeight = FontWeight.SemiBold; heading.Margin = new Thickness(0, 6, 0, 0); rows.Children.Add(heading);
        foreach (var reminder in items) rows.Children.Add(Row(reminder));
    }

    private Control Row(Reminder reminder)
    {
        var id = reminder.Note.Id; var followup = reminder.State == ReminderState.Followup; var late = reminder.State == ReminderState.Overdue;
        var kind = followup ? IconKind.Note : DayNotes.IsDeadline(reminder.Note) ? IconKind.Flag : IconKind.Clock;
        var icon = new IconGlyph(kind) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TintKey = late ? "Overtime" : null };
        var title = new TextBlock { Text = NoteShorthand.Display(reminder.Note, model.AccountNames), FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        var sub = new TextBlock { Text = model.ReminderLine(reminder) + (late ? " · " + reminder.DueAt.ToString("ddd HH:mm") : ""), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }; sub.Classes.Add(late ? "overtime" : "muted");
        var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        ToolTip.SetTip(title, title.Text); ToolTip.SetTip(sub, sub.Text);
        words.Children.Add(title); words.Children.Add(sub);
        if (NoteShorthand.AccountOf(reminder.Note.Text, model.AccountNames) is { } account)
        {
            var accountText = Ui.Text(account, 12, "muted"); accountText.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(accountText, account); words.Children.Add(accountText);
        }
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
            var meeting = TimerModel.KindOf(reminder.Note) == NoteKind.Meeting;
            var done = Ui.Button(meeting ? "Dismiss" : "Done", () => { if (meeting) model.DismissMeetingReminder(id); else model.DoneReminder(id); }, "NotificationDone_" + id);
            Avalonia.Automation.AutomationProperties.SetName(done, (meeting ? "Dismiss meeting reminder: " : "Mark done: ") + title.Text);
            actions.Children.Add(Small(done, true));
        }
        var row = Ui.Grid("Auto,*", "Auto,Auto"); row.Name = "NotificationItem_" + id; row.Margin = new Thickness(0, 2);
        Ui.Put(row, icon); Ui.Put(row, words, 1); actions.Margin = new Thickness(0, 4, 0, 0); Ui.Put(row, actions, 1, row: 1);
        return row;
    }
}
