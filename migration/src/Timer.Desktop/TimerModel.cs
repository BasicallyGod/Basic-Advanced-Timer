using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public sealed record TimerAccount(string Name, string Colour, TimeSpan? Budget, string Period = "week")
{
    public string Display => BreakAccount.Display(Name);
}

public sealed class TimerModel : INotifyPropertyChanged
{
    /// <summary>How many pixels an hour takes along the timeline: about ten working hours fit the dashboard at this scale.</summary>
    public double TimelinePixelsPerHour { get; private set; } = 90;
    /// <summary>How far the timeline is scrolled along its width, or nothing to open on the first block of the day.</summary>
    public double? TimelineOffset { get; set; }
    // The hours start a gutter in from the left of the timeline's content, so the time under a point is found from there.
    private static double OffsetKeeping(double offset, double pointerX, double previous, double target)
        => Math.Max(0, ReviewViews.TimelineGutter + (offset + pointerX - ReviewViews.TimelineGutter) / previous * target - pointerX);
    public void ZoomTimeline(double wheel, double pointerX, double offset)
    {
        if (wheel == 0) return;
        var previous = TimelinePixelsPerHour;
        TimelinePixelsPerHour = Math.Clamp(previous * Math.Pow(1.2, wheel), 60, 640);
        TimelineOffset = OffsetKeeping(offset, pointerX, previous, TimelinePixelsPerHour);
        Notify();
    }
    public const double MinTimelineScale = 60, MaxTimelineScale = 640;
    /// <summary>Sets the zoom to an exact scale (as the slider does), keeping the time under the pointer where it was.</summary>
    public void SetTimelineScale(double pixelsPerHour, double pointerX, double offset)
    {
        var previous = TimelinePixelsPerHour; var target = Math.Clamp(pixelsPerHour, MinTimelineScale, MaxTimelineScale);
        if (Math.Abs(target - previous) < .01) return;
        TimelinePixelsPerHour = target;
        TimelineOffset = OffsetKeeping(offset, pointerX, previous, target);
        Notify();
    }
    public TrackingEngine Engine { get; }
    public LedgerEditor Editor { get; }
    /// <summary>Notes and meetings (see <see cref="NotesModel"/>), kept beside this model so the two stay apart.</summary>
    public NotesModel Notes { get; }
    private string? editError;
    /// <summary>The refusal being shown. The shared packages word theirs as sentences, so those are put into the app's few words here.</summary>
    public string? EditError { get => editError; private set => editError = value is null ? null : Plain(value); }
    private static string Plain(string message)
        => message.Contains("overlaps recorded or running time", StringComparison.OrdinalIgnoreCase) ? "Overlaps another block"
         : message.Contains("moved into recorded or running time", StringComparison.OrdinalIgnoreCase) || message.Contains("moved into running time", StringComparison.OrdinalIgnoreCase) ? "Can’t move into other time"
         : message.Contains("intervening time cannot be merged", StringComparison.OrdinalIgnoreCase) ? "Time in between: can’t merge"
         : message.Contains("legacy note key", StringComparison.OrdinalIgnoreCase) ? "Edit these together"
         : message.Contains("start at the same time", StringComparison.OrdinalIgnoreCase) ? "Same start: can’t move to one account"
         : message;
    /// <summary>Asks for the Add time dialog, with the start and end it opens on.</summary>
    public event Action<DateTime, DateTime>? EntryRequested;
    public event Action? AccountsRequested;
    /// <summary>Asks for the accounts and budgets dialog. The dashboard shows it, and comes up first if only the pill is showing.</summary>
    public void OpenAccounts() => AccountsRequested?.Invoke();
    public string? ExportPath { get; private set; }
    private readonly HashSet<SessionRow> selectedRows = new(ReferenceEqualityComparer.Instance);
    public IReadOnlyCollection<SessionRow> SelectedRows => selectedRows;
    public bool IsSelected(IReadOnlyList<SessionRow> rows) => rows.Count > 0 && rows.All(selectedRows.Contains);
    public int SelectedBlockCount => selectedRows.Count == 0 ? 0 : Editor.Entries(WeekStart(SelectedDate), WeekStart(SelectedDate).AddDays(7))
        .Count(e => e.Sources.Count > 0 && e.Sources.All(selectedRows.Contains));
    public bool CanMergeSelection => SelectedBlockCount >= 2 && selectedRows.Select(s => s.Day).Distinct().Count() == 1
        && selectedRows.Select(s => s.Account).Distinct(StringComparer.Ordinal).Count() == 1 && Editor.CanCombine(selectedRows.ToArray());
    /// <summary>Why a merge can or cannot work, in a few words: the Merge menu item's tooltip, and the toast when Merge is pressed and refused.</summary>
    public string MergeHint
    {
        get
        {
            var count = SelectedBlockCount;
            if (count < 2) return "Pick a second block";
            if (CanMergeSelection)
                return Editor.MergeGapMinutes(selectedRows.ToArray()) is var gapMinutes and > 0 ? $"Joins them · adds {Format(TimeSpan.FromMinutes((double)gapMinutes))} gap" : "Joins them";
            if (selectedRows.Select(s => s.Account).Distinct(StringComparer.Ordinal).Count() > 1) return "Same account only";
            if (selectedRows.Select(s => s.Day).Distinct().Count() > 1) return "Same day only";
            return "Time in between: can’t merge";
        }
    }
    public void ToggleSelection(IReadOnlyList<SessionRow> rows)
    {
        if (rows.Count == 0 || rows.Any(s => !Engine.Sessions.Any(r => ReferenceEquals(r, s)))) return;
        var remove = IsSelected(rows); SelectedMeetingId = null;
        foreach (var row in rows) { if (remove) selectedRows.Remove(row); else selectedRows.Add(row); }
        EditError = null; Notice = null; Notify();
    }
    /// <summary>
    /// A click on a block. A plain click selects just that block, and clicking the only selected block again clears the selection; with Shift
    /// (<paramref name="additive"/>) the block is added to the selection or taken out of it, which is how two blocks are picked to merge.
    /// </summary>
    public void SelectBlock(IReadOnlyList<SessionRow> rows, bool additive)
    {
        if (rows.Count == 0 || rows.Any(s => !Engine.Sessions.Any(r => ReferenceEquals(r, s)))) return;
        if (additive) { ToggleSelection(rows); return; }
        var onlyThis = SelectedBlockCount == 1 && IsSelected(rows);
        DropSelection();
        if (!onlyThis) foreach (var row in rows) selectedRows.Add(row);
        EditError = null; Notice = null; Notify();
    }
    /// <summary>A block as the Selected block card shows it: one entry, or a merged group of entries from its first start to its last end.</summary>
    public sealed record SelectedBlock(string Account, DateTime Start, DateTime End, decimal Minutes, string Note, SessionRow? Row, ReviewEntry? Group)
    {
        public bool IsGroup => Group is not null;
        public IReadOnlyList<SessionRow> Rows => Group?.Sources ?? new[] { Row! };
    }
    /// <summary>The selected blocks in time order. Entries of a merged group count as the one block.</summary>
    public IReadOnlyList<SelectedBlock> SelectedBlocks
    {
        get
        {
            if (selectedRows.Count == 0) return Array.Empty<SelectedBlock>();
            var week = WeekStart(SelectedDate);
            var blocks = new List<SelectedBlock>();
            foreach (var entry in Editor.Entries(week, week.AddDays(7)).Where(e => e.Sources.Count > 0 && e.Sources.All(selectedRows.Contains)))
            {
                var timed = entry.Sources.Where(r => r.Duration > TimeSpan.Zero).ToArray(); if (timed.Length == 0) continue;
                blocks.Add(entry.GroupId is null ? new(entry.Account, timed[0].From, timed[0].To, entry.Minutes, entry.Note, entry.Sources[0], null)
                    : new(entry.Account, timed.Min(r => r.From), timed.Max(r => r.To), entry.Minutes, entry.Note, null, entry));
            }
            return blocks.OrderBy(b => b.Start).ToArray();
        }
    }
    public void ClearSelection() { if (selectedRows.Count == 0 && SelectedMeetingId is null) return; DropSelection(); Notify(); }
    /// <summary>The meeting the Details card shows, when a meeting (and not a block) is selected. Selecting a block, another day or nothing lets it go.</summary>
    public long? SelectedMeetingId { get; private set; }
    private void DropSelection() { selectedRows.Clear(); SelectedMeetingId = null; }
    /// <summary>A click on a meeting selects it (and a click on the only selected meeting clears it again, as for a block); the Notes card turns to Meetings so the list shows it too.</summary>
    public void SelectMeeting(long id)
    {
        if (Notes.Find(id) is not { } meeting || !NotesModel.IsMeeting(meeting)) return;
        var again = SelectedMeetingId == id;
        DropSelection(); if (!again) { SelectedMeetingId = id; NotesCardShowsMeetings = true; }
        EditError = null; Notice = null; Notify();
    }
    // After a quick slip from one account to another and back, the earlier block can be picked up again. Blocks in a group are left alone, since
    // taking one out of a group would break the group.
    private bool InGroup(SessionRow row) => Editor.Entries(row.Day, row.Day.AddDays(1)).Any(e => e.GroupId is not null && e.Sources.Contains(row));
    private (SessionRow Earlier, SessionRow Between)? ContinuableNow()
    {
        if (Engine.ContinuableBlocks() is not { } blocks) return null;
        return InGroup(blocks.Earlier) || InGroup(blocks.Between) ? null : blocks;
    }
    public bool CanContinuePrevious => ContinuableNow() is not null;
    /// <summary>What the option says, such as the account and the time its earlier block began, or null when it is not on offer.</summary>
    public string? ContinuePreviousText => ContinuableNow() is { } blocks ? $"Continue {Label(blocks.Earlier.Account)}" : null;
    /// <summary>What the offer says beside its button: the slip, as the account it was and its length.</summary>
    public string? ContinuePreviousNote => ContinuableNow() is { } blocks ? $"Switched · {FormatShort(blocks.Between.Duration)} on {Label(blocks.Between.Account)}" : null;
    /// <summary>Takes back a switch made by mistake. The short block between is dropped and the earlier block carries on as one, including the time of the slip.</summary>
    public bool ContinuePrevious()
    {
        if (ContinuableNow() is null) { EditError = "Nothing to take back"; Notify(); return false; }
        if (!Save()) { EditError = LastError; Notify(); return false; }
        var removed = Engine.ContinuePrevious();
        if (removed is { } blocks) Editor.DropNotes(new[] { (blocks.Between.Account, blocks.Between.Date, blocks.Between.Start) });
        DropSelection();
        var saved = Save();
        Notice = removed is { } done ? $"Carrying on {Label(done.Earlier.Account)} from {done.Earlier.From:HH:mm}" : null;
        Notify(); return saved;
    }
    // Copied blocks stay inside the app, not on the system clipboard, so they can carry their credited minutes and notes exactly.
    private IReadOnlyList<LedgerEditor.ClipBlock> copied = Array.Empty<LedgerEditor.ClipBlock>();
    public bool CanPaste => copied.Count > 0;
    /// <summary>Where Ctrl+V pastes. The timeline keeps it at the time under the pointer, and it is null when the pointer has not been over it.</summary>
    public DateTime? PasteTime { get; set; }
    /// <summary>Copies the blocks, or the selection when the clicked block is part of it. Blocks from several days cannot be copied together.</summary>
    public bool CopyBlocks(IReadOnlyList<SessionRow> rows)
    {
        var chosen = Engine.Sessions.Where(s => rows.Any(r => ReferenceEquals(r, s)) && s.Duration > TimeSpan.Zero).OrderBy(s => s.From).ToList();
        if (chosen.Count == 0) return false;
        if (chosen.Select(s => s.Day).Distinct().Count() > 1) { EditError = "One day at a time"; Notify(); return false; }
        var first = chosen[0].From;
        copied = chosen.Select(s => new LedgerEditor.ClipBlock(s.Account, s.From - first, s.Minutes, Editor.Note(s))).ToList();
        EditError = null; Notice = copied.Count == 1 ? "1 block copied" : $"{copied.Count} blocks copied";
        Notify(); return true;
    }
    public bool CopySelection() => CopyBlocks(Engine.Sessions.Where(selectedRows.Contains).ToList());
    /// <summary>Pastes the copied blocks with the first at this time on the day shown. One undo step, and the time is credited like Add time.</summary>
    public bool PasteBlocks(DateTime at)
    {
        if (!CanPaste) { EditError = "Copy blocks first"; Notify(); return false; }
        var count = copied.Count;
        var done = ChangeLog(() => { foreach (var block in copied) ValidateAccount(block.Account); Editor.Paste(copied, at); });
        if (done) { Notice = count == 1 ? "1 block pasted" : $"{count} blocks pasted"; Notify(); }
        return done;
    }
    /// <summary>The time Ctrl+V uses on the day shown, which is the pointer's place on the timeline, or straight after the day's last block.</summary>
    public DateTime DefaultPasteTime()
    {
        if (PasteTime is { } at && at.Date == SelectedDate.Date) return at;
        var day = SelectedDate.Date; var last = Engine.Sessions.Where(s => s.Day == day && s.Duration > TimeSpan.Zero).Select(s => s.To).DefaultIfEmpty().Max();
        return last > day ? last : Engine.Plan(day).Working ? Engine.Plan(day).Start : day.AddHours(9);
    }
    public bool MergeSelection()
    {
        if (!CanMergeSelection) { EditError = MergeHint; Notify(); return false; }
        var rows = Engine.Sessions.Where(selectedRows.Contains).ToArray();
        var gap = Editor.MergeGapMinutes(rows);
        var done = ChangeLog(() => { Editor.Group(rows); DropSelection(); });
        if (done) SayDone(gap > 0 ? $"Merged · {Format(TimeSpan.FromMinutes((double)gap))} gap added" : "Merged");
        return done;
    }
    private StoreSettings preferences;
    private readonly TimerStore? store;
    private int ticks;
    public TimerModel(TrackingEngine? engine = null, StoreSettings? preferences = null, TimerStore? store = null)
    {
        Engine = engine ?? new();
        this.preferences = preferences ?? new(new() { new("Account one"), new("Account two"), new("Internal") }, false, ThemeLibrary.Default, DayOfWeek.Monday, Shadows: false);
        this.store = store;
        Editor = store?.Editor ?? new LedgerEditor(Engine);
        Notes = new NotesModel(store, () => Engine.Now, () => ReviewAccounts.Select(a => a.Name).ToList());
        Notes.Changed += Notify;
        Engine.CanReopen = row => !InGroup(row); // A block in a group is never taken out of it to be carried on.
        this.preferences = this.preferences with { Accounts = WithColours(this.preferences.Accounts) };
        // A new timer starts with window shadows off (the choice stays in Settings). The shared package defaults them on, so the first run turns them off once and marks it,
        // which leaves an existing timer, and a choice made since, alone.
        if (store is not null && !this.preferences.SetupDone && this.preferences.Extras?.AppearanceLines.Contains(ShadowsDefaultLine) != true)
        {
            this.preferences = this.preferences with { Shadows = false }; SetAppearanceFlag("shadowsdefault=", "applied", true); Save();
        }
        SelectedAccount = Engine.Active?.Account ?? this.preferences.Accounts.FirstOrDefault(a => !a.Hidden)?.Name;
        SelectedDate = Engine.Now.Date; lastToday = SelectedDate;
        // Only Light and Dark are offered now, so a saved choice of a retired theme or one of the user's own becomes Light or Dark (the theme stays in the file).
        this.preferences = this.preferences with { Theme = AppThemes.Resolve(this.preferences.Theme, this.preferences.Themes) };
        ApplyTheme(); Ui.ApplyTooltips(this.preferences.Tooltips);
        // When the computer switches between light and dark, System follows it.
        if (Application.Current?.PlatformSettings is { } platform)
            platform.ColorValuesChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { appliedVariant = null; Notify(); });
    }
    // Sixteen soft colours, so up to sixteen accounts each have their own before any repeat, none matching the break. The first eight are
    // the original palette and keep their order, so existing accounts are unchanged. They all take dark text.
    public static readonly string[] Palette =
    {
        "#91B7D5", "#94BF9B", "#D9A18A", "#BAA4CE", "#C9D17F", "#86C5C0", "#D79BB5", "#C4A38F",
        "#E58F8F", "#E6A86B", "#F0D060", "#8F94E6", "#C08FE0", "#7FD18F", "#B8BDB0", "#E87FA0"
    };
    // An account keeps its colour for good: it is given the first colour no other account is using, once, and saved with it.
    // Reordering, hiding or renaming an account therefore never changes how its blocks look on the timeline.
    private static List<AccountSettings> WithColours(IEnumerable<AccountSettings> accounts)
    {
        var list = accounts.ToList();
        foreach (var index in Enumerable.Range(0, list.Count).Where(i => list[i].Colour is null).ToList())
        {
            var used = list.Where(a => a.Colour is not null).Select(a => a.Colour!).ToList();
            var colour = Palette.FirstOrDefault(c => !used.Contains(c, StringComparer.OrdinalIgnoreCase))
                ?? Palette.OrderBy(c => used.Count(u => u.Equals(c, StringComparison.OrdinalIgnoreCase))).First();
            list[index] = list[index] with { Colour = colour };
        }
        return list;
    }
    public IReadOnlyList<AccountSettings> AccountSettings => preferences.Accounts.AsReadOnly();
    private TimerAccount[] Catalogue()
    {
        return preferences.Accounts.Select(a => new TimerAccount(a.Name, a.Colour ?? Palette[0],
            a.BudgetHours is { } hours ? TimeSpan.FromHours(hours) : null, a.BudgetPeriod)).Append(new(BreakAccount.Name, "#D6C48C", null)).ToArray();
    }
    public IReadOnlyList<TimerAccount> ReviewAccounts => Catalogue().Concat(Engine.Sessions.Select(s => s.Account).Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(name => !Catalogue().Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).Select(name => new TimerAccount(name, "#91B7D5", null))).ToArray();
    public IReadOnlyList<TimerAccount> Accounts => Catalogue().Where(a => !preferences.Accounts.Any(p => p.Name == a.Name && p.Hidden)).ToArray();
    public string? SelectedAccount { get; private set; }
    public string? ActiveAccount => Engine.Active?.Account;
    public string AccountLabel => IsOnBreak ? "On break" : ActiveAccount ?? "Ready to track";
    public string Label(string account) => BreakAccount.Display(account);
    public string Status => IsOnBreak ? "On break" : IsTracking ? "Tracking" : "Ready to track";
    public bool IsTracking => Engine.Active is not null;
    /// <summary>
    /// The span of clock time a day's working time covers, or null when nothing was tracked on it. It starts at the first block of the day, recorded or added,
    /// and not at the scheduled start, so a day begins when work does and there is no Start day to press. It runs to now while now is inside the working day
    /// (the scheduled hours, or the last recorded activity if that is later), and to the end of that stretch once it has passed, the way the engine treats a past
    /// day. An older timer's finished day (Finish day is gone, but the engine still holds the stretches it made) ends where it was finished, or at its last block.
    /// </summary>
    public (DateTime From, DateTime Until)? DaySpan(DateTime day)
    {
        day = day.Date; var next = day.AddDays(1); var now = Engine.Now;
        var blocks = Engine.Sessions.Where(s => s.Day == day && !BreakAccount.Is(s.Account) && s.Duration > TimeSpan.Zero).ToList();
        DateTime? first = blocks.Count > 0 ? blocks.Min(s => s.From) : null; DateTime? last = blocks.Count > 0 ? blocks.Max(s => s.To) : null;
        if (Engine.Active is { } active && !BreakAccount.Is(active.Account) && now > day && active.Start < next)
        {
            var from = active.Start > day ? active.Start : day; var until = now < next ? now : next;
            first = first is { } f && f < from ? f : from; last = last is { } l && l > until ? l : until;
        }
        if (first is null || last is null) return null;
        var plan = Engine.Plan(day); var limit = plan.Working && plan.End > last.Value ? plan.End : last.Value;
        var finished = Engine.Away.Where(a => a.Start.Date == day).Select(a => (DateTime?)a.Start).Max();
        if (finished is { } stopped) limit = stopped > last.Value ? stopped : last.Value;
        var cap = new[] { now, limit, next }.Min();
        return cap > first.Value ? (first.Value, cap) : null;
    }
    /// <summary>
    /// The time worked on a day: the clock time of <see cref="DaySpan"/> less the breaks in it. The app has no overtime and no scheduled start or end of its own to
    /// measure against, so time past the day's hours is simply working time and the schedule is only what the day is compared with.
    /// </summary>
    public TimeSpan WorkingOn(DateTime day)
    {
        if (DaySpan(day) is not { } span) return TimeSpan.Zero;
        var now = Engine.Now; var breaks = Engine.Sessions.Where(s => s.Day == day.Date && BreakAccount.Is(s.Account) && s.Duration > TimeSpan.Zero).Select(s => (s.From, s.To)).ToList();
        if (Engine.Active is { } running && BreakAccount.Is(running.Account)) breaks.Add((running.Start, now));
        // Overlapping breaks are joined first, so one is never taken off twice.
        var gone = TimeSpan.Zero; var end = span.From;
        foreach (var (from, to) in breaks.Select(b => (From: b.From < span.From ? span.From : b.From, To: b.To > span.Until ? span.Until : b.To)).Where(b => b.To > b.From).OrderBy(b => b.From))
        {
            var start = from < end ? end : from;
            if (to > start) gone += to - start;
            if (to > end) end = to;
        }
        var worked = span.Until - span.From - gone;
        return worked < TimeSpan.Zero ? TimeSpan.Zero : worked;
    }
    /// <summary>Working time with nothing recorded on an account on a day: the working time less what is on accounts, never below nothing.</summary>
    public TimeSpan UnallocatedOn(DateTime day) { var left = WorkingOn(day) - Engine.Totals(day).Allocated; return left < TimeSpan.Zero ? TimeSpan.Zero : left; }
    public bool DecimalHours => preferences.DecimalHours;
    /// <summary>Where the pointer last was on the timeline's canvas, so the guide line can be put back when the timeline is drawn again under a pointer that has not moved.</summary>
    public Avalonia.Point? GuidePointer { get; set; }
    public DateTime SelectedDate { get; private set; }
    public string Theme => preferences.Theme;
    public string? LastError { get; private set; }
    /// <summary>A one-off message for the footer, such as the note that old timesheet files are not used.</summary>
    private string? notice;
    public string? Notice { get => notice; set { notice = value; NoticeHasUndo = false; } }
    /// <summary>Whether the note on show is for an edit that Undo takes back, so the message offers an Undo button.</summary>
    public bool NoticeHasUndo { get; private set; }
    /// <summary>Says that something was done, with an Undo beside the words.</summary>
    private void SayDone(string text) { Notice = text; NoticeHasUndo = true; Notify(); }
    public bool IsDemo { get; private set; }
    public bool Persistent => store is not null;
    /// <summary>The day clock: working time so far today, counting up from the first block of the day.</summary>
    public TimeSpan Elapsed => WorkingOn(Engine.Now);
    public string ClockText => $"{(int)Elapsed.TotalHours:00}:{Elapsed.Minutes:00}:{Elapsed.Seconds:00}";
    public TimeSpan Allocated => Engine.Totals(Engine.Now).Allocated;
    public TimeSpan Unallocated => UnallocatedOn(Engine.Now);
    /// <summary>The time on breaks on a day, kept apart from working time, including a break that is running now.</summary>
    public TimeSpan BreakTimeOn(DateTime day)
    {
        day = day.Date;
        var saved = Engine.Sessions.Where(s => s.Day == day && BreakAccount.Is(s.Account)).Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        // The two readings of the running block are taken a moment apart, so with no break running they can differ by a few milliseconds the wrong way,
        // which showed as -00:00. Time on a break is never below nothing.
        var running = Engine.ActiveElapsedOn(day, includeBreak: true) - Engine.ActiveElapsedOn(day);
        return saved + (running > TimeSpan.Zero ? running : TimeSpan.Zero);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void ToggleAccount(string name)
    {
        if (!Accounts.Any(a => a.Name == name)) throw new ArgumentException("Unknown account", nameof(name));
        if (BreakAccount.Is(name)) { ToggleBreak(); return; } // A break is never the selected account, so it can always hand back.
        SelectedAccount = name; Engine.ToggleAccount(name); NoteContinuedBlock(); Save(); Notify();
    }
    // Tells the user when pressing play carried on a block that was already there, since no new block appears.
    private void NoteContinuedBlock()
    {
        if (Engine.ContinuedBlock is { } block) { DropSelection(); Notice = $"Carrying on {Label(block.Account)} from {block.From:HH:mm}"; }
    }
    // The block being recorded has no ledger row yet, so it cannot be dragged or edited until tracking stops.
    /// <summary>Says that something was done, in the message bar, with no Undo (notes are outside the undo history).</summary>
    public void Tell(string text) { Notice = text; Notify(); }
    /// <summary>Shows a refused edit in the message bar, such as a time that was typed wrongly in the Selected block card.</summary>
    public void Refuse(string message) { EditError = message; Notice = null; Notify(); }
    public void ExplainRunning() { EditError = "Still recording · stop to edit"; Notify(); }
    public void TogglePause()
    {
        // On a break, resume ends the break and goes back to the account that was running before it.
        if (IsOnBreak) { EndBreak(); return; }
        if (Engine.Active is { } active) { SelectedAccount = active.Account; Engine.StopAccount(); }
        else if (SelectedAccount is { } name && Accounts.Any(a => a.Name == name)) { Engine.ToggleAccount(name); NoteContinuedBlock(); }
        Save(); Notify();
    }
    // The account to go back to after a break: the one that stopped at the moment the break began. It is found from the
    // recorded time, so it still works after the timer has been closed and reopened in the middle of a break.
    public string? ResumeAccount
    {
        get
        {
            if (!IsOnBreak || Engine.Active is not { } active) return null;
            var before = Engine.Sessions.Where(s => !BreakAccount.Is(s.Account) && Math.Abs((s.To - active.Start).TotalSeconds) <= 2)
                .OrderByDescending(s => s.To).FirstOrDefault()?.Account ?? Engine.ResumeAfterBreak;
            return before is { } name && Accounts.Any(a => a.Name == name && !BreakAccount.Is(a.Name)) ? name : null;
        }
    }
    public void ToggleBreak()
    {
        if (IsOnBreak) { EndBreak(); return; }
        Engine.ToggleAccount(BreakAccount.Name); NoteContinuedBlock();
        Save(); Notify();
    }
    private void EndBreak()
    {
        var back = ResumeAccount;
        if (back is not null) { SelectedAccount = back; Engine.ToggleAccount(back); }
        else Engine.StopAccount();
        Save(); Notify();
    }
    /// <summary>Applies the account list. <paramref name="renames"/> maps an account's old name to its new one (the list holds the new
    /// names). A rename always applies everywhere, so all recorded time, notes, groups and positions move to the new name after a backup
    /// copy is saved. To start afresh, add a new account instead.</summary>
    public void SetAccounts(IEnumerable<AccountSettings> accounts, IReadOnlyDictionary<string, string>? renames = null, IReadOnlyCollection<string>? deleted = null)
    {
        renames ??= new Dictionary<string, string>(); deleted ??= Array.Empty<string>();
        var values = accounts.ToList();
        if (values.Count == 0 || values.Any(a => string.IsNullOrWhiteSpace(a.Name) || a.Name != a.Name.Trim()
            || a.Name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || (a.Name.Equals(BreakAccount.Name, StringComparison.OrdinalIgnoreCase) || a.Name.Equals(BreakAccount.Label, StringComparison.OrdinalIgnoreCase))
            || a.BudgetPeriod is not ("day" or "week" or "month" or "quarter")
            || (a.BudgetHours is { } h && (!double.IsFinite(h) || h <= 0)))
            || values.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new ArgumentException("Use unique account names and positive budgets; Break is reserved.");
        // An account can leave the list only when it is deleted on purpose, which also removes its history. Otherwise it has to be hidden.
        if (preferences.Accounts.Any(a => !deleted.Contains(a.Name) && !values.Any(v => v.Name == (renames.TryGetValue(a.Name, out var to) ? to : a.Name))))
            throw new ArgumentException("Hide existing accounts instead of removing them.");
        if (deleted.Any(d => !preferences.Accounts.Any(a => a.Name == d) || values.Any(v => v.Name.Equals(d, StringComparison.OrdinalIgnoreCase)) || renames.ContainsKey(d)))
            throw new ArgumentException("Choose accounts to delete from the existing list, and do not keep or rename them as well.");
        var renamed = renames.Where(r => !r.Value.Equals(r.Key, StringComparison.Ordinal)).ToList();
        foreach (var (from, to) in renamed)
        {
            if (!preferences.Accounts.Any(a => a.Name == from)) throw new ArgumentException("Unknown account.");
            // A name that already appears anywhere would silently join two histories, so it is refused.
            // Changing only the capitals of the same account is fine.
            if (!to.Equals(from, StringComparison.OrdinalIgnoreCase) && (ReviewAccounts.Any(a => a.Name.Equals(to, StringComparison.OrdinalIgnoreCase))
                || renamed.Any(r => r.Key != from && r.Value.Equals(to, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException($"\"{to}\" is already used by another account or in the history.");
        }
        if (ActiveAccount is { } active && (values.Any(a => a.Name == active && a.Hidden) || renamed.Any(r => r.Key == active) || deleted.Contains(active))) Engine.StopAccount();
        if (deleted.Count > 0)
        {
            // Save first so the backup holds everything about to go, then remove each account's entries, notes and groups.
            if (store is not null && deleted.Any(d => AccountHistory(d).Entries > 0)) { store.Save(Engine, preferences); store.Backup("before-delete"); }
            foreach (var name in deleted) Editor.DeleteAccount(name);
        }
        if (renamed.Count > 0 || deleted.Count > 0) Notes.Relink(preferences.Accounts.Select(a => a.Name), renames, deleted);
        if (renamed.Count > 0)
        {
            // Save first so the backup holds the latest entries, then rewrite everything in one go.
            if (store is not null) { store.Save(Engine, preferences); store.Backup("before-rename"); }
            foreach (var (from, to) in renamed) Editor.RenameAccount(from, to);
        }
        preferences = preferences with { Accounts = WithColours(values) };
        if (renamed.Count > 0 && SelectedAccount is { } picked && renames.TryGetValue(picked, out var now)) SelectedAccount = now;
        if (!Accounts.Any(a => a.Name == SelectedAccount)) SelectedAccount = values.FirstOrDefault(a => !a.Hidden)?.Name;
        Save(); Notify();
    }
    /// <summary>How much recorded time an account has, so deleting one can say what will go.</summary>
    public (int Entries, TimeSpan Time) AccountHistory(string account)
    {
        var rows = Engine.Sessions.Where(s => s.Account.Equals(account, StringComparison.OrdinalIgnoreCase)).ToList();
        return (rows.Count, rows.Aggregate(TimeSpan.Zero, (sum, row) => sum + row.Duration));
    }
    public DateTime WeekStart(DateTime date) => date.Date.AddDays(-((7 + (int)date.DayOfWeek - (int)preferences.WeekStart) % 7));
    public TimeSpan Today(TimerAccount a) => Engine.Credited(a.Name, Engine.Now.Date, Engine.Now.Date.AddDays(1));
    public TimeSpan OnDay(TimerAccount a, DateTime day) => Engine.Credited(a.Name, day, day.AddDays(1));
    /// <summary>The span a budget covers right now, from the account's own period (today, this week, month or quarter).</summary>
    private (DateTime Start, DateTime End) BudgetWindow(TimerAccount a) => BudgetWindowAt(a, Engine.Now);
    /// <summary>The span the budget covers around a reference moment: the day, week, month or quarter that holds it.</summary>
    public (DateTime Start, DateTime End) BudgetWindowAt(TimerAccount a, DateTime reference)
    {
        var now = reference;
        var start = a.Period switch
        {
            "day" => now.Date,
            "month" => new DateTime(now.Year, now.Month, 1),
            "quarter" => new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1), _ => WeekStart(now)
        };
        var end = a.Period switch { "day" => start.AddDays(1), "month" => start.AddMonths(1), "quarter" => start.AddMonths(3), _ => start.AddDays(7) };
        return (start, end);
    }
    public TimeSpan BudgetUsed(TimerAccount a) => BudgetUsedAt(a, Engine.Now);
    /// <summary>What has been used in the budget period that holds the reference moment, which for an earlier week is that week's own period.</summary>
    public TimeSpan BudgetUsedAt(TimerAccount a, DateTime reference) { var (start, end) = BudgetWindowAt(a, reference); return Engine.Credited(a.Name, start, end); }
    /// <summary>
    /// The moment that decides which budget period a week view speaks of. For the current week it is now. For an earlier week it is the middle of the week
    /// (the Thursday), so a week that straddles two months is placed with the month that holds most of its days.
    /// </summary>
    public DateTime BudgetReference(DateTime weekStart)
    {
        var now = Engine.Now; weekStart = weekStart.Date;
        return now.Date >= weekStart && now.Date < weekStart.AddDays(7) ? now : weekStart.AddDays(3);
    }
    /// <summary>
    /// Names the budget period that holds the reference moment: "this month" when it is the current one, and otherwise which one it was, such as "September 2026",
    /// so a figure seen while browsing an earlier week is never mistaken for the present.
    /// </summary>
    public string BudgetPeriodNameAt(TimerAccount a, DateTime reference)
    {
        var british = CultureInfo.GetCultureInfo("en-GB"); var (start, end) = BudgetWindowAt(a, reference); var now = Engine.Now;
        var current = now >= start && now < end;
        return a.Period switch
        {
            "day" => current ? "today" : start.ToString("ddd d MMM", british),
            "month" => current ? "this month" : start.ToString("MMMM yyyy", british),
            "quarter" => current ? "this quarter" : $"Q{(start.Month - 1) / 3 + 1} {start.Year}",
            _ => current ? "this week" : "week of " + start.ToString("d MMM", british)
        };
    }
    /// <summary>How much of the budget is used, where 1 is all of it. Null when the account has no budget.</summary>
    public double? BudgetRatio(TimerAccount a) => a.Budget is { } target && target > TimeSpan.Zero ? BudgetUsed(a).TotalSeconds / target.TotalSeconds : null;
    /// <summary>How far through the budget's period it is now, from 0 to 1. The bar's tick sits here, so a bar past the tick is running ahead of pace.</summary>
    public double BudgetPace(TimerAccount a)
    {
        // A daily budget is spread over the working day, so the line follows that and not the clock from midnight. A day off has nothing to pace.
        if (a.Period == "day") return DayProgress ?? 0;
        var (start, end) = BudgetWindow(a);
        return Math.Clamp((Engine.Now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
    }
    public string Format(TimeSpan value) => DecimalHours ? value.TotalHours.ToString("0.00", CultureInfo.GetCultureInfo("en-GB")) + "h" : Hhmm(value);
    /// <summary>Every duration reads as HH:MM (07:00), or in decimal hours (7.00) when that is chosen. The three formats differ only in how decimal hours are written.</summary>
    public string FormatShort(TimeSpan value) => DecimalHours ? value.TotalHours.ToString("0.00", CultureInfo.GetCultureInfo("en-GB")) : Hhmm(value);
    /// <summary>For tight rows. HH:MM like the rest, or 10.58h in decimal hours.</summary>
    public string FormatCompact(TimeSpan value) => DecimalHours ? value.TotalHours.ToString("0.##", CultureInfo.GetCultureInfo("en-GB")) + "h" : Hhmm(value);
    private static string Hhmm(TimeSpan value) => $"{(value < TimeSpan.Zero ? "-" : "")}{(int)value.Duration().TotalHours:00}:{value.Duration().Minutes:00}";
    /// <summary>The budget for an account row, such as "10h35 / 20h monthly". The period is always said, since it matters when comparing accounts.</summary>
    public string BudgetLine(TimerAccount a) => a.Budget is { } target
        ? $"{FormatCompact(BudgetUsed(a))} / {FormatCompact(target)} {(a.Period switch { "day" => "daily", "month" => "monthly", "quarter" => "quarterly", _ => "weekly" })}" : "No budget";
    /// <summary>What has been used against the budget, such as "8h 45m / 15h", for the dashboard's account rows, or "No budget".</summary>
    public string BudgetUsage(TimerAccount a) => a.Budget is { } target ? $"{FormatCompact(BudgetUsed(a))} / {FormatCompact(target)}" : "No budget";
    /// <summary>The period the budget covers, in words that sit beside the usage, or an empty string when there is no budget.</summary>
    public string BudgetPeriodLabel(TimerAccount a) => a.Budget is null ? "" : a.Period switch { "day" => "Today", "month" => "This month", "quarter" => "This quarter", _ => "This week" };
    /// <summary>The detail shown on hover instead of a percentage on the row: how much is used, and what the pace line means.</summary>
    public string BudgetDetail(TimerAccount a)
    {
        if (BudgetRatio(a) is not { } ratio) return "No budget";
        var period = a.Period is "day" or "month" or "quarter" ? (a.Period == "day" ? "working day" : a.Period) : "week";
        return $"{Math.Round(ratio * 100)}% used{(ratio >= 1 ? " · over budget" : "")}\nLine: how far through the {period}";
    }
    /// <summary>What the narrow account row says on hover: today's time is on the row, so this is the budget used against its target and the period, then how it is going.</summary>
    public string BudgetRowTip(TimerAccount a)
        => a.Budget is null ? a.Display + "\nNo budget" : $"{a.Display}\n{BudgetUsage(a)} · {BudgetPeriodLabel(a).ToLowerInvariant()}\n{BudgetDetail(a)}";
    public string BudgetSummary(TimerAccount a) => a.Budget is { } target ? $"{Format(BudgetUsed(a))} / {Format(target)}" : "No budget";
    public void ToggleFormat() { preferences = preferences with { DecimalHours = !DecimalHours }; Save(); Notify(); }
    /// <summary>Puts the timeline and week views back on the current day. The dashboard does this whenever it is opened, so it never comes up on a day left over from earlier.</summary>
    public void ShowToday()
    {
        if (SelectedDate.Date == Engine.Now.Date) return;
        DropSelection(); TimelineOffset = null; SelectedDate = Engine.Now.Date; Notify();
    }
    // A timer left running past midnight carries on following the current day, if that is the day being shown. Someone reviewing an earlier day is left there.
    private DateTime lastToday;
    private void FollowToday()
    {
        var today = Engine.Now.Date; if (today == lastToday) return;
        var following = SelectedDate.Date == lastToday; lastToday = today;
        if (following) { DropSelection(); TimelineOffset = null; SelectedDate = today; }
        Notify();
    }
    /// <summary>Shows any day, for the date picker in the header. On the week views it will not go past the current week, as the arrows will not.</summary>
    public void GoToDate(DateTime day, bool weeks = false)
    {
        day = day.Date; if (day == SelectedDate.Date) return;
        if (weeks && WeekStart(day) > WeekStart(Engine.Now)) return;
        DropSelection(); TimelineOffset = null; SelectedDate = day; Notify();
    }
    public void ChangeDate(int days) { DropSelection(); TimelineOffset = null; SelectedDate = SelectedDate.AddDays(days); Notify(); }
    /// <summary>Steps the Week tab by whole weeks. It never goes past the current week, since there is nothing to review there.</summary>
    public void ChangeWeek(int weeks)
    {
        var target = WeekStart(SelectedDate).AddDays(7 * weeks);
        if (target > WeekStart(Engine.Now)) return;
        DropSelection(); TimelineOffset = null; SelectedDate = target; Notify();
    }
    /// <summary>Whether the Week overview shows its table in place of the chart. Kept for the session only.</summary>
    public bool WeekAsTable { get; private set; }
    public void SetWeekAsTable(bool table) { if (WeekAsTable == table) return; WeekAsTable = table; Notify(); }
    /// <summary>Whether the timeline's Notes card lists the day's meetings instead of its notes. Kept for the session, as the week's Chart and Table are.</summary>
    public bool NotesCardShowsMeetings { get; private set; }
    public void SetNotesCardShowsMeetings(bool meetings)
    {
        if (NotesCardShowsMeetings == meetings) return;
        NotesCardShowsMeetings = meetings; Notify();
    }
    /// <summary>Asked for by a day in the week chart: the dashboard shows that day on the Timeline page.</summary>
    public event Action<DateTime>? OpenDayRequested;
    public void OpenDayInTimeline(DateTime day) { GoToDate(day); OpenDayRequested?.Invoke(day.Date); }
    public bool CanStepForwardAWeek => WeekStart(SelectedDate) < WeekStart(Engine.Now);
    /// <summary>Adds or removes minutes on an account for a day, as one undoable edit.</summary>
    /// <summary>Moves entries to another account as one undoable edit. Every selected block moves when the one clicked is selected.</summary>
    public bool MoveEntries(IReadOnlyList<SessionRow> rows, string account)
        => ChangeLog(() => KeepingSelection(rows, () => { ValidateAccount(account); Editor.Reassign(rows, account); }));
    /// <summary>The colour for an account's slice in a chart or its legend dot. A dark theme tints its blocks down into the surface, which makes thin slices vanish, so charts there use the account's own colour (the block's edge).</summary>
    public string ChartColour(string accountColour) => AccountTones.Chart(Ui.Visible(accountColour), CurrentTheme.Dark);
    /// <summary>The fill, left edge and text colour of a timeline block for an account colour, by the selected theme's block rules.</summary>
    /// <summary>How a block of this account is drawn: a livelier, deeper shade of its colour that holds white words (or a bright one with dark words for a pale yellow), and a deeper edge.</summary>
    public ThemeDerivation.BlockLook BlockLook(string accountColour)
    {
        var (fill, ink) = AccountTones.Vivid(Ui.Visible(accountColour));
        return new ThemeDerivation.BlockLook(fill, ThemeColours.Mix(fill, "#000000", .2), ink);
    }
    public void SetTheme(string theme)
    {
        if (theme != ThemeLibrary.System && theme != AppThemes.Sun && Themes.All(t => t.Name != theme)) throw new ArgumentException("Unknown theme", nameof(theme));
        preferences = preferences with { Theme = theme }; ApplyTheme(); Save(); Notify();
    }
    /// <summary>
    /// Builds the sample-data demo. Given a store, the demo runs through the real database (a throwaway one the caller deletes),
    /// so saving, editing and undo behave as they do for real data. Like a new database it holds no merged legacy credit.
    /// </summary>
    public static TimerModel CreateDemo(Func<DateTime>? clock = null, TimerStore? store = null)
    {
        var realClock = clock ?? (() => DateTime.Now);
        var wallStart = realClock();
        var anchor = wallStart.Date.AddHours(14).AddMinutes(15);
        var engine = new TrackingEngine(clock: () => anchor + (realClock() - wallStart));
        store?.Load(engine);
        engine.Schedule.Days.Add(anchor.DayOfWeek);
        // Seven accounts with a mix of budgets, so lists, rings and meters are tried at a realistic size: week, month and quarter
        // budgets, two with none, and the weekly ones chosen so one is comfortable, one nearly used and one over.
        var prefs = new StoreSettings(new()
        {
            new("Account one", 15), new("Account two", 4.5), new("Account three", 2.5), new("Account four", 20, "month"),
            new("Account five", 40, "quarter"), new("Account six"), new("Internal"),
            // A budget per day, and an account hidden from the tracker whose time still shows in every report.
            new("Account seven", 3, "day"), new("Archive", null, "week", Hidden: true)
        }, false, ThemeLibrary.Default, DayOfWeek.Monday, Shadows: false);
        var start = anchor.Date.AddHours(9);
        // Today runs from 09:00 to the demo's 14:15, with a break in the middle and no gaps worth tidying except a few minutes.
        var today = new (string Account, int Minutes)[]
        {
            ("Account one", 50), ("Account one", 55), ("Account two", 60), ("Account four", 35), (BreakAccount.Name, 30),
            ("Account three", 40), ("Account five", 30), ("Account six", 12)
        };
        for (var i = 0; i < today.Length; i++)
        {
            engine.AddSession(today[i].Account, start, start.AddMinutes(today[i].Minutes));
            start = start.AddMinutes(today[i].Minutes + (i == 0 ? 3 : 0));
        }
        var monday = anchor.Date.AddDays(-((7 + (int)anchor.DayOfWeek - 1) % 7));
        if (monday < anchor.Date) engine.AddSession("Account one", monday.AddHours(9), monday.AddHours(16));
        // The days before today, so weekly, monthly and quarterly budgets and the week view have something to show.
        var earlier = new (string Account, int Minutes)[]
        {
            ("Account two", 60), ("Account three", 45), ("Account four", 120), ("Account five", 90), ("Account six", 30), ("Internal", 60)
        };
        for (var back = 1; back <= 6; back++)
        {
            var day = anchor.Date.AddDays(-back);
            if (day == monday) continue; // Monday already holds the long Account one stretch.
            var at = day.AddHours(9);
            foreach (var (account, minutes) in earlier) { engine.AddSession(account, at, at.AddMinutes(minutes)); at = at.AddMinutes(minutes + 5); }
        }
        // Yesterday ends with two blocks on one account with five quiet minutes between them, which are merged into one below.
        var day0 = anchor.Date; var yesterday = day0.AddDays(-1);
        engine.AddSession("Account six", yesterday.AddHours(16).AddMinutes(20), yesterday.AddHours(16).AddMinutes(50));
        engine.AddSession("Account six", yesterday.AddHours(16).AddMinutes(55), yesterday.AddHours(17).AddMinutes(15));
        // Today also holds time that overlaps (a cluster of three that cuts the day into lanes), a block planned for later, and a few short ones.
        engine.AddSession("Account seven", day0.AddHours(11), day0.AddHours(11).AddMinutes(40));
        engine.AddSession("Internal", day0.AddHours(11).AddMinutes(20), day0.AddHours(11).AddMinutes(55));
        engine.AddSession("Account one", day0.AddHours(14).AddMinutes(30), day0.AddHours(15).AddMinutes(15));
        engine.AddSession("Account six", day0.AddHours(15).AddMinutes(30), day0.AddHours(15).AddMinutes(34));
        // Ten more weeks behind, so month and quarter budgets, the week overview and the earlier weeks all have something to show: four patterns of day in rotation, with
        // different starts, gaps with nothing on an account, a few overlaps, the odd weekend session, a day off and a short day.
        var patterns = new (string Account, int Minutes)[][]
        {
            new[] { ("Account one", 120), ("Account two", 50), ("Account six", 40), (BreakAccount.Name, 30), ("Account three", 60), ("Internal", 45) },
            new[] { ("Account four", 150), ("Account one", 60), (BreakAccount.Name, 45), ("Account five", 90), ("Account six", 20) },
            new[] { ("Account two", 75), ("Account seven", 100), ("Account three", 35), (BreakAccount.Name, 30), ("Account one", 80), ("Internal", 25) },
            new[] { ("Account one", 45), ("Account five", 60), ("Account four", 60), (BreakAccount.Name, 30), ("Account two", 90), ("Account six", 30), ("Archive", 40) }
        };
        var dayOff = day0.AddDays(-9); var shortDay = day0.AddDays(-10);
        engine.Schedule.Overrides[dayOff] = new DayOverride(true, null);
        engine.Schedule.Overrides[shortDay] = new DayOverride(false, new Shift(TimeSpan.FromHours(9), TimeSpan.FromHours(13)));
        for (var back = 7; back <= 70; back++)
        {
            var day = day0.AddDays(-back);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (day == dayOff || (weekend && back % 21 != 0)) continue;
            var template = weekend ? new (string Account, int Minutes)[] { ("Account two", 90), ("Internal", 30) } : patterns[(back * 7 / 3) % patterns.Length];
            if (day == shortDay) template = template.Take(3).ToArray();
            var at = day.AddHours(8.5 + back % 3 * 0.5);
            for (var i = 0; i < template.Length; i++)
            {
                engine.AddSession(template[i].Account, at, at.AddMinutes(template[i].Minutes));
                // Nothing recorded between most blocks, a longer quiet stretch some days, so Unallocated has something to show.
                at = at.AddMinutes(template[i].Minutes + (back % 5 == 0 && i == 1 ? 40 : (i * back) % 3 * 5));
            }
            // Every ninth day an extra block lies over the middle of the day.
            if (back % 9 == 0 && !weekend) engine.AddSession("Account seven", day.AddHours(10), day.AddHours(10).AddMinutes(50));
        }
        if (store is not null) store.Save(engine, prefs);
        var demo = new TimerModel(engine, prefs, store) { IsDemo = true };
        // Notes written on a few blocks, then yesterday's last two Account six blocks merged into one, so the note mark, the merged block and Unmerge can all be tried. (These are edits, so the demo starts with something to undo.)
        void Note(Func<SessionRow, bool> pick, string note)
        {
            var row = engine.Sessions.FirstOrDefault(pick); if (row is null) return;
            demo.ChangeLog(() => demo.Editor.Edit(row, row.Account, row.From, row.To, note, allowOverlap: true));
        }
        bool Today(SessionRow r, string account, int hour) => r.Day == day0 && r.Account == account && r.From.Hour == hour;
        Note(r => Today(r, "Account one", 9), "Kick-off prep and agenda for the week");
        Note(r => Today(r, "Account two", 10), "Client workshop: notes in the shared folder");
        Note(r => Today(r, "Account seven", 11), "Pairing session, then the review");
        Note(r => r.Day == day0.AddDays(-1) && r.Account == "Account four", "Reviewed the monthly figures");
        Note(r => r.Day == day0.AddDays(-2) && r.Account == "Account five", "Quarterly planning notes");
        var morning = engine.Sessions.Where(r => r.Day == day0.AddDays(-1) && r.Account == "Account six" && r.From.Hour >= 16).OrderBy(r => r.From).Take(2).ToArray();
        if (morning.Length == 2) demo.ChangeLog(() => demo.Editor.Group(morning));
        demo.Notes.SeedDemo(day0, prefs.Accounts.Select(x => x.Name).ToList());
        return demo;
    }
    public void SetShifts(Dictionary<DayOfWeek, Shift> shifts, bool resetToday = false)
    {
        if (shifts.Count == 0 || shifts.Values.Any(s => s.Start < TimeSpan.Zero || s.End >= TimeSpan.FromDays(1) || s.End <= s.Start))
            throw new ArgumentException("Choose at least one working day, with an end after its start.");
        Engine.Schedule.Days = shifts.Keys.ToHashSet();
        Engine.Schedule.Shifts = new(shifts);
        if (resetToday) { Engine.Schedule.Overrides.Remove(Engine.Now.Date); Engine.DayStarts.Remove(Engine.Now.Date); }
        // A manual start was only needed while the day was off. Once the new hours make today or a later day a working day, the
        // usual start applies, so the old press must not keep overriding it.
        foreach (var day in Engine.DayStarts.Where(p => p.Key >= Engine.Now.Date && Engine.Schedule.Scheduled(p.Key) is { Working: true } plan && p.Value >= plan.Start).Select(p => p.Key).ToList()) Engine.DayStarts.Remove(day);
        Save(); Notify();
    }
    public void OpenGap(DateTime? at = null)
    {
        var day = SelectedDate;
        var limit = day.AddDays(1);
        var start = at ?? (Engine.Plan(day).Working ? Engine.Plan(day).Start : day.AddHours(9));
        var occupied = Engine.Sessions.Where(s => s.Duration > TimeSpan.Zero && s.From < day.AddDays(1) && s.To > day)
            .Select(s => (Start: s.From, End: s.To)).ToList();
        if (Engine.Active is { } active) occupied.Add((active.Start, Engine.Now));
        foreach (var span in occupied.OrderBy(s => s.Start))
        {
            if (span.End <= start) continue;
            if (span.Start > start) break;
            start = span.End;
        }
        var next = occupied.Where(s => s.Start > start).Select(s => s.Start).DefaultIfEmpty(limit).Min();
        var end = new[] { start.AddMinutes(30), next, limit, day.AddDays(1) }.Min();
        if (end <= start) { EditError = "No gap here"; Notify(); return; }
        EditError = null; EntryRequested?.Invoke(start, end);
    }
    public bool AddEntry(string account, DateTime start, DateTime end, string note)
        => ChangeLog(() => { ValidateAccount(account); Editor.Add(account, start, end, note); });
    /// <summary>Changes a block's account, times or note in place. Blocks may overlap (they stack on the timeline), so only the block being recorded can refuse it.</summary>
    public bool EditEntry(SessionRow row, string account, DateTime start, DateTime end, string note)
        => ChangeLog(() => KeepingSelection(new[] { row }, () => { ValidateAccount(account); Editor.Edit(row, account, start, end, note, allowOverlap: true); }));
    // An edit replaces a block's row with a new one in the same place, so the selection is moved over to it: editing a selected block leaves it selected.
    private void KeepingSelection(IReadOnlyList<SessionRow> rows, Action change)
    {
        var places = rows.Where(selectedRows.Contains).Select(r => Engine.Sessions.IndexOf(r)).Where(i => i >= 0).ToList();
        change();
        foreach (var row in rows) selectedRows.Remove(row);
        foreach (var place in places) if (place < Engine.Sessions.Count) selectedRows.Add(Engine.Sessions[place]);
    }
    public bool DragEntry(SessionRow row, DateTime start, DateTime end, bool insert)
        => ChangeLog(() => Editor.EditTimeline(row, start, end, insert));
    /// <summary>Moves an account to a new place among the accounts (hidden ones keep their own places).</summary>
    public void MoveAccount(string name, int visibleIndex)
    {
        var list = preferences.Accounts.ToList();
        var visible = list.Where(a => !a.Hidden).ToList();
        var item = visible.FirstOrDefault(a => a.Name == name);
        if (item is null) return;
        visibleIndex = Math.Clamp(visibleIndex, 0, visible.Count - 1);
        var current = visible.IndexOf(item);
        if (current == visibleIndex) return;
        visible.RemoveAt(current); visible.Insert(visibleIndex, item);
        // Put the visible accounts back into the slots the visible accounts held, so hidden ones stay where they were.
        var next = 0;
        for (var i = 0; i < list.Count; i++) if (!list[i].Hidden) list[i] = visible[next++];
        preferences = preferences with { Accounts = list };
        Save(); Notify();
    }
    /// <summary>Deletes the selected blocks as one undoable edit. Selection that cannot be deleted is left alone with an explanation.</summary>
    public bool DeleteSelection()
    {
        var rows = Engine.Sessions.Where(selectedRows.Contains).ToArray();
        if (rows.Length == 0) return false;
        var done = ChangeLog(() => { Editor.Delete(rows); DropSelection(); });
        if (done) SayDone(rows.Length > 1 ? $"{rows.Length} blocks deleted" : "Deleted");
        return done;
    }
    /// <summary>Deletes the given entries as one undoable edit.</summary>
    public bool DeleteEntries(IReadOnlyList<SessionRow> rows) => ChangeLog(() => Editor.Delete(rows));
    /// <summary>Moves the end of the one selected block by whole minutes (the + and - keys), keeping its start, as the Length box does. Anything else selected is explained in the toast, and nothing selected does nothing.</summary>
    public bool NudgeSelection(int minutes)
    {
        var blocks = SelectedBlocks;
        if (blocks.Count == 0) return false;
        if (blocks.Count > 1) { Refuse("Select one block"); return false; }
        var block = blocks[0];
        if (block.IsGroup || block.Row is null) { Refuse("Unmerge to resize"); return false; }
        var end = block.End.AddMinutes(minutes);
        if (end <= block.Start) { Refuse("Block can’t be shorter"); return false; }
        if (end > block.Start.Date.AddDays(1)) { Refuse("Can’t pass midnight"); return false; }
        return EditEntry(block.Row, block.Row.Account, block.Start, end, block.Note);
    }
    public bool UndoEdit() { var done = ChangeLog(Editor.Undo); if (done) { Notice = "Undone"; Notify(); } return done; }
    public bool RedoEdit() { var done = ChangeLog(Editor.Redo); if (done) { Notice = "Redone"; Notify(); } return done; }
    public bool UngroupEntries(string id) { var done = ChangeLog(() => Editor.Ungroup(id)); if (done) SayDone("Unmerged"); return done; }
    /// <summary>Exports the week or the whole month that the given day falls in, or the one being viewed when none is given.</summary>
    public bool ExportWeek(bool wholeMonth = false, DateTime? day = null)
    {
        try
        {
            if (!Save()) return false;
            var chosen = day ?? SelectedDate;
            var export = wholeMonth ? WeekExport.ForMonth(Editor, Engine, chosen) : new WeekExport(Editor, Engine, WeekStart(chosen));
            if (store is not null) export.VerifyAgainst(store.Database);
            ExportPath = export.Write(store?.ExportDirectory ?? Path.Combine(Path.GetTempPath(), "BasicsAdvancedTimer-demo-exports"), ReviewAccounts.Select(a => a.Name));
            EditError = null; Notify(); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException)
        { EditError = error.Message; Notify(); return false; }
    }
    private void ValidateAccount(string account)
    {
        if (!ReviewAccounts.Any(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))
            && !Engine.Sessions.Any(s => s.Account.Equals(account, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose an existing account.");
    }
    private bool ChangeLog(Action change)
    {
        try
        {
            // Save any pending recording/edit before replacing the undo snapshot.
            if (!Save()) { EditError = LastError; Notify(); return false; }
            change();
            selectedRows.RemoveWhere(s => !Engine.Sessions.Any(r => ReferenceEquals(r, s)));
            EditError = null;
            var saved = Save(); Notify(); return saved;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        { EditError = error.Message; Notify(); return false; }
    }
    // The break theme is a variant of Light and Dark that overrides colours. Not tracking has no tint of its own: it keeps the ordinary
    // theme. Only the pill is coloured by state (see PillVariant): dark while recording, and a break colours the whole capsule.
    // The dashboard stays calm, and its own Break button and clock change colour to show those states.
    public string ThemeState => IsOnBreak ? "Break" : IsTracking ? "Recording" : "Normal";
    private ThemeVariant? appliedVariant;
    /// <summary>Every theme on offer: the built-in ones, then the user's own.</summary>
    public IReadOnlyList<ThemeDefinition> Themes => AppThemes.BuiltIn;
    /// <summary>The theme actually in use. System is Light or Dark, whichever the computer is using.</summary>
    public ThemeDefinition CurrentTheme
    {
        get
        {
            if (Theme == AppThemes.Sun) return SunIsDark ? AppThemes.Dark : AppThemes.Light;
            if (Theme != ThemeLibrary.System && Themes.FirstOrDefault(t => t.Name == Theme) is { } chosen) return chosen;
            var dark = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
            return dark ? AppThemes.Dark : AppThemes.Light;
        }
    }
    /// <summary>The theme the pill wears by itself: tinted amber on a break, otherwise null so it follows the rest of the app.</summary>
    public ThemeVariant? PillVariant => ThemeState != "Normal" && Application.Current is { } current && current.CheckAccess()
        ? ThemeEngine.StateVariant(CurrentTheme, ThemeState) : null;
    private void ApplyTheme()
    {
        // The check is on the application's own dispatcher, which is what setting its theme verifies. The shared UI dispatcher is not always the same one (tests that build a
        // model off the UI thread while another headless session's application is current), and asking it let a call from the wrong thread through.
        // Setting the theme verifies against the shared UI dispatcher, which parallel headless sessions can move on from the application's own, so both must agree.
        if (Application.Current is not { } app || !app.CheckAccess() || !Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) return;
        // System is Light or Dark by the computer's own setting, so the variant is chosen here and re-chosen when that setting changes.
        var variant = ThemeEngine.VariantFor(CurrentTheme);
        if (ReferenceEquals(variant, appliedVariant)) return;
        // A set the dispatcher still refuses (the race above, between the check and the set) is skipped and tried again at the next change, never thrown into the caller.
        try { app.RequestedThemeVariant = variant; appliedVariant = variant; }
        catch (InvalidOperationException) { }
    }
    // ---- Sunset to sunrise: Light by day and Dark by night. The clock tick saves and repaints every fifteen seconds, which is when the theme follows the sun. Saved as the appearance lines suncity=<city or hand> and sunhand=<dark from>-<light from>.
    private const string SunCityLine = "suncity=", SunHandLine = "sunhand=";
    public static readonly TimeSpan DefaultDarkFrom = new(19, 0, 0), DefaultLightFrom = new(7, 0, 0);
    private string? AppearanceValue(string prefix) => preferences.Extras?.AppearanceLines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is { } line ? line[prefix.Length..] : null;
    /// <summary>The place chosen for the sun's times: a city's name, or <see cref="SunTimes.ByHand"/> for times typed in. With none chosen it is a guess from the computer's time zone.</summary>
    public string SunPlace => AppearanceValue(SunCityLine) is { Length: > 0 } saved && (saved == SunTimes.ByHand || SunTimes.Find(saved) is not null) ? saved : SunTimes.Guess(SunZone).Name;
    /// <summary>The time zone the sun's times are shown in. The computer's own; replaced in tests.</summary>
    public TimeZoneInfo SunZone { get; set; } = TimeZoneInfo.Local;
    /// <summary>The two times used when set by hand: dark from the first, light from the second.</summary>
    public (TimeSpan DarkFrom, TimeSpan LightFrom) SunHandTimes
    {
        get
        {
            var parts = AppearanceValue(SunHandLine)?.Split('-');
            return parts is { Length: 2 } && TimeSpan.TryParse(parts[0], CultureInfo.InvariantCulture, out var dark) && TimeSpan.TryParse(parts[1], CultureInfo.InvariantCulture, out var light) && dark < TimeSpan.FromDays(1) && light < TimeSpan.FromDays(1)
                ? (dark, light) : (DefaultDarkFrom, DefaultLightFrom);
        }
    }
    public void SetSunPlace(string place) { SetAppearanceFlag(SunCityLine, place, true); Notify(); }
    public void SetSunHandTimes(TimeSpan darkFrom, TimeSpan lightFrom)
    {
        SetAppearanceFlag(SunHandLine, darkFrom.ToString(@"hh\:mm", CultureInfo.InvariantCulture) + "-" + lightFrom.ToString(@"hh\:mm", CultureInfo.InvariantCulture), true); Notify();
    }
    /// <summary>When it turns light and when it turns dark on a day, in this computer's time: the sun's, or the hand-set times; a place where the sun does not rise or set that day falls back to the hand-set times.</summary>
    public (TimeSpan LightFrom, TimeSpan DarkFrom) SunDay(DateTime day)
    {
        var hand = SunHandTimes;
        if (SunPlace != SunTimes.ByHand && SunTimes.Find(SunPlace) is { } city && SunTimes.On(day, city, SunZone) is { Rise: { } rise, Set: { } set }) return (rise.TimeOfDay, set.TimeOfDay);
        return (hand.LightFrom, hand.DarkFrom);
    }
    /// <summary>Whether it is night by the sun's times now: before this morning's light or from this evening's dark.</summary>
    public bool SunIsDark { get { var (light, dark) = SunDay(Engine.Now); var now = Engine.Now.TimeOfDay; return light <= dark ? now < light || now >= dark : now >= dark && now < light; } }
    public bool IsOnBreak => BreakAccount.Is(ActiveAccount);
    /// <summary>Whether hovering a control shows its tooltip. Switched off, the words are still the accessible names.</summary>
    /// <summary>Whether a longer entry that runs into later blocks moves them without asking first. Chosen by a tick box in the entry window, and in settings.</summary>
    /// <summary>The pill size chosen by hand, as a percentage of its design size, or null for the automatic size that follows the screen.</summary>
    /// <summary>Whether the pill and the dashboard stay in front of other windows. Either can be let go, so the dashboard can drop back behind other work.</summary>
    public bool PillOnTop => preferences.PillOnTop;
    public bool DashboardOnTop => preferences.DashboardOnTop;
    public void SetPillOnTop(bool on) { preferences = preferences with { PillOnTop = on }; Save(); Notify(); }
    public void SetDashboardOnTop(bool on) { preferences = preferences with { DashboardOnTop = on }; Save(); Notify(); }
    private const string ShadowsDefaultLine = "shadowsdefault=applied";
    private void SetAppearanceFlag(string prefix, string? value, bool changed)
    {
        if (!changed) return;
        var extras = preferences.Extras ?? SettingsExtras.Empty;
        var lines = extras.AppearanceLines.Where(l => !l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (value is not null) lines.Add(prefix + value);
        preferences = preferences with { Extras = extras with { AppearanceLines = lines } }; Save();
    }
    /// <summary>
    /// The break taken out of every working day's hours, so the schedule is working time and not the clock span the breaks sit inside. Saved as the appearance line
    /// breakallowance=<minutes>; with no line it is nothing, so a timer set up before this existed measures against its full hours until one is chosen.
    /// </summary>
    public TimeSpan BreakAllowance
    {
        get
        {
            var line = preferences.Extras?.AppearanceLines.FirstOrDefault(l => l.StartsWith(BreakAllowanceLine, StringComparison.Ordinal));
            return line is not null && int.TryParse(line[BreakAllowanceLine.Length..], out var minutes) ? TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 240)) : TimeSpan.Zero;
        }
    }
    /// <summary>Whether a break allowance was ever chosen, even one of nothing, so setup can suggest one only to a timer that has none.</summary>
    public bool BreakAllowanceChosen => preferences.Extras?.AppearanceLines.Any(l => l.StartsWith(BreakAllowanceLine, StringComparison.Ordinal)) == true;
    private const string BreakAllowanceLine = "breakallowance=";
    public void SetBreakAllowance(TimeSpan allowance)
    {
        var minutes = (int)Math.Clamp(Math.Round(allowance.TotalMinutes), 0, 240);
        if (BreakAllowanceChosen && BreakAllowance == TimeSpan.FromMinutes(minutes)) return;
        SetAppearanceFlag(BreakAllowanceLine, minutes.ToString(CultureInfo.InvariantCulture), true); Notify();
    }
    /// <summary>The working time a day is scheduled for: its hours less the break allowance, or nothing on a day off (and never below nothing).</summary>
    public TimeSpan ScheduledOn(DateTime day)
    {
        var plan = Engine.Plan(day); if (!plan.Working) return TimeSpan.Zero;
        var net = plan.End - plan.Start - BreakAllowance; return net > TimeSpan.Zero ? net : TimeSpan.Zero;
    }
    public int? PillScalePercent => preferences.PillScale;
    public void SetPillScale(int? percent) { preferences = preferences with { PillScale = percent is { } p ? Math.Clamp(p, 50, 120) : null }; Save(); Notify(); }
    // ---- the pill's alerts: what is waiting, how loud it is allowed to be (Peek: a card opens by itself; Light only: the light and the list; Off), and what has been put away
    private const string PillAlertsLine = "pillalerts=";
    /// <summary>What is waiting for the pill, most urgent first (see <see cref="PillAlertItems"/>).</summary>
    public IReadOnlyList<AlertItem> Alerts => PillAlertItems.Build(this);
    public AlertState AlertState { get; } = new();
    /// <summary>"peek" (the default), "light" or "off". Saved as the appearance line pillalerts=light or pillalerts=off; peek has no line.</summary>
    public string PillAlertsMode => AppearanceValue(PillAlertsLine) is { } saved && (saved == "light" || saved == "off") ? saved : "peek";
    public void SetPillAlertsMode(string mode) { var chosen = mode is "light" or "off" ? mode : "peek"; if (chosen == PillAlertsMode) return; SetAppearanceFlag(PillAlertsLine, chosen == "peek" ? null : chosen, true); Notify(); }
    /// <summary>Puts an alert away (a budget the user pressed OK on): it stays away for the session.</summary>
    public void DismissAlert(string key) { AlertState.Dismiss(key); Notify(); }
    public void SnoozeAlert(string key, TimeSpan time) { AlertState.Snooze(key, Engine.Now + time); Notify(); }
    /// <summary>Asks the dashboard to show a page ("notes" or "week"); the coordinator brings the dashboard up first when only the pill is showing.</summary>
    public event Action<string>? PageRequested;
    public void RequestPage(string page) => PageRequested?.Invoke(page);
    public bool Tooltips => preferences.Tooltips;
    public void SetTooltips(bool on) { preferences = preferences with { Tooltips = on }; Ui.ApplyTooltips(on); Save(); Notify(); }
    /// <summary>Whether the developer features are switched on: the sample-data demo and the version on the right-click menu. Off until unlocked from the About section.</summary>
    public bool DeveloperUnlocked => (preferences.Developer || IsDemo) && BuildInfo.DeveloperFeatures; // The demo is itself a developer tool, so it always shows them.
    public void SetDeveloper(bool on) { if (on && !BuildInfo.DeveloperFeatures) return; preferences = preferences with { Developer = on }; Save(); Notify(); }
    /// <summary>Whether the getting started setup is finished or skipped. A brand new database starts without it, and it can be run again from settings.</summary>
    public bool SetupDone => preferences.SetupDone;
    /// <summary>Where this timer keeps its data, or null for a throwaway copy such as the demo.</summary>
    public string? DataFolder => IsDemo ? null : store?.DataFolder;
    public void CompleteSetup() { if (preferences.SetupDone) return; preferences = preferences with { SetupDone = true }; Save(); Notify(); }
    public void RestartSetup() { if (!preferences.SetupDone) return; preferences = preferences with { SetupDone = false }; Save(); Notify(); }
    public void SetDecimalHours(bool on) { if (preferences.DecimalHours == on) return; preferences = preferences with { DecimalHours = on }; Save(); Notify(); }
    public DayOfWeek WeekStartsOn => preferences.WeekStart;
    public void SetWeekStart(DayOfWeek day) { if (preferences.WeekStart == day) return; preferences = preferences with { WeekStart = day }; DropSelection(); TimelineOffset = null; Save(); Notify(); }
    public bool Shadows => preferences.Shadows;
    public void SetShadows(bool on) { preferences = preferences with { Shadows = on }; Save(); Notify(); }
    /// <summary>Whether the timer has a taskbar button. Turn it off to run from the notification area alone.</summary>
    public bool TaskbarButton => preferences.TaskbarButton;
    /// <summary>Set once the tray icon exists. Without a tray there would be no way back to a hidden timer, so the taskbar button is then always on.</summary>
    public bool TrayAvailable { get; set; }
    public void SetTaskbarButton(bool on)
    {
        if (!TrayAvailable && !on) return;
        preferences = preferences with { TaskbarButton = on };
        if (!on) Notice = "Running from the tray · click its icon to open";
        Save(); Notify();
    }
    /// <summary>How far through the working day it is, from 0 to 1, or null on a day off.</summary>
    public double? DayProgress
    {
        get
        {
            var plan = Engine.Plan(Engine.Now);
            if (!plan.Working || plan.End <= plan.Start) return null;
            return Math.Clamp((Engine.Now - plan.Start).TotalSeconds / (plan.End - plan.Start).TotalSeconds, 0.0, 1.0);
        }
    }
    /// <summary>Developer switch: skip the one-second refresh, so a page can be watched with nothing redrawing on the tick. Never saved; the clock stops moving while it is on.</summary>
    public static bool TickPaused { get; set; }
    public void Tick()
    {
        if (closed || TickPaused) return;
        ticks++;
        FollowToday();
        Notes.MakeSeriesNotes(); // A repeating meeting's next days are made as time goes on.
        if (ticks % 60 == 0) Notes.RetryUnsaved();
        if (ticks % 15 == 0) { Save(); Notify(); }
        else PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elapsed)));
    }
    public bool Save()
    {
        // Once the app is closing, or its database has been closed, there is nothing left to save and a late tick must not try.
        if (closed || store is { IsDisposed: true }) return true;
        try { store?.Save(Engine, preferences); if (LastError is not null) { LastError = null; Notify(); } return true; } // A failure that has cleared is shown as gone at once, not at the next change.
        // A restart or shutdown can close the database under a save that is already queued. That must be reported, never allowed to crash the app.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException || error.GetType().Namespace == "Microsoft.Data.Sqlite")
        { LastError = "Not saved · kept on screen, retrying · " + error.Message; return false; }
    }
    private bool closed;
    /// <summary>Called as the app exits, before the database is closed, so the clock's tick stops saving.</summary>
    public void MarkClosed() => closed = true;
    /// <summary>Whether to start with Windows. Null in demo mode and tests, where it is not offered.</summary>
    public IAutoStart? AutoStart { get; set; }
    public void SetAutoStart(bool enabled)
    {
        try { AutoStart?.Set(enabled); LastError = null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or PlatformNotSupportedException)
        { LastError = "Couldn’t change Start with Windows · " + error.Message; }
        Notify();
    }
    public event Action? HideRequested;
    /// <summary>Asks for the timer to tuck itself away in the tray (or the taskbar when there is no tray), with tracking carrying on.</summary>
    public void RequestHide() => HideRequested?.Invoke();
    public event Action? QuitRequested;
    public void RequestQuit() => QuitRequested?.Invoke();
    // Closing the pill closes the dashboard straight after it, and by then the first close may have let the app shut down and
    // dispose the database, so a close that has already saved is not allowed to save again.
    private bool savedForClose;
    public bool StopForClose()
    {
        if (savedForClose) return true;
        Engine.StopAccount(); var saved = Save(); if (!saved) Notify();
        savedForClose = saved; return saved;
    }
    // Notes and refused-edit messages in the footer fade after a while, so a stale one is never left sitting there.
    // A failed save is not one of these, because it stays until a save works.
    public static readonly TimeSpan MessageLifetime = TimeSpan.FromSeconds(15);
    private Avalonia.Threading.DispatcherTimer? messageTimer;
    private (string?, string?) messageSeen;
    private void WatchMessages()
    {
        var now = (Notice, EditError);
        if (now == messageSeen) return;
        messageSeen = now;
        messageTimer?.Stop(); messageTimer = null;
        if (now.Item1 is null && now.Item2 is null) return;
        messageTimer = new Avalonia.Threading.DispatcherTimer { Interval = MessageLifetime };
        messageTimer.Tick += (_, _) => ExpireMessages();
        messageTimer.Start();
    }
    /// <summary>Clears the footer notes now. The 30 second timer calls this, and tests call it directly because timers do not tick headless.</summary>
    public void ExpireMessages()
    {
        messageTimer?.Stop(); messageTimer = null;
        Notice = null; EditError = null; messageSeen = (null, null); Notify();
    }
    /// <summary>True while a reminder is due (its time has passed and it is not ticked off), which lights the blue light on the pill and the rail.</summary>
    public bool ReminderDue => Notes.AttentionCount > 0;
    private void Notify() { ApplyTheme(); WatchMessages(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
}
