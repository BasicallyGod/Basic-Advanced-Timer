using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

// The Details card for a meeting, or a stack of meetings (three or more at once). A stack is drawn as the same timeline as a stack of blocks, with the picked meeting's ends
// draggable to five minutes; under it, or alone for a single meeting, are the meeting's own fields. Each is applied when Enter is pressed in it or it loses the focus, as the
// boxes of a block are. A repeating meeting has a switch for what a change or a delete applies to (this one, or this and all later), so no question has to be asked.
public sealed partial class SelectedBlockCard
{
    private const string NoAccount = "No account";
    private sealed class MeetingFields
    {
        public long Id; public bool Series, Later, Settling, Dirty, Saved; public DateTime Day;
        public TimeSpan LastStart, LastEnd;
        public TextBlock State = null!; public Button Save = null!, Discard = null!;
        public TextBox Name = null!, Start = null!, End = null!; public ChoiceStrip Account = null!, Repeat = null!; public TextBlock DayWords = null!; public Border ScopeHost = null!;
    }
    private MeetingFields? meetingFields;
    private bool IsSeries(DayNote meeting) => model.Notes.SeriesOf(meeting) is not null && !model.Notes.IsArchivedRepeat(meeting);

    private void SyncMeeting(DayNote meeting)
    {
        var stack = ReviewViews.MeetingStackOf(model, meeting);
        var signature = $"M#{meeting.Id}#{model.Notes.Words(meeting)}#{model.Notes.IsSaved(meeting.Id)}#{model.Notes.AccountOf(meeting)}#{meeting.Day:yyyyMMdd}#{meeting.At}#{meeting.End}#{model.Notes.RepeatOf(meeting)}#{model.ReviewAccounts.Count}#{model.EditError}#"
            + (stack is null ? "" : string.Join("|", stack.Select(m => $"{m.Id};{m.At};{m.End};{model.Notes.Text(m)}")));
        if (signature == shown) return;
        if (meetingFields is { } fields && fields.Id == meeting.Id && fields.Series == IsSeries(meeting) && stackHost is not null)
        {
            shown = signature; RefreshMeeting(meeting, false); ShowMeetingStack(meeting, stack); ShowState(fields); return;
        }
        shown = signature; current = null;
        Child = MeetingCard(meeting, stack); ShowMeetingStack(meeting, stack);
    }

