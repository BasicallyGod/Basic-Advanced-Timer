using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// Rescheduling a reminder from the alerts card (the study "Rescheduling a reminder", option F): the week's days over a strip of the chosen day, set by letting go of the pin.
public sealed class RescheduleTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 0);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Named<T>(Visual root, string name) where T : Control => root.GetSelfAndVisualDescendants().OfType<T>().First(b => b.Name == name);
    private static void Press(Visual root, string name) => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static (Window Window, PillAlertsCard Card, TimerModel Model, long Id) Open()
    {
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup();
        var card = new PillAlertsCard(model, () => { }, () => { }) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 420, Height = 640, Content = card, SystemDecorations = SystemDecorations.None }; window.Show(); Settle(window);
        card.Open(); var reminder = card.Items.ToList().FindIndex(i => i.Kind == AlertKind.Reminder); Assert.True(reminder >= 0, "the demo has a late reminder");
        card.OpenRow(reminder); Settle(window);
        var id = card.Items[reminder].NoteId!.Value;
        Press(window, "AlertReschedule"); Settle(window);
        return (window, card, model, id);
    }

    [AvaloniaFact]
    public void RescheduleShowsTheWeeksDaysOverAStripOfTheDay_InPlaceOfTheRowsButtons()
    {
        var (window, card, model, _) = Open();
        try
        {
            Assert.NotNull(card.Picker);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name is "AlertDone" or "AlertLater" or "AlertReschedule");
            var days = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("RescheduleDay_") == true).ToList();
            Assert.Equal(7, days.Count);
            Assert.Equal("RescheduleDay_" + model.TodayKey.ToString("yyyyMMdd"), days[0].Name);
            Assert.Equal("14 – 20 Oct", Named<TextBlock>(window, "RescheduleWeek").Text);
            Assert.False(Named<Button>(window, "RescheduleWeekBack").IsEnabled); // Nothing to go back to before this week.
            // Nothing is written under the strip: the picker is the week line, the days and the strip, with Cancel under them.
            Assert.Equal(3, card.Picker!.Children.Count);
            // Every day sits inside the card.
            foreach (var d in days) { var right = d.TranslatePoint(new Point(d.Bounds.Width, 0), card)!.Value.X; Assert.True(right <= card.Bounds.Width - card.Padding.Right + .5, $"{d.Name} ends at {right}"); }
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "reschedule-picker.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PickingADayAndPressingTheStripSetsTheReminderThereToTheFiveMinutes()
    {
        var (window, card, model, id) = Open();
        try
        {
            var monday = Enumerable.Range(1, 7).Select(i => model.TodayKey.AddDays(i)).First(d => d.DayOfWeek == DayOfWeek.Monday);
            Press(window, "RescheduleDay_" + monday.ToString("yyyyMMdd")); Settle(window);
            Assert.Equal(monday, card.Picker!.Day);
            var strip = card.Picker.Strip; var at = strip.TranslatePoint(new Point(strip.Bounds.Width / 2, strip.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window);
            var due = NotesModel.ReminderOf(model.Notes.Find(id)!)!.Value;
            Assert.Equal(monday, model.DayOf(due));
            Assert.Equal(0, due.Minute % 5);
            Assert.InRange(due, strip.From, strip.Until);
            Assert.Null(card.Picker); // Set, and the picker gives way.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePinNeverGoesIntoThePastAndTheArrowKeysMoveItFiveMinutes()
    {
        var (window, card, model, id) = Open();
        try
        {
            var strip = card.Picker!.Strip;
            strip.MoveTo(0); Assert.True(strip.Pin >= model.Engine.Now, $"the pin is at {strip.Pin} before now"); Assert.Equal(0, strip.Pin.Minute % 5);
            var before = strip.Pin; strip.Focus();
            strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right }); Assert.Equal(before.AddMinutes(5), strip.Pin);
            strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle(window);
            Assert.Equal(before.AddMinutes(5), NotesModel.ReminderOf(model.Notes.Find(id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void APinTheClockHasPassedIsNotSaved_ItMovesOnInstead()
    {
        // The card left open while the minute turns: the pin was fine when placed, but committing it now would set a reminder in the past.
        var now = DemoNow; var model = TimerModel.CreateDemo(() => now); model.CompleteSetup();
        var saved = new List<DateTime>();
        var picker = new ReschedulePicker(model, null, at => saved.Add(at));
        var window = new Window { Width = 360, Height = 300, Content = picker }; window.Show(); Settle(window);
        try
        {
            var strip = picker.Strip; strip.PinAt(now); Assert.Equal(now, strip.Pin); // 14:15, on the minute: allowed while it is still 14:15.
            now = DemoNow.AddMinutes(1);
            strip.Commit();
            Assert.Empty(saved); // Not saved in the past...
            Assert.Equal(DemoNow.AddMinutes(5), strip.Pin); // ...the pin moved on to the next five minutes instead...
            strip.Commit(); Assert.Equal(new[] { DemoNow.AddMinutes(5) }, saved); // ...and that can be set.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekKeysMoveAWeekAndCancelOrEscapeGiveTheButtonsBack()
    {
        var (window, card, model, id) = Open();
        try
        {
            var was = NotesModel.ReminderOf(model.Notes.Find(id)!);
            Press(window, "RescheduleWeekNext"); Settle(window);
            Assert.Equal("21 – 27 Oct", Named<TextBlock>(window, "RescheduleWeek").Text);
            Assert.True(Named<Button>(window, "RescheduleWeekBack").IsEnabled);
            Assert.Equal(model.TodayKey.AddDays(7), card.Picker!.Day); // The same weekday a week on.
            Assert.True(card.HandleKey(Key.Escape)); Settle(window);
            Assert.Null(card.Picker); Named<Button>(window, "AlertReschedule");
            Press(window, "AlertReschedule"); Settle(window); Press(window, "AlertRescheduleCancel"); Settle(window);
            Assert.Null(card.Picker);
            Assert.Equal(was, NotesModel.ReminderOf(model.Notes.Find(id)!)); // Nothing moved.
        }
        finally { window.Close(); }
    }
}
