using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// What goes in the account drawer beside the pill: a heading, the accounts with today's time and their budget bars
/// (choosing one starts or switches to it), and a Manage accounts icon at the top right. The host draws the drawer itself. The prefix (Pill or Rail) starts the names of its
/// parts; the pill's narrow drawer uses the compact rows.
/// </summary>
public sealed class AccountsPanel : Grid
{
    public ScrollViewer Scroll { get; }
    private readonly Grid heading; private readonly AccountList list;
    public AccountsPanel(TimerModel model, string prefix, bool compact, bool reorderable, Action manage)
    {
        Name = prefix + "Panel"; RowDefinitions = new RowDefinitions("Auto,*");
        // Managing accounts lives in Settings and the right-click menu; the list is only the list.
        heading = new Grid { Margin = new Thickness(10, 0, 0, 8) };
        var title = compact ? Ui.Text("Accounts", 15) : Ui.Heading("Accounts"); title.FontWeight = FontWeight.SemiBold; title.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(title);
        Scroll = new ScrollViewer { Name = prefix + "AccountScroll", Content = list = new AccountList(model, reorderable: reorderable, bars: true, compact: compact), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(heading); SetRow(Scroll, 1); Children.Add(Scroll);
    }
}
