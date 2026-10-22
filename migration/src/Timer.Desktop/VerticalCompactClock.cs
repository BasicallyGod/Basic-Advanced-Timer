using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>Hours over minutes for the experimental sidebar; tracking still retains seconds.</summary>
public sealed class VerticalCompactClock : StackPanel
{
    private readonly TextBlock hours = Ui.Text("00", 28, "clock");
    private readonly TextBlock minutes = Ui.Text("00", 28, "clock");
    private readonly TextBlock? seconds;
    public VerticalCompactClock(bool showSeconds = false, double digitSize = 28, string prefix = "Compact")
    {
        Name = prefix + "VerticalClock"; Width = 64; HorizontalAlignment = HorizontalAlignment.Center;
        hours.Name = prefix + "ClockHours"; minutes.Name = prefix + "ClockMinutes";
        foreach (var digit in new[] { hours, minutes })
        {
            digit.FontSize = digitSize; digit.Width = 64; digit.TextAlignment = TextAlignment.Center;
            digit.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") };
        }
        Children.Add(hours); Children.Add(minutes);
        if (showSeconds)
        {
            seconds = Ui.Text("00", 13, "muted"); seconds.Name = prefix + "ClockSeconds";
            seconds.Width = 64; seconds.TextAlignment = TextAlignment.Center;
            seconds.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") }; Children.Add(seconds);
        }
    }
    public void Set(TimeSpan elapsed, bool onBreak, bool late, bool live)
    {
        hours.Text = ((int)elapsed.TotalHours).ToString("00", CultureInfo.InvariantCulture);
        minutes.Text = elapsed.Minutes.ToString("00", CultureInfo.InvariantCulture);
        foreach (var digit in new[] { hours, minutes })
        {
            digit.Classes.Set("break", onBreak); digit.Classes.Set("overtime", late); digit.Classes.Set("live", live);
            digit.Classes.Set("clockIdle", !onBreak && !late && !live);
        }
        if (seconds is not null) seconds.Text = elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
        var description = $"Working time: {(int)elapsed.TotalHours} hours, {elapsed.Minutes} minutes";
        Avalonia.Automation.AutomationProperties.SetName(this, description + (seconds is null ? "" : $", {elapsed.Seconds} seconds"));
    }
}
