using System.Globalization;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>How often a meeting repeats. The shared packages hold repeats by weeks and days, so these are the ones they can write.</summary>
public enum RepeatChoice { None, Day, Weekday, Week, BiWeek, Month }

/// <summary>The views down the left of the Notes page.</summary>
public enum NoteView { All, Important, Today, Overdue, Upcoming, Meetings, Done }

/// <summary>A heading and the notes under it on the Notes page.</summary>
public sealed record NoteGroup(string Label, bool Bad, IReadOnlyList<DayNote> Notes);

/// <summary>
/// Notes and meetings, kept apart from <see cref="TimerModel"/> as the Notes spec advises. A note is a <see cref="DayNote"/> in the shared storage: a plain note is an
/// undated task, or a dated one when it has a reminder (the reminder is its day and time); a meeting is a note with a start and an end, which is planned time and
/// never counts as tracked time. A repeating meeting is a series whose notes are made about eight weeks ahead, so each can be changed or removed alone. A
/// note the database will not take stays on screen with a negative number and is written again later (<see cref="RetryUnsaved"/>), so words are never lost.
/// </summary>
public sealed class NotesModel
{
    private readonly TimerStore? store;
    private readonly Func<DateTime> now;
    private readonly Func<IReadOnlyList<string>> accountNames;
    private readonly List<DayNote> notes;
    private readonly List<NoteSeries> series;
    private long localNote = 1, localSeries = 1, unsaved;
    private readonly HashSet<long> pendingDeletes = new();

    public NotesModel(TimerStore? store, Func<DateTime> now, Func<IReadOnlyList<string>> accountNames)
    {
        this.store = store; this.now = now; this.accountNames = accountNames;
        notes = store?.LoadDayNotes() ?? new List<DayNote>(); series = store?.LoadSeries() ?? new List<NoteSeries>();
        MakeSeriesNotes();
    }

    /// <summary>Raised after anything changes, so the views redraw.</summary>
    public event Action? Changed;
    /// <summary>A meeting form is wanted at a time (a click in the timeline's meeting row), or for an existing meeting (a click on its card).</summary>
    public event Action<DateTime>? NewMeetingRequested;
    public event Action<long>? EditMeetingRequested;
    public void RequestNewMeeting(DateTime start) => NewMeetingRequested?.Invoke(start);
    public void RequestEditMeeting(long id) => EditMeetingRequested?.Invoke(id);
    /// <summary>A pin on the timeline was pressed: the dashboard shows the Notes page with that note open for editing.</summary>
    public event Action<long>? OpenNoteRequested;
    public void RequestOpenNote(long id) => OpenNoteRequested?.Invoke(id);
    /// <summary>A pin on the timeline was pressed: the Notes card opens that note for editing, so the timeline stays where it is.</summary>
    public event Action<long>? EditNoteRequested;
    public void RequestEditNote(long id) => EditNoteRequested?.Invoke(id);
    /// <summary>A meeting's times were changed by dragging it on the timeline, so a form open on it can show the new times.</summary>
    public event Action<long>? MeetingMoved;
    /// <summary>The last thing the database refused, or null once everything is written.</summary>
    public string? Fault { get; private set; }

    public IReadOnlyList<DayNote> All => notes;
    public DayNote? Find(long id) => notes.FirstOrDefault(n => n.Id == id);
    public static bool IsMeeting(DayNote note) => DayNotes.IsMeeting(note);
    private IReadOnlyList<string> Names => accountNames();

    /// <summary>The meetings on a day, in time order. A ticked meeting is one that was taken off the timeline.</summary>
    public IReadOnlyList<DayNote> MeetingsOn(DateTime day) => notes.Where(n => n.HasDate && n.Day.Date == day.Date && !n.Done && IsMeeting(n)).OrderBy(n => n.At).ThenBy(n => n.Id).ToList();

    /// <summary>The notes with a reminder on a day that are not ticked off, in time order: the ones the timeline hangs a pin for.</summary>
    public IReadOnlyList<DayNote> PinnedOn(DateTime day) => notes.Where(n => !n.Done && ReminderOf(n) is { } due && due.Date == day.Date).OrderBy(n => ReminderOf(n)).ThenBy(n => n.Id).ToList();

