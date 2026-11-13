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
            strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown }); Assert.Equal(before.AddMinutes(65), strip.Pin); // Page keys move an hour.
            strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp }); Assert.Equal(before.AddMinutes(5), strip.Pin);
            strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle(window);
            Assert.Equal(before.AddMinutes(5), NotesModel.ReminderOf(model.Notes.Find(id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheStripIsTheWholeDay_APressZoomsToTheHourRoundIt_AndLettingGoSetsTheTimeAndShowsTheWholeDayAgain()
    {
        var (window, card, model, id) = Open();
        try
        {
            var strip = card.Picker!.Strip; var day = model.TodayKey;
            Assert.Equal(model.DayBegins(day), strip.From); Assert.Equal(model.DayBegins(day).AddDays(1), strip.Until); Assert.Null(strip.ZoomFrom); // At rest, 00:00 to 24:00.
            var width = strip.Bounds.Width; var evening = day.AddHours(20).AddMinutes(30);
            // Pressing where 20:30 falls on the whole day puts the pin there and zooms to the hour round it, the pin still under the pointer.
            var x = (evening - strip.From).TotalMinutes / 1440 * width;
            strip.Hold(x);
            Assert.Equal(evening, strip.Pin);
            var zoom = Assert.IsType<DateTime>(strip.ZoomFrom); Assert.InRange(strip.Pin, zoom, zoom.AddMinutes(RescheduleStrip.ZoomMinutes));
            Assert.Equal((evening - zoom).TotalMinutes / RescheduleStrip.ZoomMinutes, x / width, 2);
            // Zoomed, a twelfth of the strip is five minutes, so the exact minute is easy to reach.
            strip.MoveTo(x + width / 12); Assert.Equal(evening.AddMinutes(5), strip.Pin);
            // Holding near the right end pans the hour along the day.
            strip.PanAt(width * .98); Assert.Equal(zoom.AddMinutes(2), strip.ZoomFrom);
            var chosen = strip.Pin;
            strip.Release(); Settle(window);
            Assert.Null(strip.ZoomFrom); // Back to the whole day...
            Assert.Equal(chosen, NotesModel.ReminderOf(model.Notes.Find(id)!)); // ...and the time is set, here in the evening, outside the working day.
            Assert.True(chosen.Hour >= 20);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePointerPressesDragsAndLetsGo_ThroughTheSameSteps()
    {
        var (window, card, model, id) = Open();
        try
        {
            var strip = card.Picker!.Strip; var day = model.TodayKey; var width = strip.Bounds.Width;
            var at = strip.TranslatePoint(new Point((day.AddHours(21) - strip.From).TotalMinutes / 1440 * width, strip.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left); Settle(window);
            Assert.NotNull(strip.ZoomFrom); Assert.Equal(day.AddHours(21), strip.Pin);
            // Pressed at 21:00, seven-eighths of the way along, so a twelfth of the strip further (five minutes) stays on it.
            window.MouseMove(at - new Point(width / 12, 0)); Settle(window); Assert.Equal(day.AddHours(20).AddMinutes(55), strip.Pin);
            window.MouseUp(at - new Point(width / 12, 0), MouseButton.Left); Settle(window);
            Assert.Null(strip.ZoomFrom); Assert.Equal(day.AddHours(20).AddMinutes(55), NotesModel.ReminderOf(model.Notes.Find(id)!));
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
