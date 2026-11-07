using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The soft calendar: weekends quieter, a dot on a day that holds time, and no lamp under the chosen day.</summary>
public class SoftCalendarTests
{
    [AvaloniaFact]
    public void WeekendsAreQuietAndDaysWithTimeCarryADotButTheChosenDayHasNoLamp()
    {
        var picker = new SoftDatePicker { DayHasTime = day => day == new DateTime(2026, 10, 14) };
        var window = new Window { Width = 320, Height = 420, Content = picker }; window.Show();
        try
        {
            picker.View(new DateTime(2026, 10, 14), new DateTime(2026, 10, 15)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Button Cell(string ymd) => picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DateCell_" + ymd);
            Assert.Contains("weekendDay", Cell("20261017").Classes); Assert.Contains("weekendDay", Cell("20261018").Classes); Assert.DoesNotContain("weekendDay", Cell("20261014").Classes);
            Assert.Single(picker.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), e => e.Name == "DayTimeDot"); // Only the 14th holds time.
            Assert.NotNull(Cell("20261014").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().SingleOrDefault(e => e.Name == "DayTimeDot"));
            Assert.DoesNotContain(picker.GetVisualDescendants().OfType<Border>(), b => b.Name == "DayLamp"); // The chosen day is shown by its wash alone.
        }
        finally { window.Close(); }
    }
}