    private Control MeetingCard(DayNote meeting, IReadOnlyList<DayNote>? stack)
    {
        var notes = model.Notes; var series = IsSeries(meeting);
        var f = new MeetingFields { Id = meeting.Id, Series = series, Day = meeting.Day.Date, LastStart = meeting.At!.Value, LastEnd = meeting.End!.Value }; meetingFields = f;
        var (header, actions) = Header(stack is not null ? "Meetings" : "Meeting");
        actions.Children.Add(Ui.Ctl("DeleteMeeting", "Delete", IconKind.Trash, "Delete", () => DeleteMeeting(f), small: true, key: "Del"));
        var body = new StackPanel { Spacing = 12 };
        stackHost = new Border { Name = "StackHost", IsVisible = false }; body.Children.Add(stackHost);

        f.Name = Box("DetailsMeetingName", notes.Words(meeting), true);
        var accounts = new List<string> { NoAccount }; accounts.AddRange(model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var chosen = notes.AccountOf(meeting); if (chosen is not null && !accounts.Contains(chosen, StringComparer.OrdinalIgnoreCase)) accounts.Add(chosen);
        f.Account = AccountStrip("DetailsMeetingAccount", accounts);
        f.Account.SelectedItem = accounts.FirstOrDefault(a => chosen is not null && a.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? NoAccount;
        f.Start = Box("DetailsMeetingStart", Hm(meeting.At), true); f.End = Box("DetailsMeetingEnd", Hm(meeting.End), true);
        f.Repeat = new ChoiceStrip { Name = "DetailsMeetingRepeat", Look = ChoiceLook.Dropdown, ItemsSource = NotesModel.Offered(notes.RepeatOf(meeting)) };
        f.Repeat.SelectedItem = NotesModel.Label(notes.RepeatOf(meeting));

        // The day is chosen on the same small calendar as a reminder's, in its day-only form, hung under the button.
        f.DayWords = new TextBlock { Text = NoteDates.Day(f.Day, model.Engine.Now), VerticalAlignment = VerticalAlignment.Center };
        var popup = new Popup { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "DetailsMeetingDayPopup" };
        Button? dayButton = null;
        dayButton = Ui.Ctl("DetailsMeetingDay", null, null, "Pick day", () =>
        {
            var content = new ReminderPopup(() => model.Engine.Now, f.Name.Text ?? "Meeting", f.Day, answered =>
            {
                popup.IsOpen = false;
                if (answered is { } picked && picked.Date != f.Day) { f.Day = picked.Date; f.DayWords.Text = NoteDates.Day(f.Day, model.Engine.Now); Apply(f, "day"); }
            }, () => popup.IsOpen = false, "Meeting day", dayOnly: true);
            popup.PlacementTarget = dayButton; popup.Child = ReminderPopup.Roomy(content, popup); popup.IsOpen = true;
        });
        dayButton.Width = double.NaN; dayButton.Height = 38; dayButton.Padding = new Thickness(12, 0); dayButton.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), f.DayWords);
        var dayHost = new Grid { Children = { dayButton, popup } };

        var row1 = Ui.Grid("2*,Auto,*"); row1.ColumnSpacing = 10; Field(row1, 0, "Name", f.Name); Field(row1, 1, "Day", dayHost); Field(row1, 2, "Account", f.Account);
        var row2 = Ui.Grid("100,100,*"); row2.ColumnSpacing = 10; Field(row2, 0, "Start", f.Start); Field(row2, 1, "End", f.End); Field(row2, 2, "Repeat", f.Repeat);
        body.Children.Add(row1); body.Children.Add(row2);
        if (series)
        {
            f.ScopeHost = new Border { Name = "DetailsScope" }; var scope = new StackPanel { Spacing = 5 }; scope.Children.Add(Label("Changes apply to")); scope.Children.Add(f.ScopeHost); body.Children.Add(scope);
            DrawScope(f);
        }
        // Whether the changes are saved is always said. A plain meeting saves each change as it is made, and says so. A repeating one keeps its changes until Save, so the choice of what
        // they apply to can be made after them, and says Unsaved changes with Save and Discard while there are some.
        f.State = new TextBlock { Name = "DetailsSaveState", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        f.Save = Ui.Ctl("DetailsSave", "Save", null, "Save changes", () => Apply(f, "save", true), small: true); f.Save.Classes.Add("accentOutline");
        f.Discard = Ui.Ctl("DetailsDiscard", "Discard", null, "Discard changes", () => Discard(f), small: true);
        var status = Ui.Horizontal(f.Save, f.Discard, f.State); status.Spacing = 10; body.Children.Add(status);
        ShowState(f);
        Committing(f.Name, () => Apply(f, "name")); Committing(f.Start, () => Apply(f, "start")); Committing(f.End, () => Apply(f, "end"));
        foreach (var box in new[] { f.Name, f.Start, f.End }) box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !f.Settling) { f.Dirty = true; f.Saved = false; ShowState(f); }
        };
        f.Account.SelectionChanged += (_, _) => Apply(f, "account"); f.Repeat.SelectionChanged += (_, _) => Apply(f, "repeat");
        // A repeating meeting's card is taller than the room: the body scrolls under the header, with room round it for the keys' shadows, so Save and Discard are never cut off.
        var page = Ui.Grid("*", "Auto,*"); Ui.Put(page, header);
        var scroller = new ScrollViewer { Content = new Border { Padding = new Thickness(2, 12, 2, 8), Child = body }, Name = "DetailsScroll", HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Ui.LeaveRoomForTrack(scroller, body); Ui.Put(page, scroller, 0, 1);
        return page;
    }

    // The switch for what a change applies to, drawn again when it is pressed so the chosen half is filled.
    private void DrawScope(MeetingFields f)
    {
        var one = NotesPage.Seg("DetailsScopeOne", "This one", !f.Later, () => { f.Later = false; DrawScope(f); ShowState(f); });
        var all = NotesPage.Seg("DetailsScopeAll", "This and all later", f.Later, () => { f.Later = true; DrawScope(f); ShowState(f); });
        var track = NotesPage.Switch(one, all); track.HorizontalAlignment = HorizontalAlignment.Left; f.ScopeHost.Child = track;
    }

