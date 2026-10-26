using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// The note, refused-edit explanation or failed-save line, and the offer to continue an earlier block. It floats over the lowest part of the
/// workspace, so it never takes room from the layout. A note that something was done has a tick in the accent colour; a refusal or a failed save has a
/// ring with a mark and a quieter border (red means overtime, so it is not used here). It fades after a while, or goes with the cross.
/// </summary>
public sealed class MessageBar : Border
{
    private readonly TimerModel model;
    private readonly TextBlock text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13, FontWeight = FontWeight.SemiBold, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center, Name = "MessageText" };
    // What follows the first few words, after a dot: quieter, and only when it helps.
    private readonly TextBlock detail = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center, Name = "MessageDetail" };
    private readonly Button offer, undo;
    private readonly Border icon = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center, Name = "MessageIcon" };
    private readonly Avalonia.Controls.Shapes.Path tick = new() { Data = Geometry.Parse("M 8,14.5 L 12.5,19 L 20,9.5"), StrokeThickness = 2.2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    private readonly Avalonia.Controls.Shapes.Path mark = new() { Data = Geometry.Parse("M 14,9 L 14,15 M 14,19 L 14,19.01"), StrokeThickness = 2.4, StrokeLineCap = PenLineCap.Round };
    private string? dismissedError, dismissedOffer;
    public MessageBar(TimerModel model)
    {
        this.model = model; Name = "MessageBar";
        // A failed save stays until a save works, so the cross hides it only until it changes.
        offer = Ui.Ctl("ContinuePrevious", "", null, "Continue earlier block", () => model.ContinuePrevious(), small: true);
        offer.Classes.Add("accentOutline"); offer.Padding = new Thickness(12, 0);
        undo = Ui.Ctl("UndoMessage", "Undo", null, "Undo the last change", () => model.UndoEdit(), small: true, key: "Ctrl Z"); undo.Classes.Add("accentOutline"); undo.Padding = new Thickness(12, 0);
        var close = Ui.IconButton(IconKind.Close, "Dismiss", "DismissMessage", () => { dismissedError = model.LastError; dismissedOffer = model.ContinuePreviousText; model.ExpireMessages(); Sync(); });
        var glyphs = new Grid { Width = 28, Height = 28 }; glyphs.Children.Add(tick); glyphs.Children.Add(mark); icon.Child = new Viewbox { Child = glyphs };
        detail.Classes.Add("muted");
        tick.Bind(Shape.StrokeProperty, tick.GetResourceObservable("Accent")); mark.Bind(Shape.StrokeProperty, mark.GetResourceObservable("Ink"));
        var row = Ui.Horizontal(icon, text, detail, offer, undo, close); row.Spacing = 10;
        Child = row; CornerRadius = new CornerRadius(14); Padding = new Thickness(10, 8, 8, 8);
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
        var warning = IsWarning;
        icon.IsVisible = line is not null; tick.IsVisible = !warning; mark.IsVisible = warning;
        icon.Classes.Set("warnIcon", warning); icon.Classes.Set("okIcon", !warning); Classes.Set("warn", warning); Classes.Set("hold", model.LastError is { } held && held != dismissedError);
        // The cross puts the offer away as well, until there is a different one (a new slip to take back).
        var continueText = model.ContinuePreviousText; if (continueText is null) dismissedOffer = null;
        if (continueText is not null && continueText == dismissedOffer) continueText = null;
        offer.IsVisible = continueText is not null; if (continueText is not null) offer.Content = continueText;
        undo.IsVisible = line is not null && !warning && model.NoticeHasUndo && model.Editor.CanUndo;
        IsVisible = line is not null || continueText is not null;
    }
}
