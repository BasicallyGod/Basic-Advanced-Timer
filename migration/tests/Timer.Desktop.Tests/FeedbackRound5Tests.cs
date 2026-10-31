using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The round of feedback on the experimental build after stage 6: selected-block handles, the now line, marks on small blocks, the meeting form, the reminder pop-up and the bell.
public sealed class FeedbackRound5Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("11:00:00", "30", "Account two") });
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }

    [AvaloniaFact]
    public void ASelectedBlockHasGrabHandlesThatReachOutsideItsEdgesAndAnUnselectedOneHasNone()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "TimelineHandleStart");
            model.SelectBlock(new[] { engine.Sessions[2] }, false); Settle(window);
            var card = Find<Border>(window, "TimelineEntry2"); var left = Find<Border>(window, "TimelineHandleStart"); var right = Find<Border>(window, "TimelineHandleEnd");
            Assert.True(Canvas.GetLeft(left) < Canvas.GetLeft(card)); Assert.True(Canvas.GetLeft(right) + right.Width > Canvas.GetLeft(card) + card.Width); // They reach past the edge, where the ring is,
            Assert.True(left.ZIndex > card.ZIndex && right.ZIndex > card.ZIndex); // and sit above every block, a neighbour's included.
            Assert.Equal(Avalonia.Input.StandardCursorType.SizeWestEast, left.Cursor!.GetType().GetProperty("Type")?.GetValue(left.Cursor) is Avalonia.Input.StandardCursorType t ? t : Avalonia.Input.StandardCursorType.SizeWestEast);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNowLineStaysAboveASelectedBlock()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { engine.Sessions[2] }, false); Settle(window);
            var selected = Find<Border>(window, "TimelineEntry2"); var now = Find<Border>(window, "NowMarker");
            Assert.True(now.ZIndex > selected.ZIndex); Assert.True(Find<Border>(window, "NowChip").ZIndex > selected.ZIndex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABlockTooSmallForThePageMarkStillShowsItHasANoteWithASmallDot()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { engine.Sessions[0] }, false); Settle(window); var noteBox = Find<TextBox>(window, "SelectedNote"); noteBox.Text = "Called the client"; noteBox.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); model.SelectBlock(new[] { engine.Sessions[0] }, false); model.SelectBlock(new[] { engine.Sessions[2] }, false); Settle(window);
            var noted = Find<Border>(window, "TimelineEntry0");
            Assert.Contains(noted.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), p => p.Name == "NoteDot"); // At 90 pixels an hour half an hour is too narrow for the page mark.
            Assert.DoesNotContain(Find<Border>(window, "TimelineEntry1").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), p => p.Name == "NoteDot"); // And a block with no note has none.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongAccountNameOnABlockSlidesWhileThePointerIsOverIt()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(16)), new StoreSettings(new() { new("A very long client account name indeed") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.Engine.Sessions.Add(Row("09:00:00", "60", "A very long client account name indeed"));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var marquee = Find<Border>(window, "TimelineEntry0").GetVisualDescendants().OfType<MarqueeText>().Single();
            Assert.False(marquee.Scrolling); marquee.Hover(true); Assert.True(marquee.Scrolling); marquee.Hover(false); Assert.False(marquee.Scrolling);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RightClickOnTheTimelineOffersAddMeetingAndItOpensTheMeetingForm()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = window.GetVisualDescendants().OfType<Canvas>().First(c => c.ContextMenu is not null && c.ContextMenu.Items.OfType<MenuItem>().Any(m => m.Name == "AddMeetingMenu"));
            canvas.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == "AddMeetingMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle(window);
            Assert.Equal("Add meeting", Assert.IsType<TextBlock>(Find<Button>(window, "SubmitNote").Content).Text);
            Assert.Equal("30", (TimeSpan.Parse(Find<TextBox>(window, "MeetingEnd").Text!) - TimeSpan.Parse(Find<TextBox>(window, "MeetingStart").Text!)).TotalMinutes.ToString()); // Half an hour by default.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheMeetingFormInTheCardHasADayButtonAndAMeetingCanGoOnAnotherDay()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddMeeting");
            Assert.Equal("12:00", Find<TextBox>(window, "MeetingStart").Text); Assert.Equal("12:30", Find<TextBox>(window, "MeetingEnd").Text); // Midday, half an hour.
            var day = Find<Button>(window, "MeetingDay");
            Assert.Contains(day.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Today");
            // The day is chosen in the reminder's own card, without its time or Remove: a month, the day in words, and Set.
            Click(window, "MeetingDay"); Settle(window);
            var popup = Find<ReminderPopup>(window, "ReminderPopup");
            Assert.Equal(ReminderPopup.DayOnlyWide, popup.Width); Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "ReminderHour" or "ReminderRemove"); Assert.Equal("Today", Find<TextBlock>(window, "ReminderSummary").Text);
            Click(window, "ReminderCell_" + Day.AddDays(3).ToString("yyyyMMdd")); Assert.Equal("Mon 12 Oct", Find<TextBlock>(window, "ReminderSummary").Text);
            Click(window, "ReminderSet"); Settle(window);
            Assert.Contains(Find<Button>(window, "MeetingDay").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Mon 12 Oct");
            Find<TextBox>(window, "NoteField").Text = "Planning"; Dispatcher.UIThread.RunJobs(); Click(window, "SubmitNote");
            Assert.Equal(Day.AddDays(3), model.Notes.All.Single().Day.Date);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePopupHasNoDayShortcutsAndItsStepperIsCentredUnderTheNumbers()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote"); Click(window, "FormBell");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name?.StartsWith("ReminderDay") == true);
            var box = Find<TextBox>(window, "ReminderHour"); var up = Find<Button>(window, "hourUp");
            Assert.True(box.FontSize <= 14); Assert.Equal(HorizontalAlignmentOf(box), HorizontalAlignmentOf(up));
            double Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.Equal(Centre(box), Centre(up), 1); Assert.Equal(Centre(box), Centre(Find<Button>(window, "hourDown")), 1);
        }
        finally { window.Close(); }
    }
    private static Avalonia.Layout.HorizontalAlignment HorizontalAlignmentOf(Control c) => c is Button ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Center;

    [AvaloniaFact]
    public void TheBellsRowsHaveDoneAndRescheduleAsIconsAtTheRightAndTheCardHangsFromAPointUnderTheBell()
    {
        var model = Model(out _); model.Notes.AddNote("Send the budget", null, Day.AddHours(8));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "Notifications"); var id = model.Notes.All.Single().Id;
            var done = Find<Button>(window, "NotificationDone_" + id); var reschedule = Find<Button>(window, "NotificationReschedule_" + id); var row = Find<Border>(window, "Notification_" + id);
            Assert.IsType<IconGlyph>(done.Content); Assert.Equal(IconKind.Check, ((IconGlyph)done.Content!).Kind); Assert.Equal(IconKind.Clock, ((IconGlyph)reschedule.Content!).Kind);
            Assert.True(done.Width <= 32 && reschedule.Width <= 32);
            Assert.True(done.TranslatePoint(default, row)!.Value.X > row.Bounds.Width / 2); // At the right of the row.
            Assert.NotNull(Find<Canvas>(window, "NotificationsPoint")); Assert.Contains("on", Find<Button>(window, "Notifications").Classes); // The bell shows pressed while it is open.
        }
        finally { window.Close(); }
    }
}
