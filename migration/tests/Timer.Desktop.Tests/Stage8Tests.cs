using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// Stage 8: the finish. Text colours that hold their contrast on every ground they sit on, a name or words on everything that can be pressed, and less motion when asked for.
public sealed class Stage8Tests
{
    public static IEnumerable<object[]> Themes => new[] { new object[] { "Light" }, new object[] { "Dark" } };
    private static ThemeDefinition Theme(string name) => name == "Light" ? AppThemes.Light : AppThemes.Dark;

    [Theory]
    [MemberData(nameof(Themes))]
    public void TextColoursHoldTheirContrastOnEveryGroundTheyAreSetOn(string name)
    {
        var t = Theme(name); double C(string a, string b) => ThemeColours.Contrast(t.Colour(a), t.Colour(b));
        foreach (var ground in new[] { "Surface", "Panel", "Hover", "Selected" })
        {
            Assert.True(C("Ink", ground) >= 7, $"{name} Ink on {ground}");
            Assert.True(C("Muted", ground) >= 4.5, $"{name} Muted on {ground}"); // Quiet text, still readable at 12 pixels.
        }
        foreach (var ground in new[] { "Surface", "Panel" })
        {
            Assert.True(C("Accent", ground) >= 4.5, $"{name} Accent text on {ground}");
            Assert.True(C("Overtime", ground) >= 4.5, $"{name} Overtime text on {ground}");
            Assert.True(C("Live", ground) >= 3, $"{name} Live on {ground}"); // Dots and glyphs.
            Assert.True(C("Break", ground) >= 3 || name == "Light" && C("Break", ground) >= 2.7, $"{name} Break on {ground}");
        }
        Assert.True(C("Overtime", "Hover") >= 4.5, $"{name} Overtime on Hover");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TheShadesMadeForSmallTextHoldFourAndAHalfToOneOnTheirGrounds(string name)
    {
        var t = Theme(name); var grounds = new[] { "Panel", "Surface", "Hover" }.Select(t.Colour).ToList();
        var amber = ThemeEngine.TextShade(t.Colour("BudgetWarning"), t.Colour("Ink"), grounds);
        Assert.All(grounds, g => Assert.True(ThemeColours.Contrast(amber, g) >= 4.5, $"{name} budget warning words on {g}"));
        var accent = ThemeEngine.TextShade(t.Colour("Accent"), t.Colour("Ink"), grounds.Append(t.Colour("Selected")));
        Assert.All(grounds.Append(t.Colour("Selected")), g => Assert.True(ThemeColours.Contrast(accent, g) >= 4.5, $"{name} accent words on {g}"));
        if (name == "Dark") Assert.Equal(t.Colour("BudgetWarning"), amber); // Already readable: left alone.
    }

    private static bool HasName(Button b)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))) return true;
        // The logical tree, because a button in a page that has not been shown yet has no visuals but does have its content.
        return Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(b).OfType<TextBlock>().Any(t => !string.IsNullOrWhiteSpace(t.Text)) || b.Content is string { Length: > 0 };
    }
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Press(Window window, string name) { window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }

    [AvaloniaFact]
    public void EveryButtonOnEveryPageHasWordsOrAnAccessibleName()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 15, 0, 0)); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var missing = new List<string>();
            // The spinners' own template parts (PART_...) are not ours.
            void Scan(string where) { foreach (var b in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b.Name?.StartsWith("PART_") != true)) if (!HasName(b)) missing.Add($"{where}: {b.Name ?? b.GetType().Name} ({b.Classes.FirstOrDefault()})"); }
            Scan("timeline");
            model.SelectBlock(new[] { model.Engine.Sessions[1] }, false); Settle(window); Scan("timeline with a block selected");
            Press(window, "AddMeeting"); Scan("meeting form"); Press(window, "MeetingDay");
            window.SelectTab(true); Settle(window); Scan("week");
            window.ShowNotes(); Settle(window); Scan("notes page");
            Press(window, "Notifications"); Scan("bell"); Press(window, "Notifications");
            Press(window, "Settings"); Scan("settings");
            Assert.True(missing.Count == 0, "Buttons with no words and no accessible name: " + string.Join("; ", missing.Distinct()));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryButtonOnThePillAndItsDrawerHasWordsOrAnAccessibleName()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 15, 0, 0)); model.CompleteSetup();
        var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); pill.ShowPanel(); Dispatcher.UIThread.RunJobs();
        try
        {
            var missing = pill.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && !HasName(b)).Select(b => b.Name ?? b.GetType().Name).ToList();
            Assert.True(missing.Count == 0, "Buttons with no words and no accessible name: " + string.Join("; ", missing));
            foreach (var light in new[] { "PillStateLight", "PillReminderLight" }) Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(pill.GetVisualDescendants().OfType<Border>().Single(b => b.Name == light))), light);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void WhenLessMotionIsAskedForNothingPulsesSteamsWavesOrEases()
    {
        Motion.Override = true;
        try
        {
            var light = new Border(); Ui.Pulse(light, true); Assert.False(Ui.IsPulsing(light)); Assert.Equal(1, light.Opacity); // The reminder light just stays on.
            var cup = new BreakGlyph(); cup.SetSteaming(true); Assert.False(cup.Steaming); Assert.Equal(1, cup.Flow); // The steam is shown, still.
            var flag = new IconGlyph(IconKind.Flag); flag.Waving = true; Assert.False(flag.Waving);
            Assert.Equal(TimeSpan.Zero, Motion.Ease(350)); Assert.Equal(TimeSpan.Zero, IconGlyph.HoverDuration);
        }
        finally { Motion.Override = null; }
        Motion.Override = false;
        try { Assert.Equal(TimeSpan.FromMilliseconds(200), IconGlyph.HoverDuration); } finally { Motion.Override = null; }
    }
}
