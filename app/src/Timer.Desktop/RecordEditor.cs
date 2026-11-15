using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>One explicit record transaction in Details or an inline Notes row. Layout hosts own navigation.</summary>
public sealed class RecordEditor : Grid
{
    private readonly TimerModel model;
    private DraftLease? lease;
    private readonly Func<DraftLease> reopen;
    private readonly ViewActivity activity;
    private AccountCatalogue? accountCatalogue;
    private IReadOnlyList<AccountPresentation> Accounts => (accountCatalogue ??= AccountCatalogue.Capture(model)).Items;
    private TextBox? lastTextFocus;
    private readonly bool inline;
    private readonly Action? close;
    private bool settling, disposed, saved;
    private readonly Dictionary<DraftField, Control> fields = new();
    private readonly Dictionary<DraftField, TextBlock> errors = new();
    private readonly StackPanel conflicts = new() { Spacing = 6 };
    private readonly TextBlock state;
    private readonly Button save, cancel;
    private readonly ChoiceStrip account;
    private readonly ChoiceStrip? repeat;
    private readonly ToggleButton? important;
    private readonly TextBox words;
    private TextBox? start, end;
    private DurationBox? length;
    private Button? date, reminder;
    private ChoiceStrip? scope;
    private readonly List<Popup> popups = new();
    private string lastStart;
    public RecordDraft Draft { get; private set; }
    public StackPanel HeaderActions { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 8 };

