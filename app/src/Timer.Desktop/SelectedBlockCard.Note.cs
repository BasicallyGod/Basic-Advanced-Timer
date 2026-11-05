using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

// The Details card for a plain note: its words, account, reminder, whether it is important and whether it is done, with Delete in the header, where it is for a block and a meeting.
// Pressing a note in the Notes card (or its pin on the timeline) selects it and brings it here, so one rule covers the page: select something, edit it in Details. The words are
// applied when Enter is pressed in them or they lose the focus, as a block's boxes are; the rest apply as soon as they are chosen. The card is kept in place across edits, so a box
// being typed in is never rebuilt under the cursor.
public sealed partial class SelectedBlockCard
{
    private sealed class NoteFields
    {
        public long Id; public bool Settling;
        public TextBox Words = null!; public ComboBox Account = null!; public TextBlock ReminderWords = null!; public ContentControl StarHost = null!, DoneHost = null!;
    }
    private NoteFields? noteFields;

    private void SyncNote(DayNote note)
    {
        var notes = model.Notes;
        var signature = $"N#{note.Id}#{notes.Words(note)}#{notes.AccountOf(note)}#{NotesModel.ReminderOf(note):s}#{note.Done}#{model.ReviewAccounts.Count}#{model.EditError}#";
        if (signature == shown) return;
        if (noteFields is { } fields && fields.Id == note.Id) { shown = signature; RefreshNote(note, model.EditError is not null); return; }
        shown = signature; current = null; stackHost = null;
        Child = NoteCard(note);
    }

    private Control NoteCard(DayNote note)
    {
        var notes = model.Notes;
        var f = new NoteFields { Id = note.Id }; noteFields = f;
        var (header, actions) = Header("Note");
        actions.Children.Add(Ui.Ctl("DeleteNote", "Delete", IconKind.Trash, "Delete", () => { model.RemoveNote(f.Id); }, small: true, key: "Del"));
        var body = new StackPanel { Spacing = 12 };
        f.Words = Box("DetailsNoteWords", notes.Words(note), true); f.Words.Height = double.NaN; f.Words.MinHeight = 64; f.Words.TextWrapping = TextWrapping.Wrap; f.Words.VerticalContentAlignment = VerticalAlignment.Top;
        var accounts = new List<string> { NoAccount }; accounts.AddRange(model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var chosen = notes.AccountOf(note); if (chosen is not null && !accounts.Contains(chosen, StringComparer.OrdinalIgnoreCase)) accounts.Add(chosen);
        f.Account = new ComboBox { Name = "DetailsNoteAccount", ItemsSource = accounts, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 38,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) => name == NoAccount ? new TextBlock { Text = NoAccount, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }
                : Ui.Horizontal(Swatch(name ?? ""), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center })) };
        f.Account.SelectedItem = accounts.FirstOrDefault(a => chosen is not null && a.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? NoAccount;

        // The reminder is a button that shows the time (or says there is none) and opens the same card a reminder is set in everywhere, hung under it.
        f.ReminderWords = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
        var popup = new Popup { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "DetailsNoteReminderPopup" };
        Button? reminderButton = null;
        reminderButton = Ui.Ctl("DetailsNoteReminder", null, null, "Choose a reminder", () =>
        {
            if (notes.Find(f.Id) is not { } now) return;
            var content = new ReminderPopup(() => model.Engine.Now, notes.Text(now), NotesModel.ReminderOf(now), answered => { popup.IsOpen = false; notes.SetReminder(f.Id, answered); }, () => popup.IsOpen = false);
            popup.PlacementTarget = reminderButton; popup.Child = ReminderPopup.Roomy(content, popup); popup.IsOpen = true;
        });
        reminderButton.Width = double.NaN; reminderButton.Height = 38; reminderButton.HorizontalAlignment = HorizontalAlignment.Stretch; reminderButton.HorizontalContentAlignment = HorizontalAlignment.Left; reminderButton.Padding = new Thickness(12, 0);
        reminderButton.Content = Ui.Horizontal(new IconGlyph(IconKind.Bell), f.ReminderWords);
        var reminderHost = new Grid { Children = { reminderButton, popup } };

        f.StarHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Height = 38 }; f.DoneHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Height = 38 };
        var wordsStack = new StackPanel { Spacing = 5 }; wordsStack.Children.Add(Label("Note")); wordsStack.Children.Add(f.Words); body.Children.Add(wordsStack);
        var row = Ui.Grid("1.1*,1.2*,Auto,Auto"); row.ColumnSpacing = 12; Field(row, 0, "Account", f.Account); Field(row, 1, "Reminder", reminderHost); Field(row, 2, "Important", f.StarHost); Field(row, 3, "Done", f.DoneHost);
        body.Children.Add(row);
        Committing(f.Words, () => ApplyNote(f));
        f.Account.SelectionChanged += (_, _) => ApplyNote(f);
        RefreshNote(note, true);
        return Page(header, body);
    }

    // Applies the words and the account. A refused change (an empty note) puts the saved values back.
    private void ApplyNote(NoteFields f)
    {
        if (f.Settling || model.Notes.Find(f.Id) is not { } note) return;
        var words = (f.Words.Text ?? "").Trim();
        if (words.Length == 0) { model.Refuse("Write a note first"); RefreshNote(note, true); return; }
        var account = f.Account.SelectedItem is string chosen && chosen != NoAccount ? chosen : null;
        if (words == model.Notes.Words(note).Trim() && string.Equals(account, model.Notes.AccountOf(note), StringComparison.OrdinalIgnoreCase)) return;
        var problem = model.Notes.EditNote(note.Id, words, account, NotesModel.ReminderOf(note));
        if (problem is not null) { model.Refuse(problem); RefreshNote(note, true); }
    }

    // Brings the boxes up to date with the note in place. A box that has the focus keeps what is being typed in it, unless every box is to go back to the saved values.
    private void RefreshNote(DayNote note, bool all)
    {
        if (noteFields is not { } f) return;
        f.Settling = true;
        try
        {
            if ((all || !f.Words.IsFocused) && f.Words.Text != model.Notes.Words(note)) f.Words.Text = model.Notes.Words(note);
            var chosen = model.Notes.AccountOf(note);
            if (f.Account.ItemsSource is IEnumerable<string> names) f.Account.SelectedItem = names.FirstOrDefault(a => chosen is not null && a.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? NoAccount;
            f.ReminderWords.Text = NotesModel.ReminderOf(note) is { } due ? NoteDates.When(due, model.Engine.Now) : "No reminder";
            var important = NotesModel.IsImportant(note); var id = note.Id;
            var star = NeumorphicControls.Important(important, () => model.Notes.SetImportant(id, !important), "DetailsNoteImportant", 38); f.StarHost.Content = star;
            var ring = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1.6), VerticalAlignment = VerticalAlignment.Center }; ring.Classes.Add("tick"); ring.Classes.Set("tickDone", note.Done);
            if (note.Done) ring.Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 3.9,8.6 L 7.2,11.9 L 12.9,5.1"), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Name = "DetailsNoteDoneMark", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var done = Ui.Button("", () => model.Notes.Toggle(id), "DetailsNoteDone"); done.Classes.Add("pillBare"); done.Padding = new Thickness(0); done.Content = ring;
            Avalonia.Automation.AutomationProperties.SetName(done, note.Done ? "Not done" : "Done"); ToolTip.SetTip(done, note.Done ? "Not done" : "Done"); f.DoneHost.Content = done;
        }
        finally { f.Settling = false; }
    }
}
