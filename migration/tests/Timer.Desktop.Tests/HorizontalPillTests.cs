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

// The horizontal pill: the day clock, then the play tile and the account on one well, then the break, with the account's list hanging below. It is a choice
// beside the vertical pill, made in Getting started and in Settings, and saved as the appearance line pillshape=horizontal.
public sealed class HorizontalPillTests
{
    private static DateTime Day => new(2026, 10, 14);
    private const string LongName = "Northwind Traders International Holdings";
    private static TimerModel Model(bool horizontal = true)
    {
        var accounts = new List<AccountSettings> { new("Account one"), new("Account two"), new(LongName) };
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(accounts, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); model.SetPillHorizontal(horizontal); return model;
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
            Assert.True(pill.Horizontal);
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.HorizontalWidth, surface.Width); Assert.Equal(PillWindow.HorizontalHeight, surface.Height); Assert.Equal(PillWindow.PillRadius, surface.CornerRadius.TopLeft);
            Assert.Equal((PillWindow.HorizontalWidth + 2 * Ui.ShadowMargin) * pill.Scale, pill.WindowWidthNow, 0.5); Assert.Equal((PillWindow.HorizontalHeight + 2 * Ui.ShadowMargin) * pill.Scale, pill.WindowHeightNow, 0.5);
            double X(Control c) => c.TranslatePoint(default, surface)!.Value.X; double Y(Control c) => c.TranslatePoint(default, surface)!.Value.Y;
            var lights = Find<StackPanel>(pill, "PillLights"); var clock = Find<Button>(pill, "PillDashboard"); var well = Find<Border>(pill, "PillWell");
            var primary = Find<Button>(pill, "PillPause"); var accounts = pill.AccountsButton; var brk = Find<Button>(pill, "PillBreak");
            Assert.True(X(lights) < X(clock)); Assert.True(X(clock) < X(primary)); Assert.True(X(primary) < X(accounts)); Assert.True(X(accounts) < X(brk)); // Left to right.
            // The primary control and the account share the well, 4 pixels in from its edge and 2 apart, the well as tall as the two tiles and their room.
            Assert.Equal(2 * StatusFace.WellPadding + StatusFace.TileSize, well.Bounds.Height, 0.5);
            Assert.Equal(StatusFace.WellPadding, Find<Border>(pill, "PillPrimaryTile").TranslatePoint(default, well)!.Value.X, 0.5);
            // Everything is centred on the card's height, and the break is 14 pixels from the card's end with no spare room before it.
            Assert.True(Math.Abs(Y(well) + well.Bounds.Height / 2 - surface.Bounds.Height / 2) <= 1);
            var breakTile = brk.GetVisualDescendants().OfType<Border>().First(b => b.Width == StatusFace.TileSize);
            var end = surface.Bounds.Width - (X(breakTile) + breakTile.Bounds.Width);
            Assert.InRange(end, PillWindow.HorizontalInsetRight - 1.5, PillWindow.HorizontalInsetRight + 0.5);
            Assert.InRange(X(lights) , PillWindow.HorizontalInsetLeft - 3, PillWindow.HorizontalInsetLeft + 1);
            var gapBeforeBreak = X(breakTile) - (X(well) + well.Bounds.Width); Assert.InRange(gapBeforeBreak, 8, 16);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsBoxShowsTheNameInFullWithTheStateDotAndALongNameIsCutAndSlides()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            model.ToggleAccount("Account two"); Settle(pill);
            var tile = Find<Border>(pill, "PillAccountSelector"); var name = tile.GetVisualDescendants().OfType<MarqueeText>().Single();
            Assert.Equal("Account two", name.Label.Text); Assert.Equal(StatusFace.SelectorWidth, tile.Bounds.Width, 0.5);
            Assert.Equal("Accounts", ToolTip.GetTip(pill.AccountsButton)?.ToString());
            model.ToggleAccount(LongName); Settle(pill);
            Assert.Equal(LongName, name.Label.Text); // The whole name is there, and the box never grows past its room.
            Assert.True(name.Bounds.Width < StatusFace.SelectorWidth - 20, $"name box {name.Bounds.Width}");
            Assert.Equal(StatusFace.SelectorWidth, tile.Bounds.Width, 0.5); Assert.Equal(PillWindow.HorizontalWidth, Find<Border>(pill, "PillSurface").Bounds.Width, 0.5); // The pill keeps its size.
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
    public void ChangingTheShapeInSettingsTurnsTheWindowIntoTheOtherShapeAtOnceAndBack()
    {
        var model = Model(horizontal: false); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            Assert.False(pill.Horizontal); var tall = pill.WindowHeightNow; var narrow = pill.WindowWidthNow; Assert.True(tall > narrow);
            model.SetPillHorizontal(true); Settle(pill);
            Assert.True(pill.Horizontal); Assert.True(pill.WindowWidthNow > pill.WindowHeightNow); Assert.Equal(PillWindow.HorizontalWidth, Find<Border>(pill, "PillSurface").Width);
            Assert.Equal(pill.WindowWidthNow, pill.Width, 1); Assert.Equal(pill.WindowHeightNow, pill.Height, 1); // The window followed.
            model.SetPillHorizontal(false); Settle(pill);
            Assert.False(pill.Horizontal); Assert.Equal(tall, pill.WindowHeightNow, 0.5); Assert.Equal(narrow, pill.WindowWidthNow, 0.5);
            Assert.Equal(PillWindow.PillHeight, Find<Border>(pill, "PillSurface").Height); Assert.Equal(pill.WindowHeightNow, pill.Height, 1);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheHorizontalPillDoesWhatTheVerticalOneDoesWithItsControls()
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
    public void TheShapeIsSavedAsAnAppearanceLineAndComesBackAndVerticalIsTheDefault()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-shape-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine); var model = new TimerModel(engine, prefs, store);
                Assert.False(model.PillHorizontal); model.SetPillHorizontal(true); Assert.True(model.PillHorizontal);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                Assert.Contains("pillshape=horizontal", prefs.Extras!.AppearanceLines); Assert.True(new TimerModel(engine, prefs, store).PillHorizontal);
                var model = new TimerModel(engine, prefs, store); model.SetPillHorizontal(false);
            }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.DoesNotContain("pillshape=horizontal", prefs.Extras?.AppearanceLines ?? new List<string>()); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void SettingsOffersBothShapesWithTheChosenOneMarkedAndChoosingOneAppliesIt()
    {
        var model = Model(horizontal: false); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "Settings")); Settle(window);
            var vertical = Find<Button>(window, "PillShapeVertical"); var horizontal = Find<Button>(window, "PillShapeHorizontal");
            Assert.Contains("selected", vertical.Classes); Assert.DoesNotContain("selected", horizontal.Classes);
            Click(horizontal); Settle(window);
            Assert.True(model.PillHorizontal); Assert.Contains("selected", horizontal.Classes); Assert.DoesNotContain("selected", vertical.Classes);
            Click(vertical); Assert.False(model.PillHorizontal);
        }
        finally { window.Close(); }
    }
}