    public RecordEditor(TimerModel model, DraftLease lease, bool inline = false, Action? close = null, Action? delete = null)
    {
        this.model = model; this.lease = lease; reopen = lease.Reopen; Draft = lease.Draft; this.inline = inline; this.close = close;
        Name = inline ? "PageRecordEditor" : "DetailsRecordEditor";
        RowDefinitions = new RowDefinitions("*,Auto");
        var body = new StackPanel { Spacing = 12 }; var v = Draft.Values; lastStart = v.Start;
        if (Draft.Kind == DraftKind.Note)
        {
            important = NeumorphicControls.Important(v.Important, () => Change(Draft.Values with { Important = !Draft.Values.Important }), inline ? "PageImportant" : "DetailsNoteImportant", 30);
            HeaderActions.Children.Add(important);
        }
        if (delete is not null) HeaderActions.Children.Add(Ui.Ctl(inline ? "PageDelete" : Draft.Kind == DraftKind.Note ? "DeleteNote" : Draft.Kind == DraftKind.Meeting ? "DeleteMeeting" : "DeleteSelected", inline ? null : "Delete", IconKind.Trash, "Delete", delete, small: true));
        if (inline) { HeaderActions.HorizontalAlignment = HorizontalAlignment.Right; body.Children.Add(HeaderActions); }
        words = new TextBox { Name = WordName(), Text = v.Words, MinHeight = Draft.Kind == DraftKind.Meeting ? 38 : 70,
            AcceptsReturn = Draft.Kind != DraftKind.Meeting, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(words, Draft.Kind == DraftKind.Meeting ? "Meeting name" : Draft.Kind == DraftKind.Block ? "Entry note" : "Note");
        NoteLinks.AttachPaste(words); AddField(body, DraftField.Words, Draft.Kind == DraftKind.Meeting ? "Name" : Draft.Kind == DraftKind.Block ? "Entry note" : "Note", words);
        words.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !settling) Change(Draft.Values with { Words = words.Text ?? "" }); };
        var suggestions = new StackPanel { Name = inline ? "PageSuggestions" : "DetailsSuggestions", IsVisible = false };
        suggestions.Classes.Add("suggestions"); body.Children.Add(suggestions);
        var navigation = new NoteParts.SuggestionState(); (string Account, string Words)? offered = null;
        void Choose(string name, string rest) { suggestions.IsVisible = false; Change(Draft.Values with { Account = name, Words = rest }); words.Focus(); words.CaretIndex = rest.Length; }
        void Suggest() => offered = NoteParts.ShowSuggestions(model, words.Text ?? "", suggestions, inline ? "PageSuggest_" : "DetailsSuggest_", Choose, navigation);
        words.PropertyChanged += (_, e) => { if (!settling && e.Property == TextBox.TextProperty) { navigation.Index = 0; Suggest(); } };
        words.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!suggestions.IsVisible) return;
            if (e.Key == Key.Escape) { suggestions.IsVisible = false; e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down && navigation.Move(e.Key == Key.Down ? 1 : -1)) { Suggest(); e.Handled = true; }
            else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Tab or Key.Enter && offered is { } offer) { Choose(offer.Account, offer.Words); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        GotFocus += (_, e) => { if (e.Source is TextBox box) lastTextFocus = box; };
        var options = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10 };
        account = new ChoiceStrip { Name = inline ? "PageAccount" : Draft.Kind == DraftKind.Note ? "DetailsNoteAccount" : Draft.Kind == DraftKind.Meeting ? "DetailsMeetingAccount" : "SelectedAccount", Look = ChoiceLook.Dropdown, AccessibleName = "Account", Width = 170,
            DotFor = a => Accounts.FirstOrDefault(x => x.Key.Equals(BreakAccount.Stored(a), StringComparison.OrdinalIgnoreCase)) is { } found ? Ui.Visible(found.Colour) : null };
        AddField(options, DraftField.Account, "Account", account);
        account.SelectionChanged += (_, _) => { if (!settling) Change(Draft.Values with { Account = account.SelectedItem is string a && a != "No account" ? BreakAccount.Stored(a) : null }); };
        if (Draft.Kind == DraftKind.Block) account.Width = 140;
        var accountWidth = account.Width;
        TextSizing.Follow(account, () => account.Width = accountWidth * TextSizing.Factor);
        if (Draft.Kind == DraftKind.Note)
        {
            reminder = Ui.Ctl(inline ? "PageWhen" : "DetailsNoteReminder", "No reminder", IconKind.Bell, "Choose reminder", OpenReminder); reminder.Width = 190;
            AddField(options, DraftField.Reminder, "Reminder", reminder);
        }
        else
        {
            if (Draft.Kind == DraftKind.Meeting)
            {
                date = Ui.Ctl(inline ? "PageWhen" : "DetailsMeetingDay", "Day", IconKind.Today, "Meeting day", OpenDay); date.Width = 155;
                AddField(options, DraftField.Day, "Day", date);
            }
            start = Clock(inline ? "PageStart" : Draft.Kind == DraftKind.Meeting ? "DetailsMeetingStart" : "SelectedStart", v.Start);
            end = Clock(inline ? "PageEnd" : Draft.Kind == DraftKind.Meeting ? "DetailsMeetingEnd" : "SelectedEnd", v.End);
            if (Draft.Kind == DraftKind.Block) start.Width = end.Width = 90;
            ToolTip.SetTip(start, "Start, HH:mm"); ToolTip.SetTip(end, "End, HH:mm");
            Panel clocks = Draft.Kind == DraftKind.Meeting ? new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10 } : options;
            AddField(clocks, DraftField.Start, "Start", start); AddField(clocks, DraftField.End, "End", end);
            if (clocks != options) options.Children.Add(clocks);
            start.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !settling) Change(Draft.Values with { Start = start.Text ?? "" }); };
            end.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !settling) Change(Draft.Values with { End = end.Text ?? "", Length = null }); };
            start.LostFocus += (_, _) => MoveMeetingEnd();
            start.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { MoveMeetingEnd(); e.Handled = true; } };
            if (Draft.Kind == DraftKind.Block)
            {
                length = new DurationBox(DurationUnit.Minutes, model, "Length") { Name = "SelectedLength", Width = 100, Height = 38 };
                AddField(options, DraftField.Length, "Length", length);
                length.PropertyChanged += (_, e) =>
                {
                    if (settling || e.Property != TextBox.TextProperty) return;
                    var values = Draft.Values;
                    if (length.ReadDuration() is { } span && TimeText.TryParseClock(values.Start, values.Day, false, out var from))
                        Change(values with { End = TimeText.ShowClock(from + span, from.Date), Length = null });
                    else Change(values with { Length = length.Text ?? "" });
                };
            }
            else
            {
                repeat = new ChoiceStrip { Name = inline ? "PageRepeat" : "DetailsMeetingRepeat", Look = ChoiceLook.Dropdown, AccessibleName = "Repeat", Width = 185, ItemsSource = NotesModel.Offered(v.Repeat) };
                AddField(options, DraftField.Repeat, "Repeat", repeat);
                repeat.SelectionChanged += (_, _) => { if (!settling && repeat.SelectedItem is string label) Change(Draft.Values with { Repeat = Enum.GetValues<RepeatChoice>().First(r => NotesModel.Label(r) == label) }); };
            }
        }
        body.Children.Add(options);
        if (Draft.Kind == DraftKind.Meeting && Draft.NoteId is { } id && model.Notes.Find(id) is { SeriesId: not null })
        {
            scope = new ChoiceStrip { Name = inline ? "PageScope" : "DetailsScope", Look = ChoiceLook.Segments, ItemsSource = new[] { "This one", "This and all later" }, SelectedIndex = Draft.AllLater ? 1 : 0 };
            scope.SelectionChanged += (_, _) => { if (!settling) Draft.AllLater = scope.SelectedIndex == 1; };
            var group = new StackPanel { Spacing = 5 }; group.Children.Add(Ui.Text("Changes apply to", 12, "muted")); group.Children.Add(scope); body.Children.Add(group);
        }
        body.Children.Add(conflicts);
        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(2, 2, 8, 8), Child = body }, MaxHeight = inline ? 380 : double.PositiveInfinity, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Name = inline ? "PageEditorScroll" : "DetailsScroll" };
        Ui.Put(this, scroll);
        save = Ui.Ctl(inline ? "PageSubmit" : "DetailsSave", "Save", null, "Save changes", Save, small: true); save.Classes.Add("primary");
        cancel = Ui.Ctl(inline ? "PageCancel" : "DetailsDiscard", "Cancel", null, "Cancel changes", Cancel, small: true);
        state = Ui.Text("", 12.5, "muted"); state.Name = inline ? "PageSaveState" : "DetailsSaveState"; state.TextWrapping = TextWrapping.Wrap;
        var footer = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Margin = new Thickness(2, 8, 2, 2) };
        footer.Children.Add(save); footer.Children.Add(cancel); footer.Children.Add(state); Ui.Put(this, footer, 0, 1);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            // Multiline TextBox handles Enter before a bubbling parent sees it.
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
            else if (e.Key == Key.Escape && !popups.Any(p => p.IsOpen)) { Cancel(); e.Handled = true; }
        }, RoutingStrategies.Bubble);
        AttachedToVisualTree += (_, _) => { if (this.lease is null) { this.lease = reopen(); Draft = this.lease.Draft; } disposed = false; accountCatalogue = null; Draft.Changed += Refresh; model.PropertyChanged += ModelChanged; Refresh(); };
        DetachedFromVisualTree += (_, _) => Release();
        activity = new ViewActivity(this, active => { if (active) Refresh(); });
        Refresh();
    }
    private string WordName() => inline ? "PageNoteField" : Draft.Kind == DraftKind.Note ? "DetailsNoteWords" : Draft.Kind == DraftKind.Meeting ? "DetailsMeetingName" : "SelectedNote";
    private static TextBox Clock(string name, string text) => new() { Name = name, Text = text, Width = 118, Height = 38, VerticalContentAlignment = VerticalAlignment.Center };
    private void AddField(Panel host, DraftField field, string label, Control control)
    {
        var column = new StackPanel { Spacing = 5 }; column.Children.Add(Ui.Text(label, 12, "muted")); column.Children.Add(control);
        var error = Ui.Text("", 12, "overtime"); error.Name = control.Name + "Error"; error.TextWrapping = TextWrapping.Wrap; error.MaxWidth = field == DraftField.Words ? 600 : 190; error.IsVisible = false;
        column.Children.Add(error); host.Children.Add(column); fields[field] = control; errors[field] = error;
    }
    private void Change(RecordValues values) { saved = false; Draft.Update(values); }
    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerModel.Elapsed)) return;
        accountCatalogue = null; Refresh();
    }
    private void MoveMeetingEnd()
    {
        if (settling || Draft.Kind != DraftKind.Meeting) return;
        var v = Draft.Values;
        if (v.Start != lastStart && TimeText.TryParseClock(lastStart, v.Day, false, out var before)
            && TimeText.TryParseClock(v.Start, v.Day, false, out var after) && TimeText.TryParseClockAfter(v.End, before, true, true, out var until))
        { lastStart = v.Start; Change(v with { End = TimeText.ShowClock(after + (until - before), after.Date) }); }
    }
    public void Refresh()
    {
        if (settling || disposed || activity?.Active == false) return; settling = true;
        try
        {
            var v = Draft.Values;
            // An external drag may move both clocks. Only a locally edited start should derive a new end.
            if (v.Start == Draft.Accepted.Start) lastStart = v.Start;
            void Text(TextBox? box, string value) { if (box is not null && box.Text != value) box.Text = value; }
            string ClockText(string value) => Draft.Kind == DraftKind.Meeting && value.Length == 8 && value.EndsWith(":00") ? value[..5] : value;
            Text(words, v.Words); Text(start, ClockText(v.Start)); Text(end, ClockText(v.End));
            if (length is not null && !length.IsFocused)
            {
                if (v.Length is { } invalid) Text(length, invalid);
                else if (TimeText.TryParseClock(v.Start, v.Day, false, out var from) && TimeText.TryParseClockAfter(v.End, from, true, true, out var until)) length.SetDuration(until - from);
            }
            var names = Accounts.Where(a => Draft.Kind == DraftKind.Block || !BreakAccount.Is(a.Key)).Select(a => a.DisplayName).ToList();
            if (Draft.Kind != DraftKind.Block) names.Insert(0, "No account");
            var chosen = v.Account is null ? "No account" : BreakAccount.Display(v.Account); if (!names.Contains(chosen)) names.Add(chosen);
            if (!(account.ItemsSource ?? Array.Empty<string>()).SequenceEqual(names)) account.ItemsSource = names;
            account.SelectedItem = chosen;
            if (important is not null) important.IsChecked = v.Important;
            if (repeat is not null) { var offered = NotesModel.Offered(v.Repeat); if (!(repeat.ItemsSource ?? Array.Empty<string>()).SequenceEqual(offered)) repeat.ItemsSource = offered; repeat.SelectedItem = NotesModel.Label(v.Repeat); }
            if (scope is not null) scope.SelectedIndex = Draft.AllLater ? 1 : 0;
            if (reminder is not null) reminder.Content = Ui.Horizontal(new IconGlyph(IconKind.Bell), Ui.Text(v.Reminder is { } at ? NoteDates.When(at, model.Engine.Now) : "No reminder", 13));
            if (date is not null) date.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), Ui.Text(NoteDates.Day(v.Day, model.Engine.Now), 13));
            foreach (var pair in errors) { pair.Value.Text = Draft.Errors.FirstOrDefault(e => e.Field == pair.Key)?.Message ?? ""; pair.Value.IsVisible = pair.Value.Text.Length > 0; }
            conflicts.Children.Clear();
            if (Draft.Missing) conflicts.Children.Add(Ui.Text("This item was removed. Your draft is retained.", 13, "overtime"));
            foreach (var conflict in Draft.Conflicts)
            {
                var field = conflict.Field;
                var group = new StackPanel { Spacing = 5 };
                static string Value(object? value) { var text = value?.ToString() ?? "None"; return text.Length > 140 ? text[..140] + "…" : text; }
                var current = Ui.Text("Current: " + Value(conflict.Current), 12, "muted"); current.TextWrapping = TextWrapping.Wrap;
                var yours = Ui.Text("Your draft: " + Value(conflict.Yours), 12, "muted"); yours.TextWrapping = TextWrapping.Wrap;
                var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 5 };
                row.Children.Add(Ui.Text(field + " changed elsewhere", 12, "overtime"));
                row.Children.Add(Ui.DialogButton("Keep mine", () => Draft.Resolve(field, true)));
                row.Children.Add(Ui.DialogButton("Use current", () => Draft.Resolve(field, false)));
                group.Children.Add(row); group.Children.Add(current); group.Children.Add(yours); conflicts.Children.Add(group);
            }
            save.IsEnabled = Draft.IsDirty && !Draft.IsSubmitting && !Draft.Missing && Draft.Conflicts.Count == 0;
            cancel.IsEnabled = Draft.IsDirty || inline;
            state.Text = Draft.Refusal ?? (Draft.IsSubmitting ? "Saving…" : Draft.PendingPersistence ? Draft.IsDirty ? "Draft · previous save retrying" : "Not saved · retrying"
                : Draft.Conflicts.Count > 0 ? "Resolve changes" : Draft.IsDirty ? "Draft" : saved ? "Saved" : "No changes");
        }
        finally { settling = false; }
    }
    private void Save()
    {
        MoveMeetingEnd(); var focus = lastTextFocus;
        var result = model.Drafts.Save(Draft); saved = result.Outcome is DraftSaveOutcome.Saved or DraftSaveOutcome.PendingPersistence; Refresh();
        if (result.Errors.FirstOrDefault() is { } issue && fields.TryGetValue(issue.Field, out var target)) target.Focus(NavigationMethod.Tab);
        else if (result.Outcome is DraftSaveOutcome.Saved or DraftSaveOutcome.NoChanges or DraftSaveOutcome.PendingPersistence)
        { if (inline && !Draft.IsDirty) close?.Invoke(); else if (focus is not null && focus.IsAttachedToVisualTree()) focus.Focus(NavigationMethod.Tab); }
    }
    private void Cancel() { model.Drafts.Cancel(Draft); saved = false; Refresh(); if (inline) close?.Invoke(); }
    private void OpenReminder()
    {
        var popup = NewPopup(reminder!, inline ? "PageWhenHost" : "DetailsNoteReminderPopup");
        popup.Child = ReminderPopup.Roomy(new ReminderPickerCard(model, Draft.Values.Words, Draft.Values.Reminder,
            value => { popup.IsOpen = false; Change(Draft.Values with { Reminder = value }); }, () => popup.IsOpen = false), popup); popup.IsOpen = true;
    }
    public void PickReminder() => OpenReminder();
    private void OpenDay()
    {
        var popup = NewPopup(date!, inline ? "PageWhenHost" : "DetailsMeetingDayPopup");
        popup.Child = ReminderPopup.Roomy(new ReminderPopup(() => model.Engine.Now, Draft.Values.Words, Draft.Values.Day,
            value => { popup.IsOpen = false; if (value is { } day) Change(Draft.Values with { Day = day.Date }); }, () => popup.IsOpen = false), popup); popup.IsOpen = true;
    }
    private Popup NewPopup(Control target, string name)
    {
        foreach (var old in popups) { old.IsOpen = false; Children.Remove(old); } popups.Clear();
        var popup = new Popup { Name = name, PlacementTarget = target, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true }; popups.Add(popup); Children.Add(popup); return popup;
    }
    public void Release()
    {
        if (disposed) return; disposed = true; Draft.Changed -= Refresh; model.PropertyChanged -= ModelChanged;
        foreach (var popup in popups) popup.IsOpen = false; lease?.Dispose(); lease = null;
    }
}
