using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Timer.Desktop;

/// <summary>Hours over minutes for the experimental sidebar; tracking still retains seconds.</summary>
public sealed class VerticalCompactClock : StackPanel
{
    private readonly TextBlock hours = Ui.Text("00", 28, "clock");
    private readonly TextBlock minutes = Ui.Text("00", 28, "clock");
    public VerticalCompactClock()
    {
        Name = "CompactVerticalClock"; HorizontalAlignment = HorizontalAlignment.Center;
        hours.Name = "CompactClockHours"; minutes.Name = "CompactClockMinutes";
        Control Line(TextBlock digits, string unit)
        {
            var label = Ui.Text(unit, 11, "muted"); label.VerticalAlignment = VerticalAlignment.Bottom; label.Margin = new Thickness(4, 0, 0, 5);
            var row = Ui.Horizontal(digits, label); row.Spacing = 0; row.HorizontalAlignment = HorizontalAlignment.Center; return row;
        }
        Children.Add(Line(hours, "h")); Children.Add(Line(minutes, "m"));
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
        ToolTip.SetTip(this, description); Avalonia.Automation.AutomationProperties.SetName(this, description);
    }
}
