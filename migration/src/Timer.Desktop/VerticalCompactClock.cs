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
    public VerticalCompactClock()
    {
        Name = "CompactVerticalClock"; Width = 64; HorizontalAlignment = HorizontalAlignment.Center;
        hours.Name = "CompactClockHours"; minutes.Name = "CompactClockMinutes";
        foreach (var digit in new[] { hours, minutes })
        {
            digit.Width = 64; digit.TextAlignment = TextAlignment.Center;
            digit.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") };
        }
        Children.Add(hours); Children.Add(minutes);
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
        var description = $"Working time: {(int)elapsed.TotalHours} hours, {elapsed.Minutes} minutes";
        Avalonia.Automation.AutomationProperties.SetName(this, description);
    }
}