    private static string Hm(TimeSpan? time) => time is { } t ? t.ToString(@"hh\:mm") : "";

    // Duration follows the last committed draft, not the stored occurrence. Refused values stay available for correction.
    private void Apply(MeetingFields f, string changed, bool commit = false)
    {
        if (f.Settling || model.Notes.Find(f.Id) is not { } meeting) return;
        var words = (f.Name.Text ?? "").Trim();
        void Refuse(string message) { f.Dirty = true; f.Saved = false; ShowState(f); model.Refuse(message); }
        if (words.Length == 0) { Refuse("Name the meeting"); return; }
        if (!TimeText.TryParseClock(f.Start.Text, f.Day, false, out var start)) { Refuse("Start: try 09:30"); return; }
        DateTime end;
        if (changed == "start" && f.End.Text == Hm(f.LastEnd) && start.TimeOfDay != f.LastStart)
        {
            end = start + (f.LastEnd - f.LastStart);
            f.Settling = true; try { f.End.Text = end.ToString("HH:mm"); } finally { f.Settling = false; }
        }
        else if (!TimeText.TryParseClockAfter(f.End.Text, start, false, true, out end)) { Refuse("End: try 10:30"); return; }
        // Equal clock faces can represent a stored full-day meeting; preserve that duration when editing other fields.
        if (end == start && f.LastEnd - f.LastStart == TimeSpan.FromDays(1) && f.End.Text == Hm(f.LastEnd)) end = end.AddDays(1);
        if (end <= start || end - start > TimeSpan.FromDays(1)) { Refuse("End must be after start, within 24 hours"); return; }
        var endOffset = end - f.Day.Date;
        f.LastStart = start.TimeOfDay; f.LastEnd = endOffset;
        var account = f.Account.SelectedItem is string chosen && chosen != NoAccount ? chosen : null;
        var repeat = f.Repeat.SelectedItem is string label ? Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label) : model.Notes.RepeatOf(meeting);
        var later = f.Series && f.Later;
        var unchanged = words == model.Notes.Words(meeting).Trim() && string.Equals(account, model.Notes.AccountOf(meeting), StringComparison.OrdinalIgnoreCase) && f.Day == meeting.Day.Date
            && start.TimeOfDay == meeting.At && endOffset == meeting.End && repeat == model.Notes.RepeatOf(meeting);
        // A repeating meeting keeps what is typed until Save, so what it applies to can be chosen after the changes are made.
        if (f.Series && !commit) { f.Dirty = !unchanged; f.Saved = false; ShowState(f); return; }
        model.ClearEditRefusal();
        if (unchanged) { f.Dirty = false; ShowState(f); if (!f.Series) RefreshMeeting(meeting, true); return; }
        var problem = model.Notes.EditMeeting(meeting.Id, words, account, f.Day, start.TimeOfDay, endOffset, repeat, later);
        if (problem is not null) { Refuse(problem); return; }
        f.Dirty = false; f.Saved = true; ShowState(f);
        if (f.Series) model.Tell(!model.Notes.Unsaved && model.Notes.IsSaved(f.Id) ? (later ? "Saved for this day and all later" : "Saved for this day only") : "Meeting not saved · retrying");
    }

    private void Discard(MeetingFields f)
    {
        f.Dirty = false; f.Saved = false;
        if (model.Notes.Find(f.Id) is { } meeting) RefreshMeeting(meeting, true);
        model.ClearEditRefusal(); ShowState(f);
    }

    // The line under the boxes that says whether the changes are saved, and shows Save and Discard while a repeating meeting has some that are not.
    private void ShowState(MeetingFields f)
    {
        var pending = f.Series && f.Dirty;
        var queued = model.Notes.Unsaved || !model.Notes.IsSaved(f.Id);
        f.Save.IsVisible = f.Discard.IsVisible = pending;
        f.State.Text = f.Series
            ? (pending ? "Unsaved changes · apply to " + (f.Later ? "this and all later" : "this one") : queued ? "Not saved · retrying" : f.Saved ? "Saved" : "No changes")
            : (f.Dirty ? "Unsaved changes" : queued ? "Not saved · retrying" : f.Saved ? "Saved" : "Changes save as you make them");
        f.State.Classes.Set("muted", !pending && !queued);
        if (pending || queued) f.State.Bind(TextBlock.ForegroundProperty, f.State.GetResourceObservable("AccentText")); else f.State.ClearValue(TextBlock.ForegroundProperty);
    }

    private void DeleteMeeting(MeetingFields f)
    {
        if (model.Notes.Find(f.Id) is not { } meeting) return;
        var later = f.Series && f.Later;
        model.Notes.Remove(meeting.Id, later); model.ClearSelection();
        model.Tell(f.Series ? (later ? "Removed from this day on" : "Removed for this day") : "Meeting removed");
    }

    // Brings the boxes up to date with the meeting in place. A box that has the focus keeps what is being typed in it, unless every box is to go back to the saved values.
    private void RefreshMeeting(DayNote meeting, bool all)
    {
        if (meetingFields is not { } f) return;
        if (f.Dirty && !all) return; // Pending and refused values remain until Save, Discard or correction.
        f.Settling = true;
        try
        {
            void Set(TextBox box, string text) { if ((all || !box.IsFocused) && box.Text != text) box.Text = text; }
            Set(f.Name, model.Notes.Words(meeting)); Set(f.Start, Hm(meeting.At)); Set(f.End, Hm(meeting.End));
            f.LastStart = meeting.At!.Value; f.LastEnd = meeting.End!.Value;
            f.Day = meeting.Day.Date; f.DayWords.Text = NoteDates.Day(f.Day, model.Engine.Now);
            var chosen = model.Notes.AccountOf(meeting);
            if (f.Account.ItemsSource is IEnumerable<string> names) f.Account.SelectedItem = names.FirstOrDefault(a => chosen is not null && a.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? NoAccount;
            f.Repeat.SelectedItem = NotesModel.Label(model.Notes.RepeatOf(meeting));
        }
        finally { f.Settling = false; }
        if (all) { f.Dirty = false; ShowState(f); } // Every box back to the saved values leaves nothing unsaved.
    }

    // The timeline of a stack of meetings, in place of the blocks' one; drag a picked meeting's ends and it moves by five minutes (a repeating meeting for this day only).
    private void ShowMeetingStack(DayNote meeting, IReadOnlyList<DayNote>? stack)
    {
        if (kindText is not null) kindText.Text = stack is null ? "Meeting" : "Meetings";
        if (stackHost is null) return;
        stackHost.Child = null; stackHost.IsVisible = stack is not null;
        if (stack is null) return;
        var fill = Color.Parse(model.CurrentTheme.Colour("Hover")); var ink = Color.Parse(model.CurrentTheme.Colour("Ink"));
        var bars = stack.Select(m => new StackTimeline.Bar("M" + m.Id, model.Notes.Text(m), "", $"{model.Notes.Text(m)} · {Hm(m.At)}–{Hm(m.End)}", m.Day.Date + m.At!.Value, m.Day.Date + m.End!.Value, fill, ink,
            m.Id == model.SelectedMeetingId, false, m.Id == model.SelectedMeetingId, new IconGlyph(IconKind.Today) { VerticalAlignment = VerticalAlignment.Center })).ToList();
        var timeline = new StackTimeline(model, bars, 5);
        timeline.PickedBar += bar => { var id = stack[bars.IndexOf(bar)].Id; if (id != model.SelectedMeetingId) model.SelectMeeting(id); };
        timeline.Resized += (bar, start, end) =>
        {
            var target = stack[bars.IndexOf(bar)];
            var problem = model.Notes.MoveMeeting(target.Id, start, end);
            if (problem is not null) model.Refuse(problem);
            else model.Tell((model.Notes.SeriesOf(target) is not null ? "Moved for this day only" : "Meeting moved") + " · " + start.ToString("HH:mm") + "–" + end.ToString("HH:mm"));
        };
        timeline.Margin = new Thickness(3, 0, 3, 4);
        stackHost.Child = new ScrollViewer { Content = timeline, MaxHeight = 160, Name = "StackScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
    }
}
