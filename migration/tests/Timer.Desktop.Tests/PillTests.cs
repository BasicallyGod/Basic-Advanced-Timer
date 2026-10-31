using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PillTests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model() { var m = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10))); m.CompleteSetup(); return m; }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string? Tip(Control c) => ToolTip.GetTip(c)?.ToString();
    private static void ClickAt(Window w, Control c)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame();
        var point = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("Account three", "AT")]
    [InlineData("Northwind Traders", "NT")]
    [InlineData("Acme", "A")]
    [InlineData("Client 7", "C7")]
    [InlineData("", "")]
    public void InitialsAreTheFirstLettersOfUpToTwoWords(string name, string expected) => Assert.Equal(expected, PillWindow.Initials(name));

    [AvaloniaFact]
    public void ThePillIsASmallWideCardWithItsWindowAtTheScaledSize()
    {
        var pill = new PillWindow(Model()); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.PillWidth, surface.Width); Assert.Equal(PillWindow.PillHeight, surface.Height);
            Assert.True(surface.Width > 4 * surface.Height);
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); Assert.Equal(pill.WindowWidthNow, pill.Width, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheLightCirclePauseAndMugFollowTheState()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var lights = Find<Border>(pill, "PillStateLight"); var circle = Find<Button>(pill, "PillPause"); var mug = Find<Button>(pill, "PillBreak");
            Assert.Equal("Not recording", Tip(lights)); Assert.StartsWith("Start ", Tip(circle)); Assert.Equal("Start break", Tip(mug));
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Recording", Tip(lights)); Assert.StartsWith("Pause", Tip(circle));
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "AO"); // The circle carries the running account's initials.
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("On break", Tip(lights)); Assert.StartsWith("End break", Tip(mug)); Assert.True(pill.GetVisualDescendants().OfType<BreakGlyph>().Single().Steaming);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void HoveringThePrimaryControlDeepensItsTintInsteadOfGrowingItSoNothingIsClipped()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var button = Find<Button>(pill, "PillPause"); var tile = Find<Border>(pill, "PillPrimaryTile");
            Assert.Equal(14, tile.CornerRadius.TopLeft); // A rounded square, like the accounts icon.
            Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var rest = ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color; Assert.InRange(rest.A, 30, 60); // A soft tint, not a solid fill.
            var live = Avalonia.Media.Color.Parse(model.CurrentTheme.Colour("Live")); Assert.Equal(live.G, rest.G); // In the tracking green.
            var over = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), pill)!.Value;
            pill.MouseMoveUntil(over, () => ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color.A > rest.A);
            Assert.True(((Avalonia.Media.ISolidColorBrush)tile.Background!).Color.A > rest.A); // A stronger tint under the pointer.
            Assert.Equal(StatusFace.WellTileWidth, tile.Bounds.Width, 0.5); // And no change of size.
            pill.MouseMove(new Point(2, 2)); Assert.Equal(rest, ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardFocusRingsThePrimaryControlTheBreakAndTheAccountsButtonButAMouseClickDoesNot()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
        try
        {
            foreach (var (button, tileName) in new[] { (Find<Button>(pill, "PillPause"), "PillPrimaryTile"), (Find<Button>(pill, "PillBreak"), (string?)null), (pill.AccountsButton, (string?)null) })
            {
                var tile = tileName is not null ? Find<Border>(pill, tileName) : button.GetVisualDescendants().OfType<Border>().First(b => b.Name != "PillSurface" && b.Child is not null && b.Width is >= 40 and <= 48);
                Assert.Equal(0, tile.BoxShadow.Count);
                button.RaiseEvent(new GotFocusEventArgs { RoutedEvent = InputElement.GotFocusEvent, NavigationMethod = NavigationMethod.Pointer });
                Assert.Equal(0, tile.BoxShadow.Count); // Focus left by a click is not shown.
                button.RaiseEvent(new GotFocusEventArgs { RoutedEvent = InputElement.GotFocusEvent, NavigationMethod = NavigationMethod.Tab });
                Assert.True(tile.BoxShadow.Count > 0, button.Name); // Tab shows the ring.
                button.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent)); Assert.Equal(0, tile.BoxShadow.Count);
            }
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheReminderLightShowsOnlyWhileAReminderIsDue()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try { Assert.Equal("Nothing waiting", Avalonia.Automation.AutomationProperties.GetName(Find<Button>(pill, "PillAlertsButton"))); Assert.False(model.ReminderDue); }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ClickingTheClockOpensTheDashboardAndTheCircleStartsAndPauses()
    {
        var model = Model(); var opened = 0; var pill = new PillWindow(model) { OpenDashboard = () => opened++ }; pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            ClickAt(pill, Find<Button>(pill, "PillDashboard")); Assert.Equal(1, opened);
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            ClickAt(pill, Find<Button>(pill, "PillPause")); Assert.False(model.IsTracking); Assert.Equal("Account one", model.SelectedAccount);
            ClickAt(pill, Find<Button>(pill, "PillPause")); Assert.True(model.IsTracking);
            ClickAt(pill, Find<Button>(pill, "PillBreak")); Assert.True(model.IsOnBreak);
            ClickAt(pill, Find<Button>(pill, "PillBreak")); Assert.False(model.IsOnBreak);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void DraggingFromAnyPartOfThePillDoesNotClick()
    {
        var model = Model(); var opened = 0; var pill = new PillWindow(model) { OpenDashboard = () => opened++ }; pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs(); using var initial = pill.CaptureRenderedFrame();
            foreach (var name in new[] { "PillDashboard", "PillBreak", "PillPause" })
            {
                var button = Find<Button>(pill, name);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), pill)!.Value;
                pill.MouseDown(point, MouseButton.Left); pill.MouseMove(point + new Vector(20, 10)); pill.MouseUp(point + new Vector(20, 10), MouseButton.Left);
                Assert.Equal(0, opened); Assert.Equal("Account one", model.ActiveAccount); Assert.False(model.IsOnBreak);
            }
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsButtonOpensTheListAndFoldsItAgainAndChoosingAnAccountLeavesItOpen()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(pill.PanelOpen); Assert.Equal("Account one", ToolTip.GetTip(pill.AccountsButton)); // The box shows a dot, so its tooltip names the account.
            Click(pill.AccountsButton); Assert.True(pill.PanelOpen); Assert.Equal("Close the accounts", ToolTip.GetTip(pill.AccountsButton));
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); // The pill never grows for it.
            Assert.Equal(new CornerRadius(PillWindow.PillRadius), Find<Border>(pill, "PillSurface").CornerRadius); // The pill keeps its shape; the list hangs below it.
            Click(pill.AccountsButton); Assert.False(pill.PanelOpen); Assert.Equal(new CornerRadius(PillWindow.PillRadius), Find<Border>(pill, "PillSurface").CornerRadius);
            Click(pill.AccountsButton); Dispatcher.UIThread.RunJobs();
            model.ToggleAccount("Account two"); Dispatcher.UIThread.RunJobs();
            Assert.True(pill.PanelOpen); // Choosing an account leaves the list open, so a wrong choice can be put right at once.
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs(); Assert.True(pill.PanelOpen); Assert.Equal("Account one", model.ActiveAccount);
            pill.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(pill.PanelOpen); // Escape folds it away, as the button does.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void NothingOnThePillMovesWhenAGlowComesAndGoes()
    {
        var demo = TimerModel.CreateDemo(); demo.CompleteSetup(); var pill = new PillWindow(demo); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Point At(Control c) => c.TranslatePoint(default, pill)!.Value;
            (double, double, Point, Point, Point, Point) Snapshot() { Dispatcher.UIThread.RunJobs(); using var f = pill.CaptureRenderedFrame();
                return (surface.BorderThickness.Left, surface.Bounds.Width, At(Find<Border>(pill, "PillStateLight")), At(Find<Button>(pill, "PillDashboard")), At(Find<Button>(pill, "PillPause")), At(pill.AccountsButton)); }
            var idle = Snapshot();
            demo.ToggleAccount("Account one"); Assert.Equal(idle, Snapshot()); // Recording.
            demo.ToggleBreak(); Assert.Equal(idle, Snapshot()); // A break.
            demo.ToggleBreak(); demo.ToggleAccount("Account three"); Assert.Equal(idle, Snapshot()); // Recording again, late in the day.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ManageAccountsIsOneIconAtTheTopRightOfTheListAndOpensTheAccountsDialog()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            // The pop-up has no window of its own in a headless run, so the list's card is laid out in one made for it.
            var card = pill.MakeDropdownCard(); var host = new Window { Width = 320, Height = 600, Content = card }; host.Show(); Settle(host);
            var manage = Find<Button>(host, "PillManageAccounts");
            Assert.IsType<IconGlyph>(manage.Content); Assert.Equal(IconKind.Sliders, ((IconGlyph)manage.Content!).Kind); // An icon, with no words on it.
            Assert.Equal("Manage accounts", Tip(manage)); Assert.Equal("Manage accounts", Avalonia.Automation.AutomationProperties.GetName(manage));
            var at = manage.TranslatePoint(default, card)!.Value;
            Assert.True(at.Y < 30, $"icon top {at.Y}"); // In the heading row,
            Assert.InRange(card.Bounds.Width - (at.X + manage.Bounds.Width), 10, 18); // against the right padding.
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Manage accounts"); // There is no button at the foot.
            var scroll = pill.AccountScroll; Assert.True(scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), card)!.Value.Y >= card.Bounds.Height - 18); // The list runs to the foot.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheListHasAMostHeightAndScrollsInsideIt()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(Enumerable.Range(1, 25).Select(i => new AccountSettings("Client " + i)).ToList(), false, "System", DayOfWeek.Monday));
        var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var card = pill.MakeDropdownCard(); var host = new Window { Width = 320, Height = 700, Content = card }; host.Show(); Settle(host);
            Assert.Equal(PillWindow.DropdownMaxHeight, card.Bounds.Height, 0.5); Assert.Equal(PillWindow.DropdownWidth, card.Bounds.Width, 0.5);
            var scroll = pill.AccountScroll;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1); // Twenty-five accounts do not fit, so it scrolls.
            Assert.True(scroll.Viewport.Height >= 3 * 54 - 1); // At least three of the short rows show.
            Assert.Empty(host.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Kind == IconKind.Grip)); // The pill's list is not reordered.
            Assert.NotEmpty(host.GetVisualDescendants().OfType<AccountIndicator>()); // Each row keeps its play button, as on the dashboard.
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Today"); // And there is no Today heading.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePrimaryControlAndTheAccountsButtonShareOneWellWithTheBreakApartAtTheEnd()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
        try
        {
            double X(Control c) => c.TranslatePoint(default, pill)!.Value.X;
            var primary = Find<Button>(pill, "PillPause"); var brk = Find<Button>(pill, "PillBreak"); var clock = Find<Button>(pill, "PillDashboard");
            Assert.True(X(clock) < X(primary)); Assert.True(X(primary) < X(pill.AccountsButton)); Assert.True(X(pill.AccountsButton) < X(brk)); // Time, primary control, accounts, then the break at the end.
            var surface = Find<Border>(pill, "PillSurface");
            // The two share one well: the tiles 46 and 48 wide inside it, 4 from its edge and 2 apart, as high as the break's tile with the well's padding.
            var well = Find<Border>(pill, "PillWell");
            Assert.Equal(StatusFace.TileSize + 2 * StatusFace.WellPadding, well.Bounds.Height, 0.5);
            Assert.Equal(StatusFace.WellRadius, well.CornerRadius.TopLeft); Assert.InRange(((Avalonia.Media.ISolidColorBrush)well.Background!).Color.A, 8, 24); // A soft background, not a fill.
            Border TileIn(Control c, double width) => c.GetVisualDescendants().OfType<Border>().First(x => x.Width == width);
            var primaryTile = Find<Border>(pill, "PillPrimaryTile"); var accountsTile = TileIn(pill.AccountsButton, StatusFace.SelectorWidth); var breakTile = TileIn(brk, StatusFace.TileSize);
            foreach (var t in new[] { primaryTile, accountsTile })
            {
                Assert.Equal(StatusFace.TileSize, t.Bounds.Height); Assert.Equal(StatusFace.TileRadius, t.CornerRadius.TopLeft);
                Assert.Equal(StatusFace.WellPadding, t.TranslatePoint(default, well)!.Value.Y, 0.5);
            }
            Assert.Equal(StatusFace.WellPadding, primaryTile.TranslatePoint(default, well)!.Value.X, 0.5);
            Assert.Equal(StatusFace.WellPadding + StatusFace.WellTileWidth + StatusFace.WellGap, accountsTile.TranslatePoint(default, well)!.Value.X, 0.5);
            // The break is its own 44 pixel tile, centred on the card's height.
            Assert.Equal(StatusFace.TileSize, breakTile.Bounds.Width); Assert.Equal(StatusFace.TileRadius, breakTile.CornerRadius.TopLeft);
            var top = breakTile.TranslatePoint(default, surface)!.Value.Y; var bottom = surface.Bounds.Height - (top + breakTile.Bounds.Height);
            Assert.True(Math.Abs(bottom - top) <= 1, $"top {top} bottom {bottom}");
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheBreakIsASoftOrangeTileThatTurnsSolidWithALitCupOnABreak()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = Model(); model.SetTheme(theme); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            try
            {
                var button = Find<Button>(pill, "PillBreak");
                var tile = button.GetVisualDescendants().OfType<Border>().Single(b => b.Width == StatusFace.TileSize); var cup = button.GetVisualDescendants().OfType<BreakGlyph>().Single();
                var orange = Avalonia.Media.Color.Parse(model.CurrentTheme.Colour("Break"));
                Assert.Equal(StatusFace.TileRadius, tile.CornerRadius.TopLeft); // A rounded square, like the primary control and the accounts icon.
                var rest = ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color; Assert.InRange(rest.A, 30, 60); Assert.Equal(orange.R, rest.R); // A soft tint of the theme's orange.
                Assert.Null(cup.Fixed); Assert.Equal(0, tile.BoxShadow.Count); // The cup is the theme's orange, with no ring.
                model.ToggleBreak(); Dispatcher.UIThread.RunJobs();
                var on = ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color; Assert.Equal(orange, on); // Solid while a break is on.
                var cupColour = ((Avalonia.Media.ISolidColorBrush)cup.Fixed!).Color;
                Assert.True(ThemeColours.Contrast($"#{cupColour.R:X2}{cupColour.G:X2}{cupColour.B:X2}", $"#{on.R:X2}{on.G:X2}{on.B:X2}") >= 2.9, theme); // The lit cup reads on it.
                if (theme == "Light") Assert.Equal(Avalonia.Media.Colors.White, cupColour);
                Assert.True(tile.BoxShadow.Count > 0); // A soft ring.
            }
            finally { pill.Close(); model.SetTheme("System"); }
        }
    }

    [AvaloniaFact]
    public void TheSidebarsBreakIsThePillsBreakAtTheEndOfTheTrackingCardBesideThePlayTile()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        try
        {
            double Y(Control c) => c.TranslatePoint(default, window)!.Value.Y;
            var brk = Find<Button>(window, "DashboardBreak");
            Assert.Equal(Y(Find<Button>(window, "SidebarPause")), Y(brk), 1.0); // Level with the play tile, at the card's far end.
            Assert.NotNull(Find<Border>(window, "SidebarWell")); // The card is the pill's well.
            Assert.True(Y(brk) < Y(Find<Button>(window, "QuitTimer"))); // And not at the foot with Quit timer.
            var tile = brk.GetVisualDescendants().OfType<Border>().Single(b => b.Width == StatusFace.TileSize); Assert.InRange(((Avalonia.Media.ISolidColorBrush)tile.Background!).Color.A, 30, 60);
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); Assert.Equal(255, ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color.A);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillHasNoBreakItemOnItsMenuBecauseTheMugIsTheBreakButton()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var items = pill.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Name).ToArray();
            Assert.DoesNotContain("BreakMenu", items); Assert.Contains("CollapseToTray", items);
        }
        finally { pill.Close(); }
    }

    [Fact]
    public void IdleBreakAndFinishedWordingIsSharedWithTheTray()
    {
        var model = Model();
        Assert.Equal("Ready to track", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleAccount(model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name); Assert.Equal("Tracking", model.Status);
        model.TogglePause(); Assert.Equal("Ready to track", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleBreak(); Assert.Equal("On break", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
    }
}
