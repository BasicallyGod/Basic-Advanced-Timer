using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>A view leases a record; its dirty draft survives release without retaining the view.</summary>
public sealed class DraftLease : IDisposable
{
    private Action? release;
    internal DraftLease(RecordDraft draft, Action release) { Draft = draft; this.release = release; }
    public RecordDraft Draft { get; }
    public void Dispose() { var action = release; release = null; action?.Invoke(); }
}

/// <summary>Model-owned editor state, independent of selection, pages, popup controls and storage queues.</summary>
public sealed class DraftWorkspace
{
    private sealed class Entry(RecordDraft draft)
    {
        public readonly RecordDraft Draft = draft;
        public int Leases;
        public SessionRow? Row;
        public long? SeriesId;
        public Recurrence? Rule;
        public HashSet<long> PendingNotes = new(), PendingSeries = new();
        public bool PendingLedger;
    }
    private readonly TimerModel model;
    private readonly Dictionary<Guid, Entry> entries = new();
    private bool refreshing;
    private int deferred;
    internal bool Deferring => deferred > 0;
    private readonly HashSet<RecordDraft> relinked = new();
    internal DraftWorkspace(TimerModel model) => this.model = model;
    public event Action? Changed;
    public int TrackedCount => entries.Count;
    public IReadOnlyList<RecordDraft> Outstanding => entries.Values.Select(e => e.Draft)
        .Where(d => d.IsDirty || d.PendingPersistence || d.Conflicts.Count > 0).ToArray();
    public bool HasUnsubmittedChanges => entries.Values.Any(e => e.Draft.IsDirty);
    private sealed class EndBatch(Action end) : IDisposable { private Action? action = end; public void Dispose() { var callback = action; action = null; callback?.Invoke(); } }
    internal IDisposable DeferModelChange()
    {
        deferred++;
        return new EndBatch(() =>
        {
            if (--deferred != 0) return;
            model.DraftBatchFinished();
            foreach (var draft in relinked.ToArray()) draft.Announce();
            relinked.Clear();
        });
    }
    internal void RelinkAccounts(IReadOnlyDictionary<string, string> names)
    {
        foreach (var entry in entries.Values) if (entry.Draft.RelinkAccounts(names)) relinked.Add(entry.Draft);
    }

    public DraftLease AcquireNote(long id)
    {
        var note = model.Notes.Find(id) ?? throw new ArgumentException("That item is no longer there.", nameof(id));
        var entry = entries.Values.FirstOrDefault(e => e.Draft.NoteId == note.Id);
        if (entry is null)
        {
            var draft = new RecordDraft(NotesModel.IsMeeting(note) ? DraftKind.Meeting : DraftKind.Note, Read(note)) { NoteId = note.Id };
            entry = Add(draft); entry.SeriesId = note.SeriesId; entry.Rule = model.Notes.SeriesOf(note)?.Rule;
            entry.PendingNotes.Add(note.Id); entry.Draft.PendingPersistence = IsPending(entry);
        }
        return Lease(entry);
    }

    public DraftLease AcquireBlock(SessionRow row)
    {
        if (!model.Engine.Sessions.Any(r => ReferenceEquals(r, row))) throw new ArgumentException("That block is no longer there.", nameof(row));
        if (model.Editor.IsGrouped(row)) throw new ArgumentException("Unmerge this block before editing its fields.", nameof(row));
        var entry = entries.Values.FirstOrDefault(e => ReferenceEquals(e.Row, row));
        if (entry is null) { entry = Add(new RecordDraft(DraftKind.Block, Read(row))); entry.Row = row; entry.PendingLedger = model.LedgerUnsaved; entry.Draft.PendingPersistence = entry.PendingLedger; }
        return Lease(entry);
    }

    private Entry Add(RecordDraft draft)
    {
        var entry = new Entry(draft); entries.Add(draft.Identity, entry);
        draft.Changed += () => { Prune(entry); Changed?.Invoke(); };
        return entry;
    }
    private DraftLease Lease(Entry entry) { entry.Leases++; RefreshEntry(entry); return new(entry.Draft, () => { entry.Leases--; Prune(entry); }); }
    private void Prune(Entry entry)
    {
        if (entry.Leases == 0 && !entry.Draft.IsDirty && !entry.Draft.PendingPersistence && !entry.Draft.IsSubmitting && entry.Draft.Conflicts.Count == 0)
            entries.Remove(entry.Draft.Identity);
    }
    public void Cancel(RecordDraft draft)
    {
        if (!Owns(draft, out var entry)) return;
        RefreshEntry(entry); draft.Cancel(); Prune(entry);
    }
    private bool Owns(RecordDraft draft, out Entry entry) => entries.TryGetValue(draft.Identity, out entry!) && ReferenceEquals(entry.Draft, draft);

