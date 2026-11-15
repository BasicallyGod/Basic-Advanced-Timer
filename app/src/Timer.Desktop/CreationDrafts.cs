using Timer.Core;

namespace Timer.Desktop;

/// <summary>Independent add forms belong to the model, so hiding or rebuilding a view cannot lose them.</summary>
public sealed class CreationDrafts(TimerModel model)
{
    private sealed record Entry(RecordValues Initial, RecordValues Values);
    private readonly Dictionary<(string Context, bool Meeting), Entry> forms = new();
    public event Action? Changed;
    public event Action? Reset;
    public IReadOnlyList<(string Context, bool Meeting, RecordValues Values)> Outstanding => forms.Where(p => p.Value.Values != p.Value.Initial)
        .Select(p => (p.Key.Context, p.Key.Meeting, p.Value.Values)).ToArray();
    private RecordValues Empty() => new("", null, model.SelectedDate.Date, "12:00", "12:30", null, false, RepeatChoice.None);
    public RecordValues Get(string context, bool meeting)
    {
        var key = (context, meeting);
        if (!forms.TryGetValue(key, out var entry)) { var initial = Empty(); forms[key] = entry = new(initial, initial); }
        return entry.Values;
    }
    public void Update(string context, bool meeting, RecordValues values)
    {
        Get(context, meeting); var key = (context, meeting); var entry = forms[key];
        if (entry.Values == values) return; forms[key] = entry with { Values = values }; Changed?.Invoke();
    }
    public void Clear(string context, bool meeting)
    { var empty = Empty(); forms[(context, meeting)] = new(empty, empty); Reset?.Invoke(); Changed?.Invoke(); }
    public void DiscardAll() { forms.Clear(); Reset?.Invoke(); Changed?.Invoke(); }
    public string? Validate(bool meeting, RecordValues values)
    {
        if (string.IsNullOrWhiteSpace(values.Words)) return meeting ? "Name the meeting" : "Write a note first";
        if (values.Account is { } account && !model.ReviewAccounts.Any(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase) && !BreakAccount.Is(a.Name))) return "Choose an existing account";
        if (!meeting) return values.Reminder is { } reminder && reminder <= model.Engine.Now ? "Choose a future reminder." : null;
        if (values.Day.Date == DateTime.MaxValue.Date) return "Choose an earlier day.";
        if (!TimeText.TryParseClock(values.Start, values.Day, false, out var start)) return "Start: try 09:30";
        if (!TimeText.TryParseClockAfter(values.End, start, false, true, out var end)) return "End: try 10:00";
        if (end <= start) return "The end has to be after the start.";
        if (end - start > TimeSpan.FromDays(1)) return "At most 24 hours";
        return null;
    }
    public string? Save(string context, bool meeting)
    {
        var values = Get(context, meeting); if (Validate(meeting, values) is { } problem) return problem;
        string? error;
        if (meeting)
        {
            TimeText.TryParseClock(values.Start, values.Day, false, out var start);
            TimeText.TryParseClockAfter(values.End, start, false, true, out var end);
            error = model.Notes.AddMeeting(values.Day, start.TimeOfDay, end - values.Day.Date, values.Words.Trim(), values.Account, values.Repeat);
        }
        else error = model.Notes.AddNote(values.Words.Trim(), values.Account, values.Reminder, values.Important);
        if (error is null && Get(context, meeting) == values) Clear(context, meeting);
        return error;
    }
}
