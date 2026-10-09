using System.Text.Json;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TrackingTests
{
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
    public void CopiedLegacySettingsAndRowsRoundTripWithoutLosingMetadata()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "Timesheets"));
        File.WriteAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json"), """
        {"schemaVersion":1,"unknownRoot":"keep","sections":{"accounts":["A, quoted","Hidden\thidden"],"budgets":["A, quoted\t10\tmonth"],"workingHours":["start=09:30","end=18:00","days=Mon,Tue,Wed,Thu,Fri","hours=decimal","shift.fri=08:00,16:00"],"dayOverrides":["2026-10-10\toff"],"unrecognised":["keep me"]}}
        """);
        File.WriteAllText(Path.Combine(folder.Path, "Timesheets", "sessions.csv"), "Date,Account,Start,End,Minutes,Extra\n2026-10-09,\"A, quoted\",09:00:00,merged,60,untouched\n2026-10-09,\"A, quoted\",09:00:00,rounded down,-5,keep\n");
        var metadata = Path.Combine(folder.Path, "Timesheets", "entry-groups.csv"); File.WriteAllText(metadata, "unchanged metadata");
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(clock: () => new(2026, 10, 9, 10, 0, 0));
            var settings = store.Load(engine);
            Assert.Single(settings.Accounts.Where(a => !a.Hidden)); Assert.True(settings.Accounts.Single(a => a.Name == "Hidden").Hidden); Assert.Equal("month", settings.Accounts[0].BudgetPeriod);
            Assert.Equal(8, engine.Plan(engine.Now).Start.Hour);
            Assert.Equal(TimeSpan.FromMinutes(55), engine.Credited("A, quoted", engine.Now.Date, engine.Now.Date.AddDays(1)));
            engine.AddSession("A, quoted", engine.Now.AddMinutes(-10), engine.Now);
            store.Save(engine, settings with { Theme = "Dark" });
            Assert.Throws<IOException>(() => new TimerStore(folder.Path));
        }
        using (var store = new TimerStore(folder.Path))
        {
            var engine = new TrackingEngine(); var settings = store.Load(engine);
            Assert.Equal("Dark", settings.Theme); Assert.Equal(3, engine.Sessions.Count);
            Assert.Contains("untouched", File.ReadAllText(Path.Combine(folder.Path, "Timesheets", "sessions.csv")));
            Assert.Contains("keep me", File.ReadAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json")));
            Assert.Contains("unknownRoot", File.ReadAllText(Path.Combine(folder.Path, "basics-advanced-timer.settings.json")));
            Assert.Equal("unchanged metadata", File.ReadAllText(metadata));
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
    public void JournalReplayIsIdempotentAndMalformedFilesAreNotReset()
    {
        using var folder = new TempFolder();
        var files = new Dictionary<string, string>
        {
            ["Timesheets/sessions.csv"] = "Date,Account,Start,End,Minutes\n2026-10-09,A,09:00:00,09:01:00,1\n",
            ["basics-advanced-timer.settings.json"] = "{\"schemaVersion\":1,\"sections\":{}}",
            ["account-timer-away.txt"] = "", ["migration-recovery.json"] = JsonSerializer.Serialize(new RecoveryState(1, null, DateTime.Now)),
            ["account-timer-state.txt"] = ""
        };
        File.WriteAllText(Path.Combine(folder.Path, "migration-journal.json"), JsonSerializer.Serialize(new SaveJournal(1, files)));
        using (var store = new TimerStore(folder.Path)) { var engine = new TrackingEngine(); store.Load(engine); Assert.Single(engine.Sessions); }
        Assert.False(File.Exists(Path.Combine(folder.Path, "migration-journal.json")));
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
