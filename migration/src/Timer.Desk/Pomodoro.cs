using Timer.Core;

namespace Timer.Desk;

public enum PomodoroPhase { Idle, Work, ShortBreak, LongBreak }

/// <summary>
/// A pomodoro timer: a block of work, then a short rest, and a longer one after every few blocks. It is independent of time tracking and
/// takes the time it is given, so it can be tested. When a phase ends it moves to the next one and waits to be started, with
/// <see cref="Nudge"/> set, so the end is a quiet signal rather than something that grabs attention.
/// </summary>
public sealed class Pomodoro
{
    public TimeSpan WorkLength { get; } = TimeSpan.FromMinutes(25);
    public TimeSpan ShortBreakLength { get; } = TimeSpan.FromMinutes(5);
    public TimeSpan LongBreakLength { get; } = TimeSpan.FromMinutes(15);
    public int RoundsBeforeLongBreak { get; } = 4;

    private DateTime? runningSince; private TimeSpan carried;
    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public bool Running => runningSince is not null;
    /// <summary>Work blocks finished since the last reset.</summary>
    public int Completed { get; private set; }
    /// <summary>The account the focus is for, if one was chosen. It is a label on the session and nothing more: it never touches recorded time.</summary>
    public string? Account { get; set; }
    private readonly Dictionary<string, int> completedFor = new();
    /// <summary>Work blocks finished for each account since the last reset.</summary>
    public IReadOnlyDictionary<string, int> CompletedFor => completedFor;
    /// <summary>True from the end of a phase until the next one is started.</summary>
    public bool Nudge { get; private set; }

    public TimeSpan LengthOf(PomodoroPhase phase) => phase switch { PomodoroPhase.ShortBreak => ShortBreakLength, PomodoroPhase.LongBreak => LongBreakLength, _ => WorkLength };
    public TimeSpan Elapsed(DateTime now) => carried + (runningSince is { } since ? Positive(now - since) : TimeSpan.Zero);
    public TimeSpan Remaining(DateTime now) => Positive(LengthOf(Phase) - Elapsed(now));
    /// <summary>How far through the phase it is, 0 to 1.</summary>
    public double Fraction(DateTime now) => Math.Clamp(Elapsed(now).TotalSeconds / LengthOf(Phase).TotalSeconds, 0, 1);
    private static TimeSpan Positive(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    /// <summary>Starts, or carries on after a pause. From idle it begins a work block.</summary>
    public void Start(DateTime now)
    {
        if (Running) return;
        if (Phase == PomodoroPhase.Idle) { Phase = PomodoroPhase.Work; carried = TimeSpan.Zero; }
        Nudge = false; runningSince = now;
    }
    public void Pause(DateTime now) { if (runningSince is null) return; carried = Elapsed(now); runningSince = null; }
    public void Toggle(DateTime now) { if (Running) Pause(now); else Start(now); }
    public void Reset() { Phase = PomodoroPhase.Idle; runningSince = null; carried = TimeSpan.Zero; Completed = 0; Nudge = false; completedFor.Clear(); }
    /// <summary>Ends the phase now and waits at the start of the next.</summary>
    public void Skip(DateTime now) { if (Phase != PomodoroPhase.Idle) Finish(); }

    /// <summary>Moves on when the running phase has run out. Call it as time passes.</summary>
    public void Update(DateTime now) { if (Running && Remaining(now) == TimeSpan.Zero) Finish(); }

    private void Finish()
    {
        if (Phase == PomodoroPhase.Work) { Completed++; if (Account is { } who) completedFor[who] = completedFor.GetValueOrDefault(who) + 1; Phase = Completed % RoundsBeforeLongBreak == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak; }
        else Phase = PomodoroPhase.Work;
        runningSince = null; carried = TimeSpan.Zero; Nudge = true;
    }
}