    internal void NoteIdChanged(long previous, long current)
    {
        foreach (var entry in entries.Values)
            if (entry.Draft.NoteId == previous) entry.Draft.NoteId = current;
    }
    internal void RemapBlock(SessionRow previous, SessionRow current)
    {
        foreach (var entry in entries.Values) if (ReferenceEquals(entry.Row, previous)) entry.Row = current;
    }
    internal IReadOnlyList<SessionRow> BlockRows => entries.Values.Where(e => e.Row is not null).Select(e => e.Row!).ToArray();
    internal void Refresh()
    {
        if (refreshing || Deferring || entries.Count == 0) return;
        refreshing = true;
        try { foreach (var entry in entries.Values.ToArray()) { RefreshEntry(entry); Prune(entry); } }
        finally { refreshing = false; }
    }
    private void RefreshEntry(Entry entry)
    {
        var draft = entry.Draft; if (draft.IsSubmitting) return;
        if (Deferring) return;
        RecordValues? current = null;
        DraftConflict? recurrenceConflict = null;
        if (entry.Row is { } row)
        {
            if (!model.Engine.Sessions.Any(r => ReferenceEquals(r, row)))
            {
                // Undo/reload can restore an identical row. Never guess between duplicate candidates.
                var identical = model.Engine.Sessions.Where(r => r.Equals(row)).Take(2).ToArray();
                if (identical.Length == 1) entry.Row = row = identical[0];
            }
            if (model.Engine.Sessions.Any(r => ReferenceEquals(r, row)))
            {
                current = Read(row);
                if (current != draft.Accepted && model.LedgerUnsaved) entry.PendingLedger = true;
            }
        }
        else if (draft.NoteId is { } id && model.Notes.Find(id) is { } note && NotesModel.IsMeeting(note) == (draft.Kind == DraftKind.Meeting))
        {
            draft.NoteId = note.Id; current = Read(note);
            var rule = model.Notes.SeriesOf(note)?.Rule;
            if (draft.Kind == DraftKind.Meeting && draft.IsDirty && draft.AllLater && (entry.SeriesId != note.SeriesId || entry.Rule != rule))
                recurrenceConflict = new(DraftField.Repeat, entry.Rule, draft.Values.Repeat, rule);
            entry.SeriesId = note.SeriesId; entry.Rule = rule;
        }
        if (current is null)
        {
            var changed = !draft.Missing || draft.PendingPersistence != IsPending(entry);
            draft.Missing = true; draft.PendingPersistence = IsPending(entry); if (changed) draft.Announce(); return;
        }
        draft.Observe(current, recurrenceConflict is null ? null : DraftField.Repeat);
        if (recurrenceConflict is { } conflict) draft.MarkConflict(conflict.Field, conflict.Original, conflict.Yours, conflict.Current);
        var pending = IsPending(entry);
        if (draft.PendingPersistence != pending) { draft.PendingPersistence = pending; draft.Announce(); }
    }
    private bool IsPending(Entry entry)
    {
        entry.PendingNotes.RemoveWhere(id => !model.Notes.IsNoteWritePending(id));
        entry.PendingSeries.RemoveWhere(id => !model.Notes.IsSeriesWritePending(id));
        if (!model.LedgerUnsaved) entry.PendingLedger = false;
        return entry.PendingLedger || entry.PendingNotes.Count > 0 || entry.PendingSeries.Count > 0
            || entry.Draft.NoteId is { } id && model.Notes.IsNoteWritePending(id);
    }

    private RecordValues Read(DayNote note) => new(model.Notes.Words(note), model.Notes.AccountOf(note), NotesModel.IsMeeting(note) ? note.Day.Date : default,
        NotesModel.IsMeeting(note) ? TimeText.ShowClock(note.Day.Date + note.At!.Value, note.Day.Date) : "",
        NotesModel.IsMeeting(note) ? TimeText.ShowClock(note.Day.Date + note.End!.Value, note.Day.Date) : "",
        NotesModel.ReminderOf(note), NotesModel.IsImportant(note), model.Notes.RepeatOf(note));
    private RecordValues Read(SessionRow row, string? note = null) => new(note ?? model.Editor.Note(row), row.Account, row.From.Date,
        TimeText.ShowClock(row.From, row.From.Date), TimeText.ShowClock(row.To, row.From.Date), null, false, RepeatChoice.None);

