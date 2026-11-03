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
