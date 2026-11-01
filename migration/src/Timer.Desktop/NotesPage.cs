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
/// The Notes page (brief items 65, 66 and 69): every note and meeting, not just a day's. Views and accounts with counts down the left; on the right one form
/// at the top (a note or a meeting, the same one to add and to change) over the list in groups: Overdue, each day, no date, Done. The header's search box
/// filters it (<see cref="Query"/>). It is built once when it opens and keeps its own state, redrawing the list and the filters only when what they show
/// changes, so a clock tick never replaces a row under the pointer or what is being typed.
/// </summary>
public sealed class NotesPage : Grid
{
    private readonly TimerModel model;
    private NotesModel Notes => model.Notes;
    private readonly StackPanel views = new() { Spacing = 4 }, listColumn = new() { Spacing = 6, Margin = new Thickness(14, 10, 14, 14) };
    private readonly ContentControl formHost = new() { Name = "PageForm" };
    private readonly WrapPanel filterRow = new() { Orientation = Orientation.Horizontal, Name = "NotesAccountPills" };
    private readonly Popup whenHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "PageWhenHost" };
    private NoteView view = NoteView.All; private readonly List<string> accountFilters = new(); private string query = "";
    private string? shown;
    // The form.
    private bool meeting; private long? editing; private string draft = "", lengthText = "00:30"; private string? account; private DateTime? when; private RepeatChoice repeat;
    private string? error; private (long Id, bool Delete)? scope; private bool archived;

    public NotesPage(TimerModel model)
    {
        this.model = model; Name = "NotesPage";
        ColumnDefinitions = new ColumnDefinitions("196,14,*"); ClipToBounds = false;
        var aside = new Border { Name = "NotesViews", CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(12) }; aside.Classes.Add("dayCard");
        var viewsScroll = new ScrollViewer { Content = views, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Ui.LeaveRoomForTrack(viewsScroll, views);
        aside.Child = viewsScroll;
        Children.Add(aside);
        var card = new Border { Name = "NotesListCard", CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), ClipToBounds = true }; card.Classes.Add("dayCard");
        var inner = Ui.Grid("*", "Auto,Auto,*");
        // The form, then the row of accounts to filter by, then the list.
        var formBox = new Border { Child = formHost, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 14, 14, 12) }; formBox.Classes.Add("ruleBelow");
        var filterBox = new Border { Child = filterRow, Padding = new Thickness(14, 10, 14, 2), Name = "NotesAccountFilter" };
        Ui.Put(inner, formBox); Ui.Put(inner, filterBox, 0, 1); var pageScroll = new ScrollViewer { Content = listColumn, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Name = "NotesPageScroll" }; Ui.LeaveRoomForTrack(pageScroll, listColumn);
        Ui.Put(inner, pageScroll, 0, 2);
        inner.Children.Add(whenHost); card.Child = inner; SetColumn(card, 2); Children.Add(card);
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        ResetForm(); Sync();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    /// <summary>The words in the header's search box. The list and the counts follow them.</summary>
    public string Query { get => query; set { query = value ?? ""; shown = null; Sync(); } }
    public NoteView View => view;

    // ---- The views, the accounts and the list ----

    private string Signature() => string.Join("|", Notes.All.Select(n => $"{n.Id};{n.Day:yyyyMMdd};{n.At};{n.End};{n.Text};{n.Done};{n.SeriesId}"))
        + $"#{view}#{string.Join(",", accountFilters)}#{query}#{editing}#{model.Engine.Now:yyyyMMddHHmm}#{model.ReviewAccounts.Count}";

    public void Sync()
    {
        var signature = Signature(); if (signature == shown) return; shown = signature;
        var matches = Notes.MatchingAny(query, accountFilters);
        views.Children.Clear();
        foreach (var kind in Enum.GetValues<NoteView>())
        {
            var count = matches.Count(n => Notes.InView(n, kind));
            views.Children.Add(Item(kind == NoteView.All ? "All" : kind.ToString(), count, view == kind, () => { view = kind; scope = null; shown = null; Sync(); DrawForm(); }, "NotesView_" + kind));
        }
        DrawAccountFilter();
        // The list.
        var inView = matches.Where(n => Notes.InView(n, view)).ToList();
        listColumn.Children.Clear();
        var groups = Notes.Groups(inView);
        foreach (var group in groups)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 8, 0, 2) };
            var label = new TextBlock { Text = group.Label.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 };
            if (group.Bad) label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Overtime")); else label.Classes.Add("muted");
            head.Children.Add(label); head.Children.Add(NoteParts.Ink(group.Notes.Count.ToString(CultureInfo.InvariantCulture), "Muted", 11.5));
            listColumn.Children.Add(head);
            foreach (var note in group.Notes) listColumn.Children.Add(Row(note));
        }
        if (groups.Count == 0)
        {
            var empty = new TextBlock { Name = "NotesPageEmpty", FontSize = 13.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(6, 24), Text = query.Trim().Length > 0 ? "Nothing matches that search." : view == NoteView.Overdue ? "Nothing is overdue." : "Nothing here yet." };
            empty.Classes.Add("muted"); listColumn.Children.Add(empty);
        }
    }

    // The accounts as a row of tag pills, #Account with the number of notes under it, between the form and the list. They are drawn as the tags a note carries, so they cannot be
    // taken for the sidebar's account rows (which start timers). Pressing one filters by it, pressing it again takes it off, several can be on at once, and Clear drops them all.
    private void DrawAccountFilter()
    {
        filterRow.Children.Clear();
        var label = new TextBlock { Text = "ACCOUNT", FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5, VerticalAlignment = VerticalAlignment.Center }; label.Classes.Add("muted");
        filterRow.Children.Add(new Border { Child = label, Margin = new Thickness(2, 0, 10, 8), Height = 30 }); // The margin is on the box, not the words, so the words are never measured short.
        var listed = Notes.Listed();
        foreach (var a in model.Accounts.Where(x => !BreakAccount.Is(x.Name)))
        {
            var count = listed.Count(n => string.Equals(Notes.AccountOf(n), a.Name, StringComparison.OrdinalIgnoreCase));
            var on = accountFilters.Any(f => string.Equals(f, a.Name, StringComparison.OrdinalIgnoreCase));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(NoteParts.Dot(model, a.Name));
            line.Children.Add(new TextBlock { Text = "#" + a.Display.Replace(" ", ""), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(NoteParts.Ink(count.ToString(CultureInfo.InvariantCulture), on ? "AccentText" : "Muted", 12));
            if (on) line.Children.Add(new IconGlyph(IconKind.Close) { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, TintKey = "AccentText" });
            var pill = Ui.Button("", () => ToggleAccount(a.Name), "NotesAccount_" + a.Name.Replace(" ", "")); pill.Classes.Add("accountPill"); pill.Classes.Set("on", on); pill.Content = line;
            pill.Margin = new Thickness(0, 0, 8, 8);
            Avalonia.Automation.AutomationProperties.SetName(pill, (on ? "Stop filtering by " : "Filter by ") + a.Display + ", " + count + (count == 1 ? " note" : " notes"));
            filterRow.Children.Add(pill);
        }
        if (accountFilters.Count > 0)
        {
            var clear = Ui.Button("Clear", () => { accountFilters.Clear(); shown = null; Sync(); }, "NotesAccountClear"); clear.Classes.Add("flat"); clear.Margin = new Thickness(2, 0, 0, 8); clear.Padding = new Thickness(6, 0);
            clear.FontSize = 12.5; clear.FontWeight = FontWeight.SemiBold; ToolTip.SetTip(clear, "Show every account");
            filterRow.Children.Add(clear);
        }
    }
    private void ToggleAccount(string name)
    {
        var at = accountFilters.FindIndex(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) accountFilters.RemoveAt(at); else accountFilters.Add(name);
        shown = null; Sync();
    }

    private static Button Item(string name, int count, bool on, Action click, string id, Control? lead = null)
    {
        var row = Ui.Grid(lead is null ? "*,Auto" : "Auto,*,Auto"); row.ColumnSpacing = 8; var at = 0;
        if (lead is not null) { Ui.Put(row, lead); at = 1; }
        Ui.Put(row, new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, at);
        Ui.Put(row, NoteParts.Ink(count.ToString(CultureInfo.InvariantCulture), "Muted", 12), at + 1);
        var button = Ui.Button("", click, id); button.Content = row; button.Classes.Add("vitem"); button.Classes.Set("vitemOn", on); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private Control Row(DayNote note)
    {
        var isMeeting = NotesModel.IsMeeting(note); var plain = Notes.Text(note); var acct = Notes.AccountOf(note);
        var line = Ui.Grid(isMeeting ? "Auto,*,Auto" : "Auto,*,Auto,Auto"); line.ColumnSpacing = 10; line.VerticalAlignment = VerticalAlignment.Center;
        Control lead = isMeeting ? new IconGlyph(IconKind.Today) { TintKey = "Accent", VerticalAlignment = VerticalAlignment.Center } : NoteParts.Tick(Notes, note);
        Ui.Put(line, lead);
        var text = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, FontWeight = isMeeting ? FontWeight.SemiBold : FontWeight.Normal, Name = "PageNoteText_" + note.Id }; NoteLinks.Fill(text, Notes.Shown(note));
        if (note.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Classes.Add("muted"); }
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, LineSpacing = 4 }; // Lines that wrap never touch.
        void Add(Control chip) { chip.Margin = new Thickness(0, 0, 6, 0); chips.Children.Add(chip); }
        if (acct is not null) Add(NoteParts.Chip("#" + acct.Replace(" ", ""), "plain", null, NoteParts.Dot(model, acct)));
        if (isMeeting) Add(NoteParts.Chip(NoteDates.Day(note.Day, model.Engine.Now) + ", " + NoteParts.Span(note), "plain"));
        else if (NotesModel.ReminderOf(note) is { } due) Add(NoteParts.Chip(NoteDates.When(due, model.Engine.Now), Notes.IsLate(note) ? "late" : "accent", IconKind.Bell));
        if (isMeeting && Notes.SeriesOf(note) is { } series) Add(NoteParts.Chip(Notes.RepeatOf(note) == RepeatChoice.None ? series.Rule.Words() : NotesModel.Label(Notes.RepeatOf(note)), "quiet", IconKind.Repeat));
        var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(text); if (chips.Children.Count > 0) stack.Children.Add(chips);
        var main = Ui.Button("", () => Edit(note), "PageNoteOpen_" + note.Id); main.Classes.Add("pillBare"); main.Content = stack; main.Padding = new Thickness(0); main.HorizontalContentAlignment = HorizontalAlignment.Left; main.HorizontalAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(main, (isMeeting ? "Edit meeting: " : "Edit note: ") + plain);
        Ui.Put(line, main, 1);
        if (!isMeeting)
        {
            var important = NotesModel.IsImportant(note);
            var star = Ui.IconButton(important ? IconKind.StarFilled : IconKind.Star, important ? "Not important" : "Mark important", "PageNoteImportant_" + note.Id, () => Notes.SetImportant(note.Id, !important)); star.Classes.Add("starKey"); star.Classes.Set("on", important); star.Width = 28; star.Height = 28; star.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, star, 2);
            if (important && star.Content is IconGlyph solid) solid.TintKey = "ImportantStar";
        }
        var remove = Ui.IconButton(IconKind.Close, isMeeting ? "Remove meeting" : "Remove note", "PageNoteRemove_" + note.Id, () => AskRemove(note)); remove.Width = 28; remove.Height = 28; remove.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, remove, isMeeting ? 2 : 3);
        var card = new Border { Child = line, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Name = "PageRow_" + note.Id }; card.Classes.Add("noteRow"); if (!isMeeting && NotesModel.IsImportant(note)) card.Classes.Add("importantRow");
        if (editing == note.Id) card.Classes.Add("rowSelected"); // The row being changed has the accent edge.
        return card;
    }

    // ---- The form ----

    private void ResetForm()
    {
        meeting = false; editing = null; draft = ""; lengthText = "00:30"; account = null; when = null; repeat = RepeatChoice.None; error = null; scope = null; archived = false; whenHost.IsOpen = false;
        DrawForm();
    }

    /// <summary>Opens a note (or meeting) for editing, as a press on its row does. A pin on the timeline asks for this.</summary>
    public void OpenNote(long id) { if (Notes.Find(id) is { } note) Edit(note); }

    private void Edit(DayNote note)
    {
        meeting = NotesModel.IsMeeting(note); editing = note.Id; draft = Notes.Words(note); account = Notes.AccountOf(note); error = null; scope = null; whenHost.IsOpen = false;
        archived = meeting && Notes.IsArchivedRepeat(note);
        if (meeting) { when = note.Day.Date + note.At!.Value; lengthText = TimeText.ShowLength(note.End!.Value - note.At.Value); repeat = Notes.RepeatOf(note); }
        else { when = NotesModel.ReminderOf(note); repeat = RepeatChoice.None; lengthText = "00:30"; }
        shown = null; Sync(); DrawForm();
    }

    private void AskRemove(DayNote note)
    {
        if (Notes.SeriesOf(note) is not null && NotesModel.IsMeeting(note)) { editing ??= null; scope = (note.Id, true); DrawForm(); return; }
        Notes.Remove(note.Id); if (editing == note.Id) ResetForm(); model.Tell(NotesModel.IsMeeting(note) ? "Meeting removed" : "Note removed");
    }

    private void DrawForm()
    {
        var column = new StackPanel { Spacing = 8 };
        var top = Ui.Grid("*,Auto"); top.ColumnSpacing = 8;
        var field = new TextBox { Name = "PageNoteField", Text = draft, Height = 36, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !archived,
            Watermark = meeting ? "Meeting name" : "Write a note", HorizontalAlignment = HorizontalAlignment.Stretch };
        field.Classes.Add("accentField"); NoteLinks.AttachPaste(field);
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) draft = field.Text ?? ""; };
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        Ui.Put(top, field);
        // A word starting with # offers the accounts that match it, as in the Notes card: choosing one sets the account and takes the word out of the note.
        var suggestions = new StackPanel { Name = "PageSuggestions", IsVisible = false }; suggestions.Classes.Add("suggestions");
        void Choose(string name, string rest) { account = name; draft = rest; DrawForm(); Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (formHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "PageNoteField") is { } again) { again.Focus(); again.CaretIndex = again.Text?.Length ?? 0; } }); }
        (string Account, string Words)? first = null; var nav = new NoteParts.SuggestionState();
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { nav.Index = 0; first = NoteParts.ShowSuggestions(model, field.Text ?? "", suggestions, "PageSuggest_", Choose, nav); } };
        // The arrow keys move the light through the offers, and Tab or Enter take the lit one.
        field.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (suggestions.IsVisible && e.Key is Key.Down or Key.Up && nav.Move(e.Key == Key.Down ? 1 : -1)) { first = NoteParts.ShowSuggestions(model, field.Text ?? "", suggestions, "PageSuggest_", Choose, nav); e.Handled = true; }
            else if (first is { } offer && suggestions.IsVisible && e.Key is Key.Tab or Key.Enter) { Choose(offer.Account, offer.Words); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (!archived) { var submit = Ui.Ctl("PageSubmit", editing is not null ? "Save" : "Add", null, editing is not null ? "Save" : "Add", Submit, small: true); submit.Classes.Add("primary"); submit.Height = 36; submit.Padding = new Thickness(16, 0); Ui.Put(top, submit, 1); }
        column.Children.Add(top); column.Children.Add(suggestions);

        var options = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(Control control) { control.Margin = new Thickness(0, 0, 6, 6); options.Children.Add(control); }
        if (editing is not null)
        {
            var label = NoteParts.Chip(meeting ? "Editing a meeting" : "Editing a note", "plain"); label.Height = 36; label.Padding = new Thickness(12, 0); Add(label);
        }
        else
        {
            Add(Switch(Seg("PageKindNote", "Note", !meeting, () => { meeting = false; when = null; error = null; DrawForm(); }), Seg("PageKindMeeting", "Meeting", meeting, () => { meeting = true; when = model.SelectedDate.Date.AddHours(12); error = null; DrawForm(); })));
        }
        var names = new List<string> { "No account" }; names.AddRange(model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var accounts = new ComboBox { Name = "PageAccount", ItemsSource = names, Width = 150, Height = 36, MinHeight = 36, IsEnabled = !archived, FontSize = 13.5,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            {
                // The words' ink sits a little above the middle of their line, so the dot is lifted a pixel to read as level with them.
                var dot = name == "No account" ? NoteParts.Dot(model, null) : NoteParts.Dot(model, name); dot.Margin = new Thickness(0, 0, 0, 2);
                return Ui.Horizontal(dot, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            }) };
        accounts.SelectedItem = account is null ? "No account" : names.FirstOrDefault(n => n.Equals(account, StringComparison.OrdinalIgnoreCase)) ?? "No account";
        accounts.SelectionChanged += (_, _) => account = accounts.SelectedItem is string chosen && chosen != "No account" ? chosen : null;
        Add(accounts);
        var whenText = when is { } w ? (meeting ? NoteDates.When(w, model.Engine.Now) : NoteDates.When(w, model.Engine.Now)) : meeting ? "Pick a time" : "Reminder";
        var whenButton = Ui.Button("", OpenWhen, "PageWhen"); whenButton.Classes.Add("ctl"); whenButton.Height = 36; whenButton.Padding = new Thickness(12, 0); whenButton.IsEnabled = !archived;
        whenButton.Content = Ui.Horizontal(new IconGlyph(meeting ? IconKind.Today : IconKind.Bell), new TextBlock { Text = whenText, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center }); Add(whenButton);
        if (!meeting)
        {
            // The star puts the important mark (!!) at the start of the words, or takes it off; typing !! does the same.
            var marked = NotesModel.IsMarked(draft);
            var star = Ui.Ctl("PageImportant", null, marked ? IconKind.StarFilled : IconKind.Star, marked ? "Not important" : "Mark important", () => { draft = NotesModel.WithMark(draft, !NotesModel.IsMarked(draft)); DrawForm(); }); star.Height = 36; star.Width = 36; star.IsEnabled = !archived; Add(star);
        }
        if (meeting)
        {
            var length = new TextBox { Name = "PageLength", Text = lengthText, Width = 58, Height = 36, MinHeight = 36, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !archived };
            length.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) lengthText = length.Text ?? ""; };
            Avalonia.Automation.AutomationProperties.SetName(length, "Length, H:MM");
            // No word beside the box: its tooltip says what it is, so the line keeps its room for the repeat list.
            ToolTip.SetTip(length, "Length, H:MM"); Add(length);
            var repeats = new ComboBox { Name = "PageRepeat", ItemsSource = NotesModel.Offered(repeat), Width = 150, Height = 36, MinHeight = 36, FontSize = 13.5, IsEnabled = !archived };
            repeats.SelectedItem = NotesModel.Label(repeat);
            repeats.SelectionChanged += (_, _) => { if (repeats.SelectedItem is string label) repeat = Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label); };
            Add(repeats);
        }
        if (editing is { } id)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
            buttons.Children.Add(Ui.Ctl("PageDelete", null, IconKind.Trash, "Delete", () => { if (Notes.Find(id) is { } found) AskRemove(found); }, small: true));
            buttons.Children.Add(Ui.Ctl("PageCancel", null, IconKind.Close, "Cancel", () => { ResetForm(); shown = null; Sync(); }, small: true));
            // Delete and Cancel are a bin and a cross, at the right end of the options' own line, so a form being edited has no line of its own for them and no dead space beside them.
            var line = Ui.Grid("*,Auto"); line.ColumnSpacing = 8; Ui.Put(line, options); Ui.Put(line, buttons, 1); column.Children.Add(line);
            if (archived) { var note = new TextBlock { Text = "A repeating note from an earlier version. It stays as it is.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Name = "PageArchivedNote" }; note.Classes.Add("muted"); column.Children.Add(note); }
        }
        else column.Children.Add(options);
        if (scope is { } asked)
        {
            var q = Ui.Grid("*,Auto,Auto,Auto"); q.ColumnSpacing = 8;
            Ui.Put(q, new TextBlock { Text = asked.Delete ? "Remove this repeating meeting?" : "Save changes to this repeating meeting?", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Ui.Put(q, Ui.Ctl("PageScopeOne", "This one only", null, "This one only", () => Answer(asked, false), small: true), 1);
            Ui.Put(q, Ui.Ctl("PageScopeAll", "This and all later", null, "This and all later", () => Answer(asked, true), small: true), 2);
            Ui.Put(q, Ui.Ctl("PageScopeCancel", "Cancel", null, "Cancel", () => { scope = null; DrawForm(); }, small: true), 3);
            var box = new Border { Child = q, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10), Name = "PageScope" }; box.Classes.Add("noteRow"); column.Children.Add(box);
        }
        if (error is not null)
        {
            var mark = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = new TextBlock { Text = "!", FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, VerticalAlignment = VerticalAlignment.Center };
            mark.Classes.Add("warnIcon");
            var message = Ui.Horizontal(mark, new TextBlock { Text = error, FontSize = 13, Name = "PageError", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }); message.Spacing = 8; column.Children.Add(message);
        }
        formHost.Content = column;
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

    private void OpenWhen()
    {
        whenHost.PlacementTarget = formHost;
        whenHost.Child = ReminderPopup.Roomy(new ReminderPopup(() => model.Engine.Now, string.IsNullOrWhiteSpace(draft) ? meeting ? "New meeting" : "New note" : draft, when, answered =>
        {
            when = answered; whenHost.IsOpen = false; whenHost.Child = null; DrawForm();
        }, () => { whenHost.IsOpen = false; whenHost.Child = null; }, meeting ? "When" : "Reminder"), whenHost);
        whenHost.IsOpen = true;
    }

    private void Submit()
    {
        error = null; var words = draft.Trim();
        if (words.Length == 0) { error = meeting ? "Name the meeting" : "Write a note first"; DrawForm(); return; }
        if (!meeting)
        {
            var problem = editing is { } id ? Notes.EditNote(id, words, account, when) : Notes.AddNote(words, account, when);
            if (problem is not null) { error = problem; DrawForm(); return; }
            var said = editing is not null ? "Note saved" : when is { } at ? "Note added · reminder " + NoteDates.When(at, model.Engine.Now) : "Note added";
            ResetForm(); shown = null; Sync(); model.Tell(said); return;
        }
        if (when is not { } start) { error = "Pick the day and time the meeting starts."; DrawForm(); return; }
        if (TimeText.ParseLength(lengthText) is not { } length) { error = "Length: try 1:30 or 90"; DrawForm(); return; }
        var end = start.TimeOfDay + length; if (end >= TimeSpan.FromDays(1)) { error = "A meeting has to fall within one day."; DrawForm(); return; }
        if (editing is { } meetingId)
        {
            if (Notes.SeriesOf(Notes.Find(meetingId)!) is not null && !Notes.IsArchivedRepeat(Notes.Find(meetingId)!)) { scope = (meetingId, false); DrawForm(); return; }
            Apply(meetingId, false); return;
        }
        var failed = Notes.AddMeeting(start.Date, start.TimeOfDay, end, words, account, repeat);
        if (failed is not null) { error = failed; DrawForm(); return; }
        var message = repeat == RepeatChoice.None ? "Meeting added" : "Meeting added · repeats " + NotesModel.RepeatWords(repeat);
        ResetForm(); shown = null; Sync(); model.Tell(message);
    }

    private void Apply(long id, bool allLater)
    {
        var start = when!.Value; var end = start.TimeOfDay + TimeText.ParseLength(lengthText)!.Value;
        var problem = Notes.EditMeeting(id, draft.Trim(), account, start.Date, start.TimeOfDay, end, repeat, allLater);
        if (problem is not null) { error = problem; scope = null; DrawForm(); return; }
        ResetForm(); shown = null; Sync(); model.Tell(allLater ? "Saved for this day and all later" : "Changed for this day only");
    }

    private void Answer((long Id, bool Delete) asked, bool later)
    {
        scope = null;
        if (asked.Delete) { Notes.Remove(asked.Id, later); if (editing == asked.Id) ResetForm(); shown = null; Sync(); DrawForm(); model.Tell(later ? "Removed from this day on" : "Removed for this day"); }
        else Apply(asked.Id, later);
    }
}
