using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
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

    [Fact]
    public void ShowTodayBringsTheViewsBackToTheCurrentDayAndMidnightIsFollowedOnlyWhenTodayWasShowing()
    {
        var now = new DateTime(2026, 10, 13, 23, 59, 40);
        var model = new TimerModel(new TrackingEngine(clock: () => now));
        Assert.Equal(now.Date, model.SelectedDate);
        model.ChangeDate(-3); Assert.NotEqual(now.Date, model.SelectedDate);
        model.ShowToday(); Assert.Equal(now.Date, model.SelectedDate);
        // Left showing today over midnight, the view moves on with the clock.
        now = now.AddSeconds(30); model.Tick(); Assert.Equal(new DateTime(2026, 10, 14), model.SelectedDate);
        // Reviewing an earlier day, it stays put when midnight passes.
        var late = new DateTime(2026, 10, 13, 23, 59, 40); var review = new TimerModel(new TrackingEngine(clock: () => late));
        review.ChangeDate(-2); var chosen = review.SelectedDate; late = late.AddSeconds(30); review.Tick();
        Assert.Equal(chosen, review.SelectedDate);
        review.ShowToday(); Assert.Equal(new DateTime(2026, 10, 14), review.SelectedDate);
    }

    [AvaloniaFact]
    public void TheDashboardOpensOnTheCurrentDayEveryTimeItIsShown()
    {
        var now = new DateTime(2026, 10, 13, 10, 0, 0);
        var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup();
        var windows = new WindowCoordinator(model, () => { });
        try
        {
            model.ChangeDate(-4); windows.Pill.Show();
            windows.Pill.OpenDashboard!(); // The pill's button to the dashboard.
            Assert.Equal(now.Date, model.SelectedDate);
            model.ChangeDate(-2); windows.Hide(); windows.ShowDashboard(); // Back from the tray, or asked for by a menu.
            Assert.Equal(now.Date, model.SelectedDate);
            now = now.AddDays(1); windows.Hide(); windows.ShowDashboard(); // Left hidden overnight.
            Assert.Equal(now.Date, model.SelectedDate);
        }
        finally { windows.Pill.Close(); windows.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void SlidersThatAreSwitchedOffCanStillBeSeenInEveryBuiltInTheme()
    {
        var app = Application.Current!;
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            ThemeEngine.Register(app, new[] { theme });
            var dictionary = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeEngine.VariantFor(theme)];
            var (fill, track) = ThemeEngine.DisabledSliderColours(theme.Colours);
            Assert.Equal(fill, ((ISolidColorBrush)dictionary["SliderThumbBackgroundDisabled"]!).Color); Assert.Equal(track, ((ISolidColorBrush)dictionary["SliderTrackFillDisabled"]!).Color);
            string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            Assert.True(ThemeColours.Contrast(Hex(fill), theme.Colour("Surface")) >= 2, $"{theme.Name} disabled thumb");
            Assert.True(ThemeColours.Contrast(Hex(track), theme.Colour("Surface")) >= 1.5, $"{theme.Name} disabled track");
        }
    }

    [Theory]
    [InlineData(true, false, false, false, true)]  // Covered or snapped under another window: put back in front.
    [InlineData(true, true, false, false, false)]  // Already the active window.
    [InlineData(false, false, false, false, false)] // Hidden.
    [InlineData(true, false, true, false, false)]  // Minimised.
    [InlineData(true, false, false, true, false)]  // A topmost window of another app (such as the screenshot overlay) has the focus.
    public void TheTimerOnlyJumpsToTheFrontWhenNothingTopmostHasTheFocus(bool visible, bool active, bool minimised, bool foregroundTopmost, bool expected)
        => Assert.Equal(expected, ZOrder.ShouldReassert(visible, active, minimised, foregroundTopmost));

    [AvaloniaFact]
    public void TheCrossOnTheMessageBarPutsAwayTheContinueOfferUntilThereIsADifferentOne()
    {
        var now = new DateTime(2026, 10, 13, 9, 0, 0);
        var engine = new TrackingEngine(clock: () => now);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, ThemeLibrary.Default, DayOfWeek.Monday));
        var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            model.ToggleAccount("Account one"); now = now.AddMinutes(30);
            model.ToggleAccount("Account two"); now = now.AddSeconds(20);
            model.ToggleAccount("Account one"); now = now.AddSeconds(10); // A slip of the finger, which offers to take it back.
            Settle(dashboard);
            var bar = dashboard.GetVisualDescendants().OfType<MessageBar>().Single(); var offer = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ContinuePrevious");
            Assert.True(offer.IsVisible); Assert.True(bar.IsVisible);
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DismissMessage").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
            Assert.False(offer.IsVisible); Assert.False(bar.IsVisible); // Gone, so the timeline is clear again.
            Assert.True(model.CanContinuePrevious); // Still there from the footer, pill and menus.
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void AccountRowsPairBudgetUsedWithItsPeriodAndKeepThePercentageForTheHover()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("accountCard")).ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.DoesNotContain(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.EndsWith('%'))); // No percentage on the row itself.
            var withBudget = model.Accounts.First(a => a.Budget is not null);
            Assert.Matches(@"^\d+h\d*m? / \d+h", model.BudgetUsage(withBudget).Replace("h ", "h")); Assert.Equal("This week", model.BudgetPeriodLabel(new TimerAccount("x", "#888888", null) with { Budget = TimeSpan.FromHours(5), Period = "week" }));
            Assert.Equal("This month", model.BudgetPeriodLabel(new TimerAccount("x", "#888888", null) with { Budget = TimeSpan.FromHours(5), Period = "month" }));
            Assert.Equal("Today", model.BudgetPeriodLabel(new TimerAccount("x", "#888888", null) with { Budget = TimeSpan.FromHours(5), Period = "day" })); Assert.Equal("", model.BudgetPeriodLabel(new TimerAccount("x", "#888888", null)));
            Assert.Contains("% used", model.BudgetDetail(withBudget)); Assert.Contains("how far through", model.BudgetDetail(withBudget));
            var bars = rows.SelectMany(r => r.GetVisualDescendants().OfType<BudgetBar>()).ToList(); Assert.NotEmpty(bars);
            Assert.All(bars, bar => Assert.Contains("% used", (string?)ToolTip.GetTip(bar)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRecordingAccountsCardTakesAGentleTintOfTheTrackingColourInEveryTheme()
    {
        var app = Application.Current!;
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            ThemeEngine.Register(app, new[] { theme });
            var dictionary = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeEngine.VariantFor(theme)];
            var tint = ((ISolidColorBrush)dictionary["LiveTint"]!).Color;
            string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            Assert.NotEqual(Color.Parse(theme.Colour("Surface")), tint); // Visible against the surface...
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), Hex(tint)) >= 4.5, $"{theme.Name}: text on the recording card"); // ...and the words on it still read.
        }
    }

    [AvaloniaFact]
    public void FocusLeftBehindByAMouseClickDoesNotHoldAnIconOpen()
    {
        var glyph = new IconGlyph(IconKind.Gear) { Transitions = null }; var button = new Button { Content = glyph };
        var window = new Window { Content = button, Width = 100, Height = 100 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            button.Focus(NavigationMethod.Pointer); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, glyph.Hover); // Clicked, so the next mouse-over still has something to play.
            window.FocusManager?.ClearFocus(); Dispatcher.UIThread.RunJobs();
            button.Focus(NavigationMethod.Tab); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, glyph.Hover); // Tabbing to it still shows it.
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ALateTickOrSaveAfterTheDatabaseHasClosedDoesNotThrow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-late-save-" + Guid.NewGuid());
        try
        {
            var store = new TimerStore(folder); var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
            model.ToggleAccount("Account one");
            store.Dispose(); // A restart closes the database while the one second clock still has ticks queued.
            for (var i = 0; i < 20; i++) model.Tick();
            Assert.True(model.Save());
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void ASaveThatFailsBecauseTheDatabaseClosedUnderItIsReportedNotThrown()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-closed-under-" + Guid.NewGuid());
        try
        {
            var store = new TimerStore(folder); var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
            model.ToggleAccount("Account one"); model.MarkClosed();
            store.Dispose();
            for (var i = 0; i < 20; i++) model.Tick(); // Closed, so the clock does nothing at all.
            Assert.True(model.Save());
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
