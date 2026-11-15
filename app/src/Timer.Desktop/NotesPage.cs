using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    private abstract record ListRow;
    private sealed record HeadingRow(string Label, bool Bad, int Count) : ListRow;
    private sealed record NoteRow(DayNote Note, int DraftVersion, bool Removing, DateTime LabelDay, bool Late, string Accounts, long SeriesRevision) : ListRow;
    private sealed record EmptyRow(NoteView View, string Query) : ListRow;
    private readonly ItemsControl list = new() { Margin = new Thickness(14, 4, 14, 6) };
    private AvaloniaList<ListRow> rows = new();
    private int draftVersion;
    private readonly ContentControl formHost = new() { Name = "PageForm", ClipToBounds = false };
    private readonly Border viewsTrack = new() { Name = "NotesViews", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button accountButton = new() { Name = "NotesAccountFilter" };
    private readonly Popup whenHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "PageWhenHost" };
    private Border? filterCard;
    private readonly Popup filterHost = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "NotesAccountHost" };
    private NoteView view = NoteView.All; private readonly List<string> accountFilters = new(); private string query = "";
    private string? shown;
    // Local mirrors of the model-owned add forms. Existing rows use RecordEditor and its own lease.
    private bool meeting, important; private long? editing; private string draft = ""; private string startText = "12:00", endText = "12:30"; private string? account; private DateTime? when; private DateTime meetingDay; private RepeatChoice repeat;
    private string? error; private bool allLater; private long? removing; private bool archived;
    private bool? stashedKind;

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
        list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        list.ItemTemplate = new FuncDataTemplate<ListRow>((item, _) => BuildListRow(item));
        list.ItemsSource = rows;
        var pageScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Name = "NotesPageScroll" }; Ui.LeaveRoomForTrack(pageScroll, list);
        Ui.Put(inner, pageScroll, 0, 2);
        inner.Children.Add(whenHost); inner.Children.Add(filterHost); card.Child = inner; Children.Add(card);
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Notes.NoteIdChanged += NoteIdChanged; model.CreationDrafts.Reset += ResetComposers; if (editing is null) { RestoreComposer(); DrawForm(); } };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; Notes.NoteIdChanged -= NoteIdChanged; model.CreationDrafts.Reset -= ResetComposers; };
        activity = new ViewActivity(this, active => { if (active) Sync(); });
        // Escape puts a row being edited back as it was.
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape && !whenHost.IsOpen && !filterHost.IsOpen && (editing is not null || removing is not null)) { EndEdit(); Redraw(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        RestoreComposer(); DrawForm(); Sync();
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
    private void Redraw() { draftVersion++; shown = null; Sync(); DrawForm(); }

    /// <summary>The words in the header's search box. The list and the counts follow them.</summary>
    public string Query
    {
        get => query;
        set
        {
            var next = value ?? "";
            if (query == next) return;
            query = next; shown = null; Sync();
        }
    }
    public NoteView View => view;

    // ---- The views, the account filter and the list ----

    public void Sync()
    {
        using var timing = PerformanceTimings.Measure("notes page sync");
        if (editing is { } id && Notes.Find(id) is { } edited) editing = edited.Id;
        if (removing is { } pending && Notes.Find(pending) is { } scoped) removing = scoped.Id;
        syncedMinute = MinuteNow();
        var catalogue = model.ReviewAccounts;
        accountFilters.RemoveAll(name => !catalogue.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var accounts = string.Join(";", catalogue.Select(a => $"{a.Name}/{a.Display}/{a.Colour}"));
        var signature = $"{Notes.Revision}#{view}#{string.Join(",", accountFilters)}#{query}#{editing}#{removing}#{syncedMinute:yyyyMMddHHmm}#{accounts}";
        if (signature == shown) return; shown = signature;
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
        var inView = matches.Where(n => Notes.InView(n, view)).ToList();
        var groups = Notes.Groups(inView);
        var next = new List<ListRow>(inView.Count + groups.Count);
        foreach (var group in groups)
        {
            next.Add(new HeadingRow(group.Label, group.Bad, group.Notes.Count));
            foreach (var note in group.Notes)
                next.Add(new NoteRow(note, editing == note.Id ? draftVersion : 0, removing == note.Id,
                    note.At is null ? default : syncedMinute.Date, Notes.IsLate(note), accounts, note.SeriesId is null ? 0 : Notes.Revision));
        }
        if (groups.Count == 0) next.Add(new EmptyRow(view, query));
        UpdateRows(next);
    }

    private void UpdateRows(List<ListRow> next)
    {
        // Saving a temporary note gives it a positive ID, which changes the normal ID sort order.
        // Keep its open row anchored until editing ends so recovery cannot virtualize the focused editor.
        if (editing is { } resolved)
        {
            var index = rows.ToList().FindIndex(r => r is NoteRow { DraftVersion: > 0 } n && n.Note.Id != resolved && Notes.Find(n.Note.Id)?.Id == resolved);
            var replacement = next.FindIndex(r => r is NoteRow n && n.Note.Id == resolved);
            if (index >= 0 && replacement >= 0 && rows[index] is NoteRow old && next[replacement] is NoteRow updated && old.Removing == updated.Removing)
            { next.RemoveAt(replacement); next.Insert(Math.Min(index, next.Count), old); }
        }
        // Replacing just the edited row keeps the scroll anchor and the other realized controls.
        if (rows.Count == next.Count)
        {
            for (var i = 0; i < next.Count; i++)
                if (editing is { } id && rows[i] is NoteRow { DraftVersion: > 0 } old && next[i] is NoteRow updated && Notes.Find(old.Note.Id)?.Id == id && updated.Note.Id == id && old.Removing == updated.Removing)
                    next[i] = old; // The shared draft refreshes the realized editor without replacing its focused controls.
            var changed = new List<int>();
            for (var i = 0; i < next.Count && changed.Count <= 16; i++)
                if (rows[i] != next[i]) changed.Add(i);
            if (changed.Count <= 16)
            {
                foreach (var i in changed) rows[i] = next[i];
                return;
            }
        }
        rows = new AvaloniaList<ListRow>(next);
        list.ItemsSource = rows;
    }

    private Control BuildListRow(ListRow item)
    {
        var control = item switch
        {
            HeadingRow heading => Heading(heading),
            NoteRow note => Row(note.Note),
            _ => EmptyPanel()
        };
        var margin = control.Margin;
        control.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom + 8);
        return control;
    }

    private static Control Heading(HeadingRow group)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 8, 0, 0) };
        var label = new TextBlock { Text = group.Label.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 };
        if (group.Bad) label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Overtime")); else label.Classes.Add("muted");
        head.Children.Add(label); head.Children.Add(NoteParts.Ink(group.Count.ToString(CultureInfo.InvariantCulture), "Muted", 11.5));
        return head;
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
        var counts = Notes.AccountCounts();
        foreach (var a in model.Accounts.Where(x => !BreakAccount.Is(x.Name)))
        {
            var count = counts.GetValueOrDefault(a.Name);
            var on = accountFilters.Any(f => string.Equals(f, a.Name, StringComparison.OrdinalIgnoreCase));
            var tick = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.4), VerticalAlignment = VerticalAlignment.Center }; tick.Classes.Add("tick"); tick.Classes.Set("tickDone", on);
            if (on) tick.Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 3.2,8.2 L 6.6,11.4 L 12.2,4.6"), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
            var line = Ui.Grid("Auto,Auto,*,Auto"); line.ColumnSpacing = 9;
            Ui.Put(line, tick); Ui.Put(line, NoteParts.Dot(model, a.Name), 1);
            Ui.Put(line, new TextBlock { Text = "#" + a.Display.Replace(" ", ""), FontSize = 13, VerticalAlignment = VerticalAlignment.Center }, 2); Ui.Put(line, NoteParts.Ink(count.ToString(CultureInfo.InvariantCulture), "Muted", 12), 3);
            var row = new CompletionButton { Name = "NotesAccount_" + a.Name.Replace(" ", ""), IsChecked = on };
            row.Click += (_, _) =>
            {
                ToggleAccount(a.Name); BuildAccountFilterCard();
                Dispatcher.UIThread.Post(() => { if (filterHost.IsOpen) filterCard?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == row.Name)?.Focus(NavigationMethod.Tab); }, DispatcherPriority.Loaded);
            };
            row.Classes.Add("pillBare"); row.Content = line; row.HorizontalAlignment = HorizontalAlignment.Stretch; row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.Padding = new Thickness(8, 6);
            Avalonia.Automation.AutomationProperties.SetName(row, "Filter by " + a.Display + ", " + count + (count == 1 ? " note" : " notes"));
            rows.Children.Add(row);
        }
        var foot = Ui.Grid("*,Auto"); foot.Margin = new Thickness(10, 8, 10, 6);
        Ui.Put(foot, Ui.Text(accountFilters.Count == 0 ? "Every account" : accountFilters.Count + (accountFilters.Count == 1 ? " account chosen" : " accounts chosen"), 12, "muted"));
        if (accountFilters.Count > 0) { var clear = Ui.Button("Clear", () => { accountFilters.Clear(); shown = null; Sync(); BuildAccountFilterCard(); Dispatcher.UIThread.Post(() => { if (filterHost.IsOpen) filterCard?.GetVisualDescendants().OfType<CompletionButton>().FirstOrDefault()?.Focus(NavigationMethod.Tab); }, DispatcherPriority.Loaded); }, "NotesAccountClear"); clear.Classes.Add("flat"); clear.FontSize = 12.5; clear.FontWeight = FontWeight.SemiBold; Ui.Put(foot, clear, 1); }
        var column = new StackPanel { Width = 250 };
        var offset = filterCard?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;
        column.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Offset = offset });
        column.Children.Add(Ui.Rule()); column.Children.Add(foot);
        // One card for the page's life, whose contents are drawn again as ticks change: a popup keeps showing the child it was opened with.
        if (filterCard is null) { filterCard = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = HangingCard.Room, BoxShadow = HangingCard.Shadow, Name = "NotesAccountCard" }; filterCard.Classes.Add("surface"); filterCard.Classes.Add("flyoutCard"); }
        filterCard.Child = column;
        PickerFocus.Manage(filterHost, filterCard);
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
        // A retained virtualized row may still carry a temporary ID after persistence recovers.
        note = Notes.Find(note.Id) ?? note;
        if (removing == note.Id) return RemoveQuestion(note);
        if (editing == note.Id) return Editor(note);
        var isMeeting = NotesModel.IsMeeting(note); var plain = Notes.Text(note); var acct = Notes.AccountOf(note);
        var line = Ui.Grid("Auto,*,Auto,Auto"); line.ColumnSpacing = 10; line.VerticalAlignment = VerticalAlignment.Center;
        Control lead = isMeeting ? new IconGlyph(IconKind.Today) { TintKey = "AccentLit", VerticalAlignment = VerticalAlignment.Center } : NoteParts.Tick(Notes, note);
        Ui.Put(line, lead);
        var text = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontWeight = isMeeting ? FontWeight.SemiBold : FontWeight.Normal, Name = "PageNoteText_" + note.Id, VerticalAlignment = VerticalAlignment.Center }; NoteLinks.Fill(text, Notes.Shown(note));
        if (note.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Classes.Add("muted"); }
        ToolTip.SetTip(text, plain);
        var main = Ui.Button("", () => Edit(note), "PageNoteOpen_" + note.Id); main.Classes.Add("pillBare"); main.Content = text; main.Padding = new Thickness(0); main.HorizontalContentAlignment = HorizontalAlignment.Left; main.HorizontalAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(main, (isMeeting ? "Edit meeting: " : "Edit note: ") + plain);
        Ui.Put(line, main, 1);
        // The chips sit at the right of the line: the account (pressing it filters to that account), the reminder or the meeting's time, and how it repeats.
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(new DraftBadge(model, note.Id));
        if (acct is not null)
        {
            var chip = Ui.Button("", () => { accountFilters.Clear(); accountFilters.Add(acct); shown = null; Sync(); }, "PageRowAccount_" + note.Id); chip.Classes.Add("pillBare"); chip.Padding = new Thickness(0);
            chip.Content = NoteParts.Chip("#" + acct.Replace(" ", ""), "plain", null, NoteParts.Dot(model, acct)); ToolTip.SetTip(chip, "Show only " + acct); Avalonia.Automation.AutomationProperties.SetName(chip, "Show only " + acct); chips.Children.Add(chip);
        }
        if (isMeeting) chips.Children.Add(NoteParts.Chip(NoteDates.Day(note.Day, model.Engine.Now) + ", " + NoteParts.Span(note), "plain"));
        else if (NotesModel.ReminderOf(note) is { } due)
        {
            Button reminder = null!;
            reminder = Ui.Button("", () => OpenRowReminder(note, reminder), "PageNoteReminder_" + note.Id);
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

    private void CaptureComposer()
    {
        if (editing is null) model.CreationDrafts.Update("notes", meeting, new(draft, account, meetingDay.Date, startText, endText, when, important, repeat));
    }
    private void RestoreComposer()
    {
        var v = model.CreationDrafts.Get("notes", meeting);
        draft = v.Words; account = v.Account; meetingDay = v.Day; startText = v.Start; endText = v.End; when = v.Reminder; important = v.Important; repeat = v.Repeat;
        error = null; allLater = false; removing = null; archived = false;
    }
    private void ResetComposers() { if (editing is null) { RestoreComposer(); DrawForm(); } }
    // A note being typed in the add line is kept while a row is open for editing, and comes back when the row is closed.
    private void EndEdit()
    {
        whenHost.IsOpen = false;
        if (stashedKind is { } kept) { meeting = kept; stashedKind = null; editing = null; RestoreComposer(); }
        else { editing = null; RestoreComposer(); }
    }

    /// <summary>Switches the add line between a note and a meeting, as the kind picker does.</summary>
    public void ResumeComposer(bool isMeeting) { if (editing is not null) EndEdit(); SetKind(isMeeting); DrawForm(); }
    public void SetKind(bool isMeeting)
    {
        if (editing is not null || meeting == isMeeting) return;
        CaptureComposer(); meeting = isMeeting; RestoreComposer(); DrawForm();
    }

    /// <summary>Opens a note (or meeting) for editing, as a press on its row does. A pin on the timeline asks for this.</summary>
    public void OpenNote(long id)
    {
        if (Notes.Find(id) is not { } note) return;
        Edit(note);
        RevealEditor(note.Id);
    }

    private void RevealEditor(long id, bool openReminder = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (editing != id || !activity.Active) return;
            var row = rows.OfType<NoteRow>().FirstOrDefault(item => Notes.Find(item.Note.Id)?.Id == id);
            if (row is null) return;
            list.ScrollIntoView(row);
            if (openReminder) this.GetVisualDescendants().OfType<RecordEditor>().FirstOrDefault(e => e.Draft.NoteId == id)?.PickReminder();
        }, DispatcherPriority.Loaded);
    }

    private void Edit(DayNote note)
    {
        if (editing is null) { CaptureComposer(); stashedKind = meeting; }
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
        CaptureComposer();
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
        else column.Children.Add(MeetingFields(true));
        if (error is not null) column.Children.Add(ErrorLine());
        formHost.Content = column;
    }

    // The words of the add line or the open row, with a # word offering the accounts that match it: choosing one sets the account and takes the word out of the note.
    private Control Words(string watermark, out StackPanel suggestions)
    {
        var field = new TextBox { Name = "PageNoteField", Text = draft, Height = 36, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !archived, Watermark = watermark, HorizontalAlignment = HorizontalAlignment.Stretch };
        NoteLinks.AttachPaste(field);
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { draft = field.Text ?? ""; CaptureComposer(); } };
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        var host = new StackPanel { Name = "PageSuggestions", IsVisible = false }; host.Classes.Add("suggestions"); suggestions = host;
        void Choose(string name, string rest) { account = name; draft = rest; if (editing is not null) Redraw(); else DrawForm(); Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "PageNoteField") is { } again) { again.Focus(); again.CaretIndex = again.Text?.Length ?? 0; } }); }
        (string Account, string Words)? first = null; var nav = new NoteParts.SuggestionState();
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { nav.Index = 0; first = NoteParts.ShowSuggestions(model, field.Text ?? "", host, "PageSuggest_", Choose, nav); } };
        // The arrow keys move the light through the offers, and Tab or Enter take the lit one.
        field.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (host.IsVisible && e.Key == Key.Escape) { host.IsVisible = false; e.Handled = true; }
            else if (host.IsVisible && e.Key is Key.Down or Key.Up && nav.Move(e.Key == Key.Down ? 1 : -1)) { first = NoteParts.ShowSuggestions(model, field.Text ?? "", host, "PageSuggest_", Choose, nav); e.Handled = true; }
            else if (first is { } offer && host.IsVisible && e.Key is Key.Tab or Key.Enter) { Choose(offer.Account, offer.Words); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return field;
    }

    // The kind: a two-way slider, Note or Meeting.
    private Control KindPicker()
    {
        var note = Seg("PageKindNote", "Note", !meeting, () => SetKind(false));
        var meet = Seg("PageKindMeeting", "Meeting", meeting, () => SetKind(true));
        var track = new Border { Name = "PageKind", Height = 36, Child = Ui.Horizontal(note, meet) }; track.Classes.Add("segTrack");
        Avalonia.Automation.AutomationProperties.SetName(track, "Kind: " + (meeting ? "Meeting" : "Note"));
        return track;
    }

    // The reminder (a bell and the time when set) for a note.
    private Control WhenButton(bool top)
    {
        var text = when is { } w ? NoteDates.When(w, model.Engine.Now) : "Reminder";
        var button = Ui.Button("", () => OpenWhen(), "PageWhen"); button.Classes.Add("ctl"); button.Height = 36; button.Padding = new Thickness(top && when is null ? 10 : 12, 0); button.IsEnabled = !archived;
        ToolTip.SetTip(button, "Choose a reminder"); Avalonia.Automation.AutomationProperties.SetName(button, "Reminder: " + text);
        button.Content = top && when is null ? new IconGlyph(IconKind.Bell) : Ui.Horizontal(new IconGlyph(IconKind.Bell), new TextBlock { Text = text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
        ClosePickerOnDetach(button);
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
        ClosePickerOnDetach(day);
        day.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), new TextBlock { Text = NoteDates.Day(meetingDay, model.Engine.Now), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center }); Add(day);
        TextBox Time(string name, string text, string tip, Action<string> set)
        {
            var box = new TextBox { Name = name, Text = text, Width = 74, Height = 36, MinHeight = 36, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 5, IsEnabled = !archived };
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { set(box.Text ?? ""); CaptureComposer(); } };
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
        var repeats = new ChoiceStrip { Name = "PageRepeat", Look = ChoiceLook.Dropdown, AccessibleName = "Repeat", ItemsSource = NotesModel.Offered(repeat), IsEnabled = !archived };
        repeats.SelectedItem = NotesModel.Label(repeat);
        repeats.SelectionChanged += (_, _) => { if (repeats.SelectedItem is string label) { repeat = Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label); CaptureComposer(); } };
        Add(repeats);
        if (withAccount) Add(AccountBox());
        return row;
    }

    private ChoiceStrip AccountBox()
    {
        var names = new List<string> { "No account" }; names.AddRange(model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var accounts = new ChoiceStrip { Name = "PageAccount", Look = ChoiceLook.Dropdown, Width = 168, ItemsSource = names, IsEnabled = !archived,
            DotFor = shown => shown == "No account" ? null : model.Accounts.FirstOrDefault(x => x.Name.Equals(shown, StringComparison.OrdinalIgnoreCase)) is { } found ? Ui.Visible(found.Colour) : null };
        accounts.SelectedItem = account is null ? "No account" : names.FirstOrDefault(n => n.Equals(account, StringComparison.OrdinalIgnoreCase)) ?? "No account";
        accounts.SelectionChanged += (_, _) => { account = accounts.SelectedItem is string chosen && chosen != "No account" ? chosen : null; CaptureComposer(); };
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
    // a header delete action and a Save/Cancel footer. Other rows retain their place.
    private Control Editor(DayNote note)
    {
        var editor = new RecordEditor(model, model.Drafts.AcquireNote(note.Id), inline: true,
            close: () =>
            {
                var id = editorId(); EndEdit(); Redraw();
                Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PageNoteOpen_" + id)?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
            }, delete: () => { if (Notes.Find(note.Id) is { } found) AskRemove(found); });
        long editorId() => Notes.Find(note.Id)?.Id ?? note.Id;
        var card = new Border { Child = editor, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12), Name = "PageRow_" + note.Id };
        card.Classes.Add("noteRow"); card.Classes.Add("rowSelected"); return card;
    }
    /// <summary>One choice of a two-way switch. Put the choices in <see cref="Switch"/> so they read as one control.</summary>
    internal static Button Seg(string name, string text, bool on, Action click)
    {
        var button = new ChoiceButton("optOn") { Content = text, Name = name }; button.Click += (_, _) => click();
        button.Classes.Add("seg"); button.Classes.Set("optOn", on); button.Height = 30; button.FontSize = 13.5;
        return button;
    }
    /// <summary>The track that holds a switch's choices, 36 tall like the boxes beside it.</summary>
    internal static Border Switch(params Control[] choices)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; foreach (var c in choices) row.Children.Add(c);
        foreach (var choice in choices.OfType<ChoiceButton>()) choice.Sync();
        var track = new Border { Child = row, Height = 36, VerticalAlignment = VerticalAlignment.Center }; track.Classes.Add("segTrack"); return track;
    }

    // ---- The cards the line hangs: a reminder, or a meeting's day ----

    private void ClosePickerOnDetach(Control target)
    {
        target.DetachedFromVisualTree += (_, _) =>
        {
            if (whenHost.PlacementTarget != target) return;
            whenHost.IsOpen = false;
            whenHost.Child = null;
            whenHost.PlacementTarget = null;
        };
    }

    private void OpenWhen()
    {
        var target = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PageWhen") ?? (Control)formHost;
        whenHost.PlacementTarget = target;
        whenHost.Child = ReminderPopup.Roomy(new ReminderPickerCard(model, string.IsNullOrWhiteSpace(draft) ? "New note" : draft, when, answered =>
        {
            when = answered; whenHost.IsOpen = false; whenHost.Child = null; if (editing is not null) Redraw(); else DrawForm();
        }, () => { whenHost.IsOpen = false; whenHost.Child = null; }), whenHost);
        whenHost.IsOpen = true;
    }
    private void OpenRowReminder(DayNote note, Button target)
    {
        whenHost.PlacementTarget = target; ClosePickerOnDetach(target);
        whenHost.Child = ReminderPopup.Roomy(new ReminderPickerCard(model, Notes.Words(note), NotesModel.ReminderOf(note), value =>
        {
            whenHost.IsOpen = false; whenHost.Child = null;
            if (Notes.Find(note.Id) is { } current) Notes.SetReminder(current.Id, value);
        }, () => whenHost.IsOpen = false), whenHost);
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
        }, () => { whenHost.IsOpen = false; whenHost.Child = null; }, "Meeting day"), whenHost);
        whenHost.IsOpen = true;
    }

    // ---- Saving ----

    private void Submit()
    {
        CaptureComposer();
        error = model.CreationDrafts.Save("notes", meeting);
        if (error is not null) { Redraw(); return; }
        RestoreComposer(); Redraw(); model.Tell(Notes.Unsaved ? "Not saved · retrying" : meeting ? "Meeting added" : "Note added");
    }
}
