using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Timer.Desktop;

/// <summary>
/// The note, refused-edit explanation or failed-save line, and the offer to continue an earlier block. It floats over the lowest part of the
/// workspace, so it never takes room from the layout. It is a porcelain card raised well clear of the page, with a sunk round medallion like an inlaid light:
/// a tick in green for something done, a mark in amber for a refusal, in red for a failed save. It fades after a while (the model times it), or goes with the cross.
/// </summary>
public sealed class MessageBar : Border
{
    private readonly TimerModel model;
    private readonly TextBlock text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.SemiBold, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center, Name = "MessageText" };
    // What follows the first few words, after a dot: quieter, and only when it helps.
    private readonly TextBlock detail = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center, Name = "MessageDetail" };
    private readonly Button offer, undo;
    private readonly Border icon = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Name = "MessageIcon" };
    private readonly Avalonia.Controls.Shapes.Path tick = new() { Data = Geometry.Parse("M 8,14.5 L 12.5,19 L 20,9.5"), StrokeThickness = 2.4, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    private readonly Avalonia.Controls.Shapes.Path mark = new() { Data = Geometry.Parse("M 14,9 L 14,15 M 14,19 L 14,19.01"), StrokeThickness = 2.4, StrokeLineCap = PenLineCap.Round };
    private string? dismissedError, dismissedOffer;
    // On the pill the bar says only that a save failed: notes, refusals and the continue offer belong to the dashboard, which the pill does not sit over.
    private readonly bool failuresOnly;
    public MessageBar(TimerModel model, bool failuresOnly = false)
    {
        this.model = model; this.failuresOnly = failuresOnly; Name = failuresOnly ? "PillMessageBar" : "MessageBar";
        // A failed save stays until a save works, so the cross hides it only until it changes.
        offer = Ui.Ctl("ContinuePrevious", "", null, "Continue earlier block", () => model.ContinuePrevious(), small: true);
        offer.Padding = new Thickness(12, 0);
        undo = Ui.Ctl("UndoMessage", "Undo", null, "Undo the last change", () => model.UndoEdit(), small: true, key: "Ctrl Z"); undo.Padding = new Thickness(12, 0);
        var close = Ui.IconButton(IconKind.Close, "Dismiss", "DismissMessage", () => { dismissedError = model.LastError; dismissedOffer = model.ContinuePreviousText; model.ExpireMessages(); Sync(); });
        var glyphs = new Grid { Width = 28, Height = 28 }; glyphs.Children.Add(tick); glyphs.Children.Add(mark); icon.Child = new Viewbox { Child = glyphs };
        detail.Classes.Add("muted");
        icon.Classes.Add("medallion");
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(text); words.Children.Add(detail);
        var row = Ui.Horizontal(icon, words, offer, undo, close); row.Spacing = 11;
        Child = row; CornerRadius = new CornerRadius(18); Padding = new Thickness(12, 10, 10, 10);
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(0, 0, 0, 4);
        Classes.Add("messageBar");
        model.PropertyChanged += (_, _) => Sync();
        Sync();
    }
    /// <summary>The line to show, or null when there is nothing to say.</summary>
    public string? Text => model.LastError is { } failed && failed != dismissedError ? failed : failuresOnly ? null : model.EditError ?? model.Notice ?? (OfferShowing ? model.ContinuePreviousNote : null);
    private bool OfferShowing => model.ContinuePreviousText is { } offered && offered != dismissedOffer;
    /// <summary>Whether the line is a refusal or a failure, not a note that something was done.</summary>
    public bool IsWarning => model.LastError is { } failed && failed != dismissedError || !failuresOnly && model.EditError is not null;
    private void Sync()
    {
        if (model.LastError is null) dismissedError = null;
        var line = Text; var split = line?.IndexOf(" · ", StringComparison.Ordinal) ?? -1;
        text.Text = split > 0 ? line![..split] : line ?? ""; text.IsVisible = line is not null;
        detail.Text = split > 0 ? line![(split + 3)..] : ""; detail.IsVisible = split > 0;
        var warning = IsWarning; var held = model.LastError is { } kept && kept != dismissedError;
        icon.IsVisible = line is not null; tick.IsVisible = !warning; mark.IsVisible = warning;
        icon.Classes.Set("warnIcon", warning); icon.Classes.Set("okIcon", !warning); Classes.Set("warn", warning); Classes.Set("hold", held);
        // The light's colour is the theme's own, in the shade that holds on the card; it is drawn in the glyph, which sits in a sunk well.
        var key = held ? "Overtime" : warning ? "BreakText" : "LiveText";
        tick.Bind(Shape.StrokeProperty, tick.GetResourceObservable(key)); mark.Bind(Shape.StrokeProperty, mark.GetResourceObservable(key));
        // A failed save stays, so its edge takes the red as well.
        if (held) this.Bind(BorderBrushProperty, this.GetResourceObservable("Overtime")); else BorderBrush = null;
        BorderThickness = held ? new Thickness(1.5) : new Thickness(0);
        if (line != shownLine) shownLine = line;
        // The cross puts the offer away as well, until there is a different one (a new slip to take back).
        var continueText = model.ContinuePreviousText; if (continueText is null) dismissedOffer = null;
        if (continueText is not null && continueText == dismissedOffer || failuresOnly) continueText = null;
        offer.IsVisible = continueText is not null; if (continueText is not null) offer.Content = continueText;
        undo.IsVisible = !failuresOnly && line is not null && !warning && model.NoticeHasUndo && model.Editor.CanUndo;
        IsVisible = line is not null || continueText is not null;
    }
    private string? shownLine;
}
