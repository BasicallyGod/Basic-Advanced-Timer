using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The dashboard's sidebar, the pill drawn out with its accounts shown: one centred column with the two lights, the day clock (the way back to the pill) and a tracking
/// card (pause or play, the account and how long it has run, the break), then the accounts as one line rows with their budget bars (pressing one starts it), and at the foot
/// Finish day and Close timer as two buttons side by side. It is 248 wide, and the dashboard grows by that much so the page keeps all the room it had.
/// </summary>
public sealed class Sidebar : Border
{
    /// <summary>The sidebar's width, which the dashboard window grows by.</summary>
    public const double Width = 248;
    public StatusFace Face { get; }
    public Button QuitButton { get; }
    public Button ManageButton { get; }
    public ScrollViewer AccountScroll { get; }

    public Sidebar(TimerModel model, Action clockClick, Action circleClick, Action manage, Action quit)
    {
        Name = "Sidebar"; base.Width = Width; BorderThickness = new Thickness(0, 0, 1, 0); Padding = new Thickness(12, 14, 12, 14);
        Classes.Add("panel"); this.Bind(BorderBrushProperty, this.GetResourceObservable("Line"));
        // The lights and the day clock are the pill's own, and the card is what the pill's play tile, account chip and break become when there is room for all three.
        Face = new StatusFace(model, "Sidebar", "DashboardBreak", "Collapse to pill", clockClick, circleClick, () => { }, sidebar: true);
        var grid = Ui.Grid("*", "Auto,Auto,*,Auto"); grid.RowSpacing = 4;
        Ui.Put(grid, Face);
        // The heading is a small quiet label with Manage accounts at its right, as the account panel's was.
        var heading = Ui.Grid("*,Auto"); heading.Margin = new Thickness(8, 6, 0, 2);
        var label = new TextBlock { Text = "ACCOUNTS", FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5, VerticalAlignment = VerticalAlignment.Center, Name = "SidebarAccountsHeading" }; label.Classes.Add("muted");
        Ui.Put(heading, label);
        ManageButton = Ui.Button("", manage, "SidebarManageAccounts"); ManageButton.Classes.Add("rail"); ManageButton.Content = new IconGlyph(IconKind.Sliders);
        ManageButton.Width = ManageButton.Height = 30; ManageButton.CornerRadius = new CornerRadius(10);
        ToolTip.SetTip(ManageButton, "Manage accounts"); Avalonia.Automation.AutomationProperties.SetName(ManageButton, "Manage accounts");
        Ui.Put(heading, ManageButton, 1);
        Ui.Put(grid, heading, 0, 1);
        AccountScroll = new ScrollViewer { Name = "SidebarAccountScroll", Content = new AccountList(model, reorderable: true, bars: true), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Ui.Put(grid, Ui.WithMoreBelow(AccountScroll), 0, 2);
        // The foot: Close timer closes it for good and goes red under the pointer. There is no Start day or Finish day: the day begins with its first block.
        var quitGlyph = new IconGlyph(IconKind.Close);
        QuitButton = new Button { Name = "QuitTimer", Content = Ui.Horizontal(quitGlyph, new TextBlock { Text = "Close timer", VerticalAlignment = VerticalAlignment.Center }) }; ((StackPanel)QuitButton.Content).Spacing = 7;
        QuitButton.Classes.Add("ctl"); QuitButton.Classes.Add("quit"); QuitButton.Height = 38; QuitButton.Padding = new Thickness(4, 0); QuitButton.FontSize = 13; QuitButton.HorizontalAlignment = HorizontalAlignment.Stretch; QuitButton.Click += (_, _) => quit();
        QuitButton.PointerEntered += (_, _) => quitGlyph.TintKey = "Finish"; QuitButton.PointerExited += (_, _) => quitGlyph.TintKey = null;
        Avalonia.Automation.AutomationProperties.SetName(QuitButton, "Close timer");
        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) }; foot.Children.Add(QuitButton);
        var rule = Ui.Rule(); rule.Margin = new Thickness(4, 6, 4, 0);
        var footColumn = new StackPanel { Children = { rule, foot } };
        Ui.Put(grid, footColumn, 0, 3);
        Child = grid;
        Refresh(model);
    }

    public void Refresh(TimerModel model) => Face.Refresh();
}
