using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ToolbarAndTrackingTests
{
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheNowChipAndTheHourLabelsShareTheirRightEdge()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NowChip"); var text = (TextBlock)chip.Child!;
            var label = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "09:00");
            double Right(TextBlock t) => t.TranslatePoint(new Point(t.Bounds.Width, 0), window)!.Value.X - (t.TextAlignment == TextAlignment.Right ? 0 : 0);
            // Both are right-aligned in boxes whose right edges meet, once the chip's own padding is taken off.
            var chipTextRight = chip.TranslatePoint(new Point(chip.Bounds.Width - chip.Padding.Right, 0), window)!.Value.X;
            Assert.Equal(Right(label), chipTextRight, 0.5);
            Assert.Equal(TextAlignment.Right, label.TextAlignment); Assert.Equal(TextAlignment.Right, text.TextAlignment);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SwitchingTooltipsOffStopsThemOpeningInAnyWindow()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineZoomIn"); // Has a tooltip.
            bool Opens(Control control)
            {
                var args = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent, control); control.RaiseEvent(args); return !args.Cancel; // What hovering does before showing a tip.
            }
            Assert.True(model.Tooltips); Assert.True(Opens(button));
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var box = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ShowTooltips");
            Assert.True(box.IsChecked); box.IsChecked = false; box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // A real click toggles the box before the handler runs.
            Assert.False(model.Tooltips); Assert.False(Ui.TooltipsOn); Assert.False(Opens(button));
            var pill = new PillWindow(model); pill.Show(); // A window made afterwards obeys it too.
            try { Assert.False(Opens(pill.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) is not null))); }
            finally { pill.Close(); }
            model.SetTooltips(true); Assert.True(Ui.TooltipsOn);
        }
        finally { window.Close(); model.SetTooltips(true); }
    }

    [AvaloniaFact]
    public void TrackingTurnsTheClockSecondsAndStatusGreenOnTheDashboardAndThePill()
    {
        var now = new DateTime(2026, 10, 9, 10, 0, 0);
        var model = TimerModel.CreateDemo(() => now); var dash = new DashboardWindow(model); dash.Show(); var pill = new PillWindow(model); pill.Show();
        try
        {
            if (model.IsTracking) model.TogglePause();
            model.ToggleAccount("Account one"); Settle(dash); Settle(pill);
            Assert.True(model.IsTracking); Assert.Equal("Recording", model.ThemeState);
            var green = Color.Parse(model.CurrentTheme.Colour("Live")); Assert.True(green.G > green.R + 20, "The theme's tracking colour is green.");
            void Check(Window window, string name)
            {
                var main = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("clock"));
                var seconds = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("clockLight"));
                Assert.Contains("live", main.Classes); Assert.Contains("live", seconds.Classes);
                Assert.True(seconds.Opacity < 1 && main.Opacity == 1, name + " seconds are the lighter shade");
                Assert.True(((ISolidColorBrush)main.Foreground!).Color.G > ((ISolidColorBrush)main.Foreground!).Color.R, name + " clock is green");
                Assert.Equal(((ISolidColorBrush)main.Foreground!).Color, ((ISolidColorBrush)seconds.Foreground!).Color);
            }
            Check(dash, "dashboard"); Check(pill, "pill");
            var status = dash.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is { } x && x.StartsWith("● ") && t.Classes.Contains("live"));
            Assert.True(((ISolidColorBrush)status.Foreground!).Color.G > ((ISolidColorBrush)status.Foreground!).Color.R);
            // On a break the green goes, and the clock takes the break colour.
            model.ToggleBreak(); Settle(dash);
            Assert.DoesNotContain("live", dash.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("clock")).Classes);
        }
        finally { dash.Close(); pill.Close(); }
    }

    [Fact]
    public void TheRecordingPillsGreenReadsOnItsDarkSurfaceInBothThemes()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            var variant = ThemeDerivation.Variant(theme, ThemeDerivation.Recording);
            Assert.True(ThemeColours.Contrast(variant["Live"], variant["Surface"]) >= 4.5, theme.Name);
            var (r, g, b) = ThemeColours.Rgb(variant["Live"]); Assert.True(g > r && g > b, theme.Name + " stays green");
        }
    }
}
