using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The Christmas wreath and lights round the pill, the Seasonal setting and the Development preview.
public sealed class SeasonalTests
{
    private static DateTime Today = new(2026, 12, 10);
    private static TimerModel Model(DateTime? day = null)
    {
        var at = (day ?? Today).AddHours(10);
        var model = new TimerModel(new TrackingEngine(clock: () => at), new StoreSettings(new List<AccountSettings> { new("Account one", 10, "week"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void ChristmasRunsFromTheFirstOfDecemberToTheSixthOfJanuaryInclusive()
    {
        foreach (var d in new[] { new DateTime(2026, 12, 1), new DateTime(2026, 12, 25), new DateTime(2026, 12, 31), new DateTime(2027, 1, 1), new DateTime(2027, 1, 6) }) Assert.Equal(Seasons.Christmas, Seasons.On(d));
        foreach (var d in new[] { new DateTime(2026, 11, 30), new DateTime(2027, 1, 7), new DateTime(2027, 6, 15), new DateTime(2026, 10, 31) }) Assert.Null(Seasons.On(d));
    }

    [Fact]
    public void DecorationsAreOffUntilChosenAndThenFollowTheCalendar()
    {
        var model = Model(); Assert.False(model.SeasonalDecorations); Assert.Null(model.ActiveSeason); // December, but nothing is on until it is chosen.
        model.SetSeasonalDecorations(true); Assert.Equal(Seasons.Christmas, model.ActiveSeason);
        var summer = Model(new DateTime(2026, 7, 4)); summer.SetSeasonalDecorations(true); Assert.Null(summer.ActiveSeason); // Out of season even when on.
        model.SetSeasonalDecorations(false); Assert.Null(model.ActiveSeason);
    }

    [Fact]
    public void ThePreviewShowsASeasonOnAnyDayAndIsNotSaved()
    {
        var model = Model(new DateTime(2026, 7, 4));
        model.SetSeasonPreview("christmas"); Assert.Equal(Seasons.Christmas, model.ActiveSeason); Assert.False(model.SeasonalDecorations); // It needs neither the date nor the setting.
        model.SetSeasonPreview(null); Assert.Null(model.ActiveSeason);
        model.SetSeasonPreview("no-such-season"); Assert.Null(model.SeasonPreview);
    }

    [Fact]
    public void TheLightsFollowTheState()
    {
        var idle = Enumerable.Range(0, 8).Select(i => ChristmasDecor.BulbColour(DecorState.Idle, i)).ToList();
        Assert.Equal(4, idle.Distinct().Count()); Assert.Equal(idle[0], idle[4]); // Four colours in turn when ready.
        var rec = Enumerable.Range(0, 8).Select(i => ChristmasDecor.BulbColour(DecorState.Recording, i)).ToList();
        Assert.Equal(2, rec.Distinct().Count()); Assert.NotEqual(rec[0], rec[1]); Assert.Equal(rec[0], rec[2]); // Green and red, alternating.
        Assert.Equal(1, Enumerable.Range(0, 8).Select(i => ChristmasDecor.BulbColour(DecorState.Break, i)).Distinct().Count()); // Amber alone on a break.
        Assert.Equal(Color.Parse("#2FBF71"), rec[0]);
    }

    [Fact]
    public void EveryBulbTwinklesBetweenDimAndFullAndNeverGoesDark()
    {
        foreach (var state in new[] { DecorState.Idle, DecorState.Recording, DecorState.Break })
            for (var i = 0; i < 40; i++)
                for (var t = 0.0; t < 12; t += 0.05) { var o = ChristmasDecor.Twinkle(state, i, t); Assert.InRange(o, 0.249, 1.0); }
        // Recording dips further and more often than a break, which is the livelier-than-before twinkle asked for.
        double Dip(DecorState s) => Enumerable.Range(0, 400).Select(k => ChristmasDecor.Twinkle(s, 3, k * 0.03)).Average();
        Assert.True(Dip(DecorState.Recording) < 0.9 && Dip(DecorState.Break) < 0.9);
    }

    [Fact]
    public void TheBulbsHangOutsideTheCardWithinTheRoomTheWindowKeeps()
    {
        foreach (var compact in new[] { false, true })
        {
            var card = compact ? PillWindow.CompactWidth : PillWindow.PillWidth;
            var size = new Size(card + 2 * Ui.ShadowMargin, PillWindow.PillHeight + 2 * Ui.ShadowMargin);
            var bulbs = ChristmasDecor.BulbsFor(size, DecorState.Idle);
            Assert.InRange(bulbs.Count, compact ? 15 : 25, compact ? 30 : 45);
            var cardRect = ChristmasDecor.CardIn(size);
            foreach (var b in bulbs)
            {
                Assert.InRange(b.X, 4, size.Width - 4); Assert.InRange(b.Y, 4, size.Height - 4);
                // The wire is a ring just outside the card, never on or inside it.
                var inside = b.X > cardRect.X + 1 && b.X < cardRect.Right - 1 && b.Y > cardRect.Y + 1 && b.Y < cardRect.Bottom - 1;
                Assert.False(inside);
            }
        }
    }

    [AvaloniaFact]
    public void ThePillWearsTheWreathOnlyWhileChristmasIsOn()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            Assert.False(pill.Garland.Active); Assert.False(pill.Lights.Active); Assert.False(pill.Lights.IsVisible);
            model.SetSeasonalDecorations(true); Settle(pill);
            Assert.True(pill.Garland.Active && pill.Lights.Active); Assert.Equal(DecorState.Idle, pill.Lights.State);
            Assert.True(pill.Garland.Bounds.Width >= PillWindow.PillWidth + 2 * Ui.ShadowMargin - 1); // It fills the window, card and room round it.
            model.ToggleAccount("Account one"); Settle(pill);
            Assert.Equal(DecorState.Recording, pill.Lights.State);
            model.ToggleBreak(); Settle(pill); Assert.Equal(DecorState.Break, pill.Lights.State);
            model.SetSeasonalDecorations(false); Settle(pill); Assert.False(pill.Lights.Active);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheCompactPillIsDressedToo()
    {
        var model = Model(); model.SetSeasonalDecorations(true); model.SetCompactPill(true);
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try { Assert.True(pill.Lights.Active); Assert.True(pill.Garland.Bounds.Width <= PillWindow.CompactWidth + 2 * Ui.ShadowMargin + 1); }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void SettingsOffersTheSeasonalTickAndTheDevelopmentPreview()
    {
        var model = Model(); model.SetDeveloper(true);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var tick = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "SeasonalDecorations");
            Assert.False(tick.IsChecked); tick.IsChecked = true; tick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.True(model.SeasonalDecorations);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsAbout").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var pick = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SeasonPreview");
            Assert.Equal(0, pick.SelectedIndex); pick.SelectedIndex = 1; Assert.Equal("christmas", model.SeasonPreview);
            pick.SelectedIndex = 0; Assert.Null(model.SeasonPreview);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WritePicturesOfTheDressedPill()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
            foreach (var compact in new[] { false, true })
            {
                var model = Model(); model.SetTheme(theme); model.SetSeasonalDecorations(true); model.SetCompactPill(compact);
                var pill = new PillWindow(model); pill.Show(); Settle(pill);
                try
                {
                    pill.Lights.FixedSeconds = 1.3;
                    void Shot(string state) { Settle(pill); pill.Lights.InvalidateVisual(); Settle(pill); using var f = pill.CaptureRenderedFrame(); f?.Save(Path.Combine(folder, $"christmas-{theme}-{(compact ? "compact" : "full")}-{state}.png")); }
                    Shot("ready");
                    model.ToggleAccount("Account one"); Shot("recording");
                    model.ToggleBreak(); Shot("break");
                }
                finally { pill.Close(); }
            }
    }
}
