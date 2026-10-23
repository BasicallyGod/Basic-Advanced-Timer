using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// What goes in the account drawer beside the pill and beside the dashboard's rail: a heading, the accounts with today's time and their budget bars
/// (choosing one starts or switches to it), and Manage accounts. The host draws the drawer itself. The prefix (Pill or Rail) starts the names of its
/// parts; the pill's narrow drawer uses the compact rows.
/// </summary>
public sealed class AccountsPanel : Grid
{
    public ScrollViewer Scroll { get; }
    public AccountsPanel(TimerModel model, string prefix, bool compact, bool reorderable, Action manage)
    {
        Name = prefix + "Panel"; RowDefinitions = new RowDefinitions("Auto,*,Auto");
        var heading = Ui.Grid("*,Auto"); heading.Margin = new Thickness(4, 0, 4, 8);
        var title = compact ? Ui.Text("Accounts", 15) : Ui.Heading("Accounts"); title.FontWeight = FontWeight.SemiBold; Ui.Put(heading, title);
        var todayHead = Ui.Text("Today", 12, "muted"); todayHead.Name = "TodayHeading"; todayHead.VerticalAlignment = VerticalAlignment.Center; Ui.Put(heading, todayHead, 1);
        Scroll = new ScrollViewer { Name = prefix + "AccountScroll", Content = new AccountList(model, reorderable: reorderable, bars: true, compact: compact), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var button = Ui.DialogButton("Manage accounts", manage, prefix + "ManageAccounts", false);
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(heading); var scroll = Ui.WithMoreBelow(Scroll); SetRow(scroll, 1); Children.Add(scroll); SetRow(button, 2); Children.Add(button);
    }
}
