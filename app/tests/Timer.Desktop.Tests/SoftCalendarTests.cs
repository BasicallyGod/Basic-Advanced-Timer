using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The soft calendar uses quieter weekends and highlights the chosen day.</summary>
public class SoftCalendarTests
{
    [AvaloniaFact]
    public void WeekendsAreQuietAndTheChosenDayUsesItsWash()
    {
        var picker = new SoftDatePicker();
        var window = new Window { Width = 320, Height = 420, Content = picker }; window.Show();
        try
        {
            picker.View(new DateTime(2026, 10, 14), new DateTime(2026, 10, 15)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Button Cell(string ymd) => picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DateCell_" + ymd);
            Assert.Contains("weekendDay", Cell("20261017").Classes); Assert.Contains("weekendDay", Cell("20261018").Classes); Assert.DoesNotContain("weekendDay", Cell("20261014").Classes);
            Assert.Contains("chosenDay", Cell("20261015").Classes);
            Assert.DoesNotContain("chosenDay", Cell("20261014").Classes);
            Assert.Equal("15", Assert.IsType<TextBlock>(Cell("20261015").Content).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheMeetingDayCardsDateAndSetShareOneLine()
    {
        var card = new ReminderPopup(() => new DateTime(2026, 11, 7, 12, 0, 0), "New meeting", new DateTime(2026, 11, 18), _ => { }, () => { }, "Meeting day") { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 320, Height = 440, Content = card }; window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            double Middle(Visual c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
            var summary = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ReminderSummary"); var set = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReminderSet");
            Assert.Equal(Middle(set), Middle(summary), 1.5);
        }
        finally { window.Close(); }
    }
}
