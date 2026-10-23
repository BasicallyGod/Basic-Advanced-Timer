using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The account list card that opens beside the pill and beside the dashboard's rail: a heading, the accounts with today's time and their budget bars
/// (choosing one starts or switches to it), and Manage accounts. The prefix (Pill or Rail) starts the names of its parts.
/// </summary>
public sealed class AccountsPanel : Border
{
    public ScrollViewer Scroll { get; }
    public AccountsPanel(TimerModel model, string prefix, bool reorderable, Action manage)
    {
        Name = prefix + "Panel"; Width = 320; Padding = new Thickness(8, 12, 8, 12); CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1);
        Classes.Add("surface"); Classes.Add("flyoutCard");
        var heading = Ui.Grid("*,Auto"); heading.Margin = new Thickness(6, 0, 6, 6);
        var title = Ui.Text("Accounts", 15); title.FontWeight = FontWeight.SemiBold; Ui.Put(heading, title);
        var todayHead = Ui.Text("Today", 12, "muted"); todayHead.Name = "TodayHeading"; Ui.Put(heading, todayHead, 1);
        Scroll = new ScrollViewer { Name = prefix + "AccountScroll", Content = new AccountList(model, reorderable: reorderable, bars: true), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var button = Ui.DialogButton("Manage accounts", manage, prefix + "ManageAccounts", false);
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.Margin = new Thickness(6, 8, 6, 0);
        var list = new StackPanel { Spacing = 2 }; list.Children.Add(heading); list.Children.Add(Ui.WithMoreBelow(Scroll)); list.Children.Add(button);
        Child = list;
    }
}
