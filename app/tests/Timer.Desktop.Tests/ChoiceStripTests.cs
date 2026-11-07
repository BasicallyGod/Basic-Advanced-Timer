using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>A choice strip stands in for a drop-down: the same items, selection and event, drawn as chips or segments.</summary>
public class ChoiceStripTests
{
    private static Window Host(ChoiceStrip strip) { var w = new Window { Width = 600, Height = 200, Content = strip }; w.Show(); return w; }

    [AvaloniaFact]
    public void SelectingRaisesTheEventOnceAndLightsOnlyThatChip()
    {
        var strip = new ChoiceStrip { Name = "T", Look = ChoiceLook.Chips, ItemsSource = new[] { "One", "Two", "Three" }, SelectedItem = "One" };
        var window = Host(strip);
        try
        {
            var raised = 0; strip.SelectionChanged += (_, _) => raised++;
            strip.SelectedItem = "Two";
            Assert.Equal(1, raised); Assert.Equal(1, strip.SelectedIndex); Assert.Equal("Two", strip.SelectedItem);
            Assert.Single(strip.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("chipOn"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LongListsFoldIntoMoreAndSelectedIndexCanBeSet()
    {
        var items = Enumerable.Range(1, 12).Select(i => "Item " + i).ToList();
        var strip = new ChoiceStrip { Look = ChoiceLook.Chips, ItemsSource = items, SelectedItem = "Item 1" };
        var window = Host(strip);
        try
        {
            Assert.True(strip.GetVisualDescendants().OfType<Button>().Count() < items.Count); // Folded: fewer chips than items, plus a more chip.
            strip.SelectedIndex = 10; Assert.Equal("Item 11", strip.SelectedItem);
        }
        finally { window.Close(); }
    }
}
