using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 27: the bell's card is no taller than the window below the bell and its list scrolls; and in a stack's bars the initials, the length, the times and the two handles all sit
// on the bar's centre line.
public sealed class Round27Tests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static DateTime T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void TheBellsCardStopsAMarginAboveTheFootOfTheWindowAndNeverGetsTinyNearIt()
    {
        // A bell whose foot is 80 below the top of a window card that is 790 tall (30 of shadow room and 760): the card, its point and the margin fit in what is left.
        Assert.Equal(790 - 80 - 11 - 14, NotificationsFlyout.MaxHeightFor(80, 790), 0.01);
        Assert.Equal(160, NotificationsFlyout.MaxHeightFor(780, 790)); // Even with no room below, the card keeps a usable height.
    }

    [AvaloniaFact]
    public void ALongListScrollsInsideTheLimitedCardAndTheTitleStaysPut()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.CompleteSetup();
        var flyout = new NotificationsFlyout(model); flyout.LimitHeight(300);
        var window = new Window { Width = 520, Height = 520, Content = new Border { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = flyout } }; window.Show(); Settle(window);
        try
        {
            Assert.True(flyout.Bounds.Height <= 300.5, $"The card is {flyout.Bounds.Height} tall");
            var scroll = flyout.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "NotificationsScroll");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 20); // There is more than fits, so it scrolls.
            var title = flyout.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Notifications"); var before = title.TranslatePoint(default, flyout)!.Value.Y;
            scroll.Offset = new Vector(0, 80); Settle(window);
            Assert.Equal(before, title.TranslatePoint(default, flyout)!.Value.Y, 0.1); // The title does not scroll away.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InAStacksBarsTheInitialsLengthTimesAndHandlesAllSitOnTheCentreLine()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => T(20, 0)), new StoreSettings(new() { new("Account one"), new("Account two"), new("Internal") }, false, "Dark", DayOfWeek.Monday)); model.CompleteSetup();
        model.Engine.AddSession("Account one", T(9, 0), T(10, 0)); model.Engine.AddSession("Account two", T(9, 6), T(10, 6)); model.Engine.AddSession("Internal", T(9, 12), T(10, 12)); model.Engine.AddSession("Account one", T(9, 18), T(10, 18));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var tile = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name?.StartsWith("TimelineStack") == true);
            var at = tile.TranslatePoint(new Point(24, tile.Bounds.Height / 2), window)!.Value; window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window);
            double Middle(Control c, Control to) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), to)!.Value.Y;
            foreach (var bar in window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Stack_") == true))
            {
                var centre = bar.Bounds.Height / 2;
                foreach (var part in bar.GetVisualDescendants().OfType<Control>().Where(c => c is TextBlock { IsVisible: true } || c is Border { Child: TextBlock { Name: "BlockInitials" } }))
                    Assert.True(Math.Abs(Middle(part, bar) - centre) <= .6, $"{(part as TextBlock)?.Text ?? "the initials"} is centred {Middle(part, bar)} in a bar whose middle is {centre}");
            }
            foreach (var handle in window.GetVisualDescendants().OfType<Border>().Where(b => b.Name is "StackHandleStart" or "StackHandleEnd"))
            {
                var bar = window.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("Stack_") == true && b.Classes.Contains("mergeSelected"));
                var timeline = handle.GetVisualParent()!;
                Assert.Equal(bar.TranslatePoint(new Point(0, bar.Bounds.Height / 2), window)!.Value.Y, handle.TranslatePoint(new Point(0, handle.Bounds.Height / 2), window)!.Value.Y, 0.6);
            }
        }
        finally { window.Close(); }
    }
}
