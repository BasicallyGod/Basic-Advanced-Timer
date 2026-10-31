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

// The pill: the day clock, then the play tile and the account on one well, then the break, with the account's list hanging below. It was one of two shapes
// (the other, vertical, is gone) and an old pillshape= line is left alone in the settings file.
public sealed class HorizontalPillTests
{
    private static DateTime Day => new(2026, 10, 14);
    private const string LongName = "Northwind Traders International Holdings";
    private static TimerModel Model()
    {
        var accounts = new List<AccountSettings> { new("Account one"), new("Account two"), new(LongName) };
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(accounts, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Control c) { c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheHorizontalPillRunsAlongItsCardClockFirstThenThePlayTileAndTheAccountOnOneWellThenTheBreakAtTheEnd()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.PillWidth, surface.Width); Assert.Equal(PillWindow.PillHeight, surface.Height); Assert.Equal(PillWindow.PillRadius, surface.CornerRadius.TopLeft);
            Assert.Equal((PillWindow.PillWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.WindowWidthNow, 0.5); Assert.Equal((PillWindow.PillHeight + 2 * Ui.ShadowMargin) * pill.Scale, pill.WindowHeightNow, 0.5);
            double X(Control c) => c.TranslatePoint(default, surface)!.Value.X; double Y(Control c) => c.TranslatePoint(default, surface)!.Value.Y;
            var lights = Find<Border>(pill, "PillStateLight"); var clock = Find<Button>(pill, "PillDashboard"); var well = Find<Border>(pill, "PillWell");
            var primary = Find<Button>(pill, "PillPause"); var accounts = pill.AccountsButton; var brk = Find<Button>(pill, "PillBreak");
            Assert.True(X(lights) < X(clock)); Assert.True(X(clock) < X(primary)); Assert.True(X(primary) < X(accounts)); Assert.True(X(accounts) < X(brk)); // Left to right.
            // The primary control and the account share the well, 4 pixels in from its edge and 2 apart, the well as tall as the two tiles and their room.
            Assert.Equal(2 * StatusFace.WellPadding + StatusFace.TileSize, well.Bounds.Height, 0.5);
            Assert.Equal(StatusFace.WellPadding, Find<Border>(pill, "PillPrimaryTile").TranslatePoint(default, well)!.Value.X, 0.5);
            // Everything is centred on the card's height, and the break is 14 pixels from the card's end with no spare room before it.
            Assert.True(Math.Abs(Y(well) + well.Bounds.Height / 2 - surface.Bounds.Height / 2) <= 1);
            var breakTile = brk.GetVisualDescendants().OfType<Border>().First(b => b.Width == StatusFace.TileSize);
            var end = surface.Bounds.Width - (X(breakTile) + breakTile.Bounds.Width);
            Assert.InRange(end, PillWindow.InsetRight - 1.5, PillWindow.InsetRight + 0.5);
            Assert.InRange(X(Find<Border>(pill, "PillDisplay")), PillWindow.InsetLeft - 3, PillWindow.InsetLeft + 1); Assert.InRange(X(lights), PillWindow.InsetLeft + StatusFace.DisplayPadding - 3, PillWindow.InsetLeft + StatusFace.DisplayPadding + 1); // The display starts at the inset, the LED a padding in.
            var gapBeforeBreak = X(breakTile) - (X(well) + well.Bounds.Width); Assert.InRange(gapBeforeBreak, 8, 16);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsBoxIsADotAndAChevronAndTheNameIsItsTooltip()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            model.ToggleAccount("Account two"); Settle(pill);
            // The account's box is only the state dot and a chevron: the name is its tooltip and the list, so the pill stays narrow whatever the name.
            var tile = Find<Border>(pill, "PillAccountSelector");
            Assert.Empty(tile.GetVisualDescendants().OfType<TextBlock>()); Assert.Equal(StatusFace.SelectorWidth, tile.Bounds.Width, 0.5);
            Assert.Equal("Account two", ToolTip.GetTip(pill.AccountsButton)?.ToString());
            model.ToggleAccount(LongName); Settle(pill);
            Assert.Equal(LongName, ToolTip.GetTip(pill.AccountsButton)?.ToString());
            Assert.Equal(StatusFace.SelectorWidth, tile.Bounds.Width, 0.5); Assert.Equal(PillWindow.PillWidth, Find<Border>(pill, "PillSurface").Bounds.Width, 0.5); // The pill keeps its size.
            Assert.Contains(LongName, Avalonia.Automation.AutomationProperties.GetName(pill.AccountsButton)); // And says the full name to a screen reader.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ClickingTheAccountOpensTheListBelowAndChoosingAnAccountSwitchesToItAndLeavesItOpen()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            Assert.False(pill.PanelOpen); Click(pill.AccountsButton); Assert.True(pill.PanelOpen); Assert.Equal("Close the accounts", ToolTip.GetTip(pill.AccountsButton)?.ToString());
            var list = pill.MakeDropdownCard(); Assert.Equal(PillWindow.DropdownWidth, list.Width); // The same card the pop-up holds.
            model.ToggleAccount("Account two"); Assert.True(pill.PanelOpen); Assert.Equal("Account two", model.ActiveAccount); // Choosing leaves it open.
            Click(pill.AccountsButton); Assert.False(pill.PanelOpen);
            Click(pill.AccountsButton); Assert.True(pill.PanelOpen); pill.CloseFlyouts(); Assert.False(pill.PanelOpen);
            model.ToggleAccount("Account two"); // Stop, so nothing is running.
            Assert.False(model.IsTracking);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePillsControlsOpenTheDashboardPauseAndStartABreak()
    {
        var model = Model(); int opened = 0; var pill = new PillWindow(model) { OpenDashboard = () => opened++ }; pill.Show(); Settle(pill);
        try
        {
            Click(Find<Button>(pill, "PillDashboard")); Assert.Equal(1, opened);
            model.ToggleAccount("Account one"); Assert.True(model.IsTracking);
            Click(Find<Button>(pill, "PillPause")); Assert.False(model.IsTracking);
            Click(Find<Button>(pill, "PillPause")); Assert.True(model.IsTracking);
            Click(Find<Button>(pill, "PillBreak")); Assert.True(model.IsOnBreak); Click(Find<Button>(pill, "PillBreak")); Assert.False(model.IsOnBreak);
            Assert.Contains("AO", Find<Border>(pill, "PillPrimaryTile").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        finally { pill.Close(); }
    }

    [Fact]
    public void AnOldPillShapeLineIsLeftAloneAndTheShapeIsNoLongerAChoice()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-shape-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                // A timer that chose the horizontal pill has a line for it; it opens the same way now, and the old line stays in the file.
                prefs = prefs with { Extras = (prefs.Extras ?? SettingsExtras.Empty) with { AppearanceLines = new List<string> { "pillshape=horizontal" } } };
                var model = new TimerModel(engine, prefs, store); model.SetPillOnTop(true);
            }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.Contains("pillshape=horizontal", prefs.Extras!.AppearanceLines); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void SettingsHasNoPillShapeChoiceAndSetupIsFourSteps()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "Settings")); Settle(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name is "PillShapeVertical" or "PillShapeHorizontal");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Pill shape");
            Assert.Equal(4, OnboardingView.StepCount);
        }
        finally { window.Close(); }
    }
}
