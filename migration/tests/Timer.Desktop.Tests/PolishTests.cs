using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
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
        var model = Plain(); var window = new DashboardWindow(model); window.Show();
        try
        {
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            Assert.False(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible);
            window.OpenCategory("about"); Settle(window);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible); // No sample-data demo yet.
            var about = window.GetVisualDescendants().OfType<StackPanel>().Single(b => b.Name == "AboutSection");
            var point = about.TranslatePoint(new Point(20, 20), window)!.Value;
            for (var i = 0; i < SettingsView.UnlockClicks - 1; i++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
            Assert.False(model.DeveloperUnlocked); // Nine is not enough.
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(model.DeveloperUnlocked);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible);
            window.ContextMenu!.Close(); window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            Assert.True(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible); // The tester is for the demo only.
            // The switch in the Development section hides them again.
            model.SetDeveloper(false); Dispatcher.UIThread.RunJobs();
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible);
        }
        finally { window.Close(); }
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
            window.OpenCategory("development"); Settle(window);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible);
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
            Settle(window);
            var lines = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t is not null).ToList();
            Assert.Contains(lines, t => t!.EndsWith(" / 20:00")); Assert.Contains("This month", lines); Assert.Contains("This week", lines); Assert.Contains("This quarter", lines); // The period sits beside the usage, not tacked on its end.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.Contains(" of ") && x.Contains("h ") && t.Classes.Contains("muted") && x.Contains("m") && x.Length > 0 && x.Contains("00m"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "TodayHeading" && t.Text == "Today");
            Assert.Equal("10:35", model.FormatCompact(TimeSpan.FromMinutes(635))); Assert.Equal("20:00", model.FormatCompact(TimeSpan.FromHours(20)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheStatusDotAndTheAccountDotShareACentreLine()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("dayCard"));
            var dots = card.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().ToList();
            Assert.Equal(2, dots.Count);
            double Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.Equal(Centre(dots[0]), Centre(dots[1]), 0.6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OnABreakThePillsSecondLineSaysSomethingNew()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var pill = new PillWindow(model); pill.Show();
        try
        {
            if (model.IsTracking) model.TogglePause();
            model.ToggleBreak(); Settle(pill);
            var texts = pill.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
            Assert.Equal(1, texts.Count(t => t!.Contains("On break", StringComparison.OrdinalIgnoreCase) || t == "On a break"));
            Assert.Contains(texts, t => t!.StartsWith("Since ") || t!.StartsWith("Returns to "));
            Assert.Contains("onBreak", pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillBreak").Classes);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void EveryAccountRowsPlayCircleSitsInTheSameColumn()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var xs = window.GetVisualDescendants().OfType<AccountIndicator>().Select(i => Math.Round(i.TranslatePoint(default, window)!.Value.X, 1)).Distinct().ToList();
            Assert.Single(xs); // A long name or a wide percentage never pushes a row's button aside.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheZoomBarsIconsAndSliderAreAllCentredOnItsMiddle()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show(); window.SelectTab(false);
        try
        {
            Settle(window);
            var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineControls");
            double Mid(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
            var thumb = bar.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single(); var track = bar.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Track>().Single();
            Assert.Equal(Mid(bar), Mid(thumb), 0.6); Assert.Equal(Mid(bar), Mid(track), 0.6);
            foreach (var icon in bar.GetVisualDescendants().OfType<IconGlyph>()) Assert.Equal(Mid(bar), Mid(icon), 0.6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EntriesRunByStartTimeWithinEachDay()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.SelectTab(true); model.SetWeekSection("entries"); Settle(window);
            var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("entryRow")).ToList();
            Assert.True(rows.Count > 6);
            // Each row's time range is the second small line; read the starts in on-screen order and check they climb within a day.
            static string? Start(Button b) => b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t is { Length: 13 } && t[2] == ':' && t[5] == ' ')?[..5];
            var ordered = rows.Select(r => (Y: r.TranslatePoint(default, window)!.Value.Y, Start: Start(r))).Where(r => r.Start is not null).OrderBy(r => r.Y).ToList();
            // Each row belongs to the day heading above it; within one heading the starts must never fall.
            var headings = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is { } x && x.Contains(' ') && System.Globalization.CultureInfo.GetCultureInfo("en-GB").DateTimeFormat.DayNames.Contains(x.Split(' ')[0]))
                .Select(t => t.TranslatePoint(default, window)!.Value.Y).OrderBy(y => y).ToList();
            Assert.True(headings.Count >= 3);
            foreach (var day in ordered.GroupBy(o => headings.Count(h => h < o.Y)))
            {
                var starts = day.Select(d => d.Start!).ToList();
                Assert.Equal(starts.OrderBy(x => x, StringComparer.Ordinal).ToList(), starts);
            }
            var tuesday = ordered.GroupBy(o => headings.Count(h => h < o.Y)).Select(g => g.Select(d => d.Start!).ToList()).First(g => g.Contains("09:00") && g.Count > 3);
            Assert.Equal("09:00", tuesday[0]); // Account two, which the old account-name order put fifth.
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
    public void EveryAccountRowNameCanSlide()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try { Settle(window); Assert.True(window.GetVisualDescendants().OfType<MarqueeText>().Count(m => m.Name == "AccountName") >= 7); }
        finally { window.Close(); }
    }
}
