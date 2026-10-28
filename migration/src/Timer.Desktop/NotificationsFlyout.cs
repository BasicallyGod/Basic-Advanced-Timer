using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// What the bell opens (brief items 5 and 65): the reminders that need attention (their time has passed and they are not ticked off), with Done and Reschedule,
/// and the ones coming up, with Reschedule; "Nothing is waiting" when there are none. Reschedule opens the reminder pop-up beside the row.
/// </summary>
public sealed class NotificationsFlyout : Border
{
    private readonly TimerModel model;
    private readonly StackPanel column = new() { Spacing = 10 };
    private readonly Popup rescheduling = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "RescheduleHost" };
    private string? shown;

    public NotificationsFlyout(TimerModel model)
    {
        this.model = model; Name = "NotificationsFlyout";
        Width = 372; Padding = new Thickness(14); CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1);
        Classes.Add("surface"); Classes.Add("flyoutCard"); BoxShadow = HangingCard.Shadow;
        var host = new Grid(); host.Children.Add(column); host.Children.Add(rescheduling); Child = host;
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(true); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        Sync(true);
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync(false);

    private string Signature() => string.Join("|", model.Notes.Overdue.Concat(model.Notes.ComingUp()).Select(n => $"{n.Id};{n.Day:yyyyMMdd};{n.At};{n.Text};{n.Done}"));

    /// <summary>Redraws when what is waiting has changed (or when asked to, as the flyout opens).</summary>
    public void Sync(bool force)
    {
        var signature = Signature();
        if (!force && signature == shown) return;
        shown = signature; column.Children.Clear();
        var head = Ui.Grid("*,Auto");
        Ui.Put(head, new TextBlock { Text = "Notifications", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        column.Children.Add(head);
        var overdue = model.Notes.Overdue; var soon = model.Notes.ComingUp();
        if (overdue.Count > 0)
        {
            column.Children.Add(Label("Needs attention", "Overtime"));
            foreach (var note in overdue) column.Children.Add(Row(note, late: true));
        }
        if (soon.Count > 0)
        {
            column.Children.Add(Label("Coming up", "Muted"));
            foreach (var note in soon) column.Children.Add(Row(note, late: false));
        }
        if (overdue.Count == 0 && soon.Count == 0)
        {
            var none = new TextBlock { Text = "Nothing is waiting.", FontSize = 13, Name = "NothingWaiting", Margin = new Thickness(2, 8) }; none.Classes.Add("muted"); column.Children.Add(none);
        }
    }

    private static TextBlock Label(string text, string token)
    {
        var label = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(token)); return label;
    }

    private Control Row(DayNote note, bool late)
    {
        var notes = model.Notes; var account = notes.AccountOf(note); var due = NotesModel.ReminderOf(note)!.Value; var today = model.Engine.Now;
        var grid = Ui.Grid("Auto,*,Auto"); grid.ColumnSpacing = 10;
        var dot = NoteParts.Dot(model, account, 12); dot.VerticalAlignment = VerticalAlignment.Top; dot.Margin = new Thickness(0, 3, 0, 0); Ui.Put(grid, dot);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = notes.Text(note), FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var when = late ? (due.Date == today.Date ? "Overdue since " + due.ToString("HH:mm") : "Overdue since " + NoteDates.When(due, today)) : NoteDates.When(due, today);
        text.Children.Add(late ? NoteParts.Ink(when, "Overtime", 12) : NoteParts.Ink(when, "Muted", 12));
        Ui.Put(grid, text, 1);
        // Done and Reschedule are small icons at the right of the row, so the words keep the room.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Button? reschedule = null;
        if (late) { var done = Ui.Ctl("NotificationDone_" + note.Id, null, IconKind.Check, "Done", () => notes.Toggle(note.Id), small: true); done.Classes.Add("primary"); buttons.Children.Add(done); }
        reschedule = Ui.Ctl("NotificationReschedule_" + note.Id, null, IconKind.Clock, "Reschedule", () => Reschedule(note.Id, reschedule!), small: true); buttons.Children.Add(reschedule);
        Ui.Put(grid, buttons, 2);
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(14), Padding = new Thickness(10), BorderThickness = new Thickness(1), Name = "Notification_" + note.Id };
        card.Classes.Add(late ? "lateRow" : "noteRow");
        return card;
    }

    private void Reschedule(long id, Button anchor)
    {
        if (model.Notes.Find(id) is not { } note) return;
        rescheduling.PlacementTarget = anchor;
        rescheduling.Child = ReminderPopup.Roomy(new ReminderPopup(() => model.Engine.Now, model.Notes.Text(note), NotesModel.ReminderOf(note), answered =>
        {
            model.Notes.SetReminder(id, answered); rescheduling.IsOpen = false; rescheduling.Child = null;
        }, () => { rescheduling.IsOpen = false; rescheduling.Child = null; }), rescheduling);
        rescheduling.IsOpen = true;
    }
}
