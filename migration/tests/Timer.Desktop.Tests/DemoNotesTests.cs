using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DemoNotesTests
{
    [Fact]
    public void TheDemoHasNotesAndRemindersToday()
    {
        var demo = TimerModel.CreateDemo();
        var today = demo.NotesOn(demo.Engine.Now.Date);
        Assert.True(today.Count >= 3);
        Assert.Contains(today, n => n.At is not null);
        Assert.Contains(today, n => DayNotes.IsDeadline(n));
        Assert.Contains(today, n => n.Text.Contains("#Accountone"));
        Assert.Null(demo.Notice);
    }

    [Fact]
    public void TheDemoLooksFarAheadSoComingUpIsFull()
    {
        var demo = TimerModel.CreateDemo();
        var ahead = demo.ComingUp();
        var last = ahead.Max(n => n.Day);
        Assert.True((last - demo.Engine.Now.Date).TotalDays > 60);
        Assert.Contains(ahead, n => n.Day.Date > demo.Engine.Now.Date.AddDays(14));
    }
}
