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
    private readonly Popup whenHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "PageWhenHost" };
    private NoteView view = NoteView.All; private string? accountFilter; private string query = "";
    private string? shown;
    // The form.
    private bool meeting; private long? editing; private string draft = "", lengthText = "00:30"; private string? account; private DateTime? when; private RepeatChoice repeat;
    private string? error; private (long Id, bool Delete)? scope; private bool archived;

    public NotesPage(TimerModel model)
    {
        this.model = model; Name = "NotesPage";
        ColumnDefinitions = new ColumnDefinitions("196,14,*"); ClipToBounds = false;
        var aside = new Border { Name = "NotesViews", CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(12) }; aside.Classes.Add("dayCard");
        aside.Child = new ScrollViewer { Content = views, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        // Laid out at its own width and clipped, so it can fold away to the left, with the gap beside it, while the account panel is docked (see SetDock).
        asideHost = new Reserve(aside, AsideWidth) { Name = "NotesViewsHost" };
        Children.Add(asideHost);
        var card = new Border { Name = "NotesListCard", CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), ClipToBounds = true }; card.Classes.Add("dayCard");
        var inner = Ui.Grid("*", "Auto,Auto,*");
        // The same views as chips, in a row that opens above the form while the views column is folded away.
        chipsHost = new Border { Name = "NotesChips", Height = 0, ClipToBounds = true, IsVisible = false, Opacity = 0, Child = new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        chips.Orientation = Orientation.Horizontal; chips.Spacing = 6; chips.Margin = new Thickness(14, 0, 14, 0); chips.VerticalAlignment = VerticalAlignment.Center;
        Ui.Put(inner, chipsHost);
        var formBox = new Border { Child = formHost, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 14, 14, 12) }; formBox.Classes.Add("ruleBelow");
        Ui.Put(inner, formBox, 0, 1); Ui.Put(inner, new ScrollViewer { Content = listColumn, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Name = "NotesPageScroll" }, 0, 2);
        inner.Children.Add(whenHost); card.Child = inner; SetColumn(card, 2); Children.Add(card);
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        ResetForm(); Sync();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    /// <summary>The words in the header's search box. The list and the counts follow them.</summary>
    public string Query { get => query; set { query = value ?? ""; shown = null; Sync(); } }
    public NoteView View => view;

    // ---- Making room for the account panel ----

    private const double AsideWidth = 196, AsideGap = 14, ChipsHeight = 50;
    private readonly Reserve asideHost; private readonly Border chipsHost; private readonly StackPanel chips = new() { Name = "NotesChipRow" };
    private double room;
    /// <summary>
    /// Follows the room the docked account panel has taken from the page (0 to 1): the views and accounts column folds away to the left over the first half, and a row of the
    /// same views as chips (with an Accounts filter) opens above the form from .3 to .8, pushing the list down, so nothing it offers is lost.
    /// </summary>
    public void SetDock(double r)
    {
        room = r;
        var fold = DockMotion.Window(r, 0, .5); var open = DockMotion.Window(r, .3, .8);
        ColumnDefinitions[0].Width = new GridLength(AsideWidth * (1 - fold)); ColumnDefinitions[1].Width = new GridLength(AsideGap * (1 - fold));
        asideHost.Opacity = 1 - fold; asideHost.IsVisible = fold < 1; asideHost.IsHitTestVisible = fold < .5;
        chipsHost.Height = ChipsHeight * open; chipsHost.Opacity = open; chipsHost.IsVisible = open > 0; chipsHost.IsHitTestVisible = open > .5;
    }

    // ---- The views, the accounts and the list ----

    private string Signature() => string.Join("|", Notes.All.Select(n => $"{n.Id};{n.Day:yyyyMMdd};{n.At};{n.End};{n.Text};{n.Done};{n.SeriesId}"))
        + $"#{view}#{accountFilter}#{query}#{editing}#{model.Engine.Now:yyyyMMddHHmm}#{model.ReviewAccounts.Count}";

    public void Sync()
    {
        var signature = Signature(); if (signature == shown) return; shown = signature;
        var matches = Notes.Matching(query, accountFilter);
        views.Children.Clear();
        foreach (var kind in Enum.GetValues<NoteView>())
        {
            var count = matches.Count(n => Notes.InView(n, kind));
            views.Children.Add(Item(kind == NoteView.All ? "All" : kind.ToString(), count, view == kind, () => { view = kind; scope = null; shown = null; Sync(); DrawForm(); }, "NotesView_" + kind));
        }
        // The chips: each view with its count, then the account filter.
        chips.Children.Clear();
        foreach (var kind in Enum.GetValues<NoteView>())
        {
            var count = matches.Count(n => Notes.InView(n, kind)); var name = kind == NoteView.All ? "All" : kind.ToString();
            var chip = Ui.Ctl("NotesChip_" + kind, $"{name}  {count}", null, name, () => { view = kind; scope = null; shown = null; Sync(); DrawForm(); }, small: true);
            chip.Classes.Set("on", view == kind); chips.Children.Add(chip);
        }
        chips.Children.Add(AccountsChip());
        views.Children.Add(Ui.Rule()); ((Control)views.Children[^1]).Margin = new Thickness(4, 8);
        var heading = new TextBlock { Text = "ACCOUNTS", FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5, Margin = new Thickness(10, 0, 0, 4) }; heading.Classes.Add("muted"); views.Children.Add(heading);
        foreach (var a in model.Accounts.Where(x => !BreakAccount.Is(x.Name)))
        {
            var count = Notes.Listed().Count(n => string.Equals(Notes.AccountOf(n), a.Name, StringComparison.OrdinalIgnoreCase));
            var chosen = string.Equals(accountFilter, a.Name, StringComparison.OrdinalIgnoreCase);
            views.Children.Add(Item(a.Display, count, chosen, () => { accountFilter = chosen ? null : a.Name; shown = null; Sync(); }, "NotesAccount_" + a.Name.Replace(" ", ""), NoteParts.Dot(model, a.Name)));
        }
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

    // The Accounts chip opens a short menu of the accounts to filter by, with the chosen one marked, and one to clear the filter.
    private Button AccountsChip()
    {
        var chosen = model.Accounts.FirstOrDefault(a => string.Equals(a.Name, accountFilter, StringComparison.OrdinalIgnoreCase));
        var chip = Ui.Ctl("NotesChipAccounts", chosen is null ? "Accounts" : chosen.Display, IconKind.Accounts, "Filter by account", () => { }, small: true);
        chip.Classes.Set("on", chosen is not null);
        var items = new List<Control>();
        MenuItem Pick(string words, string? name)
        {
            var current = string.Equals(accountFilter, name, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem { Header = (current ? "✓  " : "     ") + words, Name = "NotesChipAccount_" + (name ?? "All").Replace(" ", "") };
            item.Click += (_, _) => { accountFilter = name; shown = null; Sync(); }; return item;
        }
        items.Add(Pick("All accounts", null));
        foreach (var a in model.Accounts.Where(x => !BreakAccount.Is(x.Name))) items.Add(Pick(a.Display, a.Name));
        var menu = new ContextMenu { ItemsSource = items };
        chip.ContextMenu = menu; chip.Click += (_, _) => menu.Open(chip);
        return chip;
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
        var isMeeting = NotesModel.IsMeeting(note); var words = Notes.Text(note); var acct = Notes.AccountOf(note);
        var line = Ui.Grid("Auto,*,Auto"); line.ColumnSpacing = 10; line.VerticalAlignment = VerticalAlignment.Center;
        Control lead = isMeeting ? new IconGlyph(IconKind.Today) { TintKey = "Accent", VerticalAlignment = VerticalAlignment.Center } : NoteParts.Tick(Notes, note);
        Ui.Put(line, lead);
        var text = new TextBlock { Text = words, FontSize = 14, TextWrapping = TextWrapping.Wrap, FontWeight = isMeeting ? FontWeight.SemiBold : FontWeight.Normal, Name = "PageNoteText_" + note.Id };
        if (note.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Classes.Add("muted"); }
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(Control chip) { chip.Margin = new Thickness(0, 0, 6, 0); chips.Children.Add(chip); }
        if (acct is not null) Add(NoteParts.Chip("#" + acct.Replace(" ", ""), "plain", null, NoteParts.Dot(model, acct)));
        if (isMeeting) Add(NoteParts.Chip(NoteDates.Day(note.Day, model.Engine.Now) + ", " + NoteParts.Span(note), "plain"));
        else if (NotesModel.ReminderOf(note) is { } due) Add(NoteParts.Chip(NoteDates.When(due, model.Engine.Now), Notes.IsLate(note) ? "late" : "accent", IconKind.Bell));
        if (isMeeting && Notes.SeriesOf(note) is { } series) Add(NoteParts.Chip(Notes.RepeatOf(note) == RepeatChoice.None ? series.Rule.Words() : NotesModel.Label(Notes.RepeatOf(note)), "quiet", IconKind.Repeat));
        var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(text); if (chips.Children.Count > 0) stack.Children.Add(chips);
        var main = Ui.Button("", () => Edit(note), "PageNoteOpen_" + note.Id); main.Classes.Add("pillBare"); main.Content = stack; main.Padding = new Thickness(0); main.HorizontalContentAlignment = HorizontalAlignment.Left; main.HorizontalAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(main, (isMeeting ? "Edit meeting: " : "Edit note: ") + words);
        Ui.Put(line, main, 1);
        var remove = Ui.IconButton(IconKind.Close, isMeeting ? "Remove meeting" : "Remove note", "PageNoteRemove_" + note.Id, () => AskRemove(note)); remove.Width = 28; remove.Height = 28; remove.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, remove, 2);
        var card = new Border { Child = line, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Name = "PageRow_" + note.Id }; card.Classes.Add("noteRow");
        if (editing == note.Id) card.Classes.Add("rowSelected"); // The row being changed has the accent edge.
        return card;
    }

    // ---- The form ----

    private void ResetForm()
    {
        meeting = false; editing = null; draft = ""; lengthText = "00:30"; account = null; when = null; repeat = RepeatChoice.None; error = null; scope = null; archived = false; whenHost.IsOpen = false;
        DrawForm();
    }

    private void Edit(DayNote note)
    {
        meeting = NotesModel.IsMeeting(note); editing = note.Id; draft = Notes.Text(note); account = Notes.AccountOf(note); error = null; scope = null; whenHost.IsOpen = false;
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
        field.Classes.Add("accentField");
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) draft = field.Text ?? ""; };
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        Ui.Put(top, field);
        // A word starting with # offers the accounts that match it, as in the Notes card: choosing one sets the account and takes the word out of the note.
        var suggestions = new StackPanel { Name = "PageSuggestions", IsVisible = false }; suggestions.Classes.Add("suggestions");
        void Choose(string name, string rest) { account = name; draft = rest; DrawForm(); Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (formHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "PageNoteField") is { } again) { again.Focus(); again.CaretIndex = again.Text?.Length ?? 0; } }); }
        (string Account, string Words)? first = null;
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) first = NoteParts.ShowSuggestions(model, field.Text ?? "", suggestions, "PageSuggest_", Choose); };
        field.AddHandler(KeyDownEvent, (_, e) => { if (first is { } offer && suggestions.IsVisible && e.Key is Key.Tab or Key.Enter) { Choose(offer.Account, offer.Words); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) => Ui.Horizontal(name == "No account" ? NoteParts.Dot(model, null) : NoteParts.Dot(model, name), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center })) };
        accounts.SelectedItem = account is null ? "No account" : names.FirstOrDefault(n => n.Equals(account, StringComparison.OrdinalIgnoreCase)) ?? "No account";
        accounts.SelectionChanged += (_, _) => account = accounts.SelectedItem is string chosen && chosen != "No account" ? chosen : null;
        Add(accounts);
        var whenText = when is { } w ? (meeting ? NoteDates.When(w, model.Engine.Now) : NoteDates.When(w, model.Engine.Now)) : meeting ? "Pick a time" : "Reminder";
        var whenButton = Ui.Button("", OpenWhen, "PageWhen"); whenButton.Classes.Add("ctl"); whenButton.Height = 36; whenButton.Padding = new Thickness(12, 0); whenButton.IsEnabled = !archived;
        whenButton.Content = Ui.Horizontal(new IconGlyph(meeting ? IconKind.Today : IconKind.Bell), new TextBlock { Text = whenText, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center }); Add(whenButton);
        if (meeting)
        {
            var length = new TextBox { Name = "PageLength", Text = lengthText, Width = 58, Height = 36, MinHeight = 36, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !archived };
            length.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) lengthText = length.Text ?? ""; };
            Avalonia.Automation.AutomationProperties.SetName(length, "Length, H:MM");
            var lengthLabel = Ui.Horizontal(NoteParts.Ink("Length", "Muted", 12.5), length); lengthLabel.Spacing = 6; Add(lengthLabel);
            var repeats = new ComboBox { Name = "PageRepeat", ItemsSource = Enum.GetValues<RepeatChoice>().Select(NotesModel.Label).ToList(), Width = 150, Height = 36, MinHeight = 36, FontSize = 13.5, IsEnabled = !archived };
            repeats.SelectedItem = NotesModel.Label(repeat);
            repeats.SelectionChanged += (_, _) => { if (repeats.SelectedItem is string label) repeat = Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label); };
            Add(repeats);
        }
        column.Children.Add(options);

        if (editing is { } id)
        {
            var row = Ui.Grid("*,Auto,Auto"); row.ColumnSpacing = 8;
            if (archived) { var note = new TextBlock { Text = "A repeating note from an earlier version. It stays as it is.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Name = "PageArchivedNote" }; note.Classes.Add("muted"); Ui.Put(row, note); }
            Ui.Put(row, Ui.Ctl("PageDelete", "Delete", IconKind.Trash, "Delete", () => { if (Notes.Find(id) is { } found) AskRemove(found); }, small: true), 1);
            Ui.Put(row, Ui.Ctl("PageCancel", "Cancel", null, "Cancel", () => { ResetForm(); shown = null; Sync(); }, small: true), 2);
            column.Children.Add(row);
        }
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
        whenHost.Child = new ReminderPopup(() => model.Engine.Now, string.IsNullOrWhiteSpace(draft) ? meeting ? "New meeting" : "New note" : draft, when, answered =>
        {
            when = answered; whenHost.IsOpen = false; whenHost.Child = null; DrawForm();
        }, () => { whenHost.IsOpen = false; whenHost.Child = null; }, meeting ? "When" : "Reminder");
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
