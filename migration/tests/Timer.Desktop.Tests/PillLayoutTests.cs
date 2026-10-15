using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PillLayoutTests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model() => new(new TrackingEngine(clock: () => Day.AddHours(10)));
    private static IEnumerable<string?> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text);
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(c is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));

    [Theory]
    [InlineData("Account three", "AT")]
    [InlineData("Northwind Traders", "NT")]
    [InlineData("Acme", "A")]
    [InlineData("Client 7", "C7")]
    [InlineData("", "")]
    public void InitialsAreTheFirstLettersOfUpToTwoWords(string name, string expected) => Assert.Equal(expected, PillLayouts.Initials(name));

    [Fact]
    public void TheLayoutChoiceSurvivesARestartAndAnUnknownOneIsFull()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-pill-layout-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.Equal("full", prefs.App().PillStyle); store.Save(engine, prefs.WithApp(a => a with { PillStyle = "minimal" })); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.Equal("minimal", prefs.App().PillStyle); store.Save(engine, prefs.WithApp(a => a with { PillStyle = "sideways" })); }
            using (var store = new TimerStore(folder)) Assert.Equal("full", store.Load(new TrackingEngine()).App().PillStyle);
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void EachLayoutGivesThePillItsOwnWidthAndShowsOnlyWhatItPromises()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            double Width() => (pill.Width / pill.Scale) - 2 * Ui.ShadowMargin;
            Assert.Equal(PillWindow.PillWidth, Width(), 1); Assert.True(Find<Control>(pill, "DayProgress").IsVisible); Assert.True(Find<Button>(pill, "PillBreak").IsVisible);
            model.SetPillStyle("compact"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(520, Width(), 1); Assert.False(Find<Control>(pill, "DayProgress").IsVisible); Assert.True(Find<Button>(pill, "PillBreak").IsVisible);
            model.SetPillStyle("minimal"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(360, Width(), 1); Assert.DoesNotContain(pill.GetVisualDescendants().OfType<Button>(), b => b.Name == "PillBreak"); // Break is not in the minimal layout.
            Assert.True(Find<Button>(pill, "PillPause").IsVisible && Find<Button>(pill, "PillDashboard").IsVisible);
            model.SetPillStyle("full"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(PillWindow.PillWidth, Width(), 1); Assert.True(Find<Control>(pill, "DayProgress").IsVisible);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void PreviewPictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return; Directory.CreateDirectory(folder);
        var model = Model(); var pill = new PillWindow(model); var dashboard = new DashboardWindow(model); pill.Show(); dashboard.Show();
        try
        {
            foreach (var style in new[] { "full", "compact", "minimal" })
            {
                model.SetPillStyle(style);
                foreach (var state in new[] { "idle", "tracking", "break" })
                {
                    if (state == "tracking") model.ToggleAccount(model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name);
                    if (state == "break") model.ToggleBreak();
                    Dispatcher.UIThread.RunJobs(); using var warm = pill.CaptureRenderedFrame(); using var frame = pill.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"pill-{style}-{state}.png"));
                }
                model.ToggleBreak(); model.TogglePause();
            }
            foreach (var key in new[] { "general", "appearance", "pill", "desk", "hours", "accounts", "about" })
            { dashboard.OpenCategory(key); using var warm2 = dashboard.CaptureRenderedFrame(); using var shot = dashboard.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, $"settings-{key}.png")); }
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheMinimalBadgeCarriesTheAccountsInitialsAndTheOpenListStillHasRoom()
    {
        var model = Model(); model.SetPillStyle("minimal"); var pill = new PillWindow(model); pill.Show();
        try
        {
            model.ToggleAccount(model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name); Dispatcher.UIThread.RunJobs();
            var badge = Find<Border>(pill, "PillBadge");
            Assert.Equal(PillLayouts.Initials(model.ActiveAccount!), Assert.IsType<TextBlock>(badge.Child).Text);
            pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs();
            Assert.True(pill.Width / pill.Scale - 2 * Ui.ShadowMargin >= 420 - 1);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void AnIdlePillSaysWhatItIsWaitingFor()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("Not tracking", Texts(pill));
            Assert.Contains("Ready to track", Texts(pill));
            // Starting and pausing leaves the account chosen, so the line asks for nothing more than pressing play.
            var name = model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name;
            model.ToggleAccount(name); model.TogglePause(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(name, Texts(pill)); Assert.Contains("Ready to track", Texts(pill)); Assert.DoesNotContain("Select an account", Texts(pill));
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePillsMenuHasTheQuickActionsAndTheDashboardsDoesNot()
    {
        var model = Model(); var pill = new PillWindow(model); var dashboard = new DashboardWindow(model); pill.Show(); dashboard.Show();
        try
        {
            var items = ((IEnumerable<Control>)pill.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            pill.ContextMenu.Open(pill); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Open Desk", items.Single(m => m.Name == "OpenDeskMenu").Header);
            Assert.DoesNotContain(((IEnumerable<Control>)dashboard.ContextMenu!.ItemsSource!).OfType<MenuItem>(), m => m.Name == "QuickSwitchMenu");
            // Quick switch lists every account but the break, and choosing one starts it.
            var switchItem = items.Single(m => m.Name == "QuickSwitchMenu");
            var accounts = ((IEnumerable<Control>)switchItem.ItemsSource!).OfType<MenuItem>().ToList();
            Assert.Equal(model.Accounts.Count(a => !BreakAccount.Is(a.Name)), accounts.Count);
            var target = accounts[1]; Click(target); Assert.Equal((string?)target.Header, model.ActiveAccount);
            // Break starts and ends from the menu, and Pin to screen is the always-on-top switch.
            var breakItem = items.Single(m => m.Name == "BreakMenu"); Assert.Equal("Start break", breakItem.Header);
            Click(breakItem); Assert.True(model.IsOnBreak); pill.ContextMenu.Close(); pill.ContextMenu.Open(pill); Assert.Equal("End break", breakItem.Header);
            var pin = items.Single(m => m.Name == "PinMenu"); Assert.True(pin.IsChecked); Click(pin); Assert.False(model.PillOnTop);
            var asked = 0; model.QuickCaptureRequested += () => asked++; Click(items.Single(m => m.Name == "AddNoteMenu")); Assert.Equal(1, asked); // The small capture box, not the dashboard.
            var opened = 0; model.DashboardRequested += () => opened++; Click(items.Single(m => m.Name == "OpenDeskMenu")); Assert.Equal(1, opened);
            // Closing the timer is still not on the menu: it stays alone at the foot of the dashboard's rail.
            Assert.DoesNotContain(items, m => (string?)m.Header == "Close timer");
            var settingsAsked = 0; model.SettingsRequested += () => settingsAsked++; Click(items.Single(m => m.Name == "SettingsMenu")); Assert.Equal(1, settingsAsked);
        }
        finally { pill.Close(); dashboard.Close(); }
    }

    [AvaloniaFact]
    public void AddNoteFromTheMenuComesUpOverTheDashboardAndSavesATodayNote()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            model.RequestNote(); Dispatcher.UIThread.RunJobs(); dashboard.UpdateLayout(); // The dialog joins the visual tree on layout, which on a slow runner has not always happened by now.
            var text = Find<TextBox>(dashboard, "NoteText"); text.Text = "14:30 Call back";
            Click(Find<Button>(dashboard, "SaveNote")); Dispatcher.UIThread.RunJobs();
            var note = Assert.Single(model.NotesOn(Day)); Assert.Equal("Call back", note.Text); Assert.Equal(new TimeSpan(14, 30, 0), note.At);
            Assert.Empty(dashboard.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "NoteText"));
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void SettingsShowEachLayoutAsAPictureWithItsWordsAndTheChosenOneIsMarked()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            dashboard.OpenCategory("pill");
            foreach (var layout in PillLayouts.All)
            {
                var button = Find<Button>(dashboard, "PillLayout_" + layout.Key);
                Assert.Contains(layout.Title, button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                Assert.Contains(layout.Description, button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            }
            Assert.Contains("mainAction", Find<Button>(dashboard, "PillLayout_full").Classes);
            Click(Find<Button>(dashboard, "PillLayout_compact")); Dispatcher.UIThread.RunJobs();
            Assert.Equal("compact", model.PillStyle);
            Assert.Contains("mainAction", Find<Button>(dashboard, "PillLayout_compact").Classes); Assert.DoesNotContain("mainAction", Find<Button>(dashboard, "PillLayout_full").Classes);
        }
        finally { dashboard.Close(); }
    }
}
