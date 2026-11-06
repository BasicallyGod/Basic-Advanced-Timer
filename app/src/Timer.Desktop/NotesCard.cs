using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The Notes card beside the Details card: the selected day's meetings, its notes with a reminder, and the notes with no date, as a short list under one quick-add line
/// (the words, a bell for a reminder and Add for a note; the name, a start and an end and Add for a meeting; it follows the Notes and Meetings switch). Nothing else lives
/// here: pressing a note or a meeting selects it and Details edits it, the same place a block is edited. A meeting is planned time that never counts as tracked. The reminder
/// pop-up and the question about a repeating meeting open inside the card. The list is redrawn when what it shows changes; the line is rebuilt only when its kind changes
/// or something is added, so what is being typed is kept.
/// </summary>
public sealed class NotesCard : Border
{
    private enum Form { None, Note, Meeting }
    private readonly TimerModel model;
    private NotesModel Notes => model.Notes;
    // The card's title is a switch between the day's notes and its meetings, rebuilt when the choice changes.
    private readonly ContentControl title = new() { Name = "NotesTitle", VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl formHost = new() { Name = "NotesForm" };
    private readonly StackPanel list = new() { Spacing = 8, Name = "NotesList" };
    private readonly Grid overlay = new();
    private readonly ScrollViewer scroll;
    private string? shown;
    private readonly ViewActivity activity;
    // The form's state.
    private Form form = Form.None;
    private string draft = "", startText = "12:00", endText = "12:30"; private string? draftAccount; private DateTime? draftReminder; private RepeatChoice draftRepeat;
    private DateTime formDay;
    private (long Id, bool Delete)? scope; private HashSet<long> tinted = new();

    public NotesCard(TimerModel model)
    {
        this.model = model; Name = "NotesCard";
        CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1); Padding = new Thickness(16, 14);
        Classes.Add("dayCard");
        DrawTitle();
        var header = Ui.Grid("*"); title.HorizontalAlignment = HorizontalAlignment.Left; Ui.Put(header, title);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Name = "NotesScroll" };
        var body = Ui.Grid("*", "Auto,Auto,*"); body.RowSpacing = 10;
        Ui.Put(body, header); Ui.Put(body, formHost, 0, 1); Ui.Put(body, scroll, 0, 2);
        this.scroll = scroll; Ui.LeaveRoomForTrack(scroll, list); // The slim bar lies over the content, so the rows give it room while the list scrolls.
        overlay.Children.Add(body); overlay.Children.Add(reminderHost); reminderHost.PlacementTarget = title; Child = overlay;
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Notes.NewMeetingRequested += OnNewMeeting; Notes.EditNoteRequested += OnEditNote; Notes.NoteIdChanged += NoteIdChanged; };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; Notes.NewMeetingRequested -= OnNewMeeting; Notes.EditNoteRequested -= OnEditNote; Notes.NoteIdChanged -= NoteIdChanged; };
        activity = new ViewActivity(this, active => { if (active) Sync(); });
        Sync();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (activity.Active) Sync(); }
    private void NoteIdChanged(long previous, long current) { if (scope is { } pending && pending.Id == previous) scope = (current, pending.Delete); }

    // ---- The list ----

    private string Signature() => string.Join("|", Notes.ForDay(model.SelectedDate).Select(n => $"{n.Id};{n.Day:yyyyMMdd};{n.At};{n.End};{n.Text};{n.Done};{n.SeriesId};{n.HasDate}"))
        + $"#{model.NotesCardShowsMeetings}#{model.SelectedMeetingId}#{model.SelectedNoteId}#{model.SelectedDate:yyyyMMdd}#{(Notes.All.Any(n => Notes.IsLate(n)) ? model.Engine.Now.ToString("HHmm") : "")}#{model.ReviewAccounts.Count}";

    /// <summary>Redraws the list if what it shows has changed.</summary>
    public void Sync()
    {
        var want = model.NotesCardShowsMeetings ? Form.Meeting : Form.Note;
        if (form != want) { form = want; ResetDraft(); DrawForm(false); } // The line follows the switch: a note's, or a meeting's.
        var signature = Signature();
        if (signature == shown) return;
        shown = signature;
        DrawTitle();
        var meetings = model.NotesCardShowsMeetings;
        var items = Notes.ForDay(model.SelectedDate).Where(n => NotesModel.IsMeeting(n) == meetings).ToList();
        // The meetings in the stack of the one shown in Details are tinted, so the list says which ones the timeline's tile stands for.
        tinted = model.SelectedMeetingId is { } picked && Notes.Find(picked) is { } chosen ? (ReviewViews.MeetingStackOf(model, chosen) ?? new[] { chosen }).Select(m => m.Id).ToHashSet() : new HashSet<long>();
        list.Children.Clear();
        if (items.Count == 0)
        {
            var empty = new TextBlock { Text = meetings ? "No meetings this day. Add one above." : "No notes for this day. Write one above.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Name = "NotesEmpty" }; empty.Classes.Add("muted"); list.Children.Add(empty);
        }
        foreach (var note in items) list.Children.Add(Row(note));
        // A note selected from its pin on the timeline may be below the fold of a short list, so the list scrolls to it.
        if (model.SelectedNoteId is not null && list.Children.OfType<Border>().FirstOrDefault(r => r.Classes.Contains("rowSelected")) is { } row) Avalonia.Threading.Dispatcher.UIThread.Post(() => row.BringIntoView());
    }

    /// <summary>The card's title: Notes and Meetings as one switch, the same as the Notes page's, with the shown one filled.</summary>
    private void DrawTitle()
    {
        var meetings = model.NotesCardShowsMeetings;
        var notesChoice = NotesPage.Seg("CardKindNotes", "Notes", !meetings, () => model.SetNotesCardShowsMeetings(false));
        var meetingsChoice = NotesPage.Seg("CardKindMeetings", "Meetings", meetings, () => model.SetNotesCardShowsMeetings(true));
        // A little under the page's switch, to sit level with the small buttons beside it.
        notesChoice.Height = meetingsChoice.Height = 26; notesChoice.FontSize = meetingsChoice.FontSize = 13;
        var track = NotesPage.Switch(notesChoice, meetingsChoice); track.Height = 32;
        ((StackPanel)track.Child!).Spacing = 0; notesChoice.Margin = new Thickness(0, 0, 2, 0);
        title.Content = track;
    }

    // A row is one line: the tick (a calendar mark for a meeting), the words cut with an ellipsis, a few small chips and, only while the pointer or the keyboard is on it, a star and a bin.
    // Pressing the words selects the note or meeting, and Details edits it.
    private Control Row(DayNote note)
    {
        var meeting = NotesModel.IsMeeting(note); var words = Notes.Words(note); var account = Notes.AccountOf(note);
        var line = Ui.Grid("Auto,*,Auto,Auto"); line.ColumnSpacing = 10; line.VerticalAlignment = VerticalAlignment.Center;
        Control lead = meeting ? new IconGlyph(IconKind.Today) { TintKey = "Accent", VerticalAlignment = VerticalAlignment.Center } : NoteParts.Tick(Notes, note);
        Ui.Put(line, lead);
        var text = new TextBlock { FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontWeight = meeting ? FontWeight.SemiBold : FontWeight.Normal, Name = (meeting ? "MeetingText_" : "NoteText_") + note.Id, VerticalAlignment = VerticalAlignment.Center };
        NoteLinks.Fill(text, meeting ? words : Notes.Shown(note));
        if (note.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Classes.Add("muted"); }
        ToolTip.SetTip(text, Notes.Text(note));
        var edit = Ui.Button("", () => { if (meeting) model.SelectMeeting(note.Id); else model.SelectNote(note.Id); }, "NoteEdit_" + note.Id); edit.Classes.Add("pillBare"); edit.Padding = new Thickness(0); edit.Content = text;
        edit.HorizontalAlignment = HorizontalAlignment.Stretch; edit.HorizontalContentAlignment = HorizontalAlignment.Left;
        Avalonia.Automation.AutomationProperties.SetName(edit, (meeting ? "Open meeting in Details: " : "Open note in Details: ") + Notes.Text(note));
        Ui.Put(line, edit, 1);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (account is not null) { var dot = NoteParts.Dot(model, account, 9); ToolTip.SetTip(dot, "#" + account.Replace(" ", "")); chips.Children.Add(dot); }
        if (meeting)
        {
            chips.Children.Add(NoteParts.Chip(NoteParts.Span(note), "plain"));
            if (Notes.SeriesOf(note) is not null) { var repeat = new IconGlyph(IconKind.Repeat) { TintKey = "Muted", VerticalAlignment = VerticalAlignment.Center }; ToolTip.SetTip(repeat, "Repeats"); chips.Children.Add(repeat); }
        }
        else if (NotesModel.ReminderOf(note) is { } due) chips.Children.Add(NoteParts.Chip(Short(due), Notes.IsLate(note) ? "late" : "accent", IconKind.Bell));
        Ui.Put(line, chips, 2);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var important = !meeting && NotesModel.IsImportant(note);
        ToggleButton? star = null;
        if (!meeting) { star = NeumorphicControls.Important(important, () => Notes.SetImportant(note.Id, !important), "NoteImportant_" + note.Id); actions.Children.Add(star); }
        var remove = Ui.IconButton(IconKind.Trash, meeting ? "Remove meeting" : "Remove note", "NoteRemove_" + note.Id, () => AskRemove(note)); remove.Width = 30; remove.Height = 30; actions.Children.Add(remove);
        Ui.Put(line, actions, 3);
        var card = new Border { Child = line, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6), MinHeight = 42 }; card.Classes.Add("noteRow");
        // Padding and chips select the row too; the tick, star, delete key and links keep their own actions.
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed || e.Source is not Visual source
                || source.GetSelfAndVisualAncestors().TakeWhile(v => v != card).Any(v => v is Button)) return;
            if (meeting) model.SelectMeeting(note.Id); else model.SelectNote(note.Id);
            e.Handled = true;
        };
        if ((meeting && tinted.Contains(note.Id)) || (!meeting && model.SelectedNoteId == note.Id)) card.Classes.Add("rowSelected"); if (important) card.Classes.Add("importantRow");
        void Show(bool on) { remove.Opacity = on ? 1 : 0; remove.IsHitTestVisible = on; if (star is not null) { var visible = on || important; star.Opacity = visible ? 1 : 0; star.IsHitTestVisible = visible; } }
        Show(false);
        card.PointerEntered += (_, _) => Show(true); card.PointerExited += (_, _) => Show(card.IsKeyboardFocusWithin);
        card.GotFocus += (_, _) => Show(true); card.LostFocus += (_, _) => Show(card.IsPointerOver);
        return card;
    }
    // A reminder in few characters: the time today, the day name and date after.
    private string Short(DateTime due) => due.Date == model.Engine.Now.Date ? due.ToString("HH:mm") : due.ToString("ddd d", CultureInfo.GetCultureInfo("en-GB"));

    // ---- Removing, with the question for a repeating meeting ----

    private void AskRemove(DayNote note)
    {
        if (Notes.SeriesOf(note) is not null && NotesModel.IsMeeting(note)) { scope = (note.Id, true); DrawOverlay(); return; }
        model.RemoveNote(note.Id);
    }

    // ---- The quick-add line ----

    private void ResetDraft() { draft = ""; draftAccount = null; draftReminder = null; startText = "12:00"; endText = "12:30"; formDay = model.SelectedDate.Date; draftRepeat = RepeatChoice.None; scope = null; CloseReminder(); DrawOverlay(); }

    /// <summary>A free place in the timeline's meeting row asks for a new meeting there: the Meetings switch, a start snapped to 15 minutes and half an hour long.</summary>
    private void OnNewMeeting(DateTime start)
    {
        model.SetNotesCardShowsMeetings(true); form = Form.Meeting; ResetDraft();
        var snapped = new TimeSpan(Math.Clamp((int)Math.Round(start.TimeOfDay.TotalMinutes / 15) * 15, 0, 23 * 60 + 45) * TimeSpan.TicksPerMinute);
        var end = snapped + TimeSpan.FromMinutes(30); if (end >= TimeSpan.FromDays(1)) end = new TimeSpan(23, 59, 0);
        startText = snapped.ToString(@"hh\:mm"); endText = end.ToString(@"hh\:mm"); DrawForm(true);
    }
    /// <summary>A pin on the timeline (or a press on the words) selects the note; Details edits it.</summary>
    private void OnEditNote(long id) => model.SelectNote(id);

    private TextBox field = null!;
    private void DrawForm(bool focus)
    {
        var meeting = form == Form.Meeting;
        var column = new StackPanel { Spacing = 8 };
        // The words, with the account chip and the reminder chip inside the same box. A lone account chip shares the line with the words. The reminder chip is wide, so with
        // one the words keep their own line and the chips sit quietly under them, in one row, the way the note will look in the list once it is added.
        var stacked = !meeting && draftReminder is not null;
        var box = Ui.Grid(stacked ? "*" : "Auto,*,Auto", stacked ? "Auto,Auto" : "Auto"); box.ColumnSpacing = 6;
        var frame = new Border { Child = box, MinHeight = 36, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = stacked ? new Thickness(10, 2, 6, 0) : new Thickness(10, 0, 6, 0) }; frame.Classes.Add("composer");
        var chips = stacked ? new WrapPanel { Orientation = Orientation.Horizontal, LineSpacing = 4, Margin = new Thickness(0, 0, 0, 7) } : null;
        void PutChip(Control chip, int column) { if (chips is not null) { chip.Margin = new Thickness(0, 0, 6, 0); chips.Children.Add(chip); } else Ui.Put(box, chip, column); }
        // The account chip takes the account off again when pressed (its cross says so), or with Backspace in an empty box.
        if (draftAccount is { } account)
        {
            var chip = Ui.Button("", () => { draftAccount = null; DrawForm(true); }, "FormAccountChip"); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0); chip.VerticalAlignment = VerticalAlignment.Center;
            chip.Content = NoteParts.Chip("#" + account.Replace(" ", ""), "plain", null, NoteParts.Dot(model, account), IconKind.Close); ToolTip.SetTip(chip, "Remove account");
            Avalonia.Automation.AutomationProperties.SetName(chip, "Remove account " + account);
            PutChip(chip, 0);
        }
        field = new TextBox { Name = "NoteField", Text = draft, Watermark = meeting ? "Meeting name, # for an account" : "Add a note, # for an account", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        field.Classes.Add("bareField"); NoteLinks.AttachPaste(field);
        if (stacked) Ui.Put(box, field, 0, 0); else Ui.Put(box, field, 1);
        if (!meeting && draftReminder is { } reminder)
        {
            var chip = Ui.Button("", () => OpenReminder(), "FormReminder"); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0);
            chip.Content = NoteParts.Chip(NoteDates.When(reminder, model.Engine.Now), model.Engine.Now > reminder ? "late" : "accent", IconKind.Bell); ToolTip.SetTip(chip, "Change reminder"); PutChip(chip, 2);
        }
        if (chips is not null) Ui.Put(box, chips, 0, 1);
        var row = Ui.Grid(meeting ? "*,Auto" : "*,Auto,Auto"); row.ColumnSpacing = 6;
        Ui.Put(row, frame);
        if (!meeting)
        {
            var bell = Ui.Ctl("FormBell", null, IconKind.Bell, "Set reminder", () => OpenReminder(), small: true); Ui.Put(row, bell, 1);
            bell.IsVisible = draftReminder is null; // Once a reminder is set its chip changes it, so the bell steps aside and the box gets the room.
        }
        var submit = Ui.Ctl("SubmitNote", "Add", null, "Add", Submit, small: true); submit.Classes.Add("primary"); Ui.Put(row, submit, meeting ? 1 : 2);
        // Beside a box that has grown to two lines the buttons stay level with its first line.
        if (stacked) foreach (var side in row.Children.Skip(1)) { side.VerticalAlignment = VerticalAlignment.Top; side.Margin = new Thickness(0, 3, 0, 0); }
        column.Children.Add(row);
        var suggestions = new StackPanel { Name = "AccountSuggestions", IsVisible = false }; suggestions.Classes.Add("suggestions");
        column.Children.Add(suggestions);
        field.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            draft = field.Text ?? ""; suggestNav.Index = 0; firstSuggestion = ShowSuggestions(suggestions);
        };
        // Tab (or Enter) takes the lit match (the top one until the arrow keys move the light), the same as on the Notes page, so #Acc then Tab fills the account in and
        // #Acc, Down, Enter takes the second.
        field.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (suggestions.IsVisible && e.Key is Key.Down or Key.Up && suggestNav.Move(e.Key == Key.Down ? 1 : -1)) { firstSuggestion = ShowSuggestions(suggestions); e.Handled = true; }
            else if (firstSuggestion is { } offer && suggestions.IsVisible && e.Key is Key.Tab or Key.Enter) { draftAccount = offer.Account; draft = offer.Words; DrawForm(true); e.Handled = true; }
            else if (e.Key == Key.Back && draftAccount is not null && string.IsNullOrEmpty(field.Text)) { draftAccount = null; DrawForm(true); e.Handled = true; } // Backspace in an empty box takes the chip off.
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        if (meeting)
        {
            // A start and an end beside each other, and the day (the one showing unless another is picked). A repeat is set in Details once the meeting is there.
            var times = Ui.Grid("Auto,Auto,Auto,*"); times.ColumnSpacing = 8;
            var start = TimeBox("MeetingStart", startText, v => startText = v); var end = TimeBox("MeetingEnd", endText, v => endText = v);
            // Moving the start moves the end with it, so the meeting keeps its length (12:00 to 12:30 becomes 16:00 to 16:30). It is applied when the box is
            // left or Enter is pressed, not on every key, because a half-typed time such as 1 or 16: would drag the end about.
            var committedStart = startText;
            void MoveEndWithStart()
            {
                var now = startText;
                if (now == committedStart) return;
                if (TimeText.TryParseClock(committedStart, formDay, false, out var before) && TimeText.TryParseClock(now, formDay, false, out var after) && TimeText.TryParseClock(endText, formDay, true, out var until))
                {
                    var moved = until + (after - before);
                    // A meeting stays inside its day: carried past midnight, the end stops at 23:59.
                    if (moved >= formDay.Date.AddDays(1)) moved = formDay.Date.AddDays(1).AddMinutes(-1);
                    if (moved < formDay.Date) moved = formDay.Date;
                    endText = moved.ToString("HH:mm"); end.Text = endText;
                }
                committedStart = now;
            }
            start.LostFocus += (_, _) => MoveEndWithStart();
            start.KeyDown += (_, e) => { if (e.Key == Key.Enter) MoveEndWithStart(); };
            Ui.Put(times, start); Ui.Put(times, new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, 1); Ui.Put(times, end, 2);
            // The day is a button that opens the small calendar of a reminder in its day-only form, so a meeting can go on any day from here.
            var dayWords = new TextBlock { Text = NoteDates.Day(formDay, model.Engine.Now), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Name = "MeetingDayWords" };
            Button dayButton = null!;
            dayButton = Ui.Ctl("MeetingDay", null, null, "Meeting day", () => OpenMeetingDay(dayButton), small: true);
            dayButton.Width = double.NaN; dayButton.Padding = new Thickness(10, 0); dayButton.HorizontalAlignment = HorizontalAlignment.Left; dayButton.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), dayWords);
            Ui.Put(times, dayButton, 3);
            column.Children.Add(times);
        }
        formHost.Content = column;
        if (focus) Avalonia.Threading.Dispatcher.UIThread.Post(() => field.Focus());
    }

    private TextBox TimeBox(string name, string text, Action<string> set)
    {
        var box = new TextBox { Name = name, Text = text, Width = 64, Height = 30, MinHeight = 30, TextAlignment = TextAlignment.Center, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        Avalonia.Automation.AutomationProperties.SetName(box, name == "MeetingStart" ? "Start time, HH:MM" : "End time, HH:MM");
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) set(box.Text ?? ""); };
        return box;
    }

    // A word that starts with # offers the accounts that match it. Choosing one puts its chip in the box and takes the word out.
    private (string Account, string Words)? firstSuggestion; private readonly NoteParts.SuggestionState suggestNav = new();
    private (string Account, string Words)? ShowSuggestions(StackPanel host)
        => NoteParts.ShowSuggestions(model, draft, host, "Suggest_", (name, rest) => { draftAccount = name; draft = rest; DrawForm(true); }, suggestNav);

    private void Submit()
    {
        var words = (draft ?? "").Trim(); var meeting = form == Form.Meeting;
        if (words.Length == 0) { model.Refuse(meeting ? "Name the meeting" : "Write a note first"); return; }
        if (!meeting)
        {
            var error = Notes.AddNote(words, draftAccount, draftReminder);
            if (error is not null) { model.Refuse(error); return; }
            var said = draftReminder is { } when ? "Note added · reminder " + NoteDates.When(when, model.Engine.Now) : "Note added";
            ResetDraft(); DrawForm(true); model.Tell(said); return;
        }
        if (!TimeText.TryParseClock(startText, formDay, false, out var start)) { model.Refuse("Start: try 09:30"); return; }
        if (!TimeText.TryParseClock(endText, formDay, false, out var end)) { model.Refuse("End: try 10:30"); return; }
        var problem = Notes.AddMeeting(formDay, start.TimeOfDay, end.TimeOfDay, words, draftAccount, draftRepeat);
        if (problem is not null) { model.Refuse(problem); return; }
        ResetDraft(); DrawForm(true); model.Tell("Meeting added");
    }

    // ---- The pop-ups over the card: the reminder and the question about a repeating meeting ----

    private readonly Avalonia.Controls.Primitives.Popup reminderHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "ReminderHost" };
    // The reminder for the note being typed, in the card every reminder is set in, hung under the card's title so it can reach past the short card's edge.
    private void OpenReminder()
    {
        scope = null;
        var label = string.IsNullOrWhiteSpace(draft) ? "New note" : draft;
        var content = new ReminderPopup(() => model.Engine.Now, label, draftReminder, answered => { draftReminder = answered; CloseReminder(); DrawForm(true); }, CloseReminder);
        reminderHost.Child = ReminderPopup.Roomy(content, reminderHost); reminderHost.IsOpen = true;
    }
    // The meeting's day, on the same small calendar in its day-only form, hung under the day button.
    private void OpenMeetingDay(Control anchor)
    {
        scope = null; reminderHost.PlacementTarget = anchor;
        var label = string.IsNullOrWhiteSpace(draft) ? "New meeting" : draft;
        var content = new ReminderPopup(() => model.Engine.Now, label, formDay, answered => { if (answered is { } picked) formDay = picked.Date; CloseReminder(); DrawForm(true); }, CloseReminder, "Meeting day", dayOnly: true);
        reminderHost.Child = ReminderPopup.Roomy(content, reminderHost); reminderHost.IsOpen = true;
    }
    private void CloseReminder() { reminderHost.IsOpen = false; reminderHost.Child = null; reminderHost.PlacementTarget = title; }

    private void DrawOverlay()
    {
        while (overlay.Children.Count > 2) overlay.Children.RemoveAt(2);
        if (scope is { } asked) overlay.Children.Add(ScopeQuestion(asked));
    }

    private Control ScopeQuestion((long Id, bool Delete) asked)
    {
        var column = new StackPanel { Spacing = 8 };
        column.Children.Add(new TextBlock { Text = "Remove this repeating meeting?", FontSize = 13, FontWeight = FontWeight.SemiBold });
        var line = new TextBlock { Text = "It repeats. Choose which meetings this applies to.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap }; line.Classes.Add("muted"); column.Children.Add(line);
        Button Choice(string name, string text, bool later)
        {
            var button = Ui.Ctl(name, text, null, text, () =>
            {
                scope = null; DrawOverlay();
                Notes.Remove(asked.Id, later); if (model.SelectedMeetingId == asked.Id) model.ClearSelection(); model.Tell(later ? "Removed from this day on" : "Removed for this day");
            }, small: true);
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.HorizontalAlignment = HorizontalAlignment.Stretch; return button;
        }
        column.Children.Add(Choice("ScopeOne", "This one only", false)); column.Children.Add(Choice("ScopeAll", "This and all later", true));
        var cancel = Ui.Ctl("ScopeCancel", "Cancel", null, "Cancel", () => { scope = null; DrawOverlay(); }, small: true); cancel.HorizontalAlignment = HorizontalAlignment.Right; column.Children.Add(cancel);
        var card = new Border { Child = column, Width = 280, Padding = new Thickness(12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 60, 0, 0), Name = "ScopeQuestion" };
        card.Classes.Add("surface"); card.Classes.Add("flyoutCard"); card.BoxShadow = BoxShadows.Parse("0 10 28 0 #29141E46");
        return card;
    }
}
