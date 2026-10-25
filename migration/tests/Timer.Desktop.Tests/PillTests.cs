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
    public void TheClockIsStackedAsHoursMinutesAndSeconds()
    {
        var clock = new StackedClock { Text = "01:30:45" };
        Assert.Equal("01:30:45", clock.Text);
        var lines = clock.Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Equal(new[] { "01", "30", "45" }, lines);
        Assert.True(clock.Children.OfType<TextBlock>().Last().FontSize < clock.Children.OfType<TextBlock>().First().FontSize); // The seconds are smaller.
    }

    [AvaloniaFact]
    public void ThePillIsASmallVerticalCardWithItsWindowAtTheScaledSize()
    {
        var pill = new PillWindow(Model()); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.PillWidth, surface.Width); Assert.Equal(PillWindow.PillHeight, surface.Height);
            Assert.True(surface.Height > 3 * surface.Width);
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
            var lights = Find<StackPanel>(pill, "PillLights"); var circle = Find<Button>(pill, "PillPause"); var mug = Find<Button>(pill, "PillBreak");
            Assert.Equal("Not recording", Tip(lights)); Assert.StartsWith("Start ", Tip(circle)); Assert.Equal("Start break", Tip(mug));
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Recording", Tip(lights)); Assert.StartsWith("Pause", Tip(circle));
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "AO"); // The circle carries the running account's initials.
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("On break", Tip(lights)); Assert.StartsWith("End break", Tip(mug)); Assert.True(pill.GetVisualDescendants().OfType<BreakGlyph>().Single().Steaming);
        }
        finally { pill.Close(); }
        // Overtime can only be forced in the demo.
        var demo = TimerModel.CreateDemo(); var late = new PillWindow(demo); late.Show();
        try { demo.SetOvertimeTest(true); demo.ToggleAccount("Account three"); Dispatcher.UIThread.RunJobs(); Assert.Equal("Overtime", Tip(Find<StackPanel>(late, "PillLights"))); }
        finally { late.Close(); }
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
            Assert.Equal(StatusFace.TileSize, tile.Bounds.Width, 0.5); // And no change of size.
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
                var tile = tileName is not null ? Find<Border>(pill, tileName) : button.GetVisualDescendants().OfType<Border>().First(b => b.Name != "PillSurface" && b.Child is not null && b.Width is >= 40 and <= 46);
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
        try { Assert.Equal("No reminders waiting", Avalonia.Automation.AutomationProperties.GetName(Find<Border>(pill, "PillReminderLight"))); Assert.False(model.ReminderDue); }
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
    public void TheAccountsButtonUnfoldsTheDrawerAndFoldsItAgainAndChoosingAnAccountLeavesItOpen()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(pill.PanelOpen); Assert.Equal("Accounts", ToolTip.GetTip(pill.AccountsButton));
            Click(pill.AccountsButton); Assert.True(pill.PanelOpen); Assert.Equal("Close the accounts", ToolTip.GetTip(pill.AccountsButton));
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); // The pill never grows for it.
            Assert.Equal(new CornerRadius(PillWindow.PillRadius), Find<Border>(pill, "PillSurface").CornerRadius); // The pill keeps its shape; the drawer slides out from behind it.
            Click(pill.AccountsButton); Assert.False(pill.PanelOpen); Assert.Equal(new CornerRadius(PillWindow.PillRadius), Find<Border>(pill, "PillSurface").CornerRadius);
            Click(pill.AccountsButton); Dispatcher.UIThread.RunJobs();
            model.ToggleAccount("Account two"); Dispatcher.UIThread.RunJobs();
            Assert.True(pill.PanelOpen); // Choosing an account leaves the drawer open, so a wrong choice can be put right at once.
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
                return (surface.BorderThickness.Left, surface.Bounds.Width, At(Find<StackPanel>(pill, "PillLights")), At(Find<Button>(pill, "PillDashboard")), At(Find<Button>(pill, "PillPause")), At(pill.AccountsButton)); }
            var idle = Snapshot();
            demo.ToggleAccount("Account one"); Assert.Equal(idle, Snapshot()); // Recording.
            demo.ToggleBreak(); Assert.Equal(idle, Snapshot()); // A break.
            demo.ToggleBreak(); demo.SetOvertimeTest(true); demo.ToggleAccount("Account three"); Assert.Equal(idle, Snapshot()); // Overtime.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheDrawerSlidesOutFromBehindThePillWhichKeepsItsShapeAndItsGlow()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var card = Find<Border>(pill, "PillSurface"); var host = Find<Border>(pill, "ShadowHost");
            pill.ShowPanel(); Dispatcher.UIThread.RunJobs();
            var popup = pill.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            Assert.Equal(-PillWindow.Behind * pill.Scale, popup.HorizontalOffset, 0.01); // It starts under the pill's edge,
            Assert.Equal(new CornerRadius(PillWindow.PillRadius), card.CornerRadius); // while the pill keeps all four round corners,
            Assert.Null(host.Clip); // and its glow, which is not cut off at the seam.
            var holder = Find<Grid>(pill, "PillDrawerHolder");
            Assert.IsType<Avalonia.Media.CombinedGeometry>(holder.Clip); // The drawer is cut away where the pill's capsule is.
            Assert.Equal(Avalonia.Media.GeometryCombineMode.Exclude, ((Avalonia.Media.CombinedGeometry)holder.Clip!).GeometryCombineMode);
            Assert.Equal(PillWindow.DrawerWidth, Find<Border>(pill, "PillDrawer").Bounds.Width, 0.5);
            pill.CloseFlyouts(); Assert.Equal(new CornerRadius(PillWindow.PillRadius), card.CornerRadius);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ManageAccountsIsOneIconAtTheTopRightOfTheDrawerAndOpensTheAccountsDialog()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            pill.ShowPanel(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var manage = Find<Button>(pill, "PillManageAccounts"); var drawer = Find<Border>(pill, "PillDrawer");
            Assert.IsType<IconGlyph>(manage.Content); Assert.Equal(IconKind.Sliders, ((IconGlyph)manage.Content!).Kind); // An icon, with no words on it.
            Assert.Equal("Manage accounts", Tip(manage)); Assert.Equal("Manage accounts", Avalonia.Automation.AutomationProperties.GetName(manage));
            var at = manage.TranslatePoint(default, drawer)!.Value;
            Assert.True(at.Y < 30, $"icon top {at.Y}"); // In the heading row,
            Assert.InRange(drawer.Bounds.Width - (at.X + manage.Bounds.Width), 10, 18); // against the right padding.
            Assert.DoesNotContain(pill.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Manage accounts"); // The button at the foot is gone.
            var scroll = pill.AccountScroll; Assert.True(scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), drawer)!.Value.Y >= drawer.Bounds.Height - 18); // The list runs to the foot.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheDrawerIsAsTallAsThePillAndItsListScrollsInsideIt()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(Enumerable.Range(1, 25).Select(i => new AccountSettings("Client " + i)).ToList(), false, "System", DayOfWeek.Monday));
        var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            pill.ShowPanel(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var drawer = Find<Border>(pill, "PillDrawer");
            Assert.Equal(PillWindow.PillHeight, drawer.Bounds.Height, 0.5); Assert.Equal(PillWindow.DrawerWidth, drawer.Bounds.Width, 0.5);
            var scroll = pill.AccountScroll;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1); // Twenty-five accounts do not fit, so it scrolls.
            Assert.True(scroll.Viewport.Height >= 3 * 54 - 1); // At least three of the short rows show.
            Assert.Empty(pill.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Kind == IconKind.Grip)); // The pill's list is not reordered.
            Assert.NotEmpty(pill.GetVisualDescendants().OfType<AccountIndicator>()); // Each row keeps its play button, as on the dashboard.
            Assert.DoesNotContain(pill.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Today"); // And there is no Today heading.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePillHoldsTheThreeSquareControlsWithTheBreakUnderThePrimaryControlAndTheAccountsButtonAtTheFoot()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
        try
        {
            Assert.Equal(316, PillWindow.PillHeight); Assert.Equal(PillWindow.PillHeight, Find<Border>(pill, "PillSurface").Height);
            double Y(Control c) => c.TranslatePoint(default, pill)!.Value.Y;
            var primary = Find<Button>(pill, "PillPause"); var brk = Find<Button>(pill, "PillBreak");
            Assert.True(Y(primary) < Y(brk)); Assert.True(Y(brk) < Y(pill.AccountsButton)); // Timer, primary control, break, accounts.
            var surface = Find<Border>(pill, "PillSurface"); var bottom = surface.TranslatePoint(new Point(0, surface.Bounds.Height), pill)!.Value.Y;
            Assert.True(Y(pill.AccountsButton) + pill.AccountsButton.Bounds.Height * pill.Scale <= bottom + 0.5, $"accounts bottom {Y(pill.AccountsButton) + pill.AccountsButton.Bounds.Height * pill.Scale} card bottom {bottom} scale {pill.Scale}"); // Everything fits inside the card.
            // The three controls are one size and shape, with the same room on both sides and a gap between them, so none crowds the card's edge.
            Border Tile(Control c) => c.GetVisualDescendants().OfType<Border>().First(x => x.Width == StatusFace.TileSize);
            var tiles = new[] { Tile(primary), Tile(brk), Tile(pill.AccountsButton) };
            foreach (var t in tiles)
            {
                Assert.Equal(StatusFace.TileSize, t.Bounds.Width); Assert.Equal(StatusFace.TileSize, t.Bounds.Height); Assert.Equal(StatusFace.TileRadius, t.CornerRadius.TopLeft);
                var left = t.TranslatePoint(default, surface)!.Value.X; var right = surface.Bounds.Width - (left + t.Bounds.Width);
                Assert.InRange(left, 12, 16); Assert.True(Math.Abs(right - left) <= 0.5, $"left {left} right {right}"); // Room for the focus ring, centred.
            }
            double Top(Control c) => c.TranslatePoint(default, surface)!.Value.Y;
            Assert.InRange(Top(tiles[1]) - (Top(tiles[0]) + StatusFace.TileSize), 8, 14); Assert.InRange(Top(tiles[2]) - (Top(tiles[1]) + StatusFace.TileSize), 8, 14);
            var room = surface.Bounds.Height - (Top(tiles[2]) + StatusFace.TileSize);
            Assert.True(room >= 12, $"room under the accounts button {room}");
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
    public void TheRailsBreakIsThePillsBreakUnderThePrimaryControlWithTheAccountsButtonBelowIt()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        try
        {
            double Y(Control c) => c.TranslatePoint(default, window)!.Value.Y;
            var brk = Find<Button>(window, "DashboardBreak"); var accounts = Find<Button>(window, "RailAccounts");
            Assert.True(Y(Find<Button>(window, "RailPause")) < Y(brk)); Assert.True(Y(brk) < Y(accounts)); Assert.True(Y(accounts) < Y(Find<Button>(window, "TimelineTab"))); // As on the pill.
            Assert.True(Y(brk) < Y(Find<Button>(window, "DashboardDay"))); // And no longer at the foot with the flag.
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
        model.ToggleBreak(); model.ToggleDay();
        Assert.Equal("Day finished", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
    }
}
