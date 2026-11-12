using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// The tall pill: a third style whose second display row shows the day, what is up next, budgets, the week, both or a bat, stepped by pressing it.
public sealed class TallPillTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Demo(string theme = "Dark") { var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); model.SetTheme(theme); return model; }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void TheThreeStylesAreOneChoiceAndTheLowerDisplayIsSaved()
    {
        var model = Demo();
        Assert.False(model.TallPill); Assert.Equal(PillLower.Strip, model.PillLower);
        model.SetTallPill(true); Assert.True(model.TallPill); Assert.False(model.CompactPill);
        model.SetCompactPill(true); Assert.True(model.CompactPill); Assert.False(model.TallPill); // Choosing compact ends the tall pill,
        model.SetTallPill(true); Assert.False(model.CompactPill); // and the other way round.
        // Pressing steps through every display and comes back to the first.
        var seen = new List<PillLower>();
        for (var i = 0; i < Enum.GetValues<PillLower>().Length; i++) { seen.Add(model.PillLower); model.CyclePillLower(); }
        Assert.Equal(Enum.GetValues<PillLower>(), seen); Assert.Equal(PillLower.Strip, model.PillLower);
        Assert.Equal(6, seen.Count); // No focus count-down: the day strip, up next, budgets, the week, both, and the bat.
    }

    [AvaloniaFact]
    public void TheTallPillGrowsDownwardAndPressingTheLowerDisplayStepsIt()
    {
        var model = Demo(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var surface = Find<Border>(pill, "PillSurface"); var row = Find<Border>(pill, "PillLowerRow");
            Assert.False(row.IsVisible); Assert.Equal(PillWindow.PillHeight, surface.Bounds.Height, .5);
            model.SetTallPill(true); Settle(pill);
            Assert.True(row.IsVisible);
            Assert.Equal(PillWindow.PillHeight + LowerDisplay.RowHeight, surface.Bounds.Height, .5);
            Assert.Equal((PillWindow.PillHeight + LowerDisplay.RowHeight + 2 * Ui.ShadowMargin) * pill.Scale, pill.WindowHeightNow, .5);
            Assert.Equal(PillWindow.PillWidth, surface.Bounds.Width, .5); // As wide as the full pill.
            // The clock row keeps its height; the lower display spans the whole window under it.
            var display = Find<Border>(pill, "PillDisplay"); var lower = Find<LowerDisplay>(pill, "PillLower");
            Assert.Equal(display.Bounds.Width, lower.Bounds.Width, 1.0);
            Assert.Equal(StatusFace.DisplayHeight, lower.TranslatePoint(default, display)!.Value.Y - 1, 1.0);
            var press = Find<Button>(pill, "PillLowerDisplay");
            press.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(pill);
            Assert.Equal(PillLower.Next, model.PillLower); Assert.Equal(PillLower.Next, lower.Shown);
            Assert.Contains("Up next", ToolTip.GetTip(press) as string);
            // Both rows make it taller again, and the card follows.
            model.SetPillLower(PillLower.Both); Settle(pill);
            Assert.Equal(PillWindow.PillHeight + LowerDisplay.BothHeight, surface.Bounds.Height, .5);
            Assert.Equal(pill.CardSizeNow.Height, (PillWindow.PillHeight + LowerDisplay.BothHeight) * pill.Scale, .5);
            model.SetTallPill(false); Settle(pill);
            Assert.False(row.IsVisible); Assert.Equal(PillWindow.PillHeight, surface.Bounds.Height, .5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void UpNextNamesTheNextMeetingAndReminder()
    {
        var model = Demo(); var lower = new LowerDisplay(model) { Width = 347 }; lower.Show(PillLower.Next);
        var words = string.Concat(lower.UpNext().Select(p => p.Words));
        // The demo has meetings and reminders through the afternoon; whatever is next comes with its time and how soon.
        Assert.Matches(@"NEXT \d\d:\d\d", words);
        Assert.Contains(" IN ", words);
        Assert.All(words, ch => Assert.True(LowerDisplay.CanDraw(ch), $"'{ch}' cannot be drawn"));
    }

    [Fact]
    public void WordsAreMadeDrawable()
    {
        Assert.Equal("CAFE - REVIEW", LowerDisplay.Drawable("Café · review"));
        Assert.Equal("A B", LowerDisplay.Drawable("a  % b"));
    }

    [AvaloniaFact]
    public void TheStripSpansTheWholeWorkingDayAndNow()
    {
        var model = Demo(); var (from, until) = new LowerDisplay(model).StripRange();
        Assert.True(from <= model.Engine.Now && until >= model.Engine.Now);
        Assert.Equal(0, from.Minute); Assert.Equal(0, until.Minute);
        foreach (var s in model.Engine.Sessions.Where(s => model.CountsOn(s, model.TodayKey))) Assert.True(s.From >= from && s.To <= until);
    }

    [AvaloniaFact]
    public void EveryLowerDisplayDrawsInBothLooksAndPicturesAreWritten()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var model = Demo(theme); model.SetTallPill(true); model.ToggleAccount(model.Accounts[0].Name);
            var pill = new PillWindow(model); pill.Show(); Settle(pill);
            try
            {
                foreach (var shown in Enum.GetValues<PillLower>())
                {
                    model.SetPillLower(shown); Settle(pill);
                    var lower = Find<LowerDisplay>(pill, "PillLower"); lower.Seconds = 2.4; Settle(pill);
                    Assert.Equal(LowerDisplay.HeightFor(shown), lower.Bounds.Height, .5);
                    using var frame = pill.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    if (folder is not null) { Directory.CreateDirectory(folder); frame!.Save(Path.Combine(folder, $"tallpill-{theme}-{shown}.png")); }
                }
            }
            finally { pill.Close(); }
        }
    }
}
