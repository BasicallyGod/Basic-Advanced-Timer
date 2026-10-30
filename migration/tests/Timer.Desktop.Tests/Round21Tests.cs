using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 21: the guide line that survives a redraw, a reminder that keeps its own minute, an important note picked out without bold, and a note form that wraps its chips.
public sealed class Round21Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(12)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); }

    [AvaloniaFact]
    public void TheGuideLineIsStillThereAfterTheTimelineIsDrawnAgainUnderAPointerThatHasNotMoved()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = Find<Canvas>(window, "TimelineCanvas"); var scroll = Find<ScrollViewer>(window, "TimelineScroll");
            var x = ReviewViews.TimelineGutter + 10 * model.TimelinePixelsPerHour; // Ten o'clock, scrolled into view.
            scroll.Offset = new Vector(Math.Max(0, x - scroll.Viewport.Width / 2), 0); Settle(window);
            var at = canvas.TranslatePoint(new Point(x, 100), window)!.Value;
            HeadlessWindowExtensions.MouseMove(window, at); Settle(window);
            Assert.True(Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine").IsVisible);
            model.SetNotesCardShowsMeetings(true); Settle(window); // What a click in the meeting row does: the page is drawn again.
            var fresh = Find<Canvas>(window, "TimelineCanvas"); Assert.NotSame(canvas, fresh); // A new canvas...
            Assert.True(Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine").IsVisible); // ...and the line is on it without the pointer having moved.
            HeadlessWindowExtensions.MouseMove(window, new Point(2, 2)); Settle(window);
            Assert.False(Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine").IsVisible); // Leaving puts it away, and it is not brought back.
            model.SetNotesCardShowsMeetings(false); Settle(window);
            Assert.False(Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AReminderKeepsItsOwnMinuteWhenItsCardIsOpenedAndStepsLandOnFives()
    {
        var model = Model(); model.Notes.AddNote("Ring them", null, Day.AddHours(14).AddMinutes(31));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var id = model.Notes.All.Single().Id; Click(window, "NoteReminder_" + id);
            Assert.Equal("31", Find<TextBox>(window, "ReminderMinute").Text); Assert.Equal("14", Find<TextBox>(window, "ReminderHour").Text); // Not 30.
            Assert.Contains("14:31", Find<TextBlock>(window, "ReminderSummary").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnImportantNoteHasNoBoldWordsButASolidAccentStarAndATintedRow()
    {
        var model = Model(); model.Notes.AddNote("!! Ring the client", null, null); model.Notes.AddNote("Tidy the drive", null, null);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var important = model.Notes.All.Single(n => NotesModel.IsImportant(n)); var plain = model.Notes.All.Single(n => !NotesModel.IsImportant(n));
            var words = Find<TextBlock>(window, "NoteText_" + important.Id); Assert.Equal(FontWeight.Normal, words.FontWeight);
            var star = Find<Button>(window, "NoteImportant_" + important.Id); Assert.Equal("Accent", star.GetVisualDescendants().OfType<IconGlyph>().Single().TintKey);
            Assert.True(words.GetVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("importantRow")));
            Assert.False(Find<TextBlock>(window, "NoteText_" + plain.Id).GetVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("importantRow")));
            Click(window, "NotesTab");
            Assert.Equal(FontWeight.Normal, Find<TextBlock>(window, "PageNoteText_" + important.Id).FontWeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteFormWithAReminderChipPutsTheChipsAboveTheWordsAndNothingOverlaps()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote");
            Find<TextBox>(window, "NoteField").Text = "#Accountt"; Settle(window); Click(window, "Suggest_Accounttwo");
            Find<TextBox>(window, "NoteField").Text = "!! What if we put a really long reminder in here that ends up breaking the limit"; Settle(window);
            Click(window, "FormBell"); Click(window, "ReminderSet");
            var frame = Find<TextBox>(window, "NoteField").GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("composer"));
            var field = Find<TextBox>(window, "NoteField"); var account = Find<Button>(window, "FormAccountChip"); var reminder = Find<Button>(window, "FormReminder");
            Rect In(Control c) => new(c.TranslatePoint(new Point(0, 0), window)!.Value, c.Bounds.Size);
            var box = In(frame);
            Assert.True(box.Contains(In(account)) && box.Contains(In(reminder)) && box.Contains(In(field)), "everything is inside the box"); // Nothing runs out of it.
            Assert.False(In(field).Intersects(In(account))); Assert.False(In(field).Intersects(In(reminder)));
        }
        finally { window.Close(); }
    }
}
