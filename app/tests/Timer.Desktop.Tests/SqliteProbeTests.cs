using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class SqliteProbeTests
{
    [Fact]
    public void SqliteLoadsWritesInATransactionAndReadsBack()
    {
        var version = SqliteProbe.Check();
        Assert.Matches(@"^3\.\d+\.\d+", version); // Any SQLite 3 will do; the point is that the native library loaded.
    }

    [Fact]
    public void RunningTheProbeTwiceLeavesNothingBehind()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "timer-sqlite-probe-*").Length;
        SqliteProbe.Check(); SqliteProbe.Check();
        Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "timer-sqlite-probe-*").Length);
    }
}
