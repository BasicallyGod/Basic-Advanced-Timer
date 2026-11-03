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
    [AvaloniaFact]
    public async Task PillCaptureKeepsItsGeometryAtFractionalAndDoubleCaptureScales()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 2, 14, 15, 0)); model.CompleteSetup();
        var pill = new PillWindow(model); pill.Show(); await Settle(pill);
        var content = pill.Content;
        try
        {
            using var reference = pill.CaptureRenderedFrame()!;
            using var expected = reference.Lock();
            var expectedBytes = new byte[expected.RowBytes * expected.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(expected.Address, expectedBytes, 0, expectedBytes.Length);
            using (var standard = WindowMorph.Capture(pill)) { Assert.NotNull(standard); Assert.Equal(new Vector(96, 96), standard!.Dpi); }
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                using var bitmap = WindowMorph.CaptureVisual(pill, scale);
                pill.Content = new Image { Source = bitmap, Stretch = Stretch.Fill, Width = pill.ClientSize.Width, Height = pill.ClientSize.Height };
                try
                {
                    await Settle(pill);
                    using var frame = pill.CaptureRenderedFrame()!; using var pixels = frame.Lock();
                    Assert.Equal(expected.Size, pixels.Size);
                    var bytes = new byte[pixels.RowBytes * pixels.Size.Height];
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Address, bytes, 0, bytes.Length);
                    double difference = 0; long count = 0;
                    for (var y = 0; y < pixels.Size.Height; y++)
                        for (var x = 0; x < pixels.Size.Width; x++)
                            for (var c = 0; c < 3; c++)
                            { difference += Math.Abs(bytes[y * pixels.RowBytes + x * 4 + c] - expectedBytes[y * expected.RowBytes + x * 4 + c]); count++; }
                    Assert.True(difference / count < 6, $"Pill at {scale}: mean channel difference {difference / count:0.00}");
                }
                finally { pill.Content = content; await Settle(pill); }
            }
        }
        finally { pill.Content = content; pill.Close(); }
    }

    private static async Task Settle(Window window)
    {
        // Capturing ticks the timer but may return null while the first render is queued.
        // Yield to composition and require real frames, with a deadline rather than retrying the test.
        var deadline = System.Diagnostics.Stopwatch.StartNew(); window.InvalidateVisual();
        for (var ready = 0; ready < 3;)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), $"No stable headless frame for {window.GetType().Name} at {window.ClientSize}.");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            await Task.Delay(10);
            using var frame = window.CaptureRenderedFrame(); ready = frame is null ? 0 : ready + 1;
        }
    }

    [AvaloniaFact]
    public async Task DashboardRasterKeepsItsLayoutAtEachCaptureScaleInBothThemes()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 2, 14, 15, 0)); model.CompleteSetup(); model.SetTheme(theme);
            var window = new DashboardWindow(model); window.Show(); await Settle(window);
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
                        await Settle(window); Assert.Equal(expectedBounds, body.Bounds);
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
                    finally { body.Opacity = 1; layers.Children.Remove(overlay); await Settle(window); }
                }
            }
            finally { window.Close(); }
        }
    }
}
