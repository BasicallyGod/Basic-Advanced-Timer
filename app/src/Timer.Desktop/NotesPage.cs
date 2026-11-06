using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The Notes page: every note and meeting, not just a day's. One card holds an add line at the top (a note or a meeting, the same one every time), one row of
/// views with their counts and one account filter, and under them the list in groups: Overdue, each day, no date, Done. A row is one line, and pressing its
/// words opens it into its own editor where it is, so nothing jumps to a form at the top and the list keeps its place. The header's search box filters the list
/// (<see cref="Query"/>). The page is built once when it opens and keeps its own state, redrawing the list and the filters only when what they show changes,
/// so a clock tick never replaces a row under the pointer or what is being typed.
/// </summary>
public sealed class NotesPage : Grid
{
    private readonly TimerModel model;
    private NotesModel Notes => model.Notes;
    private readonly StackPanel listColumn = new() { Spacing = 8, Margin = new Thickness(14, 4, 14, 14) };
    private readonly ContentControl formHost = new() { Name = "PageForm", ClipToBounds = false };
    private readonly Border viewsTrack = new() { Name = "NotesViews", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button accountButton = new() { Name = "NotesAccountFilter" };
    private readonly Popup whenHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "PageWhenHost" };
    private Border? filterCard;
    private readonly Popup filterHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "NotesAccountHost" };
    private NoteView view = NoteView.All; private readonly List<string> accountFilters = new(); private string query = "";
    private string? shown;
    // The form: one state, drawn in the add line at the top, or in the row that is open for editing.
    private bool meeting, important; private long? editing; private string draft = ""; private string startText = "12:00", endText = "12:30"; private string? account; private DateTime? when; private DateTime meetingDay; private RepeatChoice repeat;
    private string? error; private bool allLater; private long? removing; private bool archived;
    private (bool Meeting, bool Important, string Draft, string Start, string End, string? Account, DateTime? When, DateTime Day, RepeatChoice Repeat)? stash;

    public NotesPage(TimerModel model)
    {
        this.model = model; Name = "NotesPage"; ClipToBounds = false;
        var card = new Border { Name = "NotesListCard", CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), ClipToBounds = true }; card.Classes.Add("dayCard");
        var inner = Ui.Grid("*", "Auto,Auto,*");
        var formBox = new Border { Child = formHost, Padding = new Thickness(14, 14, 14, 8) };
        accountButton.Classes.Add("ctl"); accountButton.Classes.Add("small"); accountButton.Height = 34; accountButton.Padding = new Thickness(11, 0); accountButton.Click += (_, _) => OpenAccountFilter();
        Avalonia.Automation.AutomationProperties.SetName(accountButton, "Filter by account");
        var filterRow = Ui.Grid("*,Auto"); filterRow.Margin = new Thickness(14, 0, 14, 8); Ui.Put(filterRow, viewsTrack); Ui.Put(filterRow, accountButton, 1);
        Ui.Put(inner, formBox); Ui.Put(inner, filterRow, 0, 1);
        var pageScroll = new ScrollViewer { Content = listColumn, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Name = "NotesPageScroll" }; Ui.LeaveRoomForTrack(pageScroll, listColumn);
        Ui.Put(inner, pageScroll, 0, 2);
        inner.Children.Add(whenHost); inner.Children.Add(filterHost); card.Child = inner; Children.Add(card);
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Notes.NoteIdChanged += NoteIdChanged; };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; Notes.NoteIdChanged -= NoteIdChanged; };
        activity = new ViewActivity(this, active => { if (active) Sync(); });
        // Escape puts a row being edited back as it was.
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape && !whenHost.IsOpen && !filterHost.IsOpen && (editing is not null || removing is not null)) { EndEdit(); Redraw(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        Blank(); DrawForm(); Sync();
    }
    private readonly ViewActivity activity;
    private void NoteIdChanged(long previous, long current)
    {
        if (editing == previous) editing = current;
        if (removing == previous) removing = current;
    }
    private DateTime syncedMinute;
    private DateTime MinuteNow() { var now = model.Engine.Now; return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute)); }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!activity.Active) return;
        if (e.PropertyName == nameof(TimerModel.Elapsed) && syncedMinute == MinuteNow()) return;
        Sync();
    }
    private void Redraw() { shown = null; Sync(); DrawForm(); }

    /// <summary>The words in the header's search box. The list and the counts follow them.</summary>
    public string Query { get => query; set { query = value ?? ""; shown = null; Sync(); } }
    public NoteView View => view;

    // ---- The views, the account filter and the list ----

    private string Signature() => Notes.Revision.ToString(CultureInfo.InvariantCulture)
        + $"#{view}#{string.Join(",", accountFilters)}#{query}#{editing}#{removing}#{model.Engine.Now:yyyyMMddHHmm}#"
        + string.Join(";", model.ReviewAccounts.Select(a => $"{a.Name}/{a.Display}/{a.Colour}"));

    public void Sync()
    {
        using var timing = PerformanceTimings.Measure("notes page sync");
        if (editing is { } id && Notes.Find(id) is { } edited) editing = edited.Id;
        if (removing is { } pending && Notes.Find(pending) is { } scoped) removing = scoped.Id;
        syncedMinute = MinuteNow();
        accountFilters.RemoveAll(name => !model.ReviewAccounts.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var signature = Signature(); if (signature == shown) return; shown = signature;
        var matches = Notes.MatchingAny(query, accountFilters);
        // One row of views with their counts, the chosen one raised on a sunk track.
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var kind in Enum.GetValues<NoteView>())
        {
            var count = matches.Count(n => Notes.InView(n, kind)); var name = kind == NoteView.All ? "All" : kind.ToString();
            var label = Ui.Horizontal(Ui.Text(name, 13), NoteParts.Ink(count.ToString(CultureInfo.InvariantCulture), "Muted", 11.5)); label.Spacing = 6;
            var tab = Ui.Button("", () => { view = kind; removing = null; Redraw(); }, "NotesView_" + kind); tab.Classes.Add("viewTab"); tab.Classes.Set("selected", view == kind); tab.Content = label; tab.Height = 30; tab.Padding = new Thickness(11, 0);
            Avalonia.Automation.AutomationProperties.SetName(tab, name + ", " + count);
            tabs.Children.Add(tab);
        }
        viewsTrack.Child = tabs; viewsTrack.Classes.Add("viewTabs");
        DrawAccountButton();
        // The list.
        var inView = matches.Where(n => Notes.InView(n, view)).ToList();
        listColumn.Children.Clear();
        var groups = Notes.Groups(inView);
        foreach (var group in groups)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 8, 0, 0) };
            var label = new TextBlock { Text = group.Label.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 };
            if (group.Bad) label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Overtime")); else label.Classes.Add("muted");
            head.Children.Add(label); head.Children.Add(NoteParts.Ink(group.Notes.Count.ToString(CultureInfo.InvariantCulture), "Muted", 11.5));
            listColumn.Children.Add(head);
            foreach (var note in group.Notes) listColumn.Children.Add(Row(note));
        }
        if (groups.Count == 0) listColumn.Children.Add(EmptyPanel());
    }

    // A quiet sunk panel with one line of why and what to do, never a blank page.
    private Control EmptyPanel()
    {
        var (headline, hint) = query.Trim().Length > 0 ? ("No match", $"Nothing contains “{query.Trim()}”.") : view switch
        {
            NoteView.Overdue => ("Nothing overdue", "Reminders that pass will show here."),
            NoteView.Today => ("Nothing today", "Notes with a reminder today show here."),
            NoteView.Meetings => ("No meetings", "Switch the add line to Meeting."),
            NoteView.Done => ("Nothing done yet", "Ticked notes collect here."),
            NoteView.Important => ("Nothing important", "Press a note's star to put it here."),
            NoteView.Upcoming => ("Nothing coming up", "Notes with a later reminder show here."),
            _ => ("No notes yet", "Write one in the line above."),
        };
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26) };
        var title = new TextBlock { Text = headline, FontSize = 13.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Name = "NotesPageEmpty" };
        var sub = new TextBlock { Text = hint, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center }; sub.Classes.Add("muted");
        stack.Children.Add(title); stack.Children.Add(sub); return stack;
    }

    // ---- The account filter: one button, with a list of ticks under it ----

    private void DrawAccountButton()
    {
        var text = accountFilters.Count == 0 ? "All accounts" : accountFilters.Count == 1 ? accountFilters[0] : accountFilters.Count + " accounts";
        accountButton.Content = Ui.Horizontal(new IconGlyph(IconKind.Accounts), Ui.Text(text, 13), new Chevron());
        accountButton.Classes.Set("on", accountFilters.Count > 0);
    }

    internal void OpenAccountFilter()
    {
        filterHost.PlacementTarget = accountButton; filterHost.Placement = PlacementMode.BottomEdgeAlignedRight; filterHost.VerticalOffset = -HangingCard.Room.Top + 6; filterHost.HorizontalOffset = HangingCard.Room.Right;
        filterHost.Child = BuildAccountFilterCard(); filterHost.IsOpen = !filterHost.IsOpen;
    }

    /// <summary>The list of accounts with a tick each and the number of notes it carries; any number can be ticked. Clear drops them all.</summary>
    internal Control BuildAccountFilterCard()
    {
        var rows = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
        var listed = Notes.Listed();
        foreach (var a in model.Accounts.Where(x => !BreakAccount.Is(x.Name)))
        {
            var count = listed.Count(n => string.Equals(Notes.AccountOf(n), a.Name, StringComparison.OrdinalIgnoreCase));
            var on = accountFilters.Any(f => string.Equals(f, a.Name, StringComparison.OrdinalIgnoreCase));
            var tick = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.4), VerticalAlignment = VerticalAlignment.Center }; tick.Classes.Add("tick"); tick.Classes.Set("tickDone", on);
            if (on) tick.Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 3.2,8.2 L 6.6,11.4 L 12.2,4.6"), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
            var line = Ui.Grid("Auto,Auto,*,Auto"); line.ColumnSpacing = 9;
            Ui.Put(line, tick); Ui.Put(line, NoteParts.Dot(model, a.Name), 1);
            Ui.Put(line, new TextBlock { Text = "#" + a.Display.Replace(" ", ""), FontSize = 13, VerticalAlignment = VerticalAlignment.Center }, 2); Ui.Put(line, NoteParts.Ink(count.ToString(CultureInfo.InvariantCulture), "Muted", 12), 3);
            var row = Ui.Button("", () => { ToggleAccount(a.Name); BuildAccountFilterCard(); }, "NotesAccount_" + a.Name.Replace(" ", "")); row.Classes.Add("pillBare"); row.Content = line; row.HorizontalAlignment = HorizontalAlignment.Stretch; row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.Padding = new Thickness(8, 6);
            Avalonia.Automation.AutomationProperties.SetName(row, (on ? "Stop filtering by " : "Filter by ") + a.Display + ", " + count + (count == 1 ? " note" : " notes"));
            rows.Children.Add(row);
        }
        var foot = Ui.Grid("*,Auto"); foot.Margin = new Thickness(10, 8, 10, 6);
        Ui.Put(foot, Ui.Text(accountFilters.Count == 0 ? "Every account" : accountFilters.Count + (accountFilters.Count == 1 ? " account chosen" : " accounts chosen"), 12, "muted"));
        if (accountFilters.Count > 0) { var clear = Ui.Button("Clear", () => { accountFilters.Clear(); shown = null; Sync(); BuildAccountFilterCard(); }, "NotesAccountClear"); clear.Classes.Add("flat"); clear.FontSize = 12.5; clear.FontWeight = FontWeight.SemiBold; Ui.Put(foot, clear, 1); }
        var column = new StackPanel { Width = 250 }; column.Children.Add(rows); column.Children.Add(Ui.Rule()); column.Children.Add(foot);
        // One card for the page's life, whose contents are drawn again as ticks change: a popup keeps showing the child it was opened with.
        if (filterCard is null) { filterCard = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = HangingCard.Room, BoxShadow = HangingCard.Shadow, Name = "NotesAccountCard" }; filterCard.Classes.Add("surface"); filterCard.Classes.Add("flyoutCard"); }
        filterCard.Child = column;
        return filterCard;
    }
    private void ToggleAccount(string name)
    {
        var at = accountFilters.FindIndex(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) accountFilters.RemoveAt(at); else accountFilters.Add(name);
        shown = null; Sync();
    }

    // ---- A row ----

    private Control Row(DayNote note)
    {
        if (removing == note.Id) return RemoveQuestion(note);
        if (editing == note.Id) return Editor(note);
        var isMeeting = NotesModel.IsMeeting(note); var plain = Notes.Text(note); var acct = Notes.AccountOf(note);
        var line = Ui.Grid("Auto,*,Auto,Auto"); line.ColumnSpacing = 10; line.VerticalAlignment = VerticalAlignment.Center;
        Control lead = isMeeting ? new IconGlyph(IconKind.Today) { TintKey = "Accent", VerticalAlignment = VerticalAlignment.Center } : NoteParts.Tick(Notes, note);
        Ui.Put(line, lead);
        var text = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontWeight = isMeeting ? FontWeight.SemiBold : FontWeight.Normal, Name = "PageNoteText_" + note.Id, VerticalAlignment = VerticalAlignment.Center }; NoteLinks.Fill(text, Notes.Shown(note));
        if (note.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Classes.Add("muted"); }
        ToolTip.SetTip(text, plain);
        var main = Ui.Button("", () => Edit(note), "PageNoteOpen_" + note.Id); main.Classes.Add("pillBare"); main.Content = text; main.Padding = new Thickness(0); main.HorizontalContentAlignment = HorizontalAlignment.Left; main.HorizontalAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(main, (isMeeting ? "Edit meeting: " : "Edit note: ") + plain);
        Ui.Put(line, main, 1);
        // The chips sit at the right of the line: the account (pressing it filters to that account), the reminder or the meeting's time, and how it repeats.
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (acct is not null)
        {
            var chip = Ui.Button("", () => { accountFilters.Clear(); accountFilters.Add(acct); shown = null; Sync(); }, "PageRowAccount_" + note.Id); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0);
            chip.Content = NoteParts.Chip("#" + acct.Replace(" ", ""), "plain", null, NoteParts.Dot(model, acct)); ToolTip.SetTip(chip, "Show only " + acct); Avalonia.Automation.AutomationProperties.SetName(chip, "Show only " + acct); chips.Children.Add(chip);
        }
        if (isMeeting) chips.Children.Add(NoteParts.Chip(NoteDates.Day(note.Day, model.Engine.Now) + ", " + NoteParts.Span(note), "plain"));
        else if (NotesModel.ReminderOf(note) is { } due)
        {
            var reminder = Ui.Button("", () => { Edit(note); OpenWhen(); }, "PageNoteReminder_" + note.Id);
            reminder.Classes.Add("pillBare"); reminder.Padding = new Thickness(0); reminder.Content = NoteParts.Chip(NoteDates.When(due, model.Engine.Now), Notes.IsLate(note) ? "late" : "accent", IconKind.Bell);
            ToolTip.SetTip(reminder, "Change reminder"); Avalonia.Automation.AutomationProperties.SetName(reminder, "Change reminder for " + plain); chips.Children.Add(reminder);
        }
        if (isMeeting && Notes.SeriesOf(note) is { } series) chips.Children.Add(NoteParts.Chip(Notes.RepeatOf(note) == RepeatChoice.None ? series.Rule.Words() : NotesModel.Label(Notes.RepeatOf(note)), "quiet", IconKind.Repeat));
        Ui.Put(line, chips, 2);
        // The star and the bin appear only while the pointer or the keyboard is on the row; an important note keeps its star showing.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Name = "PageRowActions_" + note.Id };
        var important = !isMeeting && NotesModel.IsImportant(note);
        ToggleButton? star = null;
        if (!isMeeting) { star = NeumorphicControls.Important(important, () => Notes.SetImportant(note.Id, !important), "PageNoteImportant_" + note.Id); actions.Children.Add(star); }
        var remove = Ui.IconButton(IconKind.Trash, isMeeting ? "Remove meeting" : "Remove note", "PageNoteRemove_" + note.Id, () => AskRemove(note)); remove.Width = 30; remove.Height = 30; actions.Children.Add(remove);
        Ui.Put(line, actions, 3);
        var card = new Border { Child = line, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7), MinHeight = 46, Name = "PageRow_" + note.Id }; card.Classes.Add("noteRow"); if (important) card.Classes.Add("importantRow");
        void Show(bool on)
        {
            remove.Opacity = on ? 1 : 0; remove.IsHitTestVisible = on;
            if (star is not null) { var visible = on || important; star.Opacity = visible ? 1 : 0; star.IsHitTestVisible = visible; }
        }
        Show(false);
        card.PointerEntered += (_, _) => Show(true); card.PointerExited += (_, _) => Show(card.IsKeyboardFocusWithin);
        card.GotFocus += (_, _) => Show(true); card.LostFocus += (_, _) => Show(card.IsPointerOver);
        return card;
    }

    // Removing a repeating meeting asks in the row, not in a dialog.
    private Control RemoveQuestion(DayNote note)
    {
        var q = Ui.Grid("Auto,*,Auto,Auto,Auto"); q.ColumnSpacing = 10; q.VerticalAlignment = VerticalAlignment.Center;
        Ui.Put(q, new IconGlyph(IconKind.Trash) { TintKey = "Overtime", VerticalAlignment = VerticalAlignment.Center });
        Ui.Put(q, new TextBlock { Text = $"Remove “{Notes.Text(note)}”?", FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, 1);
        Ui.Put(q, ScopeSwitch(), 2);
        Ui.Put(q, Ui.Ctl("PageRemoveConfirm", "Remove", null, "Remove", () => { var later = allLater; removing = null; Notes.Remove(note.Id, later); model.Tell(later ? "Removed from this day on" : "Removed for this day"); Redraw(); }, small: true), 3);
        Ui.Put(q, Ui.Ctl("PageRemoveKeep", "Keep", null, "Keep", () => { removing = null; Redraw(); }, small: true), 4);
        var box = new Border { Child = q, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7), MinHeight = 46, Name = "PageScope" }; box.Classes.Add("noteRow"); box.Classes.Add("rowSelected");
        return box;
    }

    // This one or This and all later: for a repeating meeting, what a Save or a Remove applies to.
    private Border ScopeSwitch()
    {
        Button one = null!, all = null!;
        void ScopeShown() { one.Classes.Set("optOn", !allLater); all.Classes.Set("optOn", allLater); }
        one = Seg("PageScopeOne", "This one", !allLater, () => { allLater = false; ScopeShown(); });
        all = Seg("PageScopeAll", "This and all later", allLater, () => { allLater = true; ScopeShown(); });
        return Switch(one, all);
    }

    // ---- The form's state ----

    private void Blank()
    {
        meeting = false; important = false; editing = null; draft = ""; startText = "12:00"; endText = "12:30"; account = null; when = null; meetingDay = model.SelectedDate.Date; repeat = RepeatChoice.None; error = null; allLater = false; removing = null; archived = false; whenHost.IsOpen = false;
    }
    // A note being typed in the add line is kept while a row is open for editing, and comes back when the row is closed.
    private void EndEdit()
    {
        whenHost.IsOpen = false;
        if (stash is { } kept) { (meeting, important, draft, startText, endText, account, when, meetingDay, repeat) = kept; stash = null; editing = null; error = null; allLater = false; removing = null; archived = false; }
        else Blank();
    }

    /// <summary>Switches the add line between a note and a meeting, as the kind picker does.</summary>
    public void SetKind(bool isMeeting)
    {
        if (editing is not null || meeting == isMeeting) return;
        meeting = isMeeting; error = null; if (isMeeting) when = null; DrawForm();
    }

    /// <summary>Opens a note (or meeting) for editing, as a press on its row does. A pin on the timeline asks for this.</summary>
    public void OpenNote(long id) { if (Notes.Find(id) is { } note) Edit(note); }

    private void Edit(DayNote note)
    {
        if (editing is null) stash = (meeting, important, draft, startText, endText, account, when, meetingDay, repeat);
        whenHost.IsOpen = false; removing = null;
        meeting = NotesModel.IsMeeting(note); important = NotesModel.IsImportant(note); editing = note.Id; draft = Notes.Words(note); account = Notes.AccountOf(note); error = null; allLater = false;
        archived = meeting && Notes.IsArchivedRepeat(note);
        if (meeting) { meetingDay = note.Day.Date; when = note.Day.Date + note.At!.Value; startText = note.At.Value.ToString(@"hh\:mm"); endText = note.End!.Value.ToString(@"hh\:mm"); repeat = Notes.RepeatOf(note); }
        else { when = NotesModel.ReminderOf(note); repeat = RepeatChoice.None; }
        Redraw();
    }

    private void AskRemove(DayNote note)
    {
        if (Notes.SeriesOf(note) is not null && NotesModel.IsMeeting(note)) { removing = note.Id; allLater = false; Redraw(); return; }
        if (editing == note.Id) EndEdit();
        model.RemoveNote(note.Id); Redraw();
    }

    // ---- The add line, and the controls both it and the open row are made of ----

    private void DrawForm()
    {
        if (editing is not null)
        {
            // While a row is open the add line rests, so there is one place to type at a time.
            var rest = Ui.Grid("*"); var stub = new TextBlock { Text = "Add a note…", FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }; stub.Classes.Add("muted");
            var box = new Border { Height = 36, CornerRadius = new CornerRadius(12), Child = stub, Opacity = .6, IsHitTestVisible = false, Name = "PageAddResting" }; box.Bind(Border.BackgroundProperty, box.GetResourceObservable("Well")); box.Bind(Border.BoxShadowProperty, box.GetResourceObservable("ShadowSunk"));
            rest.Children.Add(box); formHost.Content = rest; return;
        }
        var column = new StackPanel { Spacing = 8 };
        var top = Ui.Grid("Auto,*,Auto,Auto,Auto"); top.ColumnSpacing = 8;
        Ui.Put(top, KindPicker());
        var words = Words(meeting ? "Meeting name" : "Add a note…", out var suggestions); Ui.Put(top, words, 1);
        if (!meeting)
        {
            Ui.Put(top, WhenButton(top: true), 2);
            var star = NeumorphicControls.Important(important, () => { important = !important; DrawForm(); }, "PageImportant", 36); Ui.Put(top, star, 3);
        }
        var submit = Ui.Ctl("PageSubmit", "Add", null, "Add", Submit, small: true); submit.Classes.Add("primary"); submit.Height = 36; submit.Padding = new Thickness(16, 0); Ui.Put(top, submit, 4);
        column.Children.Add(top); column.Children.Add(suggestions);
        if (!meeting) { var chips = NoteChips(); if (chips is not null) column.Children.Add(chips); }
        else column.Children.Add(MeetingFields(false));
        if (error is not null) column.Children.Add(ErrorLine());
        formHost.Content = column;
    }

    // The words of the add line or the open row, with a # word offering the accounts that match it: choosing one sets the account and takes the word out of the note.
    private Control Words(string watermark, out StackPanel suggestions)
    {
        var field = new TextBox { Name = "PageNoteField", Text = draft, Height = 36, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !archived, Watermark = watermark, HorizontalAlignment = HorizontalAlignment.Stretch };
        NoteLinks.AttachPaste(field);
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) draft = field.Text ?? ""; };
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        var host = new StackPanel { Name = "PageSuggestions", IsVisible = false }; host.Classes.Add("suggestions"); suggestions = host;
        void Choose(string name, string rest) { account = name; draft = rest; if (editing is not null) Redraw(); else DrawForm(); Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "PageNoteField") is { } again) { again.Focus(); again.CaretIndex = again.Text?.Length ?? 0; } }); }
        (string Account, string Words)? first = null; var nav = new NoteParts.SuggestionState();
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { nav.Index = 0; first = NoteParts.ShowSuggestions(model, field.Text ?? "", host, "PageSuggest_", Choose, nav); } };
        // The arrow keys move the light through the offers, and Tab or Enter take the lit one.
        field.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (host.IsVisible && e.Key is Key.Down or Key.Up && nav.Move(e.Key == Key.Down ? 1 : -1)) { first = NoteParts.ShowSuggestions(model, field.Text ?? "", host, "PageSuggest_", Choose, nav); e.Handled = true; }
            else if (first is { } offer && host.IsVisible && e.Key is Key.Tab or Key.Enter) { Choose(offer.Account, offer.Words); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return field;
    }

    // The kind picker: Note or Meeting, a small key with a chevron that opens a short menu.
    private Control KindPicker()
    {
        var button = new Button { Name = "PageKind", Height = 36, Padding = new Thickness(12, 0), Content = Ui.Horizontal(Ui.Text(meeting ? "Meeting" : "Note", 13.5), new Chevron()) };
        button.Classes.Add("ctl"); button.Classes.Add("small");
        Avalonia.Automation.AutomationProperties.SetName(button, "Kind: " + (meeting ? "Meeting" : "Note"));
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var note = new MenuItem { Header = "Note", Name = "PageKindNote" }; note.Click += (_, _) => SetKind(false);
        var meet = new MenuItem { Header = "Meeting", Name = "PageKindMeeting" }; meet.Click += (_, _) => SetKind(true);
        flyout.Items.Add(note); flyout.Items.Add(meet); button.Flyout = flyout;
        return button;
    }

    // The reminder (a bell and the time when set) for a note.
    private Control WhenButton(bool top)
    {
        var text = when is { } w ? NoteDates.When(w, model.Engine.Now) : "Reminder";
        var button = Ui.Button("", () => OpenWhen(), "PageWhen"); button.Classes.Add("ctl"); button.Height = 36; button.Padding = new Thickness(top && when is null ? 10 : 12, 0); button.IsEnabled = !archived;
        ToolTip.SetTip(button, "Choose a reminder"); Avalonia.Automation.AutomationProperties.SetName(button, "Reminder: " + text);
        button.Content = top && when is null ? new IconGlyph(IconKind.Bell) : Ui.Horizontal(new IconGlyph(IconKind.Bell), new TextBlock { Text = text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
        return button;
    }

    // What the line has taken from the words so far, as chips that put it back: the account (a cross takes it off) and the reminder.
    private Control? NoteChips()
    {
        if (account is null && when is null) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Name = "PageChips" };
        if (account is not null)
        {
            var chip = Ui.Button("", () => { account = null; if (editing is not null) Redraw(); else DrawForm(); }, "FormAccountChip"); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0);
            chip.Content = NoteParts.Chip("#" + account.Replace(" ", ""), "plain", null, NoteParts.Dot(model, account), IconKind.Close); ToolTip.SetTip(chip, "Take the account off"); row.Children.Add(chip);
        }
        if (when is { } w) row.Children.Add(NoteParts.Chip(NoteDates.When(w, model.Engine.Now), "accent", IconKind.Bell));
        return row;
    }

    // A meeting's day, start, end, repeat and account.
    private Control MeetingFields(bool withAccount)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(Control c) { c.Margin = new Thickness(0, 0, 8, 6); row.Children.Add(c); }
        var day = Ui.Button("", () => OpenDay(), "PageWhen"); day.Classes.Add("ctl"); day.Height = 36; day.Padding = new Thickness(12, 0); day.IsEnabled = !archived;
        ToolTip.SetTip(day, "Choose the meeting's day"); Avalonia.Automation.AutomationProperties.SetName(day, "Meeting day: " + NoteDates.Day(meetingDay, model.Engine.Now));
        day.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), new TextBlock { Text = NoteDates.Day(meetingDay, model.Engine.Now), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center }); Add(day);
        TextBox Time(string name, string text, string tip, Action<string> set)
        {
            var box = new TextBox { Name = name, Text = text, Width = 74, Height = 36, MinHeight = 36, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 5, IsEnabled = !archived };
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) set(box.Text ?? ""); };
            Avalonia.Automation.AutomationProperties.SetName(box, tip); ToolTip.SetTip(box, tip); return box;
        }
        var start = Time("PageStart", startText, "Start, HH:mm", t => startText = t); var end = Time("PageEnd", endText, "End, HH:mm", t => endText = t);
        // Moving the start moves the end by the same amount, when the box is left or Enter is pressed, and not on every key.
        string was = startText;
        void CommitStart()
        {
            if (TimeText.TryParseClock(start.Text, meetingDay, false, out var a) && TimeText.TryParseClock(was, meetingDay, false, out var b) && TimeText.TryParseClockAfter(end.Text, b, false, true, out var z) && a != b)
            {
                var moved = z + (a - b); end.Text = moved.ToString("HH:mm"); endText = end.Text;
            }
            was = start.Text ?? was;
        }
        start.LostFocus += (_, _) => CommitStart(); start.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitStart(); e.Handled = true; } };
        var to = Ui.Text("to", 13, "muted"); to.VerticalAlignment = VerticalAlignment.Center; Add(start); Add(new Border { Child = to, Height = 36 }); Add(end); // The margin goes on the box round the word, which the layout audit would count as squeezing it.
        var repeats = new ComboBox { Name = "PageRepeat", ItemsSource = NotesModel.Offered(repeat), Width = 150, Height = 36, MinHeight = 36, FontSize = 13.5, IsEnabled = !archived };
        repeats.SelectedItem = NotesModel.Label(repeat);
        repeats.SelectionChanged += (_, _) => { if (repeats.SelectedItem is string label) repeat = Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label); };
        Add(repeats);
        if (withAccount) Add(AccountBox());
        return row;
    }

    private ComboBox AccountBox()
    {
        var names = new List<string> { "No account" }; names.AddRange(model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var accounts = new ComboBox { Name = "PageAccount", ItemsSource = names, Width = 168, Height = 36, MinHeight = 36, IsEnabled = !archived, FontSize = 13.5,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            {
                // The words' ink sits a little above the middle of their line, so the dot is lifted a pixel to read as level with them.
                var dot = name == "No account" ? NoteParts.Dot(model, null) : NoteParts.Dot(model, name); dot.Margin = new Thickness(0, 0, 0, 2);
                return Ui.Horizontal(dot, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            }) };
        accounts.SelectedItem = account is null ? "No account" : names.FirstOrDefault(n => n.Equals(account, StringComparison.OrdinalIgnoreCase)) ?? "No account";
        accounts.SelectionChanged += (_, _) => account = accounts.SelectedItem is string chosen && chosen != "No account" ? chosen : null;
        return accounts;
    }

    private Control ErrorLine()
    {
        var mark = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = new TextBlock { Text = "!", FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, VerticalAlignment = VerticalAlignment.Center };
        mark.Classes.Add("warnIcon");
        var message = Ui.Horizontal(mark, new TextBlock { Text = error, FontSize = 13, Name = "PageError", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }); message.Spacing = 8; return message;
    }

    // ---- The open row ----

    // The row you pressed, grown into its own editor with the accent edge: the words, the account, the reminder (or a meeting's day, times and repeat), the star, and
    // Delete, Cancel and Save. Everything below it moves down a little; nothing else on the page moves.
    private Control Editor(DayNote note)
    {
        var isMeeting = meeting; var column = new StackPanel { Spacing = 10 };
        var top = Ui.Grid("Auto,*"); top.ColumnSpacing = 12;
        Control lead = isMeeting ? new IconGlyph(IconKind.Today) { TintKey = "Accent", VerticalAlignment = VerticalAlignment.Center } : NoteParts.Tick(Notes, note);
        Ui.Put(top, lead); var words = Words(isMeeting ? "Meeting name" : "Write a note", out var suggestions); Ui.Put(top, words, 1);
        column.Children.Add(top); column.Children.Add(suggestions);
        var options = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 0, 0, 0) };
        if (isMeeting) options.Children.Add(MeetingFields(true));
        else
        {
            void Add(Control c) { c.Margin = new Thickness(0, 0, 8, 0); options.Children.Add(c); }
            Add(AccountBox()); Add(WhenButton(top: false));
            Add(NeumorphicControls.Important(important, () => { important = !important; Redraw(); }, "PageImportant", 36));
        }
        column.Children.Add(options);
        var repeating = isMeeting && Notes.SeriesOf(note) is not null && !archived;
        var foot = Ui.Grid(repeating ? "Auto,Auto,*,Auto" : "*,Auto"); foot.Margin = new Thickness(32, 0, 0, 0); foot.ColumnSpacing = 10;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Ui.Ctl("PageDelete", null, IconKind.Trash, "Delete", () => { if (Notes.Find(note.Id) is { } found) AskRemove(found); }, small: true));
        buttons.Children.Add(Ui.Ctl("PageCancel", "Cancel", null, "Cancel", () => { EndEdit(); Redraw(); }, small: true));
        if (!archived) { var save = Ui.Ctl("PageSubmit", "Save", null, "Save", Submit, small: true); save.Classes.Add("primary"); save.Padding = new Thickness(16, 0); buttons.Children.Add(save); }
        if (repeating)
        {
            Ui.Put(foot, ScopeSwitch()); var hint = Ui.Text("What Save and Delete apply to", 12, "muted"); hint.VerticalAlignment = VerticalAlignment.Center; Ui.Put(foot, hint, 1); Ui.Put(foot, buttons, 3);
        }
        else Ui.Put(foot, buttons, 1);
        column.Children.Add(foot);
        if (archived) { var archivedNote = new TextBlock { Text = "A repeating note from an earlier version. It stays as it is.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32, 0, 0, 0), Name = "PageArchivedNote" }; archivedNote.Classes.Add("muted"); column.Children.Add(archivedNote); }
        if (error is not null) { var e = ErrorLine(); e.Margin = new Thickness(32, 0, 0, 0); column.Children.Add(e); }
        var card = new Border { Child = column, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 12, 12, 10), Name = "PageRow_" + note.Id }; card.Classes.Add("noteRow"); card.Classes.Add("rowSelected");
        return card;
    }

    /// <summary>One choice of a two-way switch. Put the choices in <see cref="Switch"/> so they read as one control.</summary>
    internal static Button Seg(string name, string text, bool on, Action click)
    {
        var button = Ui.Button(text, click, name); button.Classes.Add("seg"); button.Classes.Set("optOn", on); button.Height = 30; button.FontSize = 13.5;
        return button;
    }
    /// <summary>The track that holds a switch's choices, 36 tall like the boxes beside it.</summary>
    internal static Border Switch(params Control[] choices)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; foreach (var c in choices) row.Children.Add(c);
        var track = new Border { Child = row, Height = 36, VerticalAlignment = VerticalAlignment.Center }; track.Classes.Add("segTrack"); return track;
    }

    // ---- The cards the line hangs: a reminder, or a meeting's day ----

    private void OpenWhen()
    {
        var target = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PageWhen") ?? (Control)formHost;
        whenHost.PlacementTarget = target;
        whenHost.Child = ReminderPopup.Roomy(new ReminderPopup(() => model.Engine.Now, string.IsNullOrWhiteSpace(draft) ? "New note" : draft, when, answered =>
        {
            when = answered; whenHost.IsOpen = false; whenHost.Child = null; if (editing is not null) Redraw(); else DrawForm();
        }, () => { whenHost.IsOpen = false; whenHost.Child = null; }, "Reminder"), whenHost);
        whenHost.IsOpen = true;
    }

    private void OpenDay()
    {
        var target = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PageWhen") ?? (Control)formHost;
        whenHost.PlacementTarget = target;
        whenHost.Child = ReminderPopup.Roomy(new ReminderPopup(() => model.Engine.Now, string.IsNullOrWhiteSpace(draft) ? "New meeting" : draft, meetingDay, answered =>
        {
            if (answered is { } day) meetingDay = day.Date;
            whenHost.IsOpen = false; whenHost.Child = null; if (editing is not null) Redraw(); else DrawForm();
        }, () => { whenHost.IsOpen = false; whenHost.Child = null; }, "Meeting day", dayOnly: true), whenHost);
        whenHost.IsOpen = true;
    }

    // ---- Saving ----

    private void Submit()
    {
        error = null; var words = draft.Trim();
        if (words.Length == 0) { error = meeting ? "Name the meeting" : "Write a note first"; Redraw(); return; }
        if (!meeting)
        {
            var problem = editing is { } id ? Notes.EditNote(id, words, account, when, important) : Notes.AddNote(words, account, when, important);
            if (problem is not null) { error = problem; Redraw(); return; }
            var said = editing is not null ? "Note saved" : when is { } at ? "Note added · reminder " + NoteDates.When(at, model.Engine.Now) : "Note added";
            if (editing is not null) EndEdit(); else Blank();
            Redraw(); model.Tell(said); return;
        }
        if (!TimeText.TryParseClock(startText, meetingDay, false, out var start)) { error = "Start: try 09:30"; Redraw(); return; }
        if (!TimeText.TryParseClockAfter(endText, start, false, true, out var end)) { error = "End: try 10:00"; Redraw(); return; }
        if (end <= start) { error = "End must be after the start."; Redraw(); return; }

        if (editing is { } meetingId)
        {
            var inSeries = Notes.Find(meetingId) is { } before && Notes.SeriesOf(before) is not null;
            var problem = Notes.EditMeeting(meetingId, words, account, meetingDay.Date, start.TimeOfDay, end - meetingDay.Date, repeat, allLater);
            if (problem is not null) { error = problem; Redraw(); return; }
            var said = inSeries ? (allLater ? "Saved for this day and all later" : "Changed for this day only") : "Meeting saved";
            EndEdit(); Redraw(); model.Tell(said); return;
        }
        var failed = Notes.AddMeeting(meetingDay.Date, start.TimeOfDay, end - meetingDay.Date, words, account, repeat);
        if (failed is not null) { error = failed; Redraw(); return; }
        var message = repeat == RepeatChoice.None ? "Meeting added" : "Meeting added · repeats " + NotesModel.RepeatWords(repeat);
        Blank(); Redraw(); model.Tell(message);
    }
}
