using Avalonia.Controls;
using Avalonia.Threading;
using Timer.Desktop;

namespace Timer.Desktop.Tests;

// The sidebar draws its accounts as display rows, so tests about the card rows (the raised one-line rows with a play glyph,
// a sliding name and a budget bar) look at the pill's list, which keeps them.
static class CardRows
{
    public static Window Window(TimerModel model, double height = 700)
    {
        var card = new PillWindow(model).MakeDropdownCard();
        var host = new Window { Width = 320, Height = height, Content = card }; host.Show(); Dispatcher.UIThread.RunJobs();
        return host;
    }
}
