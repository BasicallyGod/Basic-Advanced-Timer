using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>A note typed with an every phrase repeats: ordinary dated notes made eight weeks ahead, each of which can be dealt with alone or with every later one.</summary>
public sealed class RecurringMeetingTests : IDisposable
{
    private static readonly DateTime Wednesday = new(2026, 10, 21);
    private DateTime now = Wednesday.AddHours(8);
    private readonly string folder = Path.Combine(Path.GetTempPath(), "recurring-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }

    private TimerModel Model() { var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup(); return model; }
    private static List<DayNote> Series(TimerModel model) => model.AllDayNotes.Where(n => n.SeriesId is not null).OrderBy(n => n.Day).ToList();
    private static int Weekdays(DateTime from, DateTime to) { var count = 0; for (var d = from; d <= to; d = d.AddDays(1)) if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++; return count; }

    [Fact]
    public void AnEveryPhraseMakesTheNotesForTheNextEightWeeksOnlyOnTheDaysItNames()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup 09:30 - 10:00 every weekday");
        var notes = Series(model);
        Assert.Equal(Weekdays(Wednesday, Wednesday.AddDays(56)), notes.Count);
        Assert.All(notes, n => { Assert.Equal("Standup", n.Text); Assert.Equal(new TimeSpan(9, 30, 0), n.At); Assert.Equal(new TimeSpan(10, 0, 0), n.End); Assert.NotEqual(DayOfWeek.Saturday, n.Day.DayOfWeek); Assert.NotEqual(DayOfWeek.Sunday, n.Day.DayOfWeek); });
        Assert.Equal(Wednesday, notes[0].Day); Assert.True(notes[^1].Day <= Wednesday.AddDays(56));
        Assert.Contains("Repeats:", model.Notice);
        Assert.Equal("Every weekday", model.SeriesOf(notes[0])!.Rule.Words());
        Assert.Single(model.ComingUp(), n => n.SeriesId is not null); // One line for the whole series.
        Assert.Single(model.MeetingsOn(Wednesday)); // Each one is a meeting on its own day.
    }

    [Fact]
    public void ARepeatThatFallsOnLaterDaysStartsOnTheFirstOfThem()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Team call every mon"); // Typed on a Wednesday.
        Assert.Equal(new DateTime(2026, 10, 26), Series(model)[0].Day); Assert.All(Series(model), n => Assert.Equal(DayOfWeek.Monday, n.Day.DayOfWeek));
    }

    [Fact]
    public void ARemovedNoteIsNotMadeAgainWhileTheSeriesKeepsMakingNewOnes()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup every weekday");
        var thursday = Series(model).Single(n => n.Day == Wednesday.AddDays(1));
        model.RemoveDayNote(thursday.Id); // This one only.
        now = Wednesday.AddDays(1).AddHours(8); model.Tick();
        Assert.DoesNotContain(Series(model), n => n.Day == Wednesday.AddDays(1));         // Not made again.
        Assert.Contains(Series(model), n => n.Day == Wednesday.AddDays(57));              // But the new day comes into reach.
        Assert.NotNull(model.SeriesOf(Series(model)[0]));
    }

    [Fact]
    public void ThisAndAllLaterRemovesFromThatNoteOnAndStopsTheSeriesThere()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup every weekday");
        var cut = Series(model).First(n => n.Day == new DateTime(2026, 10, 28));
        model.RemoveSeriesFrom(cut.Id);
        Assert.Equal(new[] { 21, 22, 23, 26, 27 }, Series(model).Select(n => n.Day.Day));  // Earlier ones stay.
        Assert.Equal(new DateTime(2026, 10, 27), model.SeriesOf(Series(model)[0])!.Rule.Until);
        now = Wednesday.AddDays(10).AddHours(8); model.Tick();
        Assert.Equal(5, Series(model).Count);                                              // Nothing is made again.
        // Cutting from the very first note takes the series with it.
        var other = Model(); other.AddDayNote(Wednesday, "Gym every mon,wed"); other.RemoveSeriesFrom(Series(other)[0].Id);
        Assert.Empty(Series(other)); Assert.Empty(other.AllDayNotes);
    }

    [Fact]
    public void ApplyToLaterGivesTheWordsToEveryLaterNoteAndTheTemplateButNotEarlierOnes()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup every weekday");
        var mid = Series(model).First(n => n.Day == new DateTime(2026, 10, 23));
        model.SetDayNoteText(mid.Id, "Daily sync"); model.ApplyToLater(mid.Id);
        var notes = Series(model);
        Assert.All(notes.Where(n => n.Day > mid.Day), n => Assert.Equal("Daily sync", n.Text));
        Assert.All(notes.Where(n => n.Day < mid.Day), n => Assert.Equal("Standup", n.Text));
        Assert.Equal("Daily sync", model.SeriesOf(mid)!.Text);
        now = Wednesday.AddDays(1).AddHours(8); model.Tick();
        Assert.Equal("Daily sync", Series(model)[^1].Text); // The next ones are made from the new template.
    }

    [Fact]
    public void StoppingARepeatKeepsThisNoteWithTheWordsPutBackAndDropsTheRest()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup every weekday");
        var first = Series(model)[0];
        model.ClearNoteRepeat(first.Id);
        var notes = model.AllDayNotes; var kept = Assert.Single(notes);
        Assert.Null(kept.SeriesId); Assert.Equal("Standup every weekday", kept.Text); Assert.Null(model.SeriesOf(kept));
    }

    [Fact]
    public void ARepeatSurvivesARestartWithoutMakingTheNotesTwice()
    {
        int count;
        using (var store = new TimerStore(folder))
        {
            var engine = new TrackingEngine(clock: () => now); var model = new TimerModel(engine, store.Load(engine), store);
            model.AddDayNote(Wednesday, "Standup 09:30 - 10:00 every weekday until 2026-12-31"); count = Series(model).Count;
            Assert.True(count > 30);
        }
        using (var store = new TimerStore(folder))
        {
            var engine = new TrackingEngine(clock: () => now); var again = new TimerModel(engine, store.Load(engine), store);
            Assert.Equal(count, Series(again).Count); Assert.Equal(new DateTime(2026, 12, 31), again.SeriesOf(Series(again)[0])!.Rule.Until);
            now = Wednesday.AddDays(1).AddHours(8); again.Tick();
            Assert.Equal(count, Series(again).Count - Series(again).Count(n => n.Day > Wednesday.AddDays(56))); // Only days newly in reach were added.
        }
    }

    [Fact]
    public void AMissedRepeatIsNotCarriedOverAndQuickCaptureSaysItRepeats()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Water the plants every day"); // A task that repeats.
        now = Wednesday.AddDays(3).AddHours(8); Assert.DoesNotContain(model.MissedNotes(), n => n.SeriesId is not null);
        Assert.Contains("every weekday", QuickCaptureWindow.Describe("Standup every weekday", now, model.AccountNames));
    }

    [AvaloniaFact]
    public void TheNotesPageShowsTheRepeatAndAsksWhichToRemove()
    {
        var model = Model(); model.AddDayNote(Wednesday, "Standup every weekday");
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            var first = Series(model)[0];
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "NoteRepeatMark_" + first.Id); // The repeat mark on the row.
            var remove = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoteRemove_" + first.Id);
            Assert.NotNull(remove.Flyout); // A note that repeats asks which, instead of removing at once.
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Contains(model.AllDayNotes, n => n.Id == first.Id);
        }
        finally { window.Close(); }
    }
}
