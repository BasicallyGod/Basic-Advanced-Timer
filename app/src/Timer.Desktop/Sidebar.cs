using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The dashboard's sidebar, the pill drawn out with its accounts shown: one centred column with the two lights, the day clock (the way back to the pill) and a tracking
/// card (pause or play, the account and how long it has run, the break), then the accounts as one line rows with their budget bars (pressing one starts it), and at the foot
/// Finish day and Quit timer as two buttons side by side. It is 248 wide, and the dashboard grows by that much so the page keeps all the room it had.
/// </summary>
public sealed class Sidebar : Border
{
    /// <summary>The sidebar's width, which the dashboard window grows by.</summary>
    public const double Width = 248;
    public StatusFace Face { get; }
    public Button QuitButton { get; }
    public ScrollViewer AccountScroll { get; }

    public Sidebar(TimerModel model, Action clockClick, Action circleClick, Action manage, Action quit, Action? alertsClick = null)
    {
        Name = "Sidebar"; base.Width = Width; BorderThickness = new Thickness(0); Padding = new Thickness(12, 14, 12, 14);
        Classes.Add("panel"); this.Bind(BorderBrushProperty, this.GetResourceObservable("Line"));
        // The lights and the day clock are the pill's own, and the card is what the pill's play tile, account chip and break become when there is room for all three.
        Face = new StatusFace(model, "Sidebar", "DashboardBreak", "Collapse to pill", clockClick, circleClick, () => { }, sidebar: true, alertsClick: alertsClick);
        var grid = Ui.Grid("*", "Auto,Auto,*,Auto"); grid.RowSpacing = 4;
        // At Christmas the wreath and lights that dressed the pill carry on round the clock display, smaller, so coming out of the pill does not lose them.
        garland = new ChristmasDecor(ChristmasDecor.Layers.Garland) { Name = "SidebarGarland", Frame = DisplayFrame, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        lights = new ChristmasDecor(ChristmasDecor.Layers.Lights) { Name = "SidebarLights", Frame = DisplayFrame, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var faceHost = new Grid(); faceHost.Children.Add(garland); faceHost.Children.Add(Face); faceHost.Children.Add(lights);
        Face.Display.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) PlaceDecor(); };
        Ui.Put(grid, faceHost);
        // Account management stays in Settings; the heading is deliberately quiet.
        var heading = Ui.Grid("*"); heading.Margin = new Thickness(8, 6, 0, 2);
        var label = new TextBlock { Text = "ACCOUNTS", FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5, VerticalAlignment = VerticalAlignment.Center, Name = "SidebarAccountsHeading" }; label.Classes.Add("muted");
        Ui.Put(heading, label);
        Ui.Put(grid, heading, 0, 1);
        // The accounts are one display window under the clock (the board "Display polish", sidebar option B): the pill's display carried down, its rows divided by
        // faint rules. The window wears the display's own well, shade and lip, so it follows the theme like the clock above it, and the list scrolls inside it.
        // The window runs the full height down to Quit timer. The list scrolls with no scroll bar; a column of cells at the right edge (the week display's indicator)
        // shows where it is, and that column is always kept, so the rows never change width when the list starts to scroll.
        AccountScroll = new ScrollViewer { Name = "SidebarAccountScroll", Content = new AccountList(model, reorderable: true, display: true), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        var indicator = new ScrollCells(AccountScroll) { Name = "SidebarAccountIndicator", Width = ScrollCells.ColumnWidth, Margin = new Thickness(0, 6, 6, 6) };
        var inside = Ui.Grid("*,Auto", "*"); Ui.Put(inside, AccountScroll, 0, 0); Ui.Put(inside, indicator, 1, 0);
        var window = new Border { Name = "SidebarAccountDisplay", CornerRadius = new CornerRadius(14), ClipToBounds = true, Padding = new Thickness(0, 4), VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 0) }; window.Child = PaperLayer.Under(window, inside);
        window.Bind(Border.BackgroundProperty, window.GetResourceObservable("DisplayWell"));
        void Shade() => window.BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 2, Blur = 5, Color = Ui.ThemeColour(window, "DisplayShade", Color.Parse("#A0000000")) }, new[] { new BoxShadow { OffsetY = 1, Color = Ui.ThemeColour(window, "DisplayEdge", Color.Parse("#12FFFFFF")) } });
        window.ActualThemeVariantChanged += (_, _) => Shade(); window.AttachedToVisualTree += (_, _) => Shade();
        Ui.Put(grid, window, 0, 2);
        // The foot: Quit timer closes it for good and goes red under the pointer. There is no Start day or Finish day: the day begins with its first block.
        var quitGlyph = new IconGlyph(IconKind.Close);
        QuitButton = new Button { Name = "QuitTimer", Content = Ui.Horizontal(quitGlyph, new TextBlock { Text = "Quit timer", VerticalAlignment = VerticalAlignment.Center }) }; ((StackPanel)QuitButton.Content).Spacing = 7;
        QuitButton.Classes.Add("ctl"); QuitButton.Classes.Add("quit"); QuitButton.Height = 38; QuitButton.Padding = new Thickness(4, 0); QuitButton.FontSize = 13; QuitButton.HorizontalAlignment = HorizontalAlignment.Stretch; QuitButton.Click += (_, _) => quit();
        QuitButton.PointerEntered += (_, _) => quitGlyph.TintKey = "Finish"; QuitButton.PointerExited += (_, _) => quitGlyph.TintKey = null;
        Avalonia.Automation.AutomationProperties.SetName(QuitButton, "Quit timer");
        var drafts = Ui.Ctl("ReviewDrafts", "Drafts", null, "Review drafts", model.ReviewDrafts, small: true);
        void DraftCount() { var count = model.Drafts.Outstanding.Count + model.CreationDrafts.Outstanding.Count; drafts.IsVisible = count > 0; drafts.Content = $"Drafts · {count}"; }
        AttachedToVisualTree += (_, _) => { model.Drafts.Changed += DraftCount; model.CreationDrafts.Changed += DraftCount; DraftCount(); };
        DetachedFromVisualTree += (_, _) => { model.Drafts.Changed -= DraftCount; model.CreationDrafts.Changed -= DraftCount; };
        DraftCount();
        var foot = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Spacing = 8 }; foot.Children.Add(drafts); foot.Children.Add(QuitButton);
        Ui.Put(grid, foot, 0, 3);
        Child = grid;
        ActualThemeVariantChanged += (_, _) => Refresh(model);
        Refresh(model);
    }

    /// <summary>The wreath round the clock display: a small card (corner 14) with 16 pixels of room, drawn at 60 per cent of the pill's size.</summary>
    public static readonly DecorFrame DisplayFrame = new(11, 14, 0.6); // 11 pixels of room, so the wreath stays inside the column's 12 pixel margin and never reaches the window's edge
    private readonly ChristmasDecor garland = null!, lights = null!;
    /// <summary>The wreath and lights on the display, so tests can read them.</summary>
    public ChristmasDecor Garland => garland; public ChristmasDecor Lights => lights;
    // The decoration is as big as the display with its room round it, set where the display is.
    private void PlaceDecor()
    {
        var b = Face.Display.Bounds; if (b.Width < 1 || b.Height < 1) return;
        foreach (var d in new[] { garland, lights }) { d.Margin = new Thickness(b.X - DisplayFrame.Room, b.Y - DisplayFrame.Room, 0, 0); d.Width = b.Width + 2 * DisplayFrame.Room; d.Height = b.Height + 2 * DisplayFrame.Room; }
    }

    public void Refresh(TimerModel model)
    {
        Face.Refresh();
        var (key, _) = StatusFace.State(model);
        var dressed = model.ActiveSeason?.Id == Seasons.Christmas.Id; var state = key == "Break" ? DecorState.Break : key == "Live" ? DecorState.Recording : DecorState.Idle;
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        garland.Configure(dressed, state, dark); lights.Configure(dressed, state, dark);
    }
}
