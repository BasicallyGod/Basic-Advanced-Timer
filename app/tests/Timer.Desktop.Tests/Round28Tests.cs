using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 28: Light is the cool, crisp palette; the initials in Details are white on a chip that holds 4.5 to 1 against them; and the floating message is inverted
// (the ink colour as its card, a medallion in a state colour, a line along its foot that runs down).
public sealed class Round28Tests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static DateTime T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Save(Window window, string name) { if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is not { } folder) return; Directory.CreateDirectory(folder); Settle(window); using var shot = window.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, name + ".png")); }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheInitialsInADetailsStackAreWhiteOnAChipThatHoldsTheContrast(string theme)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => T(20, 0)), new StoreSettings(new() { new("Account one"), new("Account two"), new("Internal") }, false, theme, DayOfWeek.Monday)); model.CompleteSetup();
        model.Engine.AddSession("Account one", T(9, 0), T(10, 0)); model.Engine.AddSession("Account two", T(9, 6), T(10, 6)); model.Engine.AddSession("Internal", T(9, 12), T(10, 12)); model.Engine.AddSession("Account one", T(9, 18), T(10, 18));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var tile = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name?.StartsWith("TimelineStack") == true);
            var at = tile.TranslatePoint(new Point(24, tile.Bounds.Height / 2), window)!.Value; window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window);
            var chips = window.GetVisualDescendants().OfType<Border>().Where(b => b.Child is TextBlock { Name: "BlockInitials" }).ToList();
            Assert.True(chips.Count >= 4);
            foreach (var chip in chips)
            {
                var letters = (TextBlock)chip.Child!;
                Assert.Equal(Colors.White, ((ISolidColorBrush)letters.Foreground!).Color);
                var fill = ((ISolidColorBrush)chip.Background!).Color; var hex = $"#{fill.R:X2}{fill.G:X2}{fill.B:X2}";
                Assert.True(ThemeColours.Contrast("#FFFFFF", hex) >= 4.5, $"white on {hex} is {ThemeColours.Contrast("#FFFFFF", hex):0.0} to 1");
            }
            Save(window, "details-white-initials-" + theme);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheFloatingMessageIsAPorcelainCardWithAnInlaidStateLight(string theme)
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("Account one") }, false, theme, DayOfWeek.Monday)); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.ExplainRunning(); Settle(window);
            var bar = window.GetVisualDescendants().OfType<MessageBar>().Single();
            Assert.True(bar.TryFindResource("Porcelain", bar.ActualThemeVariant, out var porcelain));
            Assert.Equal(((ISolidColorBrush)porcelain!).Color, ((ISolidColorBrush)bar.Background!).Color);
            Assert.True(bar.BoxShadow.Count > 0); // Raised well clear of the page.
            var words = bar.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "MessageText");
            var wordsColour = ((ISolidColorBrush)words.Foreground!).Color; var card = ((ISolidColorBrush)bar.Background!).Color;
            Assert.True(ThemeColours.Contrast($"#{wordsColour.R:X2}{wordsColour.G:X2}{wordsColour.B:X2}", $"#{card.R:X2}{card.G:X2}{card.B:X2}") >= 7, "the words read on the card");
            var medallion = bar.GetVisualDescendants().OfType<Border>().First(b => b.Name == "MessageIcon");
            Assert.True(medallion.BoxShadow.Count > 0); // Sunk, like an inlaid light.
            var well = ((ISolidColorBrush)medallion.Background!).Color;
            var glyph = bar.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(p => p.IsVisible && p.Stroke is not null);
            var g = ((ISolidColorBrush)glyph.Stroke!).Color;
            Assert.True(ThemeColours.Contrast($"#{g.R:X2}{g.G:X2}{g.B:X2}", $"#{well.R:X2}{well.G:X2}{well.B:X2}") >= 3, "the light stands clear of its well");
            Assert.Empty(bar.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "MessageCountdown")); // No countdown line.
            Save(window, "toast-porcelain-" + theme);
        }
        finally { window.Close(); }
    }
}
