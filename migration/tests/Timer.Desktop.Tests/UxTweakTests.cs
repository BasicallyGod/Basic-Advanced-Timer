using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Tooltips that wear the theme, and text that slides when it is too long for the pill.
public sealed class UxTweakTests
{
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }

    [AvaloniaFact]
    public void TheAccountsDialogCallsTheVisibilityBoxInTrackerAndExplainsHiding()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday));
        var dialog = new AccountsDialog(model);
        var texts = dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains("In tracker", texts); Assert.DoesNotContain("Show", texts);
        Assert.Contains(texts, t => t.Contains("stays in every report"));
    }

    [AvaloniaFact]
    public void SettingsSaysBudgetPercentagesStayInWeekOverviewWhenTooltipsAreOff()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday));
        var view = new SettingsView(model);
        var note = view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "TooltipsNote");
        Assert.Contains("Week overview", note.Text);
    }

    [AvaloniaFact]
    public void AFadeShowsOnlyWhileMoreContentLiesBelow()
    {
        var content = new StackPanel(); for (var i = 0; i < 30; i++) content.Children.Add(new TextBlock { Text = "row " + i, Height = 30 });
        var scroll = new ScrollViewer { Content = content, Height = 200 };
        var host = Ui.WithMoreBelow(scroll);
        var window = new Window { Width = 300, Height = 300, Content = host }; window.Show();
        try
        {
            Settle(window);
            var fade = host.Children.OfType<Border>().Single(b => b.Name == "MoreBelow");
            Assert.True(fade.IsVisible); Assert.False(fade.IsHitTestVisible);
            scroll.Offset = new Vector(0, 100000); Settle(window);
            Assert.False(fade.IsVisible);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void LightAndDarkAccentsKeepClearOfRedGreenAndOrange()
    {
        foreach (var theme in new[] { AppThemes.Light, AppThemes.Dark })
        {
            var accent = Color.Parse(theme.Colour("Accent"));
            Assert.True(accent.B > accent.R + 40 && accent.B >= accent.G, $"{theme.Name} accent {theme.Colour("Accent")} is not clearly blue");
        }
    }

    [AvaloniaFact]
    public void TheWeekOverviewNeverAsksForASidewaysScrollBar()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday));
        var scroll = ReviewViews.Week(model).GetLogicalDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
    }

    [AvaloniaFact]
    public void TheWindowsStayOnTopUnlessSwitchedOffInSettings()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday));
        var pill = new PillWindow(model); var dashboard = new DashboardWindow(model);
        Assert.True(pill.Topmost); Assert.True(dashboard.Topmost);
        model.SetDashboardOnTop(false);
        Assert.False(dashboard.Topmost); Assert.True(pill.Topmost);
        model.SetPillOnTop(false); Assert.False(pill.Topmost);
        model.SetDashboardOnTop(true); Assert.True(dashboard.Topmost);
    }

    [Fact]
    public void TheOnTopChoicesAreSavedWithTheAppearanceSettings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-ontop-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.True(prefs.DashboardOnTop); store.Save(engine, prefs with { DashboardOnTop = false }); }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.False(prefs.DashboardOnTop); Assert.True(prefs.PillOnTop); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void TheRightClickMenuShowsADividerOnlyBetweenItemsThatAreShowing()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday));
        var menu = Ui.SettingsMenu(model); var target = new Border { ContextMenu = menu, Width = 200, Height = 100 };
        var window = new Window { Content = target }; window.Show();
        try
        {
            int Dividers() => menu.ItemsSource!.OfType<Separator>().Count(r => r.IsVisible);
            menu.Open(target); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Dividers()); // Only Collapse to tray is showing, so there is nothing to divide it from.
            menu.Close(); model.SetDeveloper(true); menu.Open(target); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Dividers()); // The version, then a divider, then Collapse to tray. Nothing above the version.
            Assert.IsNotType<Separator>(menu.ItemsSource!.Cast<object>().First()); // Nothing above the version.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNameGivesWayButTheChevronStaysBesideIt()
    {
        var name = new TextBlock { Text = "A very long client account name that cannot fit in the pill at all" };
        var chevron = new Border { Width = 12, Height = 12 }; var dot = new Border { Width = 14, Height = 14 };
        var row = new NameRow { Width = 300 }; row.Children.Add(dot); row.Children.Add(name); row.Children.Add(chevron);
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
        foreach (var theme in AppThemes.BuiltIn)
        {
            ThemeEngine.Register(Application.Current!, new[] { theme });
            foreach (var variant in new[] { ThemeEngine.VariantFor(theme), ThemeEngine.StateVariant(theme, ThemeDerivation.Recording), ThemeEngine.StateVariant(theme, ThemeDerivation.Break) })
            {
                var dictionary = (ResourceDictionary)Application.Current!.Resources.ThemeDictionaries[variant];
                var back = ((ISolidColorBrush)dictionary["ToolTipBackground"]!).Color; var text = ((ISolidColorBrush)dictionary["ToolTipForeground"]!).Color;
                Assert.Equal(((ISolidColorBrush)dictionary["Panel"]!).Color, back); // The card colour, like the pop-ups and messages. Assert.Equal(((ISolidColorBrush)dictionary["Ink"]!).Color, text);
            }
        }
    }

    [AvaloniaFact]
    public void TheClockIsFirstInTheSidebarAndGoesBackToThePillAndCloseTimerIsAtTheFoot()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show(); var back = 0; window.OpenPill = () => back++;
        try
        {
            Settle(window);
            double Top(string name) { var b = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == name); return b.TranslatePoint(new Point(0, 0), window)!.Value.Y; }
            Assert.True(Top("SidebarDashboard") < Top("SidebarPause")); Assert.True(Top("SidebarPause") < Top("Account0")); // The clock, then the tracking card, then the accounts.
            Assert.Equal(Top("SidebarPause"), Top("DashboardBreak"), 1.0); Assert.True(Top("Account0") < Top("QuitTimer")); // Quit timer is at the foot.
            Assert.Equal("Collapse to pill", ToolTip.GetTip(window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "SidebarDashboard")));
            window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "SidebarDashboard").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, back);
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
        Assert.DoesNotContain(items, m => (string?)m.Header == "Decimal hours"); Assert.DoesNotContain(items, m => (string?)m.Header == "Quit timer");
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
            dashboard.Dialogs.Show(new AccountsDialog(model)); Settle(dashboard);
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
            // Inverted: the card is the theme's ink (dark on Light, light on Dark), with a soft shadow, so nothing under it can look like it.
            Assert.True(bar.TryFindResource("Ink", bar.ActualThemeVariant, out var ink) && ink is ISolidColorBrush inkBrush && ((ISolidColorBrush)bar.Background!).Color == inkBrush.Color);
            Assert.True(bar.BoxShadow.Count > 0);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void WritingAThemeTwiceLeavesUnchangedColoursAlone()
    {
        var theme = AppThemes.Dark; var app = Application.Current!;
        ThemeEngine.Register(app, new[] { theme });
        var dictionary = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeEngine.VariantFor(theme)];
        var ink = dictionary["Ink"]; var accent = dictionary["Accent"];
        ThemeEngine.Register(app, new[] { theme });
        Assert.Same(ink, dictionary["Ink"]); Assert.Same(accent, dictionary["Accent"]); // Nothing changed, so nothing was replaced.
        var edited = new Dictionary<string, string>(theme.Colours) { ["Accent"] = "#123456" };
        ThemeEngine.Register(app, new[] { theme with { Colours = edited } });
        Assert.NotSame(accent, dictionary["Accent"]); Assert.Same(ink, dictionary["Ink"]); // Only the colour that moved was replaced.
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
            Assert.Equal(5, items.Count);
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
            ThemeEngine.Register(Application.Current!, AppThemes.BuiltIn);
            var light = Frame(); window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; var dark = Frame();
            Assert.NotEqual(light, dark); // The stroke changed colour, not only the window around it.
        }
        finally { window.Close(); }
    }

    [Fact]
    public void SlidersCanBeSeenInEveryBuiltInTheme()
    {
        foreach (var theme in AppThemes.BuiltIn)
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
        foreach (var theme in AppThemes.BuiltIn)
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
            Assert.Matches(@"^\d\d:\d\d / \d\d:\d\d$", model.BudgetUsage(withBudget)); Assert.Equal("This week", model.BudgetPeriodLabel(new TimerAccount("x", "#888888", null) with { Budget = TimeSpan.FromHours(5), Period = "week" }));
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
        foreach (var theme in AppThemes.BuiltIn)
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

    private static TimerModel BudgetModel(Func<DateTime> clock, out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: clock);
        var settings = new StoreSettings(new() { new("Weekly", 10, "week"), new("Monthly", 20, "month"), new("Quarterly", 100, "quarter"), new("Daily", 6, "day") }, false, "Light", DayOfWeek.Monday);
        return new TimerModel(engine, settings);
    }

    [Fact]
    public void AnEarlierWeeksBudgetFiguresNameThePeriodTheyBelongToInsteadOfSayingThisMonth()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0); var model = BudgetModel(() => now, out var engine);
        engine.Sessions.Add(new SessionRow("2026-09-16", "Monthly", "09:00:00", "added", "120.00")); // Two hours in September.
        engine.Sessions.Add(new SessionRow("2026-10-12", "Monthly", "09:00:00", "added", "60.00"));  // One hour this month.
        var monthly = model.Accounts.Single(a => a.Name == "Monthly"); var quarterly = model.Accounts.Single(a => a.Name == "Quarterly");
        var currentWeek = new DateTime(2026, 10, 12); var earlierWeek = new DateTime(2026, 9, 14);
        Assert.Equal("this month", model.BudgetPeriodNameAt(monthly, model.BudgetReference(currentWeek)));
        Assert.Equal("September 2026", model.BudgetPeriodNameAt(monthly, model.BudgetReference(earlierWeek)));
        Assert.Equal("Q3 2026", model.BudgetPeriodNameAt(quarterly, model.BudgetReference(earlierWeek)));
        Assert.Equal("this quarter", model.BudgetPeriodNameAt(quarterly, model.BudgetReference(currentWeek)));
        Assert.Equal("week of 14 Sept", model.BudgetPeriodNameAt(model.Accounts.Single(a => a.Name == "Weekly"), model.BudgetReference(earlierWeek)));
        Assert.Equal("Thu 17 Sept", model.BudgetPeriodNameAt(model.Accounts.Single(a => a.Name == "Daily"), model.BudgetReference(earlierWeek)));
        Assert.Equal(TimeSpan.FromHours(2), model.BudgetUsedAt(monthly, model.BudgetReference(earlierWeek))); // September's own use, not this month's.
        Assert.Equal(TimeSpan.FromHours(1), model.BudgetUsedAt(monthly, model.BudgetReference(currentWeek)));
        // A week that straddles two months is placed with the month holding most of its days.
        Assert.Equal("this month", model.BudgetPeriodNameAt(monthly, model.BudgetReference(new DateTime(2026, 9, 28)))); // 28 Sep to 4 Oct has its Thursday on 1 Oct.
    }

    [AvaloniaFact]
    public void TheRunningRecordingCountsInTheWeekTable()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0); var model = BudgetModel(() => now, out var engine);
        model.ToggleAccount("Weekly"); now = now.AddHours(2); // Two hours running, nothing saved yet.
        var week = new DateTime(2026, 10, 5);
        var table = WeekTable.Build(model, new WeekExport(model.Editor, engine, week), week, new[] { 0, 1, 2, 3, 4 });
        var allocated = table.Rows.Single(r => r.Label == "Allocated"); Assert.Equal("02:00", allocated.Cells[4]); Assert.Equal("02:00", allocated.Total);
        Assert.Equal("02:00", table.Rows.Single(r => r.Label == "Weekly").Total);
    }
}
