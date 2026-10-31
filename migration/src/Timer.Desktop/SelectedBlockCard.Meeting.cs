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
        public long Id; public bool Series, Later, Settling; public DateTime Day;
        public TextBox Name = null!, Start = null!, End = null!; public ComboBox Account = null!, Repeat = null!; public TextBlock DayWords = null!; public Border ScopeHost = null!;
    }
    private MeetingFields? meetingFields;
    private bool IsSeries(DayNote meeting) => model.Notes.SeriesOf(meeting) is not null && !model.Notes.IsArchivedRepeat(meeting);

    private void SyncMeeting(DayNote meeting)
    {
        var stack = ReviewViews.MeetingStackOf(model, meeting);
        var signature = $"M#{meeting.Id}#{model.Notes.Words(meeting)}#{model.Notes.AccountOf(meeting)}#{meeting.Day:yyyyMMdd}#{meeting.At}#{meeting.End}#{model.Notes.RepeatOf(meeting)}#{model.ReviewAccounts.Count}#{model.EditError}#"
            + (stack is null ? "" : string.Join("|", stack.Select(m => $"{m.Id};{m.At};{m.End};{model.Notes.Text(m)}")));
        if (signature == shown) return;
        if (meetingFields is { } fields && fields.Id == meeting.Id && fields.Series == IsSeries(meeting) && stackHost is not null)
        {
            shown = signature; RefreshMeeting(meeting, model.EditError is not null); ShowMeetingStack(meeting, stack); return;
        }
        shown = signature; current = null;
        Child = MeetingCard(meeting, stack); ShowMeetingStack(meeting, stack);
    }

    private Control MeetingCard(DayNote meeting, IReadOnlyList<DayNote>? stack)
    {
        var notes = model.Notes; var series = IsSeries(meeting);
        var f = new MeetingFields { Id = meeting.Id, Series = series, Day = meeting.Day.Date }; meetingFields = f;
        var (header, actions) = Header(stack is not null ? "Meetings" : "Meeting");
        actions.Children.Add(Ui.Ctl("DeleteMeeting", "Delete", IconKind.Trash, "Delete", () => DeleteMeeting(f), small: true, key: "Del"));
        var body = new StackPanel { Spacing = 12 };
        stackHost = new Border { Name = "StackHost", IsVisible = false }; body.Children.Add(stackHost);

        f.Name = Box("DetailsMeetingName", notes.Words(meeting), true);
        var accounts = new List<string> { NoAccount }; accounts.AddRange(model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name));
        var chosen = notes.AccountOf(meeting); if (chosen is not null && !accounts.Contains(chosen, StringComparer.OrdinalIgnoreCase)) accounts.Add(chosen);
        f.Account = new ComboBox { Name = "DetailsMeetingAccount", ItemsSource = accounts, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 38,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) => name == NoAccount ? new TextBlock { Text = NoAccount, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }
                : Ui.Horizontal(Swatch(name ?? ""), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center })) };
        f.Account.SelectedItem = accounts.FirstOrDefault(a => chosen is not null && a.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? NoAccount;
        f.Start = Box("DetailsMeetingStart", Hm(meeting.At), true); f.End = Box("DetailsMeetingEnd", Hm(meeting.End), true);
        f.Repeat = new ComboBox { Name = "DetailsMeetingRepeat", ItemsSource = NotesModel.Offered(notes.RepeatOf(meeting)), HorizontalAlignment = HorizontalAlignment.Stretch, Height = 38 };
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

        var row1 = Ui.Grid("2*,*"); row1.ColumnSpacing = 10; Field(row1, 0, "Name", f.Name); Field(row1, 1, "Account", f.Account);
        var row2 = Ui.Grid("Auto,*,*,1.9*"); row2.ColumnSpacing = 10; Field(row2, 0, "Day", dayHost); Field(row2, 1, "Start", f.Start); Field(row2, 2, "End", f.End); Field(row2, 3, "Repeat", f.Repeat);
        body.Children.Add(row1); body.Children.Add(row2);
        if (series)
        {
            f.ScopeHost = new Border { Name = "DetailsScope" }; var scope = new StackPanel { Spacing = 5 }; scope.Children.Add(Label("Changes apply to")); scope.Children.Add(f.ScopeHost); body.Children.Add(scope);
            DrawScope(f);
        }
        Committing(f.Name, () => Apply(f, "name")); Committing(f.Start, () => Apply(f, "start")); Committing(f.End, () => Apply(f, "end"));
        f.Account.SelectionChanged += (_, _) => Apply(f, "account"); f.Repeat.SelectionChanged += (_, _) => Apply(f, "repeat");
        return Page(header, body);
    }

    // The switch for what a change applies to, drawn again when it is pressed so the chosen half is filled.
    private void DrawScope(MeetingFields f)
    {
        var one = NotesPage.Seg("DetailsScopeOne", "This one", !f.Later, () => { f.Later = false; DrawScope(f); });
        var all = NotesPage.Seg("DetailsScopeAll", "This and all later", f.Later, () => { f.Later = true; DrawScope(f); });
        var track = NotesPage.Switch(one, all); track.HorizontalAlignment = HorizontalAlignment.Left; f.ScopeHost.Child = track;
    }

    private static string Hm(TimeSpan? time) => time is { } t ? t.ToString(@"hh\:mm") : "";

    // Applies the boxes to the meeting. Moving the start moves the end with it, so the meeting keeps its length, unless the end was typed as well. A refused change puts the saved values back.
    private void Apply(MeetingFields f, string changed)
    {
        if (f.Settling || model.Notes.Find(f.Id) is not { } meeting) return;
        var words = (f.Name.Text ?? "").Trim();
        if (words.Length == 0) { model.Refuse("Name the meeting"); RefreshMeeting(meeting, true); return; }
        if (!TimeText.TryParseClock(f.Start.Text, f.Day, false, out var start)) { model.Refuse("Start: try 09:30"); RefreshMeeting(meeting, true); return; }
        if (!TimeText.TryParseClock(f.End.Text, f.Day, false, out var end)) { model.Refuse("End: try 10:30"); RefreshMeeting(meeting, true); return; }
        var savedStart = f.Day + meeting.At!.Value; var savedEnd = f.Day + meeting.End!.Value;
        if (changed == "start" && end == savedEnd && start != savedStart)
        {
            end = start + (savedEnd - savedStart);
            if (end >= f.Day.AddDays(1)) end = f.Day.AddDays(1).AddMinutes(-1);
        }
        var account = f.Account.SelectedItem is string chosen && chosen != NoAccount ? chosen : null;
        var repeat = f.Repeat.SelectedItem is string label ? Enum.GetValues<RepeatChoice>().First(c => NotesModel.Label(c) == label) : model.Notes.RepeatOf(meeting);
        var later = f.Series && f.Later;
        var unchanged = words == model.Notes.Words(meeting).Trim() && string.Equals(account, model.Notes.AccountOf(meeting), StringComparison.OrdinalIgnoreCase) && f.Day == meeting.Day.Date
            && start.TimeOfDay == meeting.At && end.TimeOfDay == meeting.End && repeat == model.Notes.RepeatOf(meeting);
        if (unchanged) { RefreshMeeting(meeting, true); return; }
        var problem = model.Notes.EditMeeting(meeting.Id, words, account, f.Day, start.TimeOfDay, end.TimeOfDay, repeat, later);
        if (problem is not null) { model.Refuse(problem); RefreshMeeting(meeting, true); return; }
        if (f.Series) model.Tell(later ? "Saved for this day and all later" : "Changed for this day only");
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
        f.Settling = true;
        try
        {
            void Set(TextBox box, string text) { if ((all || !box.IsFocused) && box.Text != text) box.Text = text; }
            Set(f.Name, model.Notes.Words(meeting)); Set(f.Start, Hm(meeting.At)); Set(f.End, Hm(meeting.End));
            f.Day = meeting.Day.Date; f.DayWords.Text = NoteDates.Day(f.Day, model.Engine.Now);
            var chosen = model.Notes.AccountOf(meeting);
            if (f.Account.ItemsSource is IEnumerable<string> names) f.Account.SelectedItem = names.FirstOrDefault(a => chosen is not null && a.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? NoAccount;
            f.Repeat.SelectedItem = NotesModel.Label(model.Notes.RepeatOf(meeting));
        }
        finally { f.Settling = false; }
    }

    // The timeline of a stack of meetings, in place of the blocks' one; drag a picked meeting's ends and it moves by five minutes (a repeating meeting for this day only).
    private void ShowMeetingStack(DayNote meeting, IReadOnlyList<DayNote>? stack)
    {
        if (kindText is not null) kindText.Text = stack is null ? "Meeting" : "Meetings";
        if (stackHost is null) return;
        stackHost.Child = null; stackHost.IsVisible = stack is not null;
        if (stack is null) return;
        var day = meeting.Day.Date; var fill = Color.Parse(model.CurrentTheme.Colour("Hover")); var ink = Color.Parse(model.CurrentTheme.Colour("Ink"));
        var bars = stack.Select(m => new StackTimeline.Bar("M" + m.Id, model.Notes.Text(m), "", $"{model.Notes.Text(m)} · {Hm(m.At)}–{Hm(m.End)}", day + m.At!.Value, day + m.End!.Value, fill, ink,
            m.Id == model.SelectedMeetingId, false, m.Id == model.SelectedMeetingId, new IconGlyph(IconKind.Today) { VerticalAlignment = VerticalAlignment.Center })).ToList();
        var timeline = new StackTimeline(model, bars, 5);
        timeline.PickedBar += bar => { var id = stack[bars.IndexOf(bar)].Id; if (id != model.SelectedMeetingId) model.SelectMeeting(id); };
        timeline.Resized += (bar, start, end) =>
        {
            var target = stack[bars.IndexOf(bar)];
            var problem = model.Notes.MoveMeeting(target.Id, start.TimeOfDay, end.TimeOfDay);
            if (problem is not null) model.Refuse(problem);
            else model.Tell((model.Notes.SeriesOf(target) is not null ? "Moved for this day only" : "Meeting moved") + " · " + start.ToString("HH:mm") + "–" + end.ToString("HH:mm"));
        };
        timeline.Margin = new Thickness(3, 0, 3, 4);
        stackHost.Child = new ScrollViewer { Content = timeline, MaxHeight = 160, Name = "StackScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
    }
}
