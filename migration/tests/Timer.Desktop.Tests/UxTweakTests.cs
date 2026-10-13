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

    [AvaloniaFact]
    public void WritingATwiceLeavesUnchangedColoursAloneAndAPreviewWritesOnlyTheMainVariant()
    {
        var theme = ThemeLibrary.BuiltIn.First(t => t.Name == "Studio"); var app = Application.Current!;
        ThemeEngine.Register(app, new[] { theme });
        var dictionary = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeEngine.VariantFor(theme)];
        var ink = dictionary["Ink"]; var accent = dictionary["Accent"];
        ThemeEngine.Register(app, new[] { theme });
        Assert.Same(ink, dictionary["Ink"]); Assert.Same(accent, dictionary["Accent"]); // Nothing changed, so nothing was replaced.
        var edited = new Dictionary<string, string>(theme.Colours) { ["Accent"] = "#123456" };
        var recording = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeEngine.StateVariant(theme, ThemeDerivation.Recording)]; var recordingAccent = recording["Accent"];
        ThemeEngine.RegisterPreview(app, theme with { Colours = edited });
        Assert.NotSame(accent, dictionary["Accent"]); Assert.Same(ink, dictionary["Ink"]); // Only the colour that moved was replaced.
        Assert.Same(recordingAccent, recording["Accent"]); // A preview leaves the pill's tinted variants alone.
        ThemeEngine.Register(app, new[] { theme }); // Put back.
    }

    [AvaloniaFact]
    public void TheTrayMenuHasWordsOnEveryItemFromTheStart()
    {
        var model = new TimerModel(new TrackingEngine()); var windows = new WindowCoordinator(model, () => { });
        var tray = new TrayController(model, windows);
        try
        {
            var items = tray.Icons[0].Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
            Assert.Equal(6, items.Count);
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Header), "A menu item has no words."));
        }
        finally { tray.Dispose(); }
    }

    [AvaloniaFact]
    public void TheChevronIsDrawnAgainInTheNewColourWhenTheThemeVariantChanges()
    {
        var chevron = new Chevron { Width = 14, Height = 14, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var window = new Window { Width = 60, Height = 60, Content = chevron, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light }; window.Show();
        try
        {
            byte[] Frame() { Settle(window); using var frame = window.CaptureRenderedFrame(); using var stream = new MemoryStream(); frame!.Save(stream); return stream.ToArray(); }
            ThemeEngine.Register(Application.Current!, ThemeLibrary.BuiltIn);
            var light = Frame(); window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; var dark = Frame();
            Assert.NotEqual(light, dark); // The stroke changed colour, not only the window around it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillIconsDotTakesTheStatusColourOfTheDay()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BackToPill");
            var glyph = (IconGlyph)button.Content!;
            Assert.Equal(IconKind.Minimise, glyph.Kind); Assert.Equal("Switch to pill", ToolTip.GetTip(button));
            if (!model.IsTracking) model.ToggleAccount("Account one"); Settle(window);
            Assert.Equal("Live", glyph.DotKey);
            model.TogglePause(); Settle(window); Assert.Equal("Accent", glyph.DotKey);
            model.ToggleBreak(); Settle(window); Assert.Equal("Break", glyph.DotKey);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void SlidersCanBeSeenInEveryBuiltInTheme()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            var (fill, track) = ThemeEngine.SliderColours(theme.Colours);
            string Hex(Avalonia.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            foreach (var (name, colour) in new[] { ("fill", fill), ("track", track) })
                foreach (var behind in new[] { "Surface", "Panel" })
                    Assert.True(ThemeColours.Contrast(Hex(colour), theme.Colour(behind)) >= 3, $"{theme.Name} slider {name} on {behind}: {ThemeColours.Contrast(Hex(colour), theme.Colour(behind)):0.0} to 1");
        }
    }

    [AvaloniaFact]
    public void TheSliderResourcesAreWrittenFromThoseColours()
    {
        var theme = ThemeLibrary.Hawthorn; var app = Application.Current!;
        ThemeEngine.Register(app, new[] { theme });
        var dictionary = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeEngine.VariantFor(theme)];
        var (fill, track) = ThemeEngine.SliderColours(theme.Colours);
        Assert.Equal(track, ((ISolidColorBrush)dictionary["SliderTrackFill"]!).Color); Assert.Equal(fill, ((ISolidColorBrush)dictionary["SliderThumbBackground"]!).Color);
        Assert.NotEqual(Color.Parse(theme.Colour("Line")), track); // No longer the hairline colour.
    }

    [Fact]
    public void AnAnimationThatStoppedByItselfIsNoLongerCountedAsRunning()
    {
        var pending = new TaskCompletionSource(); var run = new RunningAnimation(pending.Task);
        Assert.True(run.Alive);
        pending.SetResult(); Assert.False(run.Alive); // It was running and then ended: asking for it again must start it afresh.
        Assert.True(new RunningAnimation(Task.CompletedTask).Alive); // Finished at once, as where nothing ticks: counted as running so tests see the state.
    }
}
