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

    [AvaloniaFact]
    public void BackToPillIsFirstOnTheRailAndCloseTimerIsAtTheFootBelowSettings()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            double Top(string name) { var b = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == name); return b.TranslatePoint(new Point(0, 0), window)!.Value.Y; }
            Assert.True(Top("BackToPill") < Top("TimelineTab"));
            Assert.True(Top("QuitTimer") > Top("Settings"));
            var quits = 0; model.QuitRequested += () => quits++;
            window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "QuitTimer").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, quits);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Working time");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Day elapsed");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRightClickMenuCollapsesToTheTrayInsteadOfClosing()
    {
        var model = new TimerModel(new TrackingEngine()); var hides = 0; var quits = 0; model.HideRequested += () => hides++; model.QuitRequested += () => quits++;
        var menu = Ui.SettingsMenu(model); var items = ((IEnumerable<Control>)menu.ItemsSource!).OfType<MenuItem>().ToList();
        Assert.DoesNotContain(items, m => (string?)m.Header == "Decimal hours"); Assert.DoesNotContain(items, m => (string?)m.Header == "Close timer");
        items.Single(m => (string?)m.Header == "Collapse to tray").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(1, hides); Assert.Equal(0, quits);
    }

    [AvaloniaFact]
    public void CollapsingWithoutATrayMinimisesInsteadOfLosingTheTimer()
    {
        var model = new TimerModel(new TrackingEngine()); var windows = new WindowCoordinator(model, () => { });
        windows.Pill.Show();
        try { model.RequestHide(); Assert.Equal(WindowState.Minimized, windows.Pill.WindowState); }
        finally { windows.Pill.WindowState = WindowState.Normal; windows.Pill.Close(); }
    }

    [AvaloniaFact]
    public void DialogAndSettingsHeadingsUseTheDisplayFace()
    {
        var model = new TimerModel(new TrackingEngine()); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            dashboard.Dialogs.Show(new ThemesDialog(model)); Settle(dashboard);
            Assert.Contains("clock", dashboard.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "DialogTitle").Classes);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheFloatingMessageHasItsOwnRaisedLookAgainstTheTimeline()
    {
        var model = new TimerModel(new TrackingEngine()); var bar = new MessageBar(model);
        Assert.Contains("messageBar", bar.Classes); Assert.DoesNotContain("chip", bar.Classes);
    }

    [AvaloniaFact]
    public void TheFloatingMessageReallyGetsABackgroundAndAnOutline()
    {
        var model = new TimerModel(new TrackingEngine()); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            model.ExplainRunning(); Settle(dashboard);
            var bar = dashboard.GetVisualDescendants().OfType<MessageBar>().Single();
            Assert.True(bar.IsVisible);
            Assert.IsAssignableFrom<ISolidColorBrush>(bar.Background); // A style that names Border alone skips a subclass, which once left it see-through.
            Assert.NotNull(bar.BorderBrush); Assert.True(bar.BorderThickness.Left > 1);
        }
        finally { dashboard.Close(); }
    }
}
