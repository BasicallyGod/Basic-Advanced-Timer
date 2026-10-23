using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PolishTests
{
    // A few passes, because a new page's visuals appear only after a layout and render pass, and a slow Windows runner has been seen to need more than one.
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Plain() => new(new TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0)));

    [AvaloniaFact]
    public void DeveloperFeaturesAreHiddenUntilTenQuickClicksOnAbout()
    {
        // Each press is 50 ms after the last by a fake clock, so a slow runner cannot stretch the gap past the unlock window.
        var fake = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc); SettingsView.UnlockClock = () => fake = fake.AddMilliseconds(50);
        var model = Plain(); var window = new DashboardWindow(model); window.Show();
        try
        {
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            Assert.False(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); Settle(window);
            Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible); // No sample-data demo yet.
            window.GetVisualDescendants().OfType<SettingsView>().Single().ShowSection("about"); Settle(window); // About is its own section.
            var about = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "AboutSection");
            var point = about.TranslatePoint(new Point(20, 20), window)!.Value;
            for (var i = 0; i < SettingsView.UnlockClicks - 1; i++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
            Assert.False(model.DeveloperUnlocked); // Nine is not enough.
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(model.DeveloperUnlocked);
            Assert.True(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible);
            window.ContextMenu!.Close(); window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            Assert.True(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible); // The tester is for the demo only.
            // The switch in the Development section hides them again.
            model.SetDeveloper(false); Dispatcher.UIThread.RunJobs();
            Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible);
        }
        finally { window.Close(); SettingsView.UnlockClock = () => DateTime.UtcNow; }
    }

    [AvaloniaFact]
    public void ADemoAlwaysShowsTheDeveloperMenuItems()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            Assert.True(menu.Single(m => m.Name == "TestOvertime").IsVisible); Assert.True(menu.Single(m => m.Name == "BuildVersion").IsVisible);
            // The settings page shows the Development section already open, with its switch fixed on and no button to open a demo from inside one.
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.True(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DevelopmentSection").IsVisible);
            var toggle = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "DeveloperFeatures");
            Assert.True(toggle.IsChecked); Assert.False(toggle.IsEnabled);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenDemo").IsVisible == true && window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenDemo").GetVisualAncestors().OfType<Grid>().First().IsVisible);
            Assert.True(window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "InDemoNote").IsVisible);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ThePrivateSettingSurvivesSavingAndLoading()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-dev-" + Guid.NewGuid());
        try
        {
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                Assert.False(prefs.Developer);
                store.Save(engine, prefs with { Developer = true, Tooltips = false });
            }
            using (var store = new Timer.Storage.TimerStore(folder))
            { var prefs = store.Load(new TrackingEngine()); Assert.True(prefs.Developer); Assert.False(prefs.Tooltips); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void BudgetsReadAsCompactFiguresWithTheirPeriodAndTodayIsLabelled()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.ShowAccountsPanel();
            Settle(window);
            var lines = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t is not null).ToList();
            Assert.Contains(lines, t => t!.EndsWith(" / 20:00")); Assert.Contains("This month", lines); Assert.Contains("This week", lines); Assert.Contains("This quarter", lines); // The period sits beside the usage, not tacked on its end.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.Contains(" of ") && x.Contains("h ") && t.Classes.Contains("muted") && x.Contains("m") && x.Length > 0 && x.Contains("00m"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Today" && t.Name == "TodayHeading"); // No Today heading over the accounts.
            Assert.Equal("10:35", model.FormatCompact(TimeSpan.FromMinutes(635))); Assert.Equal("20:00", model.FormatCompact(TimeSpan.FromHours(20)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryAccountRowsPlayCircleSitsInTheSameColumn()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.ShowAccountsPanel();
            Settle(window);
            var xs = window.GetVisualDescendants().OfType<AccountIndicator>().Select(i => Math.Round(i.TranslatePoint(default, window)!.Value.X, 1)).Distinct().ToList();
            Assert.Single(xs); // A long name or a wide percentage never pushes a row's button aside.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANameThatDoesNotFitSlidesOnHoverAndGoesBackOnLeave()
    {
        var narrow = new MarqueeText("A very long client account name that cannot fit", 15) { Width = 90 };
        var short_ = new MarqueeText("Short", 15) { Width = 90 };
        var window = new Window { Width = 300, Height = 120, Content = new StackPanel { Children = { narrow, short_ } } }; window.Show();
        try
        {
            Settle(window);
            Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, narrow.Label.TextTrimming); Assert.False(narrow.Scrolling);
            narrow.Hover(true);
            Assert.True(narrow.Scrolling); Assert.Equal(Avalonia.Media.TextTrimming.None, narrow.Label.TextTrimming); Assert.True(narrow.Label.Width > 90); // The whole name, in a clipped window.
            narrow.Hover(false);
            Assert.False(narrow.Scrolling); Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, narrow.Label.TextTrimming); Assert.Null(narrow.Label.RenderTransform); Assert.True(double.IsNaN(narrow.Label.Width));
            short_.Hover(true); Assert.False(short_.Scrolling); // A name that fits stays put.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ASlidingNameNeverGrowsPastItsRoomOrOverTheTimeBesideIt()
    {
        var marquee = new MarqueeText("A very long client account name that cannot fit", 15);
        var time = new TextBlock { Text = "01:30" };
        var grid = new Grid { Width = 200, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new NameRow { Spacing = 0, Children = { new Border { Width = 10, Height = 10 }, marquee, new Border { Width = 8, Height = 8 } } }); Grid.SetColumn(time, 1); grid.Children.Add(time);
        var window = new Window { Width = 300, Height = 120, Content = grid }; window.Show();
        try
        {
            Settle(window); var room = marquee.Bounds.Width; Assert.True(room > 20 && room < 200, $"room {room}");
            marquee.Hover(true); Settle(window);
            Assert.True(marquee.Scrolling); Assert.InRange(marquee.Bounds.Width, room - 0.5, room + 2); // The box keeps its size (the trimmed name left a pixel or so spare), so the clock beside it is not run over.
            var right = marquee.TranslatePoint(new Point(marquee.Bounds.Width, 0), window)!.Value.X; Assert.True(right <= time.TranslatePoint(default, window)!.Value.X + 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryAccountRowNameCanSlide()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try { window.ShowAccountsPanel(); Settle(window); Assert.True(window.GetVisualDescendants().OfType<MarqueeText>().Count(m => m.Name == "AccountName") >= 7); }
        finally { window.Close(); }
    }
}