    /// <summary>
    /// What the Notes card lists for a day: the day's meetings, then notes with a reminder that day, then notes with no date, and whatever is done last of all (kept in
    /// the order it had), whatever its own time, so the list reads as what is still to do and then what is finished.
    /// </summary>
    public IReadOnlyList<DayNote> ForDay(DateTime day)
    {
        var meetings = notes.Where(n => n.HasDate && n.Day.Date == day.Date && IsMeeting(n)).OrderBy(n => n.At).ThenBy(n => n.Id);
        var dated = notes.Where(n => n.HasDate && n.Day.Date == day.Date && !IsMeeting(n)).OrderBy(n => n.At ?? TimeSpan.FromDays(1)).ThenBy(n => n.Id);
        var undated = notes.Where(n => !n.HasDate).OrderBy(n => n.Id);
        // What matters comes first among the notes (the sort keeps time order within each kind), and what is done goes last.
        return meetings.Concat(dated.Concat(undated).OrderByDescending(IsImportant)).OrderBy(n => n.Done).ToList();
    }

    /// <summary>The words shown for a note: without a deadline marker, without the important mark and without the #Tag that has its own chip.</summary>
    public string Text(DayNote note) => NoteLinks.Flatten(Shown(note));
    /// <summary>The same words as they are written, with any links still in their <c>[words](address)</c> form and the important mark if it has one: what the form is given to edit.</summary>
    public string Words(DayNote note) => NoteShorthand.Display(note, Names);
    /// <summary>The words as a row draws them: as written, but without the important mark, which a row shows as its star.</summary>
    public string Shown(DayNote note) => IsMeeting(note) ? Words(note) : WithoutMark(Words(note));

    // An important note starts with !!, which is how it is kept (the shared package has no field for it, and a note written with !! at the start is important the same way as
    // one starred in a row). Meetings are plans, not tasks, so they are never important.
    /// <summary>Whether some words (as typed in a form, or as stored) start with the important mark.</summary>
    public static bool IsMarked(string text) => text.StartsWith("!!", StringComparison.Ordinal) && (text.Length == 2 || char.IsWhiteSpace(text[2]));
    private static string WithoutMark(string text) => IsMarked(text) ? text[2..].TrimStart() : text;
    /// <summary>The words with the important mark put at the start, or taken off: what a form's star does to the box.</summary>
    public static string WithMark(string text, bool important) => important ? (IsMarked(text) ? text : ("!! " + text).TrimEnd()) : WithoutMark(text);
    /// <summary>Whether a note is marked important.</summary>
    public static bool IsImportant(DayNote note) => !IsMeeting(note) && IsMarked(note.Text);
    /// <summary>Marks a note important or takes the mark off, keeping everything else.</summary>
    public void SetImportant(long id, bool important)
    {
        if (Find(id) is not { } note || IsMeeting(note) || IsImportant(note) == important) return;
        Change(id, n => n with { Text = WithMark(n.Text, important) }); Changed?.Invoke();
    }
    /// <summary>The account a note is tagged with, or null.</summary>
    public string? AccountOf(DayNote note) => NoteShorthand.AccountOf(note.Text, Names);
    /// <summary>When a note's reminder is, or null for a note with none.</summary>
    public static DateTime? ReminderOf(DayNote note) => note.HasDate && note.At is { } at && !IsMeeting(note) ? note.Day.Date + at : null;
    public bool IsLate(DayNote note) => !note.Done && ReminderOf(note) is { } due && due < now();

