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

// Round 26: the first feedback on stacks and Details. Even lines in a tile, a note box with its text in the middle, stack bars with air round their initials, handles that show on
// Light, a rows-and-columns tooltip, no counts in the kind chip, no Tracking line in the sidebar, a block's note clear of its note mark, and tile lines that read on Light.
public sealed class Round26Tests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static DateTime T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Model(string theme = "Dark", double nowHours = 20)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(nowHours)), new StoreSettings(new() { new("Account one"), new("Account two"), new("Internal") }, false, theme, DayOfWeek.Monday)); model.CompleteSetup();
        model.Engine.AddSession("Account one", T(9, 0), T(10, 0)); model.Engine.AddSession("Account two", T(9, 6), T(10, 6)); model.Engine.AddSession("Internal", T(9, 12), T(10, 12)); model.Engine.AddSession("Account one", T(9, 18), T(10, 18));
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static Point Near(Window window, Control c) => c.TranslatePoint(new Point(24, c.Bounds.Height / 2), window)!.Value;
    private static void Press(Window window, Point p) { window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Settle(window); }
    private static Border Tile(Window w) => w.GetVisualDescendants().OfType<Border>().Single(b => b.Name?.StartsWith("TimelineStack") == true);

    [AvaloniaFact]
    public void TheNoteBoxKeepsItsTextInTheMiddleAndTheStackBarsHaveAirRoundTheirInitials()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Press(window, Near(window, Tile(window)));
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, Find<TextBox>(window, "SelectedNote").VerticalContentAlignment);
            Assert.Equal("Blocks", Find<Border>(window, "DetailsKind").GetVisualDescendants().OfType<TextBlock>().First().Text); // No counts in the chip.
            foreach (var bar in window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Stack_") == true))
            {
                var chip = bar.GetVisualDescendants().OfType<Border>().First(b => b.Child is TextBlock { Name: "BlockInitials" });
                var chipTop = chip.TranslatePoint(default, bar)!.Value.Y;
                Assert.True(chipTop >= 2.5 && bar.Bounds.Height - (chipTop + chip.Bounds.Height) >= 2.5, $"The initials sit {chipTop} from the top of a bar {bar.Bounds.Height} tall, {chip.Bounds.Height} high");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheHandlesAreTheAccentNotTheTextColourSoTheyShowOnLight()
    {
        var model = Model("Light"); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Press(window, Near(window, Tile(window)));
            var handle = Find<Border>(window, "StackHandleEnd"); var accent = Color.Parse(model.CurrentTheme.Colour("Accent"));
            Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(handle.Background).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRecordingRowAndItsRingAreNotCutOffAtTheFootOfTheStackTimeline()
    {
        var model = Model("Dark", 9.7); model.ToggleAccount("Account two"); var window = new DashboardWindow(model); window.Show(); Settle(window); // Recording from 09:42, over the four.
        try
        {
            Press(window, Near(window, Tile(window)));
            var scroll = Find<ScrollViewer>(window, "StackScroll"); var live = window.GetVisualDescendants().OfType<Border>().Single(b => b.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Any(e => e.Name == "StackRecordingDot") && b.Name?.StartsWith("Stack_") == true);
            var bottom = live.TranslatePoint(new Point(0, live.Bounds.Height), scroll)!.Value.Y;
            Assert.True(bottom + 2 <= scroll.Bounds.Height, $"The recording bar ends at {bottom} in a scroll area {scroll.Bounds.Height} tall, so its 2 pixel ring is cut"); // Room for the ring below it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTooltipOfATileHasARowAndThreeColumnsForEachBlockSoTheTimesLineUp()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(Tile(window))); var grid = Assert.IsType<Grid>(tip.Children[1]);
            Assert.Equal(4, grid.RowDefinitions.Count); Assert.Equal(3, grid.ColumnDefinitions.Count);
            var times = grid.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) == 1).ToList(); Assert.Equal(4, times.Count);
            Assert.All(times, t => Assert.NotEmpty(t.FontFeatures!)); // Tabular figures: every digit the same width.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABlocksNoteStopsShortOfItsNoteMarkAndTheSidebarSaysNothingUnderTheNameWhileTracking()
    {
        var model = Model(); model.Engine.AddSession("Account one", T(12, 0), T(14, 0)); var noted = model.Engine.Sessions[^1]; model.EditEntry(noted, noted.Account, noted.From, noted.To, "Kick-off prep and agenda for the week");
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var block = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry" + (model.Engine.Sessions.Count - 1));
            var note = block.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.StartsWith("• "));
            var mark = block.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Name == "NoteMark");
            Assert.True(note.Margin.Right >= 20); // The words are cut before the mark, not run under it.
            Assert.True(note.TranslatePoint(new Point(note.Bounds.Width, 0), block)!.Value.X <= mark.TranslatePoint(default, block)!.Value.X + .5);
            model.ToggleAccount("Account two"); Settle(window);
            Assert.False(Find<TextBlock>(window, "SidebarWhoStatus").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheLinesInATileHoldThreeToOneOnTheTilesFillInLightAndDark()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = Model(theme); var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                var fill = model.CurrentTheme.Colour("Hover");
                var lines = Tile(window).GetVisualDescendants().OfType<ReviewViews.StackLines>().Single();
                var colours = lines.Lines;
                Assert.All(colours, l => Assert.True(Timer.Core.ThemeColours.Contrast($"#{l.Colour.R:X2}{l.Colour.G:X2}{l.Colour.B:X2}", fill) >= 3, $"{theme}: {l.Colour} on {fill}"));
            }
            finally { window.Close(); }
        }
    }
}