    public DraftSaveResult Validate(RecordDraft draft)
    {
        if (Deferring) return Result(DraftSaveOutcome.Busy, "The model is applying another change.");
        if (!Owns(draft, out var entry)) return Result(DraftSaveOutcome.Refused, "This draft is no longer open.");
        RefreshEntry(entry);
        if (draft.IsSubmitting) return Result(DraftSaveOutcome.Busy);
        if (draft.Missing) return Result(DraftSaveOutcome.Missing, "That item is no longer there. Your draft is retained.");
        if (draft.Conflicts.Count > 0) return Result(DraftSaveOutcome.Conflict, "Resolve the changed fields before saving.");
        var errors = ValidateValues(entry, out _, out _); draft.Errors = errors;
        if (errors.Count > 0) { draft.Announce(); return new(DraftSaveOutcome.Invalid, errors); }
        return Result(DraftSaveOutcome.NoChanges);
    }

    private List<DraftError> ValidateValues(Entry entry, out DateTime start, out DateTime end)
    {
        var d = entry.Draft; var v = d.Values; start = end = default; var errors = new List<DraftError>();
        var fields = d.Kind switch
        {
            DraftKind.Note => new[] { DraftField.Words, DraftField.Account, DraftField.Reminder, DraftField.Important },
            DraftKind.Meeting => new[] { DraftField.Words, DraftField.Account, DraftField.Day, DraftField.Start, DraftField.End, DraftField.Repeat },
            _ => new[] { DraftField.Words, DraftField.Account, DraftField.Day, DraftField.Start, DraftField.End }
        };
        foreach (var field in RecordDraft.Fields.Except(fields))
            if (!Equals(v.Get(field), d.Accepted.Get(field))) errors.Add(new(field, "This field is not editable for this item."));
        if (d.Kind != DraftKind.Block && string.IsNullOrWhiteSpace(v.Words)) errors.Add(new(DraftField.Words, d.Kind == DraftKind.Note ? "Write a note first" : "Name the meeting"));
        if (v.Account is { } account && !model.ReviewAccounts.Any(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))
            && !(d.Kind == DraftKind.Block && model.Engine.Sessions.Any(r => r.Account.Equals(account, StringComparison.OrdinalIgnoreCase))))
            errors.Add(new(DraftField.Account, "Choose an existing account."));
        if (d.Kind == DraftKind.Block && v.Account is null) errors.Add(new(DraftField.Account, "Choose an account."));
        if (d.Kind == DraftKind.Note && v.Reminder != d.Accepted.Reminder && v.Reminder is { } when && when <= model.Engine.Now)
            errors.Add(new(DraftField.Reminder, "Choose a future reminder."));
        if (d.Kind != DraftKind.Note)
        {
            if (v.Day.Date == DateTime.MaxValue.Date) { errors.Add(new(DraftField.Day, "Choose an earlier day.")); return errors; }
            if (!TimeText.TryParseClock(v.Start, v.Day, false, out start)) errors.Add(new(DraftField.Start, "Start: try 09:30"));
            else
            {
                if (!TimeText.TryParseClockAfter(v.End, start, false, true, out end)) errors.Add(new(DraftField.End, "End: try 10:30"));
                else
                {
                    if (end == start && v.Start == d.Accepted.Start && v.End == d.Accepted.End
                        && TimeText.TryParseClockAfter(d.Accepted.End, start, true, true, out var originalEnd) && originalEnd - start == TimeSpan.FromDays(1)) end = originalEnd;
                    if (end <= start || end - start > TimeSpan.FromDays(1)) errors.Add(new(DraftField.End, "End must be after start, within 24 hours."));
                }
            }
            if (d.Kind == DraftKind.Block && entry.Row is { } row && model.Editor.IsGrouped(row)) errors.Add(new(DraftField.Start, "Unmerge this block before editing its fields."));
            if (d.Kind == DraftKind.Meeting && d.AllLater && d.NoteId is { } id && model.Notes.Find(id) is { } note && model.Notes.IsArchivedRepeat(note))
                errors.Add(new(DraftField.Repeat, "This repeat can only be edited one occurrence at a time."));
        }
        return errors;
    }

    public DraftSaveResult Save(RecordDraft draft)
    {
        var validation = Validate(draft); if (validation.Outcome != DraftSaveOutcome.NoChanges) return validation;
        if (!Owns(draft, out var entry)) return Result(DraftSaveOutcome.NoChanges);
        if (!model.CanEditRecords) return Result(DraftSaveOutcome.Refused, "The timer is closing or its storage is closed.");
        if (!draft.IsDirty) return Result(draft.PendingPersistence ? DraftSaveOutcome.PendingPersistence : DraftSaveOutcome.NoChanges);
        ValidateValues(entry, out var start, out var end);
        var submitted = draft.Values; var revision = draft.Revision; var later = draft.AllLater;
        draft.IsSubmitting = true; draft.Announce();
        DraftSaveResult result;
        try
        {
            string? problem = null; bool saved = true;
            RecordValues? commandValues = null;
            if (draft.Kind == DraftKind.Block)
            {
                saved = model.EditEntryDraft(entry.Row!, submitted.Account!, start, end, submitted.Words, (row, note) => commandValues = Read(row, note));
                if (commandValues is null) problem = model.EditError ?? model.LastError ?? "That change was refused.";
                if (problem is null) entry.PendingLedger = !saved;
            }
            else
            {
                var note = model.Notes.Find(draft.NoteId!.Value)!;
                var beforeSeries = note.SeriesId;
                entry.PendingNotes.UnionWith(RelatedNotes(note, later));
                if (beforeSeries is { } series) entry.PendingSeries.Add(series);
                DayNote? acceptedNote;
                problem = draft.Kind == DraftKind.Note
                    ? model.Notes.EditNoteDraft(note.Id, submitted.Words, submitted.Account, submitted.Reminder, submitted.Important, out acceptedNote)
                    : model.Notes.EditMeetingDraft(note.Id, submitted.Words, submitted.Account, submitted.Day, start.TimeOfDay, end - submitted.Day.Date, submitted.Repeat, later, out acceptedNote);
                if (acceptedNote is not null) commandValues = Read(acceptedNote) with { Repeat = submitted.Repeat };
                if (model.Notes.Find(note.Id) is { } after)
                {
                    entry.PendingNotes.UnionWith(RelatedNotes(after, later));
                    if (after.SeriesId is { } seriesId) entry.PendingSeries.Add(seriesId);
                }
            }
            if (problem is not null) { draft.Refusal = problem; result = Result(DraftSaveOutcome.Refused, problem); }
            else
            {
                var accepted = commandValues!;
                if (draft.NoteId is { } id && model.Notes.Find(id) is { } note)
                { entry.SeriesId = note.SeriesId; entry.Rule = model.Notes.SeriesOf(note)?.Rule; }
                draft.Accept(accepted, submitted, revision, IsPending(entry));
                result = Result(draft.PendingPersistence ? DraftSaveOutcome.PendingPersistence : DraftSaveOutcome.Saved);
            }
        }
        finally { draft.IsSubmitting = false; RefreshEntry(entry); draft.Announce(); Prune(entry); }
        return result;
    }

    private HashSet<long> RelatedNotes(DayNote note, bool later) => later && note.SeriesId is { } id
        ? model.Notes.All.Where(n => n.Id == note.Id || n.SeriesId == id && n.Day.Date >= note.Day.Date).Select(n => n.Id).ToHashSet()
        : new() { note.Id };

    /// <summary>Validate the whole proposed group before writing any of it; refused/queued items remain owned.</summary>
    public IReadOnlyList<(Guid Identity, DraftSaveResult Result)> SaveAll()
    {
        var drafts = entries.Values.Select(e => e.Draft).Where(d => d.IsDirty).ToArray();
        var checks = drafts.Select(d => (d.Identity, Result: Validate(d))).ToArray();
        if (checks.Any(c => c.Result.Outcome != DraftSaveOutcome.NoChanges))
            return checks.Select(c => (c.Identity, c.Result.Outcome == DraftSaveOutcome.NoChanges ? Result(DraftSaveOutcome.Refused, "Correct the other drafts before saving this group.") : c.Result)).ToArray();
        return drafts.Select(d => (d.Identity, Save(d))).ToArray();
    }
    private static DraftSaveResult Result(DraftSaveOutcome outcome, string? message = null) => new(outcome, Array.Empty<DraftError>(), message);
}
