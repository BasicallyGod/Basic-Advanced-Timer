using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class SnapshotFidelityTests
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }

    [AvaloniaFact]
    public void DashboardRasterKeepsItsLayoutAtEachCaptureScaleInBothThemes()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 2, 14, 15, 0)); model.CompleteSetup(); model.SetTheme(theme);
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                var surface = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "DashboardSurface");
                var layers = (Grid)surface.Child!; var body = (Control)layers.Children[0];
                var expectedBounds = body.Bounds;
                using var reference = window.CaptureRenderedFrame()!;
                using var expected = reference.Lock();
                var expectedBytes = new byte[expected.RowBytes * expected.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(expected.Address, expectedBytes, 0, expectedBytes.Length);
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
                {
                    using var bitmap = WindowMorph.CaptureVisual(body, scale);
                    Assert.Equal(new Vector(96, 96), bitmap.Dpi);
                    Assert.Equal((int)Math.Ceiling(expectedBounds.Width * scale), bitmap.PixelSize.Width);
                    var image = new Image { Source = bitmap, Width = expectedBounds.Width, Height = expectedBounds.Height, Stretch = Stretch.Fill };
                    Canvas.SetLeft(image, expectedBounds.X); Canvas.SetTop(image, expectedBounds.Y);
                    var overlay = new Canvas { IsHitTestVisible = false }; overlay.Children.Add(image); layers.Children.Insert(1, overlay); body.Opacity = 0;
                    try
                    {
                        Settle(window); Assert.Equal(expectedBounds, body.Bounds);
                        using var actualFrame = window.CaptureRenderedFrame()!;
                        using var actual = actualFrame.Lock();
                        var actualBytes = new byte[actual.RowBytes * actual.Size.Height];
                        System.Runtime.InteropServices.Marshal.Copy(actual.Address, actualBytes, 0, actualBytes.Length);
                        Assert.Equal(expected.Size, actual.Size);
                        double difference = 0; long samples = 0;
                        for (var y = 0; y < expected.Size.Height; y++)
                            for (var x = 0; x < expected.Size.Width; x++)
                                for (var channel = 0; channel < 3; channel++)
                                { difference += Math.Abs(expectedBytes[y * expected.RowBytes + x * 4 + channel] - actualBytes[y * actual.RowBytes + x * 4 + channel]); samples++; }
                        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
                        if (folder is not null) { Directory.CreateDirectory(folder); reference.Save(Path.Combine(folder, $"snapshot-reference-{theme}.png")); actualFrame.Save(Path.Combine(folder, $"snapshot-{theme}-{scale:0.##}.png")); }
                        Assert.True(difference / samples < 6, $"{theme} at {scale}: mean channel difference {difference / samples:0.00}; snapshot should preserve the live layout.");
                    }
                    finally { body.Opacity = 1; layers.Children.Remove(overlay); Settle(window); }
                }
            }
            finally { window.Close(); }
        }
    }
}
