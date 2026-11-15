namespace Timer.Desktop;

public enum DraftKind { Note, Meeting, Block }
public enum DraftField { Words, Account, Day, Start, End, Reminder, Important, Repeat, Length }
public enum DraftPhase { Clean, Draft, Invalid, Conflict, Submitting, Missing }
public enum DraftSaveOutcome { NoChanges, Saved, PendingPersistence, Invalid, Conflict, Missing, Refused, Busy }
public sealed record DraftError(DraftField Field, string Message);
public sealed record DraftConflict(DraftField Field, object? Original, object? Yours, object? Current);
public sealed record DraftSaveResult(DraftSaveOutcome Outcome, IReadOnlyList<DraftError> Errors, string? Message = null);

/// <summary>Editor text is retained verbatim, including intermediate invalid clock values.</summary>
public sealed record RecordValues(string Words, string? Account, DateTime Day, string Start, string End,
    DateTime? Reminder, bool Important, RepeatChoice Repeat, string? Length = null)
{
    internal object? Get(DraftField field) => field switch
    {
        DraftField.Words => Words, DraftField.Account => Account, DraftField.Day => Day,
        DraftField.Start => Start, DraftField.End => End, DraftField.Reminder => Reminder,
        DraftField.Important => Important, DraftField.Repeat => Repeat, DraftField.Length => Length, _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
    internal RecordValues With(DraftField field, object? value) => field switch
    {
        DraftField.Words => this with { Words = (string)value! }, DraftField.Account => this with { Account = (string?)value },
        DraftField.Day => this with { Day = (DateTime)value! }, DraftField.Start => this with { Start = (string)value! },
        DraftField.End => this with { End = (string)value! }, DraftField.Reminder => this with { Reminder = (DateTime?)value },
        DraftField.Important => this with { Important = (bool)value! }, DraftField.Repeat => this with { Repeat = (RepeatChoice)value! },
        DraftField.Length => this with { Length = (string?)value },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}

/// <summary>Local edit state and accepted persistence state are independent. No view or timer is retained.</summary>
public sealed class RecordDraft
{
    internal static readonly DraftField[] Fields = Enum.GetValues<DraftField>();
    private readonly Dictionary<DraftField, DraftConflict> conflicts = new();
    private RecordValues baseline;
    private bool allLater;
    internal RecordDraft(DraftKind kind, RecordValues values) { Kind = kind; baseline = Values = values; }
    public Guid Identity { get; } = Guid.NewGuid();
    public DraftKind Kind { get; }
    public long? NoteId { get; internal set; }
    public RecordValues Values { get; private set; }
    public RecordValues Accepted => baseline;
    public long Revision { get; private set; }
    public long? SubmittedRevision { get; internal set; }
    public bool PendingPersistence { get; internal set; }
    public bool Missing { get; internal set; }
    public bool IsSubmitting { get; internal set; }
    public bool IsDirty => Values != baseline;
    public IReadOnlyCollection<DraftConflict> Conflicts => conflicts.Values.ToArray();
    public IReadOnlyList<DraftError> Errors { get; internal set; } = Array.Empty<DraftError>();
    public string? Refusal { get; internal set; }
    public DraftPhase Phase => IsSubmitting ? DraftPhase.Submitting : Missing ? DraftPhase.Missing
        : conflicts.Count > 0 ? DraftPhase.Conflict : Errors.Count > 0 ? DraftPhase.Invalid : IsDirty ? DraftPhase.Draft : DraftPhase.Clean;
    public bool AllLater { get => allLater; set { if (allLater == value) return; allLater = value; Revision++; Changed?.Invoke(); } }
    public event Action? Changed;

    public void Update(RecordValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (Values == values) return;
        Values = values; Revision++; Errors = Array.Empty<DraftError>(); Refusal = null;
        foreach (var field in conflicts.Keys.ToArray())
            if (Equals(Values.Get(field), baseline.Get(field))) conflicts.Remove(field);
            else conflicts[field] = conflicts[field] with { Yours = Values.Get(field) };
        Changed?.Invoke();
    }

    /// <summary>Resolve one conflicting field explicitly; the other fields and versions remain available.</summary>
    public void Resolve(DraftField field, bool keepMine)
    {
        if (!conflicts.Remove(field)) return;
        if (!keepMine) Values = Values.With(field, baseline.Get(field));
        Revision++; Errors = Array.Empty<DraftError>(); Refusal = null; Changed?.Invoke();
    }

    internal void Observe(RecordValues current, DraftField? retainContext = null)
    {
        var previous = Values; var previousBase = baseline; var previousConflicts = conflicts.Count;
        foreach (var field in Fields)
        {
            var accepted = baseline.Get(field); var yours = Values.Get(field); var latest = current.Get(field);
            if (!Equals(accepted, latest))
            {
                if (field != retainContext && Equals(yours, accepted) || Equals(yours, latest)) { Values = Values.With(field, latest); conflicts.Remove(field); }
                else conflicts[field] = new(field, conflicts.TryGetValue(field, out var conflict) ? conflict.Original : accepted, yours, latest);
            }
        }
        baseline = current; Missing = false;
        if (Values != previous || baseline != previousBase || conflicts.Count != previousConflicts) Changed?.Invoke();
    }

    internal void Accept(RecordValues accepted, RecordValues submitted, long revision, bool pending)
    {
        // An acknowledgement of the submitted values cannot erase text typed during the command.
        foreach (var field in Fields)
            if (Equals(Values.Get(field), submitted.Get(field))) Values = Values.With(field, accepted.Get(field));
        baseline = accepted; SubmittedRevision = revision; PendingPersistence = pending;
        Errors = Array.Empty<DraftError>(); Refusal = null; conflicts.Clear(); Missing = false;
    }
    internal void MarkConflict(DraftField field, object? original, object? yours, object? current)
    {
        conflicts[field] = new(field, original, yours, current); Changed?.Invoke();
    }
    internal bool RelinkAccounts(IReadOnlyDictionary<string, string> names)
    {
        string? Relink(string? value) => value is not null && names.FirstOrDefault(p => p.Key.Equals(value, StringComparison.OrdinalIgnoreCase)) is { Key: not null } pair ? pair.Value : value;
        var previous = Values; var previousBase = baseline;
        Values = Values with { Account = Relink(Values.Account) }; baseline = baseline with { Account = Relink(baseline.Account) };
        if (conflicts.TryGetValue(DraftField.Account, out var c))
            conflicts[DraftField.Account] = c with { Original = Relink(c.Original as string), Yours = Relink(c.Yours as string), Current = Relink(c.Current as string) };
        return Values != previous || baseline != previousBase;
    }

    internal void Cancel()
    {
        Values = baseline; conflicts.Clear(); Errors = Array.Empty<DraftError>(); Refusal = null; allLater = false; Revision++;
        // A submitted retry is accepted model data, not an unsubmitted form edit that Cancel can undo.
        Changed?.Invoke();
    }
    internal void Announce() => Changed?.Invoke();
}
