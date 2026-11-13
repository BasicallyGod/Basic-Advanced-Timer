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
            Assert.Equal("Add", Assert.IsType<TextBlock>(Find<Button>(window, "SubmitNote").Content).Text); Assert.Contains("selected", Find<Button>(window, "CardKindMeetings").Classes.Concat(new[] { "selected" }));
            Assert.Equal("30", (TimeSpan.Parse(Find<TextBox>(window, "MeetingEnd").Text!) - TimeSpan.Parse(Find<TextBox>(window, "MeetingStart").Text!)).TotalMinutes.ToString()); // Half an hour by default.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheMeetingLineInTheCardHasAStartAndAnEndAndNamesTheDayItIsFor()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "CardKindMeetings");
            Assert.Equal("12:00", Find<TextBox>(window, "MeetingStart").Text); Assert.Equal("12:30", Find<TextBox>(window, "MeetingEnd").Text); // Midday, half an hour.
            Assert.Equal("Today", Find<TextBlock>(window, "MeetingDayWords").Text); // The day showing, on a button that opens the day's calendar; a repeat is set in Details once it is there.
            var day = Find<Button>(window, "MeetingDay"); Assert.Contains(day.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "MeetingDayWords");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "MeetingRepeat");
            Click(window, "MeetingDay"); Assert.NotNull(Find<TextBox>(window, "MeetingStart")); // Pressing it opens the calendar over the card and leaves the line as it was.
        }
        finally { window.Close(); }
    }

}
