using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AuditRegressionTests
{
    private static readonly DateTime Day = new(2026, 11, 13);
    private static TimerModel Model(Func<DateTime>? clock = null)
    {
        var model = new TimerModel(new TrackingEngine(clock: clock ?? (() => Day.AddHours(14))),
            new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Dark", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaTheory]
    [InlineData(28)]
    [InlineData(365)]
    public void DistantRemindersOpenOnTheirActualDate(int ahead)
    {
        var model = Model(); var due = Day.AddDays(ahead).AddHours(20).AddMinutes(31);
        var saved = new List<DateTime>(); var picker = new ReschedulePicker(model, due, saved.Add);
        Assert.Equal(model.DayOf(due), picker.Day); Assert.Equal(due, picker.Strip.Pin);
        picker.Strip.Commit(); Assert.Equal(due, Assert.Single(saved));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(18)]
    public void ExistingReminderInTheFinalMinuteKeepsItsTimeEvenAfterTheLastSnappedSlot(int dayStart)
    {
        var now = Day.AddDays(1).AddHours(dayStart).AddMinutes(-4);
        var model = Model(() => now); model.SetDayStart(TimeSpan.FromHours(dayStart));
        var due = model.DayEnds(model.TodayKey).AddMinutes(-1);
        var saved = new List<DateTime>(); var picker = new ReschedulePicker(model, due, saved.Add);
        Assert.Equal(due, picker.Strip.Pin);
        picker.Strip.Commit(); Assert.Equal(due, Assert.Single(saved));
        now = due.AddSeconds(1); picker.Strip.Commit(); Assert.Single(saved);
    }

    [Fact]
    public void LongBreakUsesTheWorkingDayAcrossMidnight()
    {
        var now = Day.AddHours(23); var model = Model(() => now);
        model.SetDayStart(TimeSpan.FromHours(18)); model.SetBreakAllowance(TimeSpan.FromMinutes(15));
        model.ToggleAccount("Account one"); model.ToggleBreak(); now = Day.AddDays(1).AddHours(1);
        Assert.Equal(Day, model.TodayKey);
        Assert.Contains(model.Alerts, a => a.Kind == AlertKind.Break);
    }

    [AvaloniaFact]
    public void OpenAlertReflectsEditedWordsAndAccountWithoutWaitingForTheClock()
    {
        var model = Model(); var due = Day.AddHours(13);
        model.Notes.AddNote("Before", "Account one", due); var id = Assert.Single(model.Notes.All).Id;
        var card = new PillAlertsCard(model, () => { }, () => { });
        var window = new Window { Width = 400, Height = 500, Content = card }; window.Show(); Settle(window);
        try
        {
            card.Open(); Settle(window);
            model.Notes.EditNote(id, "After", "Account two", due); Settle(window);
            Assert.Equal("After", card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AlertWords").Text);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account two");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NoteDotsFollowAccountColourChangesWithoutChangingTheAccountCount()
    {
        var model = Model(); model.SetNotesCardShowsMeetings(false);
        model.Notes.AddNote("Tagged", "Account one", null);
        var card = new NotesCard(model); var window = new Window { Width = 600, Height = 400, Content = card };
        window.Show(); Settle(window);
        try
        {
            Color Colour() => card.GetVisualDescendants().OfType<Border>()
                .Where(b => Equals(ToolTip.GetTip(b), "#Accountone"))
                .Select(b => Assert.IsType<LinearGradientBrush>(b.Background).GradientStops[0].Color).Single();
            var before = Colour();
            model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { Colour = "#E5484D" } : a));
            Settle(window); Assert.NotEqual(before, Colour());
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AlertsAreSharedDuringNotificationButRecomputedForTheNextChange()
    {
        var model = Model(); IReadOnlyList<AlertItem>? first = null;
        model.PropertyChanged += (_, _) => { first = model.Alerts; Assert.Same(first, model.Alerts); };
        model.Notes.AddNote("First", null, Day.AddHours(13)); Assert.Single(first!);
        model.Notes.AddNote("Second", null, Day.AddHours(13)); Assert.Equal(2, first!.Count);
        var old = model.Alerts; model.Notes.Toggle(model.Notes.All[0].Id);
        Assert.Single(model.Alerts); Assert.Equal(2, old.Count);
    }

    [AvaloniaFact]
    public void HiddenPillKeepsItsDisplayUntilRevealThenCatchesUp()
    {
        var now = Day.AddHours(14); var model = Model(() => now);
        model.ToggleAccount("Account one");
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var clock = pill.GetVisualDescendants().OfType<SegmentClock>().Single();
            var before = clock.Text; pill.Hide();
            now = now.AddHours(1); model.Tick();
            Assert.Equal(before, clock.Text);
            pill.Show(); Settle(pill); Assert.Equal(model.ClockText, clock.Text);
            Assert.NotEqual(before, clock.Text);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void MeetingCalendarStillPicksPastDaysWithoutTheRetiredTimeEditor()
    {
        DateTime? saved = null;
        var calendar = new ReminderPopup(() => Day, "Meeting", Day, at => saved = at, () => { });
        var window = new Window { Width = 350, Height = 500, Content = calendar }; window.Show(); Settle(window);
        try
        {
            Button Named(string name) => calendar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            Named("ReminderCell_20261112").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Named("ReminderSet").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Day.AddDays(-1), saved);
            Assert.DoesNotContain(calendar.GetVisualDescendants().OfType<Control>(), c => c.Name is "ReminderHour" or "ReminderWheelToggle");
        }
        finally { window.Close(); }
    }
}
