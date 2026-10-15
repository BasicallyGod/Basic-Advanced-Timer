using Timer.Core;
using Timer.Desk;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PomodoroTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0);

    [Fact]
    public void ItStartsIdleAndBeginsAWorkBlockWhenStarted()
    {
        var p = new Pomodoro(); Assert.Equal(PomodoroPhase.Idle, p.Phase); Assert.False(p.Running);
        p.Start(T0); Assert.Equal(PomodoroPhase.Work, p.Phase); Assert.True(p.Running);
        Assert.Equal(TimeSpan.FromMinutes(20), p.Remaining(T0.AddMinutes(5))); Assert.Equal(0.2, p.Fraction(T0.AddMinutes(5)), 3);
    }

    [Fact]
    public void PausingKeepsTheTimeAndResumingCarriesOn()
    {
        var p = new Pomodoro(); p.Start(T0); p.Pause(T0.AddMinutes(10));
        Assert.False(p.Running); Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(T0.AddHours(3))); // Time passing while paused costs nothing.
        p.Start(T0.AddHours(3)); Assert.Equal(TimeSpan.FromMinutes(10), p.Remaining(T0.AddHours(3).AddMinutes(5)));
    }

    [Fact]
    public void AFinishedWorkBlockMovesToAShortBreakAndWaitsWithANudge()
    {
        var p = new Pomodoro(); p.Start(T0); p.Update(T0.AddMinutes(24)); Assert.Equal(PomodoroPhase.Work, p.Phase); Assert.False(p.Nudge);
        p.Update(T0.AddMinutes(25));
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase); Assert.False(p.Running); Assert.True(p.Nudge); Assert.Equal(1, p.Completed);
        Assert.Equal(TimeSpan.FromMinutes(5), p.Remaining(T0.AddHours(1))); // Waiting costs nothing either.
        p.Start(T0.AddHours(1)); Assert.False(p.Nudge); Assert.True(p.Running);
    }

    [Fact]
    public void EveryFourthWorkBlockEarnsALongBreak()
    {
        var p = new Pomodoro(); var now = T0; var phases = new List<PomodoroPhase>();
        for (var i = 0; i < 4; i++)
        {
            p.Start(now); now = now.AddMinutes(25); p.Update(now); phases.Add(p.Phase);
            if (i < 3) { p.Start(now); now = now.AddMinutes(5); p.Update(now); }
        }
        Assert.Equal(new[] { PomodoroPhase.ShortBreak, PomodoroPhase.ShortBreak, PomodoroPhase.ShortBreak, PomodoroPhase.LongBreak }, phases);
        Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(now));
    }

    [Fact]
    public void SkipEndsThePhaseAndResetStartsOver()
    {
        var p = new Pomodoro(); p.Start(T0); p.Skip(T0.AddMinutes(3));
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase); Assert.Equal(1, p.Completed);
        p.Reset(); Assert.Equal(PomodoroPhase.Idle, p.Phase); Assert.Equal(0, p.Completed); Assert.False(p.Nudge);
    }

    [Fact]
    public void ANoRunningTimerNeverAdvancesByItself() { var p = new Pomodoro(); p.Update(T0.AddDays(1)); Assert.Equal(PomodoroPhase.Idle, p.Phase); }
}
