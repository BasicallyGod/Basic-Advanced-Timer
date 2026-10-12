using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Timer.Core;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class HoverTests
{
    [AvaloniaFact]
    public void IconMotionFollowsItsButtonEvenWhenNestedAndStopsWhenDisabled()
    {
        var glyph = new IconGlyph(IconKind.Gear) { Transitions = null };
        var button = new Button { Content = new StackPanel { Children = { glyph } } };
        var window = new Window { Content = button, Width = 100, Height = 100 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            button.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, glyph.Hover);
            button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, glyph.Hover);
            button.IsEnabled = true; button.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, glyph.Hover);
            button.Content = null; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, glyph.Hover);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PillAndNavigationIconsChangeOnHoverAndReturnToTheSameRestingDrawing()
    {
        foreach (var kind in new[] { IconKind.Minimise, IconKind.Gear, IconKind.Timeline, IconKind.Week, IconKind.Undo, IconKind.Previous, IconKind.Next })
        {
            var glyph = new IconGlyph(kind) { Transitions = null };
            var window = new Window { Content = glyph, Width = 72, Height = 72 }; window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var resting = Pixels(glyph);
                glyph.Hover = 1; Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(resting, Pixels(glyph));
                glyph.Hover = 0; Dispatcher.UIThread.RunJobs();
                Assert.Equal(resting, Pixels(glyph));
            }
            finally { window.Close(); }
        }
    }

    private static byte[] Pixels(Control control)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(72, 72));
        bitmap.Render(control);
        using var stream = new MemoryStream(); bitmap.Save(stream);
        return stream.ToArray();
    }

    [AvaloniaFact]
    public void AnAccountButtonLooksDifferentUnderThePointer()
    {
        var model = TimerModel.CreateDemo(); var account = model.Accounts.First(a => !BreakAccount.Is(a.Name));
        var indicator = new AccountIndicator(account, model) { Transitions = null };
        var window = new Window { Content = indicator, Width = 80, Height = 80 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var resting = Pixels(indicator); indicator.Glow = 1; Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(resting, Pixels(indicator));
            indicator.Glow = 0; Dispatcher.UIThread.RunJobs();
            Assert.Equal(resting, Pixels(indicator));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryAccountRowAnswersThePointerWithAnEasedGlow()
    {
        var model = TimerModel.CreateDemo(); var list = new AccountList(model);
        var window = new Window { Content = list, Width = 400, Height = 600 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var row = list.Children.OfType<Button>().First(); var icon = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(row).OfType<AccountIndicator>().Single();
            Assert.NotNull(icon.Transitions); Assert.Contains(icon.Transitions!, t => t is Avalonia.Animation.DoubleTransition d && d.Property == AccountIndicator.GlowProperty);
        }
        finally { window.Close(); }
    }
}
