using System.Text.Json;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TrackingTests
{
    [Fact]
    public void APastDaysOvertimeStopsAtItsLastActivityNotAtMidnight()
    {
        var now = new DateTime(2026, 10, 9, 14, 0, 0); // A Friday; the shift is 09:00 to 17:00.
        var engine = new TrackingEngine(clock: () => now);
        var tuesday = new DateTime(2026, 10, 6);
        Assert.Equal(new DayTotals(TimeSpan.FromHours(8), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromHours(8)), engine.Totals(tuesday)); // Nothing recorded.
        engine.AddSession("A", tuesday.AddHours(9), tuesday.AddHours(12));
        var worked = engine.Totals(tuesday);
        Assert.Equal(TimeSpan.FromHours(8), worked.Working); Assert.Equal(TimeSpan.Zero, worked.Overtime); Assert.Equal(TimeSpan.FromHours(5), worked.Unallocated);
        engine.AddSession("A", tuesday.AddHours(17), tuesday.AddHours(19)); // Two hours after the shift.
        var late = engine.Totals(tuesday);
        Assert.Equal(TimeSpan.FromHours(2), late.Overtime); Assert.Equal(TimeSpan.FromHours(5), late.Unallocated);
        Assert.Equal(TimeSpan.Zero, engine.Totals(new DateTime(2026, 10, 10)).Working); // A day off is still empty.
        Assert.Equal(TimeSpan.FromHours(5), engine.Totals(now.Date).Working); // Today still runs up to now, 09:00 to 14:00.
    }

    [Fact]
    public void DayClockContinuesWhileAccountsPauseOrSwitch()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 17);
        var engine = new TrackingEngine(clock: () => now);
        engine.StartDay();
        now = now.AddMinutes(20);
        Assert.Equal(TimeSpan.FromMinutes(20), engine.Totals(now).Elapsed);
        engine.ToggleAccount("A"); now = now.AddMinutes(5); engine.ToggleAccount("A");
        now = now.AddMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(35), engine.Totals(now).Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Credited("A", now.Date, now.Date.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(30), engine.Totals(now).Unallocated);
        engine.ToggleAccount("B"); now = now.AddMinutes(2); engine.ToggleAccount("C");
        Assert.Equal(TimeSpan.FromMinutes(37), engine.Totals(now).Elapsed);
        Assert.Equal(TimeSpan.Zero, engine.ActiveElapsed);
        Assert.Equal(17, engine.Plan(now).Start.Second);
        Assert.Equal(9, engine.Plan(now.AddDays(3)).Start.Hour);
    }

    [Fact]
    public void FinishResumeLunchAndOvertimeMatchEstablishedDayControls()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 17);
        var engine = new TrackingEngine(clock: () => now);
        engine.StartDay(); now = now.AddMinutes(20); engine.FinishDay();
        now = now.AddMinutes(10); Assert.Equal(TimeSpan.FromMinutes(20), engine.Totals(now).Elapsed);
        engine.StartDay(); now = now.AddMinutes(10); Assert.Equal(TimeSpan.FromMinutes(30), engine.Totals(now).Elapsed);
        engine.ToggleAccount("Lunch"); now = now.AddMinutes(15);
        Assert.Equal(TimeSpan.FromMinutes(30), engine.Totals(now).Elapsed);
        engine.ToggleAccount("Lunch");
        now = now.Date.AddHours(18); engine.FinishDay(); now = now.AddMinutes(15);
        Assert.Equal(TimeSpan.FromHours(1), engine.Totals(now).Overtime);
        engine.StartDay(); now = now.AddMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(70), engine.Totals(now).Overtime);
    }

    [Fact]
    public void DayOffLateStartDoesNotInventEarlierOvertime()
    {
        var now = new DateTime(2026, 10, 10, 18, 0, 0);
        var engine = new TrackingEngine(clock: () => now);
        Assert.False(engine.DayRunning);
        engine.StartDay(); Assert.Equal(TimeSpan.Zero, engine.Totals(now).Overtime);
        now = now.AddMinutes(5); Assert.Equal(TimeSpan.FromMinutes(5), engine.Totals(now).Overtime);
    }

    [Fact]
    public void SessionsSplitAtMidnightAndCreditAdjustmentsRemainDistinct()
    {
        var engine = new TrackingEngine();
        engine.AddSession("A", new(2026, 10, 9, 23, 59, 0), new(2026, 10, 10, 0, 1, 0));
        Assert.Equal(2, engine.Sessions.Count);
        Assert.All(engine.Sessions, s => Assert.Equal(TimeSpan.FromMinutes(1), s.Duration));
        var adjustment = new SessionRow("2026-10-09", "A", "10:00:00", "rounded down", "-5");
        var merged = new SessionRow("2026-10-09", "a", "11:00:00", "Merged", "30");
        engine.Sessions.Add(adjustment); engine.Sessions.Add(merged);
        Assert.False(adjustment.IsPositioned); Assert.False(merged.IsPositioned);
        Assert.Equal(TimeSpan.FromMinutes(26), engine.Credited("A", new(2026, 10, 9), new(2026, 10, 10)));
    }

    [Fact]
    public void OverlappingLunchAndAwayAreSubtractedOnlyOnce()
    {
        var now = new DateTime(2026, 10, 9, 11, 0, 0);
        var engine = new TrackingEngine(clock: () => now);
        engine.AddSession("Lunch", now.Date.AddHours(10), now.Date.AddHours(10.5));
        engine.Away.Add(new(now.Date.AddHours(10.25), now.Date.AddHours(10.75)));
        Assert.Equal(TimeSpan.FromMinutes(75), engine.Totals(now).Elapsed);
    }

    [Fact]
    public void RecordsRoundTripThroughTheDatabaseAndSettingsKeepUnknownSections()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json"), """
        {"schemaVersion":1,"unknownRoot":"keep","sections":{"workingHours":["start=09:30","end=18:00","days=Mon,Tue,Wed,Thu,Fri","hours=decimal","shift.fri=08:00,16:00"],"dayOverrides":["2026-10-10\toff"],"unrecognised":["keep me"]}}
        """);
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(clock: () => new(2026, 10, 9, 10, 0, 0));
            var settings = store.Load(engine);
            Assert.Equal(3, settings.Accounts.Count); Assert.Empty(engine.Sessions);
            Assert.True(settings.DecimalHours); Assert.Equal(8, engine.Plan(engine.Now).Start.Hour);
            settings = settings with { Accounts = new() { new("A, quoted", 10, "month"), new("Hidden", Hidden: true, Colour: "#112233") } };
            engine.AddSession("A, quoted", engine.Now.AddMinutes(-10), engine.Now);
            engine.AddSession(BreakAccount.Name, engine.Now.AddMinutes(-30), engine.Now.AddMinutes(-20));
            store.Save(engine, settings with { Theme = "Dark" });
            Assert.ThrowsAny<IOException>(() => new TimerStore(folder.Path));
        }
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(); var settings = store.Load(engine);
            Assert.Equal("Dark", settings.Theme); Assert.Equal(2, engine.Sessions.Count);
            Assert.Equal("month", settings.Accounts[0].BudgetPeriod); Assert.Equal(10, settings.Accounts[0].BudgetHours);
            Assert.True(settings.Accounts[1].Hidden); Assert.Equal("#112233", settings.Accounts[1].Colour);
            Assert.Equal(TimeSpan.FromMinutes(10), engine.Credited("A, quoted", new(2026, 10, 9), new(2026, 10, 10)));
            Assert.Equal(BreakAccount.Name, engine.Sessions[1].Account);
            Assert.Contains("keep me", File.ReadAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json")));
            Assert.Contains("unknownRoot", File.ReadAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json")));
        }
    }

    [Fact]
    public void OldTimesheetFilesAreNeverReadChangedOrDeleted()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "Timesheets"));
        var sessions = Path.Combine(folder.Path, "Timesheets", "sessions.csv");
        File.WriteAllText(sessions, "Date,Account,Start,End,Minutes\n2026-10-09,A,09:00:00,10:00:00,60\n");
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(); var settings = store.Load(engine);
            Assert.True(store.OldFilesFound); Assert.Empty(engine.Sessions);
            engine.AddSession("Account one", new(2026, 10, 9, 9, 0, 0), new(2026, 10, 9, 9, 5, 0)); store.Save(engine, settings);
        }
        Assert.Equal("Date,Account,Start,End,Minutes\n2026-10-09,A,09:00:00,10:00:00,60\n", File.ReadAllText(sessions));
        Assert.True(File.Exists(Path.Combine(folder.Path, "timer.db")));
        using (var store = new TimerStore(folder.Path)) { Assert.True(store.TakeOldFilesNotice()); Assert.False(store.TakeOldFilesNotice()); }
        using (var store = new TimerStore(folder.Path)) Assert.False(store.TakeOldFilesNotice()); // Said once, not on every launch.
    }

    [Fact]
    public void TheDemoRunsThroughADatabaseAndReloadsTheSameWithNoMergedCredit()
    {
        using var folder = new TempFolder(); int count;
        using (var store = new TimerStore(folder.Path))
        {
            var model = TimerModel.CreateDemo(store: store);
            count = model.Engine.Sessions.Count; Assert.True(count > 20); Assert.DoesNotContain(model.Engine.Sessions, s => s.IsMerged);
        }
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(); var prefs = store.Load(engine);
            Assert.Equal(count, engine.Sessions.Count); Assert.Equal(7, prefs.Accounts.Count);
        }
    }

    [Fact]
    public void RecoveryCreditsOnlyLastCheckpointAndIsNotRepeated()
    {
        using var folder = new TempFolder();
        var now = new DateTime(2026, 10, 9, 9, 0, 0);
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(clock: () => now); var settings = store.Load(engine);
            engine.ToggleAccount("Account one"); now = now.AddMinutes(3); store.Save(engine, settings);
        }
        now = now.AddHours(1);
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(clock: () => now); var settings = store.Load(engine);
            Assert.Single(engine.Sessions); Assert.Null(engine.Active);
            Assert.Equal(TimeSpan.FromMinutes(3), engine.Sessions[0].Duration); store.Save(engine, settings);
        }
        using (var store = new TimerStore(folder.Path))
        { var engine = new TrackingEngine(clock: () => now); store.Load(engine); Assert.Single(engine.Sessions); }
    }

    [Fact]
    public void AMalformedSettingsFileIsNotReset()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json"), "broken");
        using (var store = new TimerStore(folder.Path)) Assert.ThrowsAny<JsonException>(() => store.Load(new TrackingEngine()));
        Assert.Equal("broken", File.ReadAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json")));
    }
    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timer-test-" + Guid.NewGuid());
        public TempFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
