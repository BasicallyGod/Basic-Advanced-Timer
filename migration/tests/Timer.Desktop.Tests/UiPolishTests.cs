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

/// <summary>Regressions from the second round of Windows screenshots: a settings row running off its card and a heavy calendar.</summary>
public sealed class UiPolishTests
{
    private static DashboardWindow Open(TimerModel model, string tab)
    {
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == tab).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void NothingInSettingsRunsPastTheEdgeOfItsCard()
    {
        var model = TimerModel.CreateDemo();
        var window = Open(model, "Settings");
        try
        {
            var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("dayCard") && b.IsVisible && b.Bounds.Width > 100).ToList();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
                foreach (var child in card.GetVisualDescendants().OfType<Control>().Where(c => c.IsVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0))
                {
                    if (child.GetVisualAncestors().OfType<ScrollViewer>().Any(s => card.IsVisualAncestorOf(s))) continue; // Inside a box of its own that scrolls.
                    var right = child.TranslatePoint(new Point(child.Bounds.Width, 0), card)?.X ?? 0;
                    Assert.True(right <= card.Bounds.Width + 1, $"{child.GetType().Name} {child.Name} ends at {right} in a card {card.Bounds.Width} wide");
                }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCalendarWearsTheThemeNotTheComputersGreys()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Dark");
        var window = Open(model, "TimelineTab");
        try
        {
            Assert.True(window.TryFindResource("CalendarViewBackground", window.ActualThemeVariant, out var background));
            Assert.Equal(Color.Parse(model.CurrentTheme.Colour("Surface")), ((SolidColorBrush)background!).Color);
            Assert.True(window.TryFindResource("CalendarViewCalendarItemRevealBorderBrush", window.ActualThemeVariant, out var gaps));
            Assert.Equal(0, ((SolidColorBrush)gaps!).Color.A); // No grid of grey bars between the days.
        }
        finally { window.Close(); }
    }
}
