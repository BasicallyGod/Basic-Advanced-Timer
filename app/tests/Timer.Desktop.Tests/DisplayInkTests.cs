using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// E-ink stage 0: the display style and its strength, the paper and ink colours, the grain, the type and the account textures.
public sealed class DisplayInkTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static TimerModel Demo() { var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); return model; }
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static double Over(Color a, Color paper) => ThemeColours.Contrast(Hex(a), Hex(paper));

    [Fact]
    public void PhosphorIsTheDefaultAndSixtyIsTheStrength()
    {
        var model = Demo();
        Assert.Equal(DisplayStyle.Phosphor, model.DisplayStyle); Assert.Equal(60, model.InkPercent);
        Assert.False(DisplayInk.On);
    }

    [Fact]
    public void TheStyleAndStrengthAreSavedAndPushedToTheDrawingCode()
    {
        var model = Demo();
        model.SetDisplayStyle(DisplayStyle.Ink); Assert.Equal(DisplayStyle.Ink, model.DisplayStyle); Assert.True(DisplayInk.On);
        model.SetInkPercent(30); Assert.Equal(30, model.InkPercent); Assert.Equal(.3, DisplayInk.Strength, 3);
        model.SetInkPercent(33); Assert.Equal(30, model.InkPercent); // Steps of ten.
        model.SetInkPercent(250); Assert.Equal(100, model.InkPercent);
        model.SetDisplayStyle(DisplayStyle.Phosphor); Assert.False(DisplayInk.On); Assert.Equal(100, model.InkPercent); // The strength is kept for next time.
    }

    [Fact]
    public void ASavedStyleAndStrengthComeBackWhenTheTimerOpens()
    {
        var extras = SettingsExtras.Empty with { AppearanceLines = new List<string> { "displaystyle=ink", "inkstrength=40" } };
        var model = new TimerModel(new TrackingEngine(clock: () => DemoNow), new StoreSettings(new() { new("Account one") }, false, "Dark", DayOfWeek.Monday, SetupDone: true, Extras: extras));
        Assert.Equal(DisplayStyle.Ink, model.DisplayStyle); Assert.Equal(40, model.InkPercent); Assert.True(DisplayInk.On); Assert.Equal(.4, DisplayInk.Strength, 3);
        DisplayInk.Style = DisplayStyle.Phosphor;
    }

    [Fact]
    public void InkOnPaperAndItsQuietWordsAndStatusColoursAreReadableInBothThemes()
    {
        foreach (var colours in new[] { DisplayInk.Light, DisplayInk.Dark })
        {
            Assert.True(Over(colours.Ink, colours.Paper) >= 7, $"ink on paper, light paper {colours.OnLightPaper}");
            Assert.True(Over(colours.Muted, colours.Paper) >= 4.5, "quiet words");
            foreach (var status in new[] { colours.Live, colours.Amber, colours.Red, colours.Lit }) Assert.True(Over(status, colours.Paper) >= 4.5, $"status {Hex(status)}");
        }
    }

    [AvaloniaFact]
    public void TheEffectAtZeroDrawsNoGrainAndAboveZeroItDoes()
    {
        DisplayInk.Style = DisplayStyle.Ink;
        try
        {
            Pixels(0, out var clean); Pixels(100, out var grainy);
            Assert.Equal(1, clean.Distinct().Count()); // A flat fill: nothing was laid over it.
            Assert.True(grainy.Distinct().Count() > 1);
        }
        finally { DisplayInk.Style = DisplayStyle.Phosphor; DisplayInk.Percent = DisplayInk.DefaultPercent; }
    }
    private static void Pixels(int percent, out uint[] pixels)
    {
        DisplayInk.Percent = percent;
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        var host = new DrawHost(); host.Measure(new Size(64, 64)); host.Arrange(new Rect(0, 0, 64, 64)); bitmap.Render(host);
        using var copy = new MemoryStream(); bitmap.Save(copy); copy.Position = 0; using var decoded = new Bitmap(copy);
        var buffer = new uint[64 * 64]; var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { decoded.CopyPixels(new PixelRect(0, 0, 64, 64), handle.AddrOfPinnedObject(), buffer.Length * 4, 64 * 4); } finally { handle.Free(); }
        pixels = buffer;
    }
    private sealed class DrawHost : Control
    {
        public override void Render(DrawingContext context) => DisplayInk.Cell(context, DisplayInk.Light, Brushes.White, new Rect(0, 0, 64, 64), 0);
    }

    [AvaloniaFact]
    public void FiguresAreTabularSoATimeNeverChangesWidth()
    {
        var widths = new[] { "00:00:00", "11:11:11", "08:08:08", "19:59:59" }.Select(t => InkType.Width(t, InkRole.Clock)).ToList();
        Assert.All(widths, w => Assert.Equal(widths[0], w, 3));
        Assert.Equal("On pace", InkType.Sentence("ON PACE")); Assert.Equal("Export CSV", InkType.Sentence("Export CSV"));
    }

    [Fact]
    public void EachAccountKeepsItsTextureThroughReordersAndRenames()
    {
        var model = Demo();
        var names = model.Accounts.Select(a => a.Name).Take(3).ToList();
        var first = names.Select(model.TextureOf).ToList();
        Assert.Equal(first.Distinct().Count(), first.Count); // Different accounts get different textures.
        Assert.Equal(first, Enumerable.Reverse(names).Reverse().Select(model.TextureOf).ToList());
        Assert.All(first, n => Assert.InRange(n, 0, DisplayInk.TextureCount - 1));
        // A rename carries it over.
        var settings = model.AccountSettings.ToList();
        var renamed = settings.Select(s => s.Name == names[0] ? s with { Name = "Renamed account" } : s).ToList();
        model.SetAccounts(renamed, new Dictionary<string, string> { [names[0]] = "Renamed account" });
        Assert.Equal(first[0], model.TextureOf("Renamed account"));
    }

    [AvaloniaFact]
    public void SettingsOffersTheTwoStylesAndTheSliderRestsUnderPhosphor()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); window.OpenSettings(); Dispatcher.UIThread.RunJobs();
        try
        {
            var view = window.GetVisualDescendants().OfType<SettingsView>().Single();
            T Find<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
            Assert.Contains("selected", Find<Button>("DisplayStylePhosphor").Classes); Assert.False(Find<Slider>("InkStrength").IsEnabled);
            Find<Button>("DisplayStyleInk").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DisplayStyle.Ink, model.DisplayStyle); Assert.Contains("selected", Find<Button>("DisplayStyleInk").Classes);
            var slider = Find<Slider>("InkStrength"); Assert.True(slider.IsEnabled); Assert.Equal(60, slider.Value);
            slider.Value = 20; Dispatcher.UIThread.RunJobs(); Assert.Equal(20, model.InkPercent);
        }
        finally { window.Close(); DisplayInk.Style = DisplayStyle.Phosphor; }
    }

    // Pictures of the pages in each look and theme, written when TIMER_PREVIEW_DIR is set, for looking at the layout.
    [AvaloniaFact]
    public void PicturesOfTheWeekAndTimelineInBothLooks()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var style in new[] { DisplayStyle.Phosphor, DisplayStyle.Ink })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 14, 15, 7)); model.CompleteSetup(); model.SetTheme(theme); model.SetDisplayStyle(style);
                var window = new DashboardWindow(model); window.Show();
                try
                {
                    for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); }
                    void Shot(string name) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); } using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"{style}-{theme}-{name}.png")); }
                    Shot("timeline");
                    window.SelectTab(true); Shot("week");
                    model.SetWeekAsTable(true); Shot("timesheet"); model.SetWeekAsTable(false);
                }
                finally { window.Close(); }
                DisplayInk.Style = style; // the window closing leaves the thread's look as the model set it, but be sure
                // The pill in its three styles, tracking, so the display's clock, count, name and keys can be looked at.
                model.ToggleAccount("Account one");
                foreach (var shape in new[] { "full", "compact", "tall" })
                {
                    model.SetCompactPill(shape == "compact"); model.SetTallPill(shape == "tall");
                    var pill = new PillWindow(model); pill.Show();
                    try
                    {
                        for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var f = pill.CaptureRenderedFrame(); }
                        using var frame = pill.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"{style}-{theme}-pill-{shape}.png"));
                    }
                    finally { pill.Close(); }
                }
                model.SetCompactPill(false); model.SetTallPill(false);
                // The alerts card, which the pill and the sidebar hang under their count.
                var host = new Window { Width = 380, Height = 420, Content = new PillAlertsCard(model, () => { }, () => { }) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0) } };
                host.Show();
                try { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var f = host.CaptureRenderedFrame(); } using var frame = host.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"{style}-{theme}-alerts.png")); }
                finally { host.Close(); }
            }
        DisplayInk.Style = DisplayStyle.Phosphor;
    }

    [AvaloniaFact]
    public void TheTwoKeysHaveTheirOwnSizeInEachLook()
    {
        var glyph = new BreakBadgeGlyph(); var play = new TransportGlyph();
        DisplayInk.Style = DisplayStyle.Phosphor; glyph.Measure(Size.Infinity); play.Measure(Size.Infinity);
        Assert.Equal(new Size(VfdGlyph.BadgeWidth, VfdGlyph.BadgeHeight), glyph.DesiredSize); Assert.Equal(glyph.DesiredSize, play.DesiredSize);
        DisplayInk.Style = DisplayStyle.Ink; glyph.InvalidateMeasure(); glyph.Measure(Size.Infinity); play.InvalidateMeasure(); play.Measure(Size.Infinity);
        Assert.Equal(new Size(VfdGlyph.InkKeyWidth, VfdGlyph.InkKeyHeight), glyph.DesiredSize); Assert.Equal(glyph.DesiredSize, play.DesiredSize);
        DisplayInk.Style = DisplayStyle.Phosphor;
    }

    [AvaloniaFact]
    public void TheKeysAndTheLedFollowTheLookAtOnceAndTheWellsCarryPaper()
    {
        var model = Demo(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Border Led() => pill.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PillStateLight");
            Button Key(string name) => pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            Assert.NotNull(Led().OpacityMask); Assert.Equal(34, Key("PillBreak").Width); Assert.Equal(16, Key("PillBreak").Height); // Phosphor: the honeycomb and the small keys.
            model.SetDisplayStyle(DisplayStyle.Ink); Dispatcher.UIThread.RunJobs();
            Assert.Null(Led().OpacityMask); // E-ink: a plain LED, no mesh.
            Assert.Equal(40, Key("PillBreak").Width); Assert.Equal(19, Key("PillBreak").Height); Assert.Equal(40, Key("PillPause").Width); // Room for the word Break inside its box.
            Assert.NotEmpty(pill.GetVisualDescendants().OfType<PaperLayer>()); // The well has its paper grain.
            model.SetDisplayStyle(DisplayStyle.Phosphor); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Led().OpacityMask); Assert.Equal(34, Key("PillBreak").Width);
        }
        finally { pill.Close(); DisplayInk.Style = DisplayStyle.Phosphor; }
    }

    [AvaloniaFact]
    public void OutlinedBlocksDrawAColouredOutlineAndWashInPlaceOfTheFilledBlock()
    {
        var model = Demo(); model.SetOutlinedBlocks(true); Assert.True(model.OutlinedBlocks);
        var window = new DashboardWindow(model); window.Show(); for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); }
        try
        {
            var block = window.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("TimelineEntry") == true && !string.IsNullOrEmpty(b.Name));
            Assert.True(block.BorderThickness.Left > 1); Assert.NotNull(block.BorderBrush);
            Assert.Contains(block.GetVisualDescendants().OfType<Border>(), b => b.Name == "BlockOutlineWash"); Assert.DoesNotContain(block.GetVisualDescendants().OfType<Border>(), b => b.Name == "BlockPhosphor");
            model.SetOutlinedBlocks(false); Assert.False(model.OutlinedBlocks);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWheelScrollsTheWeeksAccountsFromAnywhereOverThem()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); }
        try
        {
            var instrument = window.GetVisualDescendants().OfType<WeekInstrument>().Single();
            if (instrument.RowCount <= instrument.Capacity) return; // nothing to scroll with this much room
            var row = window.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("WeekAccount_") == true);
            var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value; // on a button, not on the bare drawing
            var before = instrument.ShownAccounts.ToList();
            Avalonia.Headless.HeadlessWindowExtensions.MouseWheel(window, at, new Vector(0, -1)); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, instrument.ShownAccounts);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheSidebarsWreathStaysInsideTheColumnsMargin() => Assert.True(Sidebar.DisplayFrame.Room <= 12);
}
