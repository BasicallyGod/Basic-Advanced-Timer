using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

// The Details card for a plain note: words, account and reminder, with the important star and Delete in the header.
// Pressing a note in the Notes card (or its pin on the timeline) selects it and brings it here, so one rule covers the page: select something, edit it in Details. The words are
// applied when Enter is pressed in them or they lose the focus, as a block's boxes are; the rest apply as soon as they are chosen. The card is kept in place across edits, so a box
// being typed in is never rebuilt under the cursor.
public sealed partial class SelectedBlockCard
{
    private sealed class NoteFields
    {
        public long Id; public bool Settling;
        public TextBox Words = null!; public ChoiceStrip Account = null!; public TextBlock ReminderWords = null!; public ContentControl StarHost = null!;
    }
    private NoteFields? noteFields;

    private void SyncNote(DayNote note)
    {
        var notes = model.Notes;
        var signature = $"N#{note.Id}#{notes.Words(note)}#{NotesModel.IsImportant(note)}#{notes.AccountOf(note)}#{NotesModel.ReminderOf(note):s}#{note.Done}#{model.ReviewAccounts.Count}#{model.EditError}#";
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
        f.StarHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Height = 30 };
        actions.Children.Add(f.StarHost);
        actions.Children.Add(Ui.Ctl("DeleteNote", "Delete", IconKind.Trash, "Delete", () => { model.RemoveNote(f.Id); }, small: true, key: "Del"));
        var body = new StackPanel { Spacing = 12 };
        f.Words = Box("DetailsNoteWords", notes.Words(note), true); f.Words.Height = double.NaN; f.Words.MinHeight = 64; f.Words.TextWrapping = TextWrapping.Wrap; f.Words.VerticalContentAlignment = VerticalAlignment.Top;
        var accounts = new List<string> { NoAccount }; accounts.AddRange(model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var chosen = notes.AccountOf(note); if (chosen is not null && !accounts.Contains(chosen, StringComparer.OrdinalIgnoreCase)) accounts.Add(chosen);
        f.Account = AccountStrip("DetailsNoteAccount", accounts);
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

        var wordsStack = new StackPanel { Spacing = 5 }; wordsStack.Children.Add(Label("Note")); wordsStack.Children.Add(f.Words); body.Children.Add(wordsStack);
        var row = Ui.Grid("1.1*,1.2*"); row.ColumnSpacing = 12; Field(row, 0, "Account", f.Account); Field(row, 1, "Reminder", reminderHost);
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
            var star = NeumorphicControls.Important(important, () => model.Notes.SetImportant(id, !important), "DetailsNoteImportant", 30); f.StarHost.Content = star;
        }
        finally { f.Settling = false; }
    }
}
