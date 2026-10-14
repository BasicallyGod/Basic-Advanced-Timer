using Avalonia;
using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>A quick note for today, from the pill's menu. A time such as 14:30 in the words becomes the note's time, and a leading ! makes it a deadline.</summary>
public sealed class NoteDialog : ReviewDialog
{
    public NoteDialog(TimerModel model) : base("Add note")
    {
        Width = 420;
        var text = new TextBox { Name = "NoteText", Watermark = "A note, such as 14:30 Call back", AcceptsReturn = false };
        Root.Children.Add(text);
        Root.Children.Add(Ui.Text("Add a time to set a reminder, or start with ! for a deadline.", 12, "muted"));
        void Save() { if (!string.IsNullOrWhiteSpace(text.Text)) model.AddDayNote(model.Engine.Now.Date, text.Text); Close(); }
        text.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Save(); } };
        Root.Children.Add(Ui.Horizontal(Ui.DialogButton("Save", Save, "SaveNote"), Ui.DialogButton("Cancel", Close)));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => text.Focus());
    }
}
