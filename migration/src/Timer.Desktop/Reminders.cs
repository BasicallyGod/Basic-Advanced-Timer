using Timer.Core;

namespace Timer.Desktop;

public enum ReminderState { Soon, Due, Overdue, Followup }

/// <summary>A note with a time that is coming up, due or past. <see cref="Since"/> is when it last came due (the note's time, or the end of a snooze).</summary>
public sealed record Reminder(DayNote Note, ReminderState State, DateTime DueAt, DateTime Since);

/// <summary>
/// When a note should remind. Only notes with a time, and deadlines (which fall due at 17:00 when they have none), remind, and a ticked one never does.
/// A reminder is Soon for the lead time before it falls due, Due for five minutes after, and Overdue until it is ticked off or snoozed. It looks back a
/// day, so something that fell due while the app was closed still shows on start, but an old note never nags for ever. It is pure so it can be tested.
/// </summary>
public static class ReminderRules
{
    public static readonly TimeSpan DueFor = TimeSpan.FromMinutes(5), LookBack = TimeSpan.FromHours(24);
    public static readonly int[] LeadChoices = { 0, 5, 10, 15, 30 }, SnoozeChoices = { 5, 10, 15, 30 };
    public const int DefaultLead = 10, DefaultSnooze = 10;

    public static IReadOnlyList<Reminder> For(IEnumerable<DayNote> notes, DateTime now, int leadMinutes, IReadOnlyDictionary<long, DateTime>? snoozed = null)
    {
        var found = new List<Reminder>();
        foreach (var note in notes)
        {
            if (note.Done || (note.At is null && !DayNotes.IsDeadline(note))) continue;
            // Once a meeting is over it asks, once, whether it took place. Snoozing does not apply to the question, and an answer (done) ends it.
            if (DayNotes.EndsAt(note) is { } over && now >= over) { if (over >= now - LookBack) found.Add(new Reminder(note, ReminderState.Followup, over, over)); continue; }
            var due = DayNotes.DueAt(note);
            if (due < now - LookBack || due > now.AddMinutes(leadMinutes)) continue;
            var since = due;
            if (snoozed is not null && snoozed.TryGetValue(note.Id, out var until)) { if (until > now) continue; if (until > due) since = until; }
            var state = now < due ? ReminderState.Soon : now - since < DueFor ? ReminderState.Due : ReminderState.Overdue;
            if (state == ReminderState.Overdue && DayNotes.IsMeeting(note)) continue; // A meeting under way is not overdue.
            found.Add(new Reminder(note, state, due, since));
        }
        // What needs attention comes first, then the soonest.
        return found.OrderBy(r => r.State == ReminderState.Soon ? 1 : 0).ThenBy(r => r.DueAt).ThenBy(r => r.Note.Id).ToList();
    }

    private static string SoonIn(int minutes) => "In " + minutes + (minutes == 1 ? " minute" : " minutes");

    /// <summary>The second line of a reminder card.</summary>
    public static string Describe(Reminder reminder, DateTime now)
    {
        var minutes = (int)Math.Max(0, (now - reminder.DueAt).TotalMinutes);
        return reminder.State switch
        {
            ReminderState.Soon => SoonIn(Math.Max(1, (int)Math.Ceiling((reminder.DueAt - now).TotalMinutes))) + " · " + reminder.DueAt.ToString("HH:mm"),
            ReminderState.Due => "Due now · " + reminder.DueAt.ToString("HH:mm"),
            ReminderState.Followup => "Did it take place? · " + NoteTimes.Of(reminder.Note),
            _ => "Overdue by " + (minutes < 60 ? minutes + (minutes == 1 ? " minute" : " minutes") : (minutes / 60) + "h " + (minutes % 60) + "m")
        };
    }
}

/// <summary>
/// The short sound a reminder makes when it falls due, so one behind other windows, or while you are looking elsewhere, is not missed. It is the system's
/// own alert sound through one Windows call (no files, no install), follows the system volume, and does nothing on other systems.
/// </summary>
public static class ReminderSound
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    private const uint IconAsterisk = 0x40;
    public static void Play()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { MessageBeep(IconAsterisk); } catch (Exception) { /* No sound is better than a failed reminder. */ }
    }
}

/// <summary>How a note's time reads: 14:30, or 14:00–15:30 for a meeting.</summary>
public static class NoteTimes
{
    public static string Of(DayNote note)
        => note.At is not { } start ? "" : note.End is { } end && end > start ? start.ToString(@"hh\:mm") + "–" + end.ToString(@"hh\:mm") : start.ToString(@"hh\:mm");
}
