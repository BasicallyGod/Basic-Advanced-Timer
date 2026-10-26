using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// What goes in the account drawer beside the pill and beside the dashboard's rail: a heading, the accounts with today's time and their budget bars
/// (choosing one starts or switches to it), and a Manage accounts icon at the top right. The host draws the drawer itself. The prefix (Pill or Rail) starts the names of its
/// parts; the pill's narrow drawer uses the compact rows.
/// </summary>
public sealed class AccountsPanel : Grid
{
    public ScrollViewer Scroll { get; }
    private readonly Grid heading; private readonly AccountList list; private readonly IconGlyph? pinGlyph;
    /// <summary>The button that docks the panel or floats it, when the host asked for one.</summary>
    public Button? PinButton { get; }
    /// <summary>Words the pin button for what pressing it will do, and lights it while the panel is docked.</summary>
    public void SetPinned(bool pinned)
    {
        if (PinButton is null || pinGlyph is null) return;
        var words = pinned ? "Float over the page" : "Keep beside the page";
        ToolTip.SetTip(PinButton, words); Avalonia.Automation.AutomationProperties.SetName(PinButton, words);
        pinGlyph.TintKey = pinned ? "Accent" : null;
    }
    /// <param name="pin">With a toggle, the heading carries a button that docks the panel beside the page or floats it over (the dashboard's panel only).</param>
    public AccountsPanel(TimerModel model, string prefix, bool compact, bool reorderable, Action manage, Action? pin = null)
    {
        Name = prefix + "Panel"; RowDefinitions = new RowDefinitions("Auto,*");
        // Manage accounts is one icon at the heading's right, so the list has the room the button used to take.
        heading = new Grid { Margin = new Thickness(10, 0, 0, 8), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = compact ? Ui.Text("Accounts", 15) : Ui.Heading("Accounts"); title.FontWeight = FontWeight.SemiBold; title.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(title);
        var manageButton = Ui.Button("", manage, prefix + "ManageAccounts"); manageButton.Classes.Add("rail"); manageButton.Content = new IconGlyph(IconKind.Sliders);
        manageButton.Width = manageButton.Height = compact ? 28 : 36; manageButton.CornerRadius = new CornerRadius(compact ? 10 : 12); manageButton.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(manageButton, "Manage accounts"); Avalonia.Automation.AutomationProperties.SetName(manageButton, "Manage accounts");
        Grid.SetColumn(manageButton, 1);
        if (pin is null) heading.Children.Add(manageButton);
        else
        {
            // Pin and Manage accounts sit together at the right, the pin first.
            pinGlyph = new IconGlyph(IconKind.Dock);
            PinButton = Ui.Button("", pin, prefix + "PinAccounts"); PinButton.Classes.Add("rail"); PinButton.Content = pinGlyph;
            PinButton.Width = PinButton.Height = manageButton.Width; PinButton.CornerRadius = manageButton.CornerRadius; PinButton.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(Ui.Horizontal(PinButton, manageButton));
            Grid.SetColumn(heading.Children[^1], 1);
            SetPinned(true);
        }
        Scroll = new ScrollViewer { Name = prefix + "AccountScroll", Content = list = new AccountList(model, reorderable: reorderable, bars: true, compact: compact), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(heading); var scroll = Ui.WithMoreBelow(Scroll); SetRow(scroll, 1); Children.Add(scroll);
    }

    /// <summary>
    /// Brings the panel's contents in as it opens (the dock's progress, 0 to 1): the heading and Manage accounts fade in once it is wide enough to hold them (.12 to .47),
    /// and the rows follow one after another (see <see cref="AccountList.Reveal"/>).
    /// </summary>
    public void Reveal(double progress)
    {
        heading.Opacity = progress >= 1 ? 1 : DockMotion.Window(progress, .12, .47);
        list.Reveal(progress);
    }
}
