using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The note, refused-edit explanation or failed-save line, and the offer to continue an earlier block. It floats over the lowest part of the
/// workspace, so it never takes room from the layout. It is inverted, so nothing under it can look like it: a dark card on Light and a light one on Dark.
/// A round medallion in a state colour says what kind it is (a tick in green for something done, a mark in amber for a refusal, in red for a failed save),
/// and a line along its foot runs down over the time it stays. It fades after a while, or goes with the cross.
/// </summary>
public sealed class MessageBar : Border
{
    private readonly TimerModel model;
    private readonly TextBlock text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.SemiBold, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center, Name = "MessageText" };
    // What follows the first few words, after a dot: quieter, and only when it helps.
    private readonly TextBlock detail = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center, Name = "MessageDetail" };
    private readonly Button offer, undo;
    private readonly Border icon = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Name = "MessageIcon" };
    // The line along the foot: its filled part (the column "left") shrinks as the message's 15 seconds run out; a failed save, which stays, keeps it full.
    private readonly ColumnDefinition left = new(1, GridUnitType.Star), right = new(0, GridUnitType.Star);
    private readonly Border countdownFill = new() { CornerRadius = new CornerRadius(1), Name = "MessageCountdown" };
    private readonly Grid countdown = new() { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, -9), IsHitTestVisible = false, Name = "MessageCountdownTrack" };
    private readonly Avalonia.Threading.DispatcherTimer countdownTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? shownLine; private DateTime shownAt;
    private readonly Avalonia.Controls.Shapes.Path tick = new() { Data = Geometry.Parse("M 8,14.5 L 12.5,19 L 20,9.5"), StrokeThickness = 2.4, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    private readonly Avalonia.Controls.Shapes.Path mark = new() { Data = Geometry.Parse("M 14,9 L 14,15 M 14,19 L 14,19.01"), StrokeThickness = 2.4, StrokeLineCap = PenLineCap.Round };
    private string? dismissedError, dismissedOffer;
    public MessageBar(TimerModel model)
    {
        this.model = model; Name = "MessageBar";
        // A failed save stays until a save works, so the cross hides it only until it changes.
        offer = Ui.Ctl("ContinuePrevious", "", null, "Continue earlier block", () => model.ContinuePrevious(), small: true);
        offer.Padding = new Thickness(12, 0);
        undo = Ui.Ctl("UndoMessage", "Undo", null, "Undo the last change", () => model.UndoEdit(), small: true, key: "Ctrl Z"); undo.Padding = new Thickness(12, 0);
        var close = Ui.IconButton(IconKind.Close, "Dismiss", "DismissMessage", () => { dismissedError = model.LastError; dismissedOffer = model.ContinuePreviousText; model.ExpireMessages(); Sync(); });
        if (close.Content is IconGlyph closeGlyph) closeGlyph.TintKey = "Surface"; // drawn in the page colour, which is the opposite of the card's
        var glyphs = new Grid { Width = 28, Height = 28 }; glyphs.Children.Add(tick); glyphs.Children.Add(mark); icon.Child = new Viewbox { Child = glyphs };
        detail.Classes.Add("muted");
        // The mark is drawn in the card's own colour (the ink) so it reads on the bright medallion in either theme.
        tick.Bind(Shape.StrokeProperty, tick.GetResourceObservable("Ink")); mark.Bind(Shape.StrokeProperty, mark.GetResourceObservable("Ink"));
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(text); words.Children.Add(detail);
        var row = Ui.Horizontal(icon, words, offer, undo, close); row.Spacing = 11;
        countdown.ColumnDefinitions.Add(left); countdown.ColumnDefinitions.Add(right); Grid.SetColumn(countdownFill, 0); countdown.Children.Add(countdownFill);
        countdown.Background = null; countdownFill.Background = Brushes.Gray;
        var holder = new Grid(); holder.Children.Add(row); holder.Children.Add(countdown);
        Child = holder; CornerRadius = new CornerRadius(14); Padding = new Thickness(12, 10, 10, 15);
        countdownTick.Tick += (_, _) => UpdateCountdown();
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(0, 0, 0, 4);
        Classes.Add("messageBar");
        model.PropertyChanged += (_, _) => Sync();
        Sync();
    }
    /// <summary>The line to show, or null when there is nothing to say.</summary>
    public string? Text => model.LastError is { } failed && failed != dismissedError ? failed : model.EditError ?? model.Notice ?? (OfferShowing ? model.ContinuePreviousNote : null);
    private bool OfferShowing => model.ContinuePreviousText is { } offered && offered != dismissedOffer;
    /// <summary>Whether the line is a refusal or a failure, not a note that something was done.</summary>
    public bool IsWarning => model.LastError is { } failed && failed != dismissedError || model.EditError is not null;
    private void Sync()
    {
        if (model.LastError is null) dismissedError = null;
        var line = Text; var split = line?.IndexOf(" · ", StringComparison.Ordinal) ?? -1;
        text.Text = split > 0 ? line![..split] : line ?? ""; text.IsVisible = line is not null;
        detail.Text = split > 0 ? line![(split + 3)..] : ""; detail.IsVisible = split > 0;
        var warning = IsWarning; var held = model.LastError is { } kept && kept != dismissedError;
        icon.IsVisible = line is not null; tick.IsVisible = !warning; mark.IsVisible = warning;
        icon.Classes.Set("warnIcon", warning); icon.Classes.Set("okIcon", !warning); Classes.Set("warn", warning); Classes.Set("hold", held);
        // The state colours are the other theme's, since the card is the opposite of the theme: bright ones on the dark card, deep ones on the light card.
        var onto = model.CurrentTheme.Dark ? AppThemes.Light : AppThemes.Dark;
        var state = new SolidColorBrush(Color.Parse(onto.Colour(held ? "Overtime" : warning ? "Break" : "Live")));
        icon.Background = state; countdownFill.Background = state;
        // A failed save stays, so its edge takes the red as well.
        BorderBrush = held ? state : null; BorderThickness = held ? new Thickness(1.5) : new Thickness(0);
        if (line != shownLine) { shownLine = line; shownAt = DateTime.UtcNow; }
        countdown.IsVisible = line is not null;
        if (line is not null && !held) countdownTick.Start(); else countdownTick.Stop();
        UpdateCountdown();
        // The cross puts the offer away as well, until there is a different one (a new slip to take back).
        var continueText = model.ContinuePreviousText; if (continueText is null) dismissedOffer = null;
        if (continueText is not null && continueText == dismissedOffer) continueText = null;
        offer.IsVisible = continueText is not null; if (continueText is not null) offer.Content = continueText;
        undo.IsVisible = line is not null && !warning && model.NoticeHasUndo && model.Editor.CanUndo;
        IsVisible = line is not null || continueText is not null;
    }
    /// <summary>Sets how much of the line along the foot is left: all of it for a failed save, else what is left of <see cref="TimerModel.MessageLifetime"/>.</summary>
    public void UpdateCountdown()
    {
        var kept = model.LastError is { } failed && failed != dismissedError;
        var fraction = kept || shownLine is null ? 1 : Math.Clamp(1 - (DateTime.UtcNow - shownAt).TotalSeconds / TimerModel.MessageLifetime.TotalSeconds, 0, 1);
        left.Width = new GridLength(fraction, GridUnitType.Star); right.Width = new GridLength(1 - fraction, GridUnitType.Star);
    }
}
