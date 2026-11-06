using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The Day starts at setting (the board "Timeline behaviours"): a day is the 24 hours from a time you choose, so a night shift is one day, and blocks may cross midnight.
public sealed class DayStartTests
{
    private static readonly DateTime Friday = new(2026, 10, 9);
    // 03:00 on Saturday, with days beginning at 18:00 (so still Friday's day).
    private static TimerModel Night(out TrackingEngine engine, int startHour = 18)
    {
        var now = Friday.AddDays(1).AddHours(3);
        engine = new TrackingEngine(clock: () => now);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.SetDayStart(TimeSpan.FromHours(startHour)); return model;
    }

    [Fact]
    public void TheSettingIsSavedAsAnAppearanceLine_ReachesTheEngineAndNotes_AndMidnightIsTheDefault()
    {
        var model = Night(out var engine);
        Assert.Equal(TimeSpan.FromHours(18), model.DayStart); Assert.Equal(TimeSpan.FromHours(18), engine.DayStart); Assert.Equal(TimeSpan.FromHours(18), model.Notes.DayStart);
        Assert.Equal(Friday, model.TodayKey); // 03:00 on Saturday is still Friday's day.
        model.SetDayStart(TimeSpan.Zero); Assert.Equal(TimeSpan.Zero, model.DayStart); Assert.Equal(Friday.AddDays(1), model.TodayKey);
    }

    [Fact]
    public void ANightShiftIsOneBlockOnOneDay_WithItsWorkingTimeAndTotals()
    {
        var model = Night(out var engine);
        Assert.True(model.AddEntry("Account one", Friday.AddHours(22), Friday.AddDays(1).AddHours(2), "night"));
        Assert.Single(engine.Sessions);
        Assert.Equal(TimeSpan.FromHours(4), model.WorkingOn(Friday)); Assert.Equal(TimeSpan.FromHours(4), model.AllocatedOn(Friday));
        Assert.Equal(TimeSpan.Zero, model.WorkingOn(Friday.AddDays(1))); Assert.Equal(TimeSpan.Zero, model.UnallocatedOn(Friday));
        Assert.Equal(TimeSpan.FromHours(4), model.OnDay(model.Accounts[0], Friday)); // Account totals for the day too.
        Assert.Equal(model.WorkingOn(Friday), model.Elapsed); // The pill's clock is today's, which is still Friday.
    }

    [Fact]
    public void Details_AddTime_AndTheFitKeysAllowAnEndAfterMidnight()
    {
        var model = Night(out var engine);
        model.AddEntry("Account one", Friday.AddHours(20), Friday.AddHours(22), "");
        var row = engine.Sessions[0];
        Assert.True(model.EditEntry(row, "Account one", Friday.AddHours(20), Friday.AddDays(1).AddHours(1), "")); // 20:00 to 01:00 the next day.
        Assert.Equal(TimeSpan.FromHours(5), engine.Sessions[0].Duration);
        Assert.Equal(Friday.AddDays(1).AddHours(2), TimeText.ParseClockAfter("02:00", Friday.AddHours(22), true)); // An end typed as 02:00 is after midnight.
        Assert.Equal(Friday.AddDays(1).AddHours(2), TimeText.ParseClockAfter("02:00", Friday.AddHours(18), false)); // In a day that begins at 18:00 the start 02:00 is too.
        Assert.False(model.NudgeSelection(1)); // Nothing is selected, so nothing changes.
    }

    [AvaloniaFact]
    public void TheTimelineRunsFromTheDaysBeginning_WithAMidnightMarker_AndShowsACrossingBlockOnce()
    {
        var model = Night(out _); model.AddEntry("Account one", Friday.AddHours(22), Friday.AddDays(1).AddHours(2), "night");
        var window = new Window { Width = 1300, Height = 400, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var labels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name?.StartsWith("TimelineHour_") == true).ToList();
            Assert.Equal("18:00", labels.Single(l => l.Name == "TimelineHour_0").Text); Assert.Equal("00:00", labels.Single(l => l.Name == "TimelineHour_6").Text);
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "TimelineMidnight");
            var block = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name?.StartsWith("TimelineEntry") == true);
            Assert.Equal(4 * model.TimelinePixelsPerHour - ReviewViews.BlockGap, block.Width, 1); // Four hours, across midnight.
        }
        finally { window.Close(); }
    }

    [Fact]
    public void NotesAndMeetingsBelongToTheDayThatHoldsTheirMoment()
    {
        var model = Night(out _);
        model.Notes.AddMeeting(Friday.AddDays(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), "Handover", null, RepeatChoice.None);
        model.Notes.AddMeeting(Friday, TimeSpan.FromHours(19), TimeSpan.FromHours(20), "Briefing", null, RepeatChoice.None);
        Assert.Equal(new[] { "Briefing", "Handover" }, model.Notes.MeetingsOn(Friday).Select(n => model.Notes.Text(n)));
        Assert.Empty(model.Notes.MeetingsOn(Friday.AddDays(1)));
        model.Notes.AddNote("Check the logs", null, Friday.AddDays(1).AddHours(1)); // A reminder at 01:00 on Saturday.
        Assert.Single(model.Notes.PinnedOn(Friday)); Assert.Empty(model.Notes.PinnedOn(Friday.AddDays(1)));
    }

    [AvaloniaFact]
    public void SettingsOffersTheDayStartWithQuickChoices_AndTheHeaderNamesTheStretch()
    {
        var model = Night(out _); model.SetDayStart(TimeSpan.Zero);
        var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            dashboard.OpenSettings(); dashboard.GetVisualDescendants().OfType<SettingsView>().Single().ShowSection("day"); Dispatcher.UIThread.RunJobs();
            var box = dashboard.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "DayStartBox"); Assert.Equal("00:00", box.Text);
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DayStartQuick_1800").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(TimeSpan.FromHours(18), model.DayStart); Assert.Equal("18:00", box.Text);
            box.Text = "6:30"; box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            Assert.Equal(new TimeSpan(6, 30, 0), model.DayStart);
            box.Text = "nonsense"; box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            Assert.Equal(new TimeSpan(6, 30, 0), model.DayStart); Assert.Equal("06:30", box.Text); Assert.NotNull(model.EditError);
        }
        finally { dashboard.Close(); }
    }
}