    public NoteSeries? SeriesOf(DayNote note) => note.SeriesId is { } id ? series.FirstOrDefault(s => s.Id == id) : null;
    public static RepeatChoice ChoiceOf(Recurrence? rule) => rule switch
    {
        null => RepeatChoice.None,
        { EveryMonths: 1 } => RepeatChoice.Month,
        { EveryMonths: > 1 } => RepeatChoice.None,
        { EveryWeeks: 1, DaysMask: Recurrence.AllDays } => RepeatChoice.Day,
        { EveryWeeks: 1, DaysMask: Recurrence.Weekdays } => RepeatChoice.Weekday,
        { EveryWeeks: 1, DaysMask: 0 } => RepeatChoice.Week,
        { EveryWeeks: 2, DaysMask: 0 } => RepeatChoice.BiWeek,
        _ => RepeatChoice.None
    };
    public static Recurrence? RuleOf(RepeatChoice choice) => choice switch
    {
        RepeatChoice.Day => new Recurrence(1, Recurrence.AllDays), RepeatChoice.Weekday => new Recurrence(1, Recurrence.Weekdays), RepeatChoice.Week => new Recurrence(1, 0), RepeatChoice.BiWeek => new Recurrence(2, 0), RepeatChoice.Month => new Recurrence(1, 0, null, 1), _ => null
    };
    public RepeatChoice RepeatOf(DayNote note) => ChoiceOf(SeriesOf(note)?.Rule);
    /// <summary>A repeat the Notes card cannot change (one made by an older build: on chosen days, every third week, every other month). It is shown and can be removed.</summary>
    public bool IsArchivedRepeat(DayNote note) => SeriesOf(note) is { } s && ChoiceOf(s.Rule) == RepeatChoice.None;
    /// <summary>How a repeat reads at the end of a message: "daily", "weekly", "every other week", "monthly".</summary>
    public static string RepeatWords(RepeatChoice choice) => choice switch { RepeatChoice.Day => "daily", RepeatChoice.Weekday => "on weekdays", RepeatChoice.Week => "weekly", RepeatChoice.BiWeek => "every other week", RepeatChoice.Month => "monthly", _ => "" };
    public static string Label(RepeatChoice choice) => choice switch
    {
        RepeatChoice.Day => "Daily", RepeatChoice.Weekday => "Every weekday", RepeatChoice.Week => "Weekly", RepeatChoice.BiWeek => "Bi-weekly", RepeatChoice.Month => "Monthly", _ => "Does not repeat"
    };
    /// <summary>
    /// The choices the repeat lists offer: Does not repeat, Daily, Weekly, Bi-weekly and Monthly. Every weekday is no longer offered, but a series made with it
    /// while it was still a choice keeps it in its own list, so what it says is not lost.
    /// </summary>
    public static List<string> Offered(RepeatChoice current) =>
        new[] { RepeatChoice.None, RepeatChoice.Day, RepeatChoice.Week, RepeatChoice.BiWeek, RepeatChoice.Month }.Concat(current == RepeatChoice.Weekday ? new[] { RepeatChoice.Weekday } : Array.Empty<RepeatChoice>()).Select(Label).ToList();

    // ---- What is waiting: the bell, the reminder light and the Notes page ----

    /// <summary>Reminders whose time has passed and that are not ticked off, the oldest first. They stay until ticked off or changed.</summary>
    public IReadOnlyList<DayNote> Overdue => notes.Where(IsLate).OrderBy(n => ReminderOf(n)).ThenBy(n => n.Id).ToList();
    /// <summary>How many reminders need attention. The light on the pill and the rail is on while this is not zero.</summary>
    public int AttentionCount => notes.Count(IsLate);
    /// <summary>Reminders still to come, soonest first.</summary>
    public IReadOnlyList<DayNote> ComingUp(int limit = 6)
        => notes.Where(n => !n.Done && ReminderOf(n) is { } due && due >= now()).OrderBy(n => ReminderOf(n)).ThenBy(n => n.Id).Take(limit).ToList();

    /// <summary>
    /// What the Notes page lists: everything, but only the next day of a repeating meeting (not its eight weeks) and no meeting that has gone, unless it was ticked off.
    /// </summary>
    public IReadOnlyList<DayNote> Listed()
    {
        var today = now().Date; var firstOf = new HashSet<long>();
        var list = new List<DayNote>();
        foreach (var n in notes.OrderBy(n => n.Day).ThenBy(n => n.At).ThenBy(n => n.Id))
        {
            if (IsMeeting(n) && !n.Done)
            {
                if (n.Day.Date < today) continue;
                if (n.SeriesId is { } id && n.Day.Date > today && !firstOf.Add(id)) continue; // One occurrence of a series is enough in a list.
                if (n.SeriesId is { } today1 && n.Day.Date == today) firstOf.Add(today1);
            }
            list.Add(n);
        }
        return list;
    }

