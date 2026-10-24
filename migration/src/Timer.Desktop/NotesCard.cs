using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The Notes card beside the Selected block card (brief items 5, 33, 48, 49): the selected day's meetings, its notes with a reminder, and the notes with no date, with
/// Add note and Add meeting. Both open one form here. A note is words, an account (typed as #name) and an optional reminder; a meeting adds a start, an end and
/// a repeat, and is planned time that never counts as tracked. The reminder pop-up and the question about a repeating meeting open inside the card.
/// The list is redrawn when what it shows changes; the form is rebuilt only when it is opened or closed, so what is being typed is kept.
/// </summary>
public sealed class NotesCard : Border
{
    private enum Form { None, Note, Meeting }
    private readonly TimerModel model;
    private NotesModel Notes => model.Notes;
    private readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "NotesTitle" };
    private readonly Button addNote, addMeeting;
    private readonly ContentControl formHost = new() { Name = "NotesForm" };
    private readonly StackPanel list = new() { Spacing = 8, Name = "NotesList" };
    private readonly Grid overlay = new();
    private string? shown;
    // The form's state.
    private Form form = Form.None; private long? editing;
    private string draft = "", startText = "09:00", endText = "10:00"; private string? draftAccount; private DateTime? draftReminder; private RepeatChoice draftRepeat;
    private DateTime formDay;
    private (long Id, bool Delete)? scope;

    public NotesCard(TimerModel model)
    {
        this.model = model; Name = "NotesCard";
        CornerRadius = new CornerRadius(20); BorderThickness = new Thickness(1); Padding = new Thickness(16, 14);
        Classes.Add("dayCard");
        addNote = Ui.Ctl("AddNote", "Add note", IconKind.Plus, "Add a note for this day", () => Open(Form.Note), small: true);
        addMeeting = Ui.Ctl("AddMeeting", "Add meeting", IconKind.Plus, "Add a meeting. It shows on the timeline as planned time", () => Open(Form.Meeting), small: true);
        var header = Ui.Grid("*,Auto"); Ui.Put(header, title);
        var buttons = Ui.Horizontal(addNote, addMeeting); buttons.Spacing = 6; Ui.Put(header, buttons, 1);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Name = "NotesScroll" };
        var body = Ui.Grid("*", "Auto,Auto,*"); body.RowSpacing = 10;
        Ui.Put(body, header); Ui.Put(body, formHost, 0, 1); Ui.Put(body, scroll, 0, 2);
        overlay.Children.Add(body); overlay.Children.Add(reminderHost); reminderHost.PlacementTarget = title; reminderHost.Closed += (_, _) => popup = null; Child = overlay;
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Notes.NewMeetingRequested += OnNewMeeting; Notes.EditMeetingRequested += OnEditMeeting; Sync(); };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; Notes.NewMeetingRequested -= OnNewMeeting; Notes.EditMeetingRequested -= OnEditMeeting; };
        Sync();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    // ---- The list ----

    private string Signature() => string.Join("|", Notes.ForDay(model.SelectedDate).Select(n => $"{n.Id};{n.Day:yyyyMMdd};{n.At};{n.End};{n.Text};{n.Done};{n.SeriesId};{n.HasDate}"))
        + $"#{model.SelectedDate:yyyyMMdd}#{(Notes.All.Any(n => Notes.IsLate(n)) ? model.Engine.Now.ToString("HHmm") : "")}#{model.ReviewAccounts.Count}";

    /// <summary>Redraws the list if what it shows has changed.</summary>
    public void Sync()
    {
        var signature = Signature();
        if (signature == shown) return;
        shown = signature;
        var items = Notes.ForDay(model.SelectedDate);
        title.Text = $"Notes ({items.Count})";
        list.Children.Clear();
        if (items.Count == 0)
        {
            var empty = new TextBlock { Text = "Nothing for this day. Add a note or a meeting.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Name = "NotesEmpty" }; empty.Classes.Add("muted"); list.Children.Add(empty);
        }
        foreach (var note in items) list.Children.Add(Row(note));
    }

    private static TextBlock Ink(string text, string token, double size = 12) { var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center }; t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(token)); return t; }

    private Border Chip(string text, string tone, IconKind? icon = null, Control? lead = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var token = tone switch { "accent" => "Accent", "late" => "Overtime", "quiet" => "Muted", _ => "Ink" };
        if (lead is not null) row.Children.Add(lead);
        if (icon is { } kind) row.Children.Add(new IconGlyph(kind) { TintKey = token == "Ink" ? null : token });
        row.Children.Add(Ink(text, token));
        var chip = new Border { Child = row, CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        chip.Classes.Add("chip"); chip.Classes.Add(tone == "accent" ? "accentChip" : tone == "late" ? "lateChip" : "plainChip");
        return chip;
    }
    private Control Dot(string? account)
    {
        IBrush fill = new SolidColorBrush(Colors.Gray);
        if (account is not null && model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))?.Colour is { } colour)
        {
            var look = model.BlockLook(colour);
            fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(look.Fill), 0), new GradientStop(Color.Parse(ReviewViews.DeeperEnd(look)), 1) } };
        }
        return new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = fill, VerticalAlignment = VerticalAlignment.Center };
    }

    private Control Row(DayNote note)
    {
        var meeting = NotesModel.IsMeeting(note); var words = Notes.Text(note); var account = Notes.AccountOf(note);
        var line = Ui.Grid("Auto,*,Auto,Auto"); line.ColumnSpacing = 10; line.VerticalAlignment = VerticalAlignment.Top;
        // A note has a round tick; a meeting has a calendar mark instead, and is opened by its name.
        Control lead = meeting ? new IconGlyph(IconKind.Today) { TintKey = "Accent", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) } : Tick(note);
        Ui.Put(line, lead);
        var column = new StackPanel { Spacing = 5 };
        if (meeting)
        {
            var name = Ui.Button(words, () => Notes.RequestEditMeeting(note.Id), "NoteEdit_" + note.Id); name.Classes.Add("flat"); name.Padding = new Thickness(0); name.FontWeight = FontWeight.SemiBold; name.FontSize = 14; name.Bind(TemplatedControl.ForegroundProperty, name.GetResourceObservable("Ink"));
            name.HorizontalAlignment = HorizontalAlignment.Left; ToolTip.SetTip(name, "Edit this meeting"); column.Children.Add(name);
        }
        else
        {
            var text = new TextBlock { Text = words, FontSize = 14, TextWrapping = TextWrapping.Wrap, Name = "NoteText_" + note.Id };
            if (note.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Classes.Add("muted"); }
            column.Children.Add(text);
        }
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(Control chip) { chip.Margin = new Thickness(0, 0, 6, 4); chips.Children.Add(chip); }
        if (meeting) Add(Chip(note.At is { } a && note.End is { } z ? a.ToString(@"hh\:mm") + "–" + z.ToString(@"hh\:mm") : "", "plain"));
        var repeat = Notes.RepeatOf(note);
        if (meeting && Notes.SeriesOf(note) is { } series)
            Add(Chip(repeat == RepeatChoice.None ? series.Rule.Words() : NotesModel.Label(repeat), "quiet", IconKind.Repeat));
        if (account is not null) { var chip = Chip("#" + account.Replace(" ", ""), "plain", null, Dot(account)); Add(chip); }
        if (!meeting && NotesModel.ReminderOf(note) is { } due)
        {
            var late = Notes.IsLate(note);
            var chip = Ui.Button("", () => OpenReminder(note.Id), "NoteReminder_" + note.Id); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0);
            chip.Content = Chip(NoteDates.When(due, model.Engine.Now), late ? "late" : "accent", IconKind.Bell); ToolTip.SetTip(chip, "Change the date or time"); chip.Margin = new Thickness(0, 0, 6, 4); chips.Children.Add(chip);
        }
        column.Children.Add(chips);
        Ui.Put(line, column, 1);
        if (!meeting && NotesModel.ReminderOf(note) is null)
        {
            var bell = Ui.IconButton(IconKind.Bell, "Add a reminder", "NoteAddReminder_" + note.Id, () => OpenReminder(note.Id)); bell.Width = 28; bell.Height = 28; bell.VerticalAlignment = VerticalAlignment.Top; Ui.Put(line, bell, 2);
        }
        var remove = Ui.IconButton(IconKind.Close, meeting ? "Remove meeting" : "Remove note", "NoteRemove_" + note.Id, () => AskRemove(note)); remove.Width = 28; remove.Height = 28; remove.VerticalAlignment = VerticalAlignment.Top;
        Ui.Put(line, remove, 3);
        var card = new Border { Child = line, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9) }; card.Classes.Add("noteRow");
        return card;
    }

    private Button Tick(DayNote note)
    {
        var ring = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.6), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        ring.Classes.Add("tick"); ring.Classes.Set("tickDone", note.Done);
        var button = Ui.Button("", () => Notes.Toggle(note.Id), "NoteDone_" + note.Id); button.Classes.Add("pillBare"); button.Padding = new Thickness(0); button.Content = ring; button.VerticalAlignment = VerticalAlignment.Top;
        if (note.Done) ring.Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 5.5,10.5 L 8.8,13.8 L 14.5,7"), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Stroke = new SolidColorBrush(Colors.White), Name = "TickMark" };
        Avalonia.Automation.AutomationProperties.SetName(button, "Done: " + Notes.Text(note)); ToolTip.SetTip(button, note.Done ? "Not done" : "Done");
        return button;
    }

    // ---- Removing, with the question for a repeating meeting ----

    private void AskRemove(DayNote note)
    {
        if (Notes.SeriesOf(note) is not null && NotesModel.IsMeeting(note)) { scope = (note.Id, true); DrawOverlay(); return; }
        Notes.Remove(note.Id);
        if (editing == note.Id) Close();
        model.Tell(NotesModel.IsMeeting(note) ? "Meeting removed." : "Note removed.");
    }

    // ---- The form ----

    private void OnNewMeeting(DateTime start)
    {
        Open(Form.Meeting, fresh: true);
        var snapped = new TimeSpan(Math.Clamp((int)Math.Round(start.TimeOfDay.TotalMinutes / 15) * 15, 0, 23 * 60 + 45) * TimeSpan.TicksPerMinute);
        var end = snapped + TimeSpan.FromHours(1); if (end >= TimeSpan.FromDays(1)) end = new TimeSpan(23, 59, 0);
        startText = snapped.ToString(@"hh\:mm"); endText = end.ToString(@"hh\:mm"); DrawForm();
    }
    private void OnEditMeeting(long id)
    {
        if (Notes.Find(id) is not { } meeting || !NotesModel.IsMeeting(meeting)) return;
        form = Form.Meeting; editing = id; formDay = meeting.Day.Date;
        var account = Notes.AccountOf(meeting);
        draft = Notes.Text(meeting); draftAccount = account; draftReminder = null; startText = meeting.At!.Value.ToString(@"hh\:mm"); endText = meeting.End!.Value.ToString(@"hh\:mm");
        draftRepeat = Notes.RepeatOf(meeting); scope = null; DrawOverlay(); DrawForm();
    }

    private void Open(Form kind, bool fresh = false)
    {
        // Pressing the button of the form that is open closes it; any other press opens that form, empty.
        if (form == kind && editing is null && !fresh) { Close(); return; }
        form = kind; editing = null; formDay = model.SelectedDate.Date; scope = null; CloseReminder(); DrawOverlay();
        draft = ""; draftAccount = null; draftReminder = null; startText = "09:00"; endText = "10:00"; draftRepeat = RepeatChoice.None;
        DrawForm();
    }
    private void Close() { form = Form.None; editing = null; scope = null; CloseReminder(); DrawOverlay(); DrawForm(); }

    /// <summary>Whether a form is open (for tests), and which.</summary>
    public bool FormOpen => form != Form.None;

    private TextBox field = null!;
    private void DrawForm()
    {
        addNote.Classes.Set("on", form == Form.Note); addMeeting.Classes.Set("on", form == Form.Meeting);
        if (form == Form.None) { formHost.Content = null; return; }
        var meeting = form == Form.Meeting;
        var column = new StackPanel { Spacing = 8 };
        // The words, with the account chip and the reminder chip inside the same box.
        var box = Ui.Grid("Auto,*,Auto"); box.ColumnSpacing = 6;
        var frame = new Border { Child = box, Height = 36, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 6, 0) }; frame.Classes.Add("composer");
        if (draftAccount is { } account) { var chip = Chip("#" + account.Replace(" ", ""), "plain", null, Dot(account)); chip.VerticalAlignment = VerticalAlignment.Center; Ui.Put(box, chip); }
        field = new TextBox { Name = "NoteField", Text = draft, Watermark = meeting ? "Meeting name, # for an account" : "Write a note, # for an account", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        field.Classes.Add("bareField");
        Ui.Put(box, field, 1);
        if (!meeting && draftReminder is { } reminder)
        {
            var chip = Ui.Button("", () => OpenReminder(null), "FormReminder"); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0);
            chip.Content = Chip(NoteDates.When(reminder, model.Engine.Now), model.Engine.Now > reminder ? "late" : "accent", IconKind.Bell); ToolTip.SetTip(chip, "Change the reminder"); Ui.Put(box, chip, 2);
        }
        var row = Ui.Grid("*,Auto,Auto"); row.ColumnSpacing = 6;
        Ui.Put(row, frame);
        if (!meeting) { var bell = Ui.Ctl("FormBell", null, IconKind.Bell, "Set a reminder", () => OpenReminder(null), small: true); Ui.Put(row, bell, 1); }
        var submit = Ui.Ctl("SubmitNote", editing is not null ? "Save" : meeting ? "Add meeting" : "Add note", null, "Save", Submit, small: true); submit.Classes.Add("primary"); Ui.Put(row, submit, 2);
        column.Children.Add(row);
        var suggestions = new StackPanel { Name = "AccountSuggestions", IsVisible = false }; suggestions.Classes.Add("suggestions");
        column.Children.Add(suggestions);
        field.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            draft = field.Text ?? ""; ShowSuggestions(suggestions);
        };
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } else if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        if (meeting)
        {
            var times = Ui.Grid("Auto,Auto,Auto,*,Auto"); times.ColumnSpacing = 8;
            var start = TimeBox("MeetingStart", startText, v => startText = v); var end = TimeBox("MeetingEnd", endText, v => endText = v);
            Ui.Put(times, start); Ui.Put(times, new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, 1); Ui.Put(times, end, 2);
            var repeat = new ComboBox { Name = "MeetingRepeat", ItemsSource = Enum.GetValues<RepeatChoice>().Select(NotesModel.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch, Height = 30, MinHeight = 30, FontSize = 13 };
            repeat.SelectedItem = NotesModel.Label(draftRepeat);
            repeat.SelectionChanged += (_, _) => { if (repeat.SelectedItem is string label) draftRepeat = Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label); };
            Ui.Put(times, repeat, 3);
            if (editing is { } id) Ui.Put(times, Ui.Ctl("MeetingDelete", "Delete", null, "Remove this meeting", () => { if (Notes.Find(id) is { } found) AskRemove(found); }, small: true), 4);
            column.Children.Add(times);
        }
        formHost.Content = column;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => field.Focus());
    }

    private TextBox TimeBox(string name, string text, Action<string> set)
    {
        var box = new TextBox { Name = name, Text = text, Width = 64, Height = 30, MinHeight = 30, TextAlignment = TextAlignment.Center, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        Avalonia.Automation.AutomationProperties.SetName(box, name == "MeetingStart" ? "Start time, HH:MM" : "End time, HH:MM");
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) set(box.Text ?? ""); };
        return box;
    }

    // A word that starts with # offers the accounts that match it. Choosing one puts its chip in the box and takes the word out.
    private void ShowSuggestions(StackPanel host)
    {
        host.Children.Clear();
        var match = System.Text.RegularExpressions.Regex.Match(draft, @"#([\p{L}\p{N}_-]*)$");
        if (!match.Success) { host.IsVisible = false; return; }
        var typed = match.Groups[1].Value;
        var options = model.Accounts.Where(a => a.Name.Replace(" ", "").StartsWith(typed, StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        foreach (var option in options)
        {
            var pick = Ui.Button("", () => { draftAccount = option.Name; draft = draft[..match.Index].TrimEnd(); DrawForm(); }, "Suggest_" + option.Name.Replace(" ", ""));
            pick.Classes.Add("flat"); pick.HorizontalAlignment = HorizontalAlignment.Stretch; pick.HorizontalContentAlignment = HorizontalAlignment.Left;
            pick.Content = Ui.Horizontal(Dot(option.Name), new TextBlock { Text = "#" + option.Name.Replace(" ", ""), FontWeight = FontWeight.SemiBold, FontSize = 13 }, Ink(option.Name, "Muted", 13));
            host.Children.Add(pick);
        }
        host.IsVisible = options.Count > 0;
    }

    private void Submit()
    {
        var words = (draft ?? "").Trim(); var meeting = form == Form.Meeting;
        if (words.Length == 0) { model.Refuse(meeting ? "Write a name for the meeting." : "Write something for the note."); return; }
        if (!meeting)
        {
            var error = editing is { } id ? Notes.EditNote(id, words, draftAccount, draftReminder) : Notes.AddNote(words, draftAccount, draftReminder);
            if (error is not null) { model.Refuse(error); return; }
            var said = editing is not null ? "Note saved." : draftReminder is { } when ? "Note added with a reminder for " + NoteDates.When(when, model.Engine.Now) + "." : "Note added.";
            Close(); model.Tell(said); return;
        }
        if (!TimeText.TryParseClock(startText, formDay, false, out var start)) { model.Refuse("Start looks like 09:30."); return; }
        if (!TimeText.TryParseClock(endText, formDay, false, out var end)) { model.Refuse("End looks like 10:30."); return; }
        if (editing is { } meetingId)
        {
            if (Notes.SeriesOf(Notes.Find(meetingId)!) is not null && !Notes.IsArchivedRepeat(Notes.Find(meetingId)!)) { scope = (meetingId, false); DrawOverlay(); return; }
            ApplyMeeting(meetingId, false);
            return;
        }
        var problem = Notes.AddMeeting(formDay, start.TimeOfDay, end.TimeOfDay, words, draftAccount, draftRepeat);
        if (problem is not null) { model.Refuse(problem); return; }
        Close(); model.Tell(draftRepeat == RepeatChoice.None ? "Meeting added." : "Meeting added. It repeats " + NotesModel.Label(draftRepeat).ToLowerInvariant().Replace("every ", "every ") + ".");
    }

    private void ApplyMeeting(long id, bool allLater)
    {
        TimeText.TryParseClock(startText, formDay, false, out var start); TimeText.TryParseClock(endText, formDay, false, out var end);
        var problem = Notes.EditMeeting(id, draft.Trim(), draftAccount, formDay, start.TimeOfDay, end.TimeOfDay, draftRepeat, allLater);
        if (problem is not null) { model.Refuse(problem); scope = null; DrawOverlay(); return; }
        Close(); model.Tell(allLater ? "Meeting saved for this day and all later." : "Changed for this day only. The series carries on as before.");
    }

    // ---- The pop-ups over the card: the reminder and the question about a repeating meeting ----

    private long? popupTarget; private Control? popup;
    private readonly Avalonia.Controls.Primitives.Popup reminderHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "ReminderHost" };
    private void OpenReminder(long? noteId)
    {
        popupTarget = noteId; scope = null;
        DateTime? current = noteId is { } id && Notes.Find(id) is { } note ? NotesModel.ReminderOf(note) : draftReminder;
        var label = noteId is { } n && Notes.Find(n) is { } found ? Notes.Text(found) : string.IsNullOrWhiteSpace(draft) ? form == Form.Meeting ? "New meeting" : "New note" : draft;
        // A floating pop-up, so it can reach past the card's edge (the card is short), under the card's title.
        var content = new ReminderPopup(() => model.Engine.Now, label, current, answered =>
        {
            if (popupTarget is { } target) Notes.SetReminder(target, answered); else { draftReminder = answered; DrawForm(); }
            CloseReminder();
        }, CloseReminder);
        reminderHost.Child = content; reminderHost.IsOpen = true; popup = content;
    }
    private void CloseReminder() { reminderHost.IsOpen = false; reminderHost.Child = null; popup = null; }

    private void DrawOverlay()
    {
        while (overlay.Children.Count > 2) overlay.Children.RemoveAt(2);
        if (scope is { } asked) overlay.Children.Add(ScopeQuestion(asked));
    }

    private Control ScopeQuestion((long Id, bool Delete) asked)
    {
        var column = new StackPanel { Spacing = 8 };
        column.Children.Add(new TextBlock { Text = asked.Delete ? "Remove this repeating meeting?" : "Save changes to this repeating meeting?", FontSize = 13, FontWeight = FontWeight.SemiBold });
        var line = new TextBlock { Text = "It repeats. Choose which meetings this applies to.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap }; line.Classes.Add("muted"); column.Children.Add(line);
        Button Choice(string name, string text, bool later)
        {
            var button = Ui.Ctl(name, text, null, text, () =>
            {
                scope = null; DrawOverlay();
                if (asked.Delete) { Notes.Remove(asked.Id, later); if (editing == asked.Id) Close(); model.Tell(later ? "This meeting and all later ones removed." : "Meeting removed for this day. The series carries on."); }
                else ApplyMeeting(asked.Id, later);
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
