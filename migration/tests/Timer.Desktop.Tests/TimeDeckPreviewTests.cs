using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

// Renders the dashboard in Time Deck light and in two candidate dark versions, for looking at. Nothing is asserted beyond the windows drawing.
public sealed class TimeDeckPreviewTests
{
    private static ThemeDefinition Dark(string name, params (string Token, string Value)[] over)
    {
        var colours = new Dictionary<string, string>(ThemeLibrary.TimeDeck.Colours);
        foreach (var (token, value) in over) colours[token] = value;
        return new ThemeDefinition(name, true, colours, false, ThemeBlockStyle.Tinted, .38, 0);
    }
    private static readonly ThemeDefinition Warm = Dark("Time Deck Warm Dark", ("Surface", "#17140F"), ("Panel", "#201C16"), ("Ink", "#F4EFE8"), ("Muted", "#A59C8F"), ("Line", "#37312A"),
        ("Hover", "#2B261F"), ("Selected", "#3B2B19"), ("Live", "#FF9A3C"), ("Overtime", "#FF7A8A"), ("BudgetWarning", "#F2B84B"), ("Break", "#FF9A3C"), ("Finish", "#FF6B6B"),
        ("Accent", "#FF8A1F"), ("AccentInk", "#1B1209"), ("Clock", "#F4EFE8"), ("ShadowColor", "#B3000000"));
    private static readonly ThemeDefinition Cool = Dark("Time Deck Cool Dark", ("Surface", "#14161A"), ("Panel", "#1C1F24"), ("Ink", "#EEF0F3"), ("Muted", "#9CA3AD"), ("Line", "#2E333A"),
        ("Hover", "#262A31"), ("Selected", "#33281D"), ("Live", "#FF9A3C"), ("Overtime", "#FF7A8A"), ("BudgetWarning", "#F2B84B"), ("Break", "#FF9A3C"), ("Finish", "#FF6B6B"),
        ("Accent", "#FF8A1F"), ("AccentInk", "#1B1209"), ("Clock", "#EEF0F3"), ("ShadowColor", "#B3000000"));

    [AvaloniaTheory]
    [InlineData("Time Deck", "timedeck-light")]
    [InlineData("Time Deck Warm Dark", "timedeck-dark-warm")]
    [InlineData("Time Deck Cool Dark", "timedeck-dark-cool")]
    public void TheDashboardDrawsInEachTimeDeckTheme(string theme, string file)
    {
        var model = TimerModel.CreateDemo();
        if (theme.Contains("Warm")) model.SaveTheme(Warm); else if (theme.Contains("Cool")) model.SaveTheme(Cool);
        model.SetTheme(theme);
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Assert.Equal(theme, model.CurrentTheme.Name);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var shot = window.CaptureRenderedFrame(); shot!.Save(Path.Combine(folder, file + ".png")); }
        }
        finally { window.Close(); if (theme.Contains("Dark")) model.DeleteTheme(theme); model.SetTheme("System"); }
    }
}
