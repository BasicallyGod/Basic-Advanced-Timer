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
                    var frame = tile.GetVisualDescendants().OfType<Border>().First(b => b.Height == PillShapeChoice.PictureHeight);
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

    // Fitting the frame is not enough: the vertical pill is a capsule, and a part that sits in its rounded end pokes out past the curve at its corners.
    [AvaloniaFact]
    public void EveryPartOfEachPicturedPillLiesInsideThePillsOwnOutlineWithRoomToSpare()
    {
        var (tiles, _) = PillShapeChoice.Build(Model(), "Shape", compact: true);
        var window = new Window { Width = 700, Height = 400, Content = tiles }; window.Show(); Settle(window);
        try
        {
            foreach (var tile in tiles.GetVisualDescendants().OfType<Button>())
            {
                var pill = tile.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius.TopLeft >= 15 && b.Height < PillShapeChoice.PictureHeight && b.BorderThickness.Left > 0);
                var box = pill.Bounds.Size; var radius = pill.CornerRadius.TopLeft; var margin = 4.0; // The outline is a pixel wide and the parts should not touch it.
                bool Inside(Point p)
                {
                    if (p.X < margin || p.X > box.Width - margin || p.Y < margin || p.Y > box.Height - margin) return false;
                    var vertical = box.Height >= box.Width; var along = vertical ? p.Y : p.X; var across = vertical ? p.X : p.Y; var length = vertical ? box.Height : box.Width; var width = vertical ? box.Width : box.Height;
                    var end = Math.Min(along, length - along); if (end >= radius) return true;
                    var half = Math.Sqrt(radius * radius - (radius - end) * (radius - end)); // How far from the middle line the curve allows at this distance from the end.
                    return Math.Abs(across - width / 2) <= half - margin;
                }
                foreach (var part in pill.GetVisualDescendants().OfType<Border>())
                {
                    var at = part.TranslatePoint(default, pill)!.Value; var size = part.Bounds.Size;
                    foreach (var corner in new[] { at, at + new Point(size.Width, 0), at + new Point(0, size.Height), at + new Point(size.Width, size.Height) })
                        Assert.True(Inside(corner), $"{tile.Name}: a part at {at} {size} pokes out of the {box} outline at {corner}");
                }
            }
        }
        finally { window.Close(); }
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

    // The pulse ring is a round shape behind the light, free to grow past its cell. As a box shadow on the light its corners stayed tight and the glow looked cut off flat at the top.
    [AvaloniaFact]
    public void TheReminderPulseIsARoundRingBehindTheLightNotABoxShadowOnIt()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Light");
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            Assert.True(model.ReminderDue);
            var light = pill.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PillReminderLight"); var ring = pill.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PillReminderRing");
            Assert.True(Ui.IsPulsing(light)); Assert.Equal(default, light.BoxShadow); Assert.False(ring.IsHitTestVisible);
            Assert.Same(ring.Parent, light.Parent); Assert.Equal(new Size(6, 14), ((Control)light.Parent!).Bounds.Size); // The cell stays the light's size, so nothing moves as the ring swells.
            PreviewCaptureTests.SetSwollen(ring, 1); ring.Background = Avalonia.Media.Brushes.Red; Settle(pill);
            var corner = ring.TranslatePoint(new Point(1.5, 1.5), pill)!.Value; var middle = ring.TranslatePoint(new Point(10, 4), pill)!.Value; var body = new Point(middle.X, middle.Y + 60);
            using var frame = pill.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var shot = pill.CaptureRenderedFrame();
            var card = Pixel(shot!, body); var atCorner = Pixel(shot!, corner); var atTop = Pixel(shot!, middle);
            Assert.True(Distance(atCorner, card) < 12, "the ring's corner should be round, so its very corner shows the card"); Assert.True(Distance(atTop, card) > 150, "the ring's top edge should be filled");
        }
        finally { pill.Close(); }
        static int[] Pixel(Avalonia.Media.Imaging.WriteableBitmap bitmap, Point at)
        {
            using var pixels = bitmap.Lock(); var data = new byte[pixels.RowBytes * pixels.Size.Height]; System.Runtime.InteropServices.Marshal.Copy(pixels.Address, data, 0, data.Length);
            var o = (int)at.Y * pixels.RowBytes + (int)at.X * 4; return new[] { (int)data[o], data[o + 1], data[o + 2] };
        }
        static int Distance(int[] a, int[] b) => a.Zip(b).Sum(c => Math.Abs(c.First - c.Second));
    }
}
