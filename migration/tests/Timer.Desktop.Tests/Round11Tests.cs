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

// Round 11: the shape tiles' pictures fit their frames, the horizontal pill's list is joined to the account's box by a point, and the bell's point keeps off the card's corner.
public sealed class Round11Tests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(new List<AccountSettings> { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheHorizontalPillsListHangsFromAPointUnderTheAccountsBox()
    {
        var model = Model();
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            pill.ShowPanel(); Dispatcher.UIThread.RunJobs();
            var point = pill.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "PillDropdownPoint");
            var fromRight = point.Margin.Right - HangingCard.Room.Right + point.Width / 2;
            var fromLeft = PillWindow.DropdownWidth - fromRight;
            // The card's right edge is level with the pill's, so its left edge is a card's width in from there, and the point's centre lies under the middle of the box.
            var surface = pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PillSurface");
            var middle = pill.AccountsButton.TranslatePoint(new Point(pill.AccountsButton.Bounds.Width / 2, 0), surface)!.Value.X - (PillWindow.PillWidth - PillWindow.DropdownWidth);
            Assert.InRange(fromLeft, middle - 1, middle + 1);
            Assert.Equal(11, PillWindow.DropdownGap); // The card's top edge is 11 under the pill, the point's tip about 2 under it.
        }
        finally { pill.Close(); }
    }

    [Fact]
    public void ThePointStaysOnTheStraightPartOfACardsTopEdge()
    {
        // The bell is 38 wide and its centre 19 from the card's end, which is inside the 18 radius corner plus half the point.
        var shift = HangingCard.ShiftForCorner(19, 18);
        Assert.True(19 + shift - 10 >= 18 + 2, "the point's base would run out over the corner's curve");
        Assert.Equal(0, HangingCard.ShiftForCorner(80, 18)); // A button far from the end needs no shift.
        Assert.Equal(24 + 10 + 3, HangingCard.ClampFromRight(5, 300, 24)); Assert.Equal(300 - 37, HangingCard.ClampFromRight(299, 300, 24)); Assert.Equal(150, HangingCard.ClampFromRight(150, 300, 24));
    }

    [AvaloniaFact]
    public void TheOpenBellCardsPointIsClearOfItsRoundedCorner()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var bell = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Notifications");
            bell.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var point = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "NotificationsPoint");
            var card = window.GetVisualDescendants().OfType<NotificationsFlyout>().Single();
            var fromRight = point.Margin.Right - HangingCard.Room.Right + point.Width / 2;
            Assert.True(fromRight - point.Width / 2 >= card.CornerRadius.TopRight + 2, $"the point's base starts {fromRight - point.Width / 2} from the end, inside the {card.CornerRadius.TopRight} corner");
        }
        finally { window.Close(); }
    }

    // The card and its point are one shape: under the point there is no line where the card's edge would run, in either theme and whatever half pixel the point lands on.
    [AvaloniaFact]
    public void NoLineRunsAcrossTheBaseOfACardsPoint()
    {
        foreach (var theme in new[] { "Light", "Dark" })
            foreach (var nudge in new[] { 0.0, 0.5 })
            {
                var model = Model(); model.SetTheme(theme);
                var hung = HangingCard.Hang(new NotificationsFlyout(model), -1);
                var window = new Window { Width = 412, Height = 300, RequestedThemeVariant = theme == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light, Content = new Border { Margin = new Thickness(0, nudge, 0, 0), Child = hung } };
                window.Show(); Settle(window);
                try
                {
                    using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var shot = window.CaptureRenderedFrame();
                    using var pixels = shot!.Lock(); var data = new byte[pixels.RowBytes * pixels.Size.Height]; System.Runtime.InteropServices.Marshal.Copy(pixels.Address, data, 0, data.Length);
                    int[] At(int x, int y) { var o = y * pixels.RowBytes + x * 4; return new[] { (int)data[o], data[o + 1], data[o + 2] }; }
                    bool Same(int[] a, int[] b) => a.Zip(b).All(c => Math.Abs(c.First - c.Second) <= 2);
                    var inside = At(206, 60); var top = (int)HangingCard.Room.Top;
                    foreach (var row in new[] { top, top + 1 }) Assert.True(Same(At(206, row), inside), $"{theme}, nudge {nudge}: row {row} under the point {string.Join(",", At(206, row))} is not the card's {string.Join(",", inside)}");
                    Assert.True(Enumerable.Range(top - 1, 3).Any(row => !Same(At(120, row), inside)), "the edge beside the point should still be drawn"); // So the check can tell.
                }
                finally { window.Close(); }
            }
    }

    [AvaloniaFact]
    public void TheReminderCardIsGivenRoomForItsWholeShadowInItsPopup()
    {
        // A pop-up's window clips what is drawn past its edge, so a shadow with no room showed as flat grey corners beyond the card's rounded ones.
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddNote").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FormBell").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var card = window.GetVisualDescendants().OfType<ReminderPopup>().Single(); var host = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(p => p.Name == "ReminderHost");
            Assert.Equal(HangingCard.Room, card.Margin); Assert.Equal(HangingCard.Shadow, card.BoxShadow); Assert.Equal(-HangingCard.Room.Top, host.VerticalOffset); // And the card still lands where it did.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ScrollBarThumbsAreAThinLineAtRestInTheMiddleOfTheirTrack()
    {
        // The track is 10 pixels and the thumb 4 at rest (8 under the pointer), so a scroll bar is felt rather than seen; it was a heavy 8 pixel bar before.
        var demo = TimerModel.CreateDemo(); demo.CompleteSetup(); var window = new DashboardWindow(demo); window.Show(); Dispatcher.UIThread.RunJobs(); // The demo has a week long enough to scroll.
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekOverviewTab").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            var thumbs = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "ThumbFill" && b.Bounds.Height > b.Bounds.Width).ToList();
            Assert.NotEmpty(thumbs); Assert.All(thumbs, t => Assert.InRange(t.Bounds.Width, 3.5, 4.5));
        }
        finally { window.Close(); }
    }
}
