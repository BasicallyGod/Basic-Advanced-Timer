using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

// Tooltips that wear the theme, and text that slides when it is too long for the pill.
public sealed class UxTweakTests
{
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }

    [AvaloniaFact]
    public void LongTextSlidesAndShortTextStaysPut()
    {
        var longText = new AutoScrollText(new TextBlock { Text = "A very long client account name that cannot fit in the pill" }) { Width = 120 };
        var shortText = new AutoScrollText(new TextBlock { Text = "Short" }) { Width = 120 };
        var window = new Window { Content = new StackPanel { Children = { longText, shortText } } }; window.Show();
        try
        {
            Settle(window);
            Assert.True(longText.Overflow > 100); Assert.Equal(0, shortText.Overflow);
            Assert.True(longText.Scrolling); Assert.False(shortText.Scrolling);
            Assert.Equal(120, longText.Bounds.Width, 0.5); // It keeps to the room it was given.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNameGivesWayButTheChevronStaysBesideIt()
    {
        var name = new TextBlock { Text = "A very long client account name that cannot fit in the pill at all" };
        var chevron = new Border { Width = 12, Height = 12 }; var dot = new Border { Width = 14, Height = 14 };
        var row = new NameRow { Width = 300 }; row.Children.Add(dot); row.Children.Add(new AutoScrollText(name)); row.Children.Add(chevron);
        var window = new Window { Content = row }; window.Show();
        try
        {
            Settle(window);
            Assert.True(chevron.Bounds.Right <= 300.5, $"chevron ends at {chevron.Bounds.Right}");
            Assert.True(chevron.Bounds.Left > dot.Bounds.Right + 40);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryThemeAndPillStateWritesTooltipColoursFromItsOwnSurfaceAndText()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            ThemeEngine.Register(Application.Current!, new[] { theme });
            foreach (var variant in new[] { ThemeEngine.VariantFor(theme), ThemeEngine.StateVariant(theme, ThemeDerivation.Recording), ThemeEngine.StateVariant(theme, ThemeDerivation.Break), ThemeEngine.StateVariant(theme, ThemeDerivation.Overtime) })
            {
                var dictionary = (ResourceDictionary)Application.Current!.Resources.ThemeDictionaries[variant];
                var back = ((ISolidColorBrush)dictionary["ToolTipBackground"]!).Color; var text = ((ISolidColorBrush)dictionary["ToolTipForeground"]!).Color;
                Assert.Equal(((ISolidColorBrush)dictionary["Surface"]!).Color, back); Assert.Equal(((ISolidColorBrush)dictionary["Ink"]!).Color, text);
            }
        }
    }
}
