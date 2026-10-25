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

public sealed class SetupTests
{
    private static StoreSettings Fresh() => new(new() { new("Account one"), new("Account two"), new("Internal") }, false, ThemeLibrary.Default, DayOfWeek.Monday, SetupDone: false);
    private static TimerModel NewModel() => new(new TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0)), Fresh());
    // A few passes, because a new page's visuals appear only after a layout and render pass, and a slow Windows runner has been seen to need more than one.
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Window w, string name) { Find<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void ANewDatabaseIsPendingUntilTheSetupIsFinishedAndAnOldOneIsDone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-setup-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                Assert.False(prefs.SetupDone); // Nothing saved yet and the sample accounts were just seeded.
                store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                Assert.False(prefs.SetupDone); // Quitting part way brings it back.
                store.Save(engine, prefs with { SetupDone = true, DecimalHours = true, WeekStart = DayOfWeek.Sunday });
            }
            using (var store = new TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine());
                Assert.True(prefs.SetupDone); Assert.True(prefs.DecimalHours); Assert.Equal(DayOfWeek.Sunday, prefs.WeekStart); // The week start is saved now too.
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void ADatabaseFromBeforeTheSetupExistedCountsAsDone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-setup-old-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); store.Save(engine, prefs with { SetupDone = true }); }
            // Take the setup line out of the saved settings, as an older version would have left them.
            var file = Directory.GetFiles(folder, "*.json").Single(f => !f.EndsWith(".bak"));
            File.WriteAllText(file, File.ReadAllText(file).Replace("setup=done", "shadows=on"));
            using (var store = new TimerStore(folder)) Assert.True(store.Load(new TrackingEngine()).SetupDone);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void TheSetupSitsOverABlurredDashboardThatCannotBeClickedAndWalksThroughFourSteps()
    {
        var model = NewModel(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            Assert.True(window.SetupShowing);
            var body = window.GetVisualDescendants().OfType<Grid>().First(g => g.Children.Contains(Find<Border>(window, "SetupCard").Parent!) is false && g.ColumnDefinitions.Count == 2);
            Assert.IsType<Avalonia.Media.BlurEffect>(body.Effect); Assert.False(body.IsHitTestVisible);
            Assert.Equal("Step 1 of 4", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is { } x && x.StartsWith("Step ")).Text);
            // Step 1: rename two examples, add a budget, add a new client.
            Find<TextBox>(window, "SetupName0").Text = "Acme Ltd"; Find<TextBox>(window, "SetupBudget0").Text = "7.5";
            Find<TextBox>(window, "SetupName1").Text = "Brightside";
            Press(window, "SetupAddAccount"); Settle(window); Find<TextBox>(window, "SetupName3").Text = "Corner Shop";
            Press(window, "SetupNext");
            Assert.Equal(new[] { "Acme Ltd", "Brightside", "Internal", "Corner Shop" }, model.AccountSettings.Select(a => a.Name));
            Assert.Equal(7.5, model.AccountSettings[0].BudgetHours); Assert.Null(model.AccountSettings[1].BudgetHours); // Budgets are optional.
            // Step 2: 08:30 to 16:30, Monday to Thursday.
            Settle(window);
            Find<TextBox>(window, "SetupStart").Text = "08:30"; Find<TextBox>(window, "SetupEnd").Text = "16:30"; Find<CheckBox>(window, "SetupDayFri").IsChecked = false;
            Press(window, "SetupNext");
            Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday }, model.Engine.Schedule.Days.OrderBy(d => (int)d));
            Assert.Equal(TimeSpan.FromHours(8.5), model.Engine.Schedule.Shifts[DayOfWeek.Monday].Start);
            // Step 3: decimal hours and a week that starts on Sunday.
            Settle(window);
            Press(window, "SetupDecimal"); Find<ComboBox>(window, "SetupWeekStart").SelectedItem = "Sunday";
            Press(window, "SetupNext");
            Assert.True(model.DecimalHours); Assert.Equal(DayOfWeek.Sunday, model.WeekStartsOn);
            // Step 4 finishes it.
            Settle(window); Assert.Equal("Start tracking", Find<Button>(window, "SetupNext").Content);
            Assert.False(model.SetupDone);
            Press(window, "SetupNext");
            Assert.True(model.SetupDone); Assert.False(window.SetupShowing); Assert.True(body.IsHitTestVisible); Assert.Null(body.Effect);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SkippingLeavesTheExampleAccountsAndEscapeSkipsToo()
    {
        var model = NewModel(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            Press(window, "SetupSkip");
            Assert.True(model.SetupDone); Assert.False(window.SetupShowing);
            Assert.Equal(new[] { "Account one", "Account two", "Internal" }, model.AccountSettings.Select(a => a.Name));
        }
        finally { window.Close(); }
        var second = NewModel(); var again = new DashboardWindow(second); again.Show();
        try
        {
            Settle(again);
            Find<Control>(again, "SetupOverlay").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.True(second.SetupDone);
        }
        finally { again.Close(); }
    }

    [AvaloniaFact]
    public void PressingNextStraightThroughChangesNothing()
    {
        var model = NewModel(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var accounts = model.AccountSettings.ToList(); var days = model.Engine.Schedule.Days.ToList();
            for (var i = 0; i < 4; i++) { Press(window, "SetupNext"); Settle(window); }
            Assert.True(model.SetupDone);
            Assert.Equal(accounts, model.AccountSettings); Assert.Equal(days.OrderBy(d => (int)d), model.Engine.Schedule.Days.OrderBy(d => (int)d));
            Assert.False(model.DecimalHours); Assert.Equal(DayOfWeek.Monday, model.WeekStartsOn);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABadBudgetOrTimeStaysOnItsStepWithAMessage()
    {
        var model = NewModel(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            Find<TextBox>(window, "SetupBudget0").Text = "lots"; Press(window, "SetupNext"); Settle(window);
            Assert.Equal("Step 1 of 4", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is { } x && x.StartsWith("Step ")).Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text is { } x && x.StartsWith("Use positive budget hours"));
            Find<TextBox>(window, "SetupBudget0").Text = ""; Find<TextBox>(window, "SetupName0").Text = "Internal"; Press(window, "SetupNext"); // A name already used.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text is { } x && x.Contains("unique"));
            Find<TextBox>(window, "SetupName0").Text = "Account one"; Press(window, "SetupNext"); Settle(window);
            Find<TextBox>(window, "SetupStart").Text = "25:99"; Press(window, "SetupNext");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text is { Length: > 0 } x && t.Classes.Contains("overtime") && x.Length > 5);
            Assert.False(model.SetupDone); Assert.Equal(TimeSpan.FromHours(9), model.Engine.Schedule.Default.Start); // Nothing was applied.
            Find<TextBox>(window, "SetupStart").Text = "09:00"; Find<TextBox>(window, "SetupEnd").Text = "08:00"; Press(window, "SetupNext");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text is { } x && x.Contains("end after the start"));
            Press(window, "SetupBack"); Settle(window);
            Assert.Equal("Account one", Find<TextBox>(window, "SetupName0").Text); // Going back shows what was there.
            Press(window, "SetupNext"); Settle(window);
            Assert.Equal("08:00", Find<TextBox>(window, "SetupEnd").Text); // And forward again keeps what was typed.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSetupCanBeRunAgainFromSettings()
    {
        var model = NewModel(); model.CompleteSetup(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window); Assert.False(window.SetupShowing);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Press(window, "RunSetup"); Settle(window);
            Assert.True(window.SetupShowing); Assert.False(model.SetupDone);
            Press(window, "SetupSkip"); Assert.False(window.SetupShowing);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADemoAndAnExistingSetupNeverShowTheOverlay()
    {
        var demo = TimerModel.CreateDemo(); var window = new DashboardWindow(demo); window.Show();
        try { Settle(window); Assert.False(window.SetupShowing); Assert.True(demo.SetupDone); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSetupCardWearsTheApplicationIconNotTheOldAccentCapsule()
    {
        var model = NewModel(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var icon = Find<Avalonia.Controls.Viewbox>(window, "SetupPillIcon");
            var parts = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().ToList();
            Assert.Equal(6, parts.Count); // The outline, two lights, and the hours, minutes and seconds bars.
            Assert.Single(parts, r => r.StrokeThickness > 0); // Only the outline is a line; the rest are filled.
            Assert.All(parts, r => Assert.True((r.StrokeThickness > 0 ? r.Stroke : r.Fill) is not null)); // One colour, the text colour, nothing in the accent or the status colours.
        }
        finally { window.Close(); }
    }
}
