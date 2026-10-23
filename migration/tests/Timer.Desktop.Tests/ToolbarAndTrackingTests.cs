using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ToolbarAndTrackingTests
{
    // A few passes, because a new page's visuals appear only after a layout and render pass, and a slow Windows runner has been seen to need more than one.
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheNowChipSitsUnderTheMarkerAtTheCurrentTimeAndSaysWhatTimeItIs()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NowChip"); var line = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NowMarker");
            Assert.Equal(model.Engine.Now.ToString("HH:mm"), ((TextBlock)chip.Child!).Text);
            double Centre(Border b) => b.TranslatePoint(new Point(b.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.Equal(Centre(line), Centre(chip), 0.6); // The chip is centred on the line.
            // And the line is where the current time falls on the hours: so many hours from midnight, at so many pixels an hour, after the gutter.
            var hour = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "TimelineHour_12");
            var twelve = hour.TranslatePoint(new Point(hour.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.Equal(twelve + (model.Engine.Now.TimeOfDay.TotalHours - 12) * model.TimelinePixelsPerHour, Centre(line), 1.0);
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
            window.OpenSettings();
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
    public void TrackingTurnsTheRailsLightAndPrimaryControlGreenAndABreakTurnsTheLightAmber()
    {
        var now = new DateTime(2026, 10, 9, 10, 0, 0);
        var model = TimerModel.CreateDemo(() => now); var dash = new DashboardWindow(model); dash.Show();
        try
        {
            if (model.IsTracking) model.TogglePause();
            model.ToggleAccount("Account one"); Settle(dash);
            Assert.True(model.IsTracking); Assert.Equal("Recording", model.ThemeState);
            var green = Color.Parse(model.CurrentTheme.Colour("Live")); Assert.True(green.G > green.R + 20, "The theme's tracking colour is green.");
            var light = dash.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RailStateLight");
            var tile = dash.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RailPrimaryTile");
            Assert.Equal(green, ((ISolidColorBrush)light.Background!).Color); Assert.Equal(green.G, ((ISolidColorBrush)tile.Background!).Color.G); // The light, and a soft tint of the same green.
            // On a break the light takes the break colour, and the primary control stays green with the account's initials.
            model.ToggleBreak(); Settle(dash);
            var amber = Color.Parse(model.CurrentTheme.Colour("Break"));
            Assert.Equal(amber, ((ISolidColorBrush)light.Background!).Color); Assert.Equal(green.G, ((ISolidColorBrush)tile.Background!).Color.G);
        }
        finally { dash.Close(); }
    }

    [Fact]
    public void TheRecordingPillsGreenReadsOnItsDarkSurfaceInBothThemes()
    {
        foreach (var theme in AppThemes.BuiltIn)
        {
            var variant = ThemeDerivation.Variant(theme, ThemeDerivation.Recording);
            Assert.True(ThemeColours.Contrast(variant["Live"], variant["Surface"]) >= 4.5, theme.Name);
            var (r, g, b) = ThemeColours.Rgb(variant["Live"]); Assert.True(g > r && g > b, theme.Name + " stays green");
        }
    }

    [AvaloniaFact]
    public void TheEditButtonsShareTheBreakButtonsOutlineAndThePageHasRoomUnderItsTitle()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            foreach (var name in new[] { "AddTime", "UndoEdit" })
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                Assert.Contains("ctl", button.Classes); Assert.Equal(new Thickness(1), button.BorderThickness); Assert.Equal(38, button.Height);
                Assert.Single(button.GetVisualDescendants().OfType<IconGlyph>()); Assert.NotEmpty(button.GetVisualDescendants().OfType<TextBlock>().Single().Text!);
            }
            var title = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Timeline" && t.Classes.Contains("clock"));
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            var gap = scroll.TranslatePoint(new Point(0, 0), window)!.Value.Y - (title.TranslatePoint(new Point(0, title.Bounds.Height), window)!.Value.Y);
            Assert.True(gap >= 14, $"only {gap:0}px between the title and the timeline");
        }
        finally { window.Close(); }
    }
}
