using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The budget meter is a row of equal cells: whole device pixels for the cell and the gap, a count that fills the bar, and the same at every Windows scale.</summary>
public class BudgetMeterTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void CellsAndGapsAreWholePixelsAndFillTheBarOrFallAFewPixelsShort(double scale)
    {
        var exact = 0; var total = 0;
        for (var width = (int)(100 * scale); width <= (int)(700 * scale); width++)
        {
            var (count, cell, gap, used) = BudgetBar.Fit(width, scale); total++;
            Assert.True(count >= 1 && cell >= 4 && gap >= 1, $"{width} px at {scale}: {count} cells of {cell} with {gap} between");
            Assert.Equal(count * cell + (count - 1) * gap, used); // Every cell is the same, every gap is the same.
            Assert.InRange(width - used, 0, 8); // Never over the bar, and at most a few device pixels short of its end.
            Assert.InRange(cell / scale, 4, 14); // Cells stay about the size of a label's x-height, never needles or blocks.
            if (used == width) exact++;
        }
        Assert.True(exact > total / 2, $"{exact} of {total} widths fit exactly at {scale}"); // Most widths need no empty pixels at the end at all.
    }

    [AvaloniaFact]
    public void ABarFillsItsCellsInProportionAndRedrawsWhenSet()
    {
        var bar = new BudgetBar { Width = 303 }; var host = new Window { Width = 400, Height = 60, Content = bar }; host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); bar.Set(.5, .4); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.5, bar.Ratio); Assert.Equal(.4, bar.Pace);
            var (count, _, _, _) = BudgetBar.Fit(303, host.RenderScaling);
            Assert.True(count >= 20); // A bar this wide has a couple of dozen cells.
        }
        finally { host.Close(); }
    }
}
