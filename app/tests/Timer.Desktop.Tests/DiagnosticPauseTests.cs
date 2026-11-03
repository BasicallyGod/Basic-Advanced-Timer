using System.Globalization;
using Avalonia.Headless.XUnit;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

[CollectionDefinition("Diagnostic pause state", DisableParallelization = true)]
public sealed class DiagnosticPauseStateCollection { }

[Collection("Diagnostic pause state")]
public sealed class DiagnosticPauseTests
{
    [AvaloniaFact]
    public void PausedVisualTicksPersistLedgerAndRunningCheckpointsWithoutRoutineNotifications()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-paused-checkpoint-" + Guid.NewGuid());
        var previous = TimerModel.TickPaused;
        try
        {
            using var store = new TimerStore(folder);
            var now = new DateTime(2026, 11, 2, 9, 0, 0);
            var engine = new TrackingEngine(clock: () => now);
            var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
            model.ToggleAccount("Account one");
            engine.Sessions.Add(new("2026-11-01", "Account one", "09:00:00", "added", "30"));
            var routine = 0;
            model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(TimerModel.Elapsed) or TimerModel.CheckpointChange) routine++; };
            TimerModel.TickPaused = true;
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.Null(model.LastError); Assert.Single(StorageFaults.Saved(store));
            var savedAt = store.Database.Query("SELECT last_seen FROM running", r => DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture)).Single();
            Assert.Equal(now, savedAt); Assert.True(model.IsTracking); Assert.Equal(0, routine);

            engine.Sessions.Add(new("2026-11-01", "Account one", "10:00:00", "added", "20"));
            var important = 0; model.PropertyChanged += (_, _) => important++;
            StorageFaults.Block(store);
            try
            {
                for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
                Assert.NotNull(model.LastError); Assert.True(important > 0); Assert.Single(StorageFaults.Saved(store));
            }
            finally { StorageFaults.Unblock(store); }
            for (var i = 0; i < 15; i++) { now = now.AddSeconds(1); model.Tick(); }
            Assert.Null(model.LastError); Assert.Equal(2, StorageFaults.Saved(store).Count);
            Assert.Equal(0, routine);
        }
        finally { TimerModel.TickPaused = previous; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void PausedTicksStillAnnounceDueRemindersAndMaintainDateAndRepeatingMeetings()
    {
        var now = new DateTime(2026, 11, 2, 9, 0, 0);
        var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup();
        model.Notes.AddNote("Due during pause", null, now.AddMinutes(1));
        model.Notes.AddMeeting(now.Date, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Daily meeting", null, RepeatChoice.Day);
        var initial = model.Notes.All.Count;
        var previous = TimerModel.TickPaused;
        try
        {
            TimerModel.TickPaused = true; model.Tick();
            var important = 0; model.PropertyChanged += (_, _) => important++;
            now = now.AddMinutes(2); model.Tick();
            Assert.True(model.ReminderDue); Assert.True(important > 0);
            var announced = important; now = now.AddMinutes(1); model.Tick();
            Assert.Equal(announced, important); // An unchanged alert does not restart routine redraws.
            now = now.AddDays(1); model.Tick();
            Assert.Equal(now.Date, model.SelectedDate); Assert.True(model.Notes.All.Count > initial);
        }
        finally { TimerModel.TickPaused = previous; }
    }
}