    /// <summary>The listed notes that match what was typed in the search box (its words or its account) and the account chosen, if any.</summary>
    public IReadOnlyList<DayNote> Matching(string search, string? account) => MatchingAny(search, account is null ? null : new[] { account });

    /// <summary>The same, for any number of accounts chosen at once (a note tagged with any of them matches); none chosen means every account.</summary>
    public IReadOnlyList<DayNote> MatchingAny(string search, IReadOnlyCollection<string>? accounts)
    {
        var words = search.Trim();
        return Listed().Where(n => (accounts is not { Count: > 0 } || accounts.Any(a => string.Equals(AccountOf(n), a, StringComparison.OrdinalIgnoreCase)))
            && (words.Length == 0 || Text(n).Contains(words, StringComparison.OrdinalIgnoreCase) || (AccountOf(n)?.Contains(words, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
    }

    public bool InView(DayNote n, NoteView view)
    {
        var today = now().Date;
        return view switch
        {
            NoteView.Important => IsImportant(n) && !n.Done,
            NoteView.Today => n.HasDate && n.Day.Date == today,
            NoteView.Overdue => IsLate(n),
            NoteView.Upcoming => n.HasDate && n.Day.Date > today && !n.Done,
            NoteView.Meetings => IsMeeting(n),
            NoteView.Done => n.Done,
            _ => true
        };
    }

    /// <summary>The Notes page's list in groups: Overdue, then each day from today, then notes with no date, then Done. Each group is in time order.</summary>
    public IReadOnlyList<NoteGroup> Groups(IReadOnlyList<DayNote> shown)
    {
        var today = now().Date; var groups = new List<NoteGroup>();
        var overdue = shown.Where(IsLate).OrderBy(n => ReminderOf(n)).ToList();
        if (overdue.Count > 0) groups.Add(new("Overdue", true, overdue));
        var open = shown.Where(n => !IsLate(n) && !n.Done).ToList();
        foreach (var day in open.Where(n => n.HasDate).Select(n => n.Day.Date).Distinct().OrderBy(d => d))
        {
            var label = NoteDates.Day(day, today) + (day == today || day == today.AddDays(1) ? " · " + day.ToString("d MMM", CultureInfo.GetCultureInfo("en-GB")) : "");
            groups.Add(new(label, false, open.Where(n => n.HasDate && n.Day.Date == day).OrderByDescending(IsImportant).ThenBy(n => n.At ?? TimeSpan.FromDays(1)).ThenBy(n => n.Id).ToList()));
        }
        var undated = open.Where(n => !n.HasDate).OrderByDescending(IsImportant).ThenBy(n => n.Id).ToList();
        if (undated.Count > 0) groups.Add(new("No date", false, undated));
        var done = shown.Where(n => n.Done).OrderByDescending(n => n.Day).ThenByDescending(n => n.Id).ToList();
        if (done.Count > 0) groups.Add(new("Done", false, done));
        return groups;
    }

    // ---- Changing notes ----

    private string Compose(string words, string? account)
    {
        words = NoteShorthand.WithoutAccountTag(words.Trim(), Names);
        return account is { Length: > 0 } ? (words + " " + NoteShorthand.TagFor(account)).Trim() : words;
    }

    /// <summary>Adds a note. With a reminder it is dated and timed; without, it is an undated task. Returns what is wrong, or null.</summary>
    public string? AddNote(string words, string? account, DateTime? reminder)
    {
        if (string.IsNullOrWhiteSpace(words)) return "Write a note first";
        var text = Compose(words, account);
        if (reminder is { } when) AddRow(when.Date, when.TimeOfDay.Truncate(), text, null, null, DayNoteKind.Task, true);
        else AddRow(now().Date, null, text, null, null, DayNoteKind.Task, false);
        Changed?.Invoke(); return null;
    }

    /// <summary>Adds a meeting on a day, with a repeat if one is chosen. Returns what is wrong, or null.</summary>
    public string? AddMeeting(DateTime day, TimeSpan start, TimeSpan end, string words, string? account, RepeatChoice repeat)
    {
        if (string.IsNullOrWhiteSpace(words)) return "Name the meeting";
        if (MeetingTimeProblem(start, end) is { } problem) return problem;
        var row = AddRow(day.Date, start, Compose(words, account), end, null, DayNoteKind.Meeting, true);
        if (RuleOf(repeat) is { } rule) StartSeriesFrom(row, rule);
        Changed?.Invoke(); return null;
    }

    private static string? MeetingTimeProblem(TimeSpan start, TimeSpan end)
        => start < TimeSpan.Zero || end >= TimeSpan.FromDays(1) ? "A meeting has to fall within one day." : end <= start ? "The end has to be after the start." : null;

    /// <summary>
    /// Gives a meeting new times on its own day, as when it is dragged or resized on the timeline. A meeting in a series changes for this day only. Returns what is
    /// wrong, or null.
    /// </summary>
    public string? MoveMeeting(long id, TimeSpan start, TimeSpan end)
    {
        if (Find(id) is not { } meeting || !IsMeeting(meeting)) return "That meeting is no longer there.";
        if (MeetingTimeProblem(start, end) is { } problem) return problem;
        Change(id, n => n with { At = start, End = end });
        MeetingMoved?.Invoke(id); Changed?.Invoke(); return null;
    }

    /// <summary>Changes a note's words, account and reminder (a null reminder makes it undated again).</summary>
    public string? EditNote(long id, string words, string? account, DateTime? reminder)
    {
        if (Find(id) is not { } note) return "That note is no longer there.";
        if (string.IsNullOrWhiteSpace(words)) return "Write a note first";
        var text = Compose(words, account);
        Change(id, n => reminder is { } when ? n with { Text = text, Day = when.Date, At = when.TimeOfDay.Truncate(), HasDate = true, Done = n.Done && ReminderOf(n) == when }
            : n with { Text = text, Day = now().Date, At = null, HasDate = false });
        Changed?.Invoke(); return null;
    }

    /// <summary>Sets or removes a note's reminder, keeping its words.</summary>
    public void SetReminder(long id, DateTime? reminder)
    {
        if (Find(id) is not { } note) return;
        EditNote(id, note.Text, null, reminder);
    }

    /// <summary>
    /// Saves a meeting. For one in a series, <paramref name="allLater"/> gives the change to this one and every later one (and a changed repeat remakes the later
    /// ones from this day); otherwise only this one changes and the series carries on as before.
    /// </summary>
    public string? EditMeeting(long id, string words, string? account, DateTime day, TimeSpan start, TimeSpan end, RepeatChoice repeat, bool allLater)
    {
        if (Find(id) is not { } meeting) return "That meeting is no longer there.";
        if (string.IsNullOrWhiteSpace(words)) return "Name the meeting";
        if (MeetingTimeProblem(start, end) is { } problem) return problem;
        var text = Compose(words, account); var current = SeriesOf(meeting);
        Change(id, n => n with { Text = text, Day = day.Date, At = start, End = end, Kind = DayNoteKind.Meeting, HasDate = true });
        if (current is null) { if (RuleOf(repeat) is { } rule) StartSeriesFrom(Find(id)!, rule); }
        else if (allLater && !IsArchivedRepeat(meeting))
        {
            var later = notes.Where(n => n.SeriesId == current.Id && n.Day.Date > day.Date && n.Id != id).ToList();
            var wanted = RuleOf(repeat);
            if (wanted is null) { foreach (var gone in later) RemoveRow(gone); EndSeries(current); }
            else if (wanted != current.Rule)
            {
                foreach (var gone in later) RemoveRow(gone);
                var remade = current with { Rule = wanted, Start = day.Date, GeneratedTo = day.Date, Text = text, At = start, End = end, Kind = DayNoteKind.Meeting };
                series[series.FindIndex(s => s.Id == current.Id)] = remade; PersistSeries(remade); MakeSeriesNotes();
            }
            else
            {
                foreach (var other in later.Where(n => !n.Done)) Change(other.Id, n => n with { Text = text, At = start, End = end, Kind = DayNoteKind.Meeting });
                var updated = current with { Text = text, At = start, End = end, Kind = DayNoteKind.Meeting };
                series[series.FindIndex(s => s.Id == current.Id)] = updated; PersistSeries(updated);
            }
        }
        Changed?.Invoke(); return null;
    }

    public void Toggle(long id)
    {
        if (Find(id) is null) return;
        Change(id, n => n with { Done = !n.Done }); Changed?.Invoke();
    }

    /// <summary>Removes a note or meeting. For one in a series, <paramref name="allLater"/> removes this one and every later one and ends the series there; otherwise only this day goes and the series carries on.</summary>
    public void Remove(long id, bool allLater = false)
    {
        if (Find(id) is not { } note) return;
        if (allLater && SeriesOf(note) is { } s)
        {
            foreach (var gone in notes.Where(n => n.SeriesId == s.Id && n.Day.Date >= note.Day.Date).ToList()) RemoveRow(gone);
            if (notes.Any(n => n.SeriesId == s.Id)) { var ended = s with { Rule = s.Rule with { Until = note.Day.Date.AddDays(-1) } }; series[series.FindIndex(x => x.Id == s.Id)] = ended; PersistSeries(ended); }
            else EndSeries(s);
        }
        else RemoveRow(note);
        Changed?.Invoke();
    }

    /// <summary>Notes tagged with an account follow its rename, and the tag becomes an ordinary word if the account is deleted.</summary>
    public void Relink(IEnumerable<string> before, IReadOnlyDictionary<string, string> renamed, IReadOnlyCollection<string> deleted)
    {
        var names = before.ToList(); var any = false;
        for (var i = 0; i < notes.Count; i++)
        {
            var text = NoteShorthand.Relink(notes[i].Text, names, renamed, deleted);
            if (text == notes[i].Text) continue;
            notes[i] = notes[i] with { Text = text }; Persist(notes[i]); any = true;
        }
        for (var i = 0; i < series.Count; i++)
        {
            var text = NoteShorthand.Relink(series[i].Text, names, renamed, deleted);
            if (text != series[i].Text) { series[i] = series[i] with { Text = text }; PersistSeries(series[i]); }
        }
        if (any) Changed?.Invoke();
    }

    // ---- Repeats ----

    /// <summary>Makes the notes each series still needs, from today up to eight weeks ahead. A day already made is never made again, so one removed stays removed.</summary>
    public int MakeSeriesNotes()
    {
        var today = now().Date; var made = 0;
        for (var i = 0; i < series.Count; i++)
        {
            var s = series[i]; var reach = today.AddDays(NoteSeries.HorizonDays);
            if (s.GeneratedTo.Date >= reach) continue;
            foreach (var day in s.Pending(today)) { AddRow(day, s.At, s.Text, s.End, s.Id, s.Kind ?? DayNoteKind.Meeting, true); made++; }
            series[i] = s with { GeneratedTo = reach }; PersistSeries(series[i]);
        }
        if (made > 0) Changed?.Invoke();
        return made;
    }

    private void StartSeriesFrom(DayNote first, Recurrence rule)
    {
        var template = new NoteSeries(0, rule, first.Day, first.Day, first.Text, first.At, first.End, first.Kind);
        long id = localSeries++;
        if (store is not null)
        {
            long stored = 0;
            if (!Try(() => stored = store.AddSeries(template), "that repeat")) return;
            id = stored;
        }
        series.Add(template with { Id = id });
        Change(first.Id, n => n with { SeriesId = id });
        MakeSeriesNotes();
    }

    private void EndSeries(NoteSeries s)
    {
        series.RemoveAll(x => x.Id == s.Id);
        if (store is not null && s.Id >= 0) Try(() => store.DeleteSeries(s.Id), "that repeat");
        for (var i = 0; i < notes.Count; i++) if (notes[i].SeriesId == s.Id) notes[i] = notes[i] with { SeriesId = null };
    }

    private void PersistSeries(NoteSeries s) { if (store is not null && s.Id >= 0) Try(() => store.UpdateSeries(s), "that repeat"); }

    // ---- Storage: a write that fails never loses the words ----

    private DayNote AddRow(DateTime day, TimeSpan? at, string text, TimeSpan? end, long? seriesId, DayNoteKind kind, bool hasDate)
    {
        long id;
        if (store is null) id = localNote++;
        else { long stored = 0; id = Try(() => stored = store.AddDayNote(day, at, text, end, seriesId, kind, hasDate), "that note") ? stored : --unsaved; }
        var note = new DayNote(id, day, at, text, false, end, seriesId, kind, hasDate);
        notes.Add(note); return note;
    }

    private void Change(long id, Func<DayNote, DayNote> change)
    {
        var index = notes.FindIndex(n => n.Id == id); if (index < 0) return;
        notes[index] = change(notes[index]); Persist(notes[index]);
    }

    private void Persist(DayNote note)
    {
        if (store is null || note.Id < 0) return; // An unsaved note is written whole when it is retried.
        Try(() => store.UpdateDayNote(note), "that change");
    }

    private void RemoveRow(DayNote note)
    {
        notes.Remove(note);
        if (store is null) return;
        if (note.Id < 0) return;
        if (!Try(() => store.DeleteDayNote(note.Id), "that removal")) pendingDeletes.Add(note.Id);
    }

    private bool Try(Action write, string what)
    {
        try { write(); Fault = null; return true; }
        catch (Exception) { Fault = $"{char.ToUpperInvariant(what[0])}{what[1..]} not saved · will retry"; return false; }
    }

    /// <summary>True while any note is on screen but not yet in the database.</summary>
    public bool Unsaved => pendingDeletes.Count > 0 || notes.Any(n => n.Id < 0);

    /// <summary>Writes again whatever the database refused: notes never stored, and removals. Called now and then.</summary>
    public void RetryUnsaved()
    {
        if (store is null || !Unsaved) return;
        bool Quiet(Action write) { try { write(); return true; } catch (Exception) { return false; } }
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i]; if (note.Id >= 0) continue;
            long stored = 0;
            if (!Quiet(() => stored = store.AddDayNote(note.Day, note.At, note.Text, note.End, note.SeriesId, note.Kind, note.HasDate))) continue;
            notes[i] = note with { Id = stored }; if (note.Done) Quiet(() => store.UpdateDayNote(notes[i]));
        }
        foreach (var id in pendingDeletes.ToList()) if (Quiet(() => store.DeleteDayNote(id))) pendingDeletes.Remove(id);
        if (!Unsaved) { Fault = null; Changed?.Invoke(); }
    }

    /// <summary>
    /// Notes and meetings for the sample-data demo, enough to try every part of them: meetings that repeat (daily, weekly, every other week, monthly) and overlap on one day and
    /// run long on another, notes that are late (one by days), coming up, at the same minute (a cluster on the timeline), important with and without a reminder, with links,
    /// with an account, long, done, and with no date at all.
    /// </summary>
    public void SeedDemo(DateTime today, IReadOnlyList<string> accounts)
    {
        string? Account(int n) => n < accounts.Count ? accounts[n] : null;
        DayNote Last() => notes[^1];
        // The first six keep their order: a meeting that repeats, a call, a review, two notes with a reminder and one with no date.
        AddMeeting(today, TimeSpan.FromHours(9.25), TimeSpan.FromHours(10), "Team sync", null, RepeatChoice.Week);
        AddMeeting(today, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Client call", Account(1), RepeatChoice.None);
        AddMeeting(today, TimeSpan.FromHours(15), TimeSpan.FromHours(16), "Plan review", null, RepeatChoice.None);
        AddNote("Send the revised budget", Account(0), today.AddHours(14));
        AddNote("Follow up on the new brief", Account(1), today.AddHours(15.5));
        AddNote("Tidy the shared drive", null, null);

        // More meetings today: a daily one beside the weekly one, two that overlap the call, one that overlaps the review, a monthly one and a late one.
        AddMeeting(today, TimeSpan.FromHours(9), TimeSpan.FromHours(9.25), "Stand-up", null, RepeatChoice.Day);
        AddMeeting(today, TimeSpan.FromHours(11.5), TimeSpan.FromHours(12.25), "Design review", Account(2), RepeatChoice.None);
        AddMeeting(today, TimeSpan.FromHours(11.75), TimeSpan.FromHours(12.5), "Supplier check-in", Account(4), RepeatChoice.None);
        AddMeeting(today, TimeSpan.FromHours(13.5), TimeSpan.FromHours(14), "Vendor demo", Account(4), RepeatChoice.None);
        AddMeeting(today, TimeSpan.FromHours(14.5), TimeSpan.FromHours(15), "Finance close", Account(3), RepeatChoice.Month);
        AddMeeting(today, TimeSpan.FromHours(15.5), TimeSpan.FromHours(16.5), "1:1", Account(0), RepeatChoice.BiWeek);
        AddMeeting(today, TimeSpan.FromHours(16.5), TimeSpan.FromHours(17), "Wrap-up", null, RepeatChoice.None);
        // Other days: yesterday, tomorrow, a long one, one further on and one in the past.
        AddMeeting(today.AddDays(-1), TimeSpan.FromHours(16), TimeSpan.FromHours(17), "Retro", null, RepeatChoice.None);
        AddMeeting(today.AddDays(-3), TimeSpan.FromHours(13), TimeSpan.FromHours(14.5), "Training", Account(5), RepeatChoice.None);
        AddMeeting(today.AddDays(1), TimeSpan.FromHours(10), TimeSpan.FromHours(12), "Workshop", Account(3), RepeatChoice.None);
        AddMeeting(today.AddDays(1), TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Budget check-in", Account(0), RepeatChoice.Week);
        AddMeeting(today.AddDays(3), TimeSpan.FromHours(9), TimeSpan.FromHours(17), "Offsite", null, RepeatChoice.None);
        AddMeeting(today.AddDays(7), TimeSpan.FromHours(9), TimeSpan.FromHours(10.5), "Planning", Account(1), RepeatChoice.None);

        // Notes that are late: one a few minutes ago, one a day old, one days old, and important ones.
        AddNote("!! Call the client back about the contract", Account(1), today.AddHours(13.75));
        AddNote("Chase the invoice", Account(4), today.AddDays(-1).AddHours(16));
        AddNote("Renew the software licence", null, today.AddDays(-3).AddHours(10));
        AddNote("!! Reply to the auditor", Account(3), today.AddDays(-2).AddHours(9.5));
        // Notes coming up: three at nearly the same minute (one pin on the timeline), then later today, tomorrow and further on.
        AddNote("Book the meeting room", null, today.AddHours(15.5));
        AddNote("!! Print the agenda", Account(2), today.AddHours(15.5).AddMinutes(5));
        AddNote("Submit the timesheet", Account(6), today.AddHours(16.75));
        AddNote("!! Send the invoice batch", Account(3), today.AddHours(17));
        AddNote("!! Confirm the catering", null, today.AddDays(1).AddHours(8.5));
        AddNote("Prepare the workshop slides", Account(3), today.AddDays(1).AddHours(9));
        AddNote("Review the [contract draft](https://example.com/contract) before the call", Account(1), today.AddDays(2).AddHours(11));
        AddNote("Quarterly report due: see [the template](https://example.com/report) and [last quarter](https://example.com/report-q3)", Account(4), today.AddDays(8).AddHours(12));
        // Notes with no date: one important, one with a link, one with an account, a long one that wraps, and a few more for the lists.
        AddNote("!! Back up the laptop", null, null);
        AddNote("Look at [other time-tracking tools](https://example.com/research) for ideas", null, null);
        AddNote("Write up the retro", Account(6), null);
        AddNote("Ask finance about the Q4 budget", Account(4), null);
        AddNote("Draft the long summary of the client's feedback, covering pricing, timelines, the staffing plan and the risks they raised on the call, then share it with the team for comments before Friday", Account(1), null);
        AddNote("Sort out the expenses", null, null);
        // Done: one with a reminder, one without.
        AddNote("Update the project plan", Account(0), today.AddDays(-1).AddHours(11)); Toggle(Last().Id);
        AddNote("Order stationery", null, null); Toggle(Last().Id);
    }
}

internal static class TimeSpanRounding
{
    /// <summary>The time cut to whole minutes, which is how notes store it.</summary>
    public static TimeSpan Truncate(this TimeSpan time) => new(time.Hours, time.Minutes, 0);
}
