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
    public void EveryPartOfEachShapePictureLiesInsideItsFrameInSettingsAndInTheSetup()
    {
        foreach (var compact in new[] { true, false })
        {
            var (tiles, _) = PillShapeChoice.Build(Model(), "Test", compact);
            var window = new Window { Width = 700, Height = 400, Content = tiles }; window.Show(); Settle(window);
            try
            {
                foreach (var tile in tiles.GetVisualDescendants().OfType<Button>())
                {
                    var frame = tile.GetVisualDescendants().OfType<Border>().First(b => b.Height == 92);
                    var box = frame.TranslatePoint(default, window)!.Value; var size = frame.Bounds.Size;
                    foreach (var part in frame.GetVisualDescendants().OfType<Border>())
                    {
                        var at = part.TranslatePoint(default, window)!.Value;
                        Assert.True(at.X >= box.X - 0.5 && at.Y >= box.Y - 0.5 && at.X + part.Bounds.Width <= box.X + size.Width + 0.5 && at.Y + part.Bounds.Height <= box.Y + size.Height + 0.5,
                            $"{tile.Name} (compact {compact}): a part at {at} {part.Bounds.Size} runs past its frame at {box} {size}");
                    }
                }
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void TheHorizontalPillsListHangsFromAPointUnderTheAccountsBox()
    {
        var model = Model(); model.SetPillHorizontal(true);
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            pill.ShowPanel(); Dispatcher.UIThread.RunJobs();
            var point = pill.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "PillDropdownPoint");
            var fromRight = point.Margin.Right - HangingCard.Room.Right + point.Width / 2;
            var fromLeft = PillWindow.DropdownWidth - fromRight;
            var well = pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PillWell").TranslatePoint(default, pill)!.Value.X;
            var button = pill.AccountsButton.TranslatePoint(default, pill)!.Value.X;
            // The point's centre, measured from the card's left edge (which is under the well's), lies within the box.
            Assert.InRange(fromLeft, button - well, button - well + pill.AccountsButton.Bounds.Width);
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
}
