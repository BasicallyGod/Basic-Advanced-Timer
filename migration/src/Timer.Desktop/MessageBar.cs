using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Timer.Desktop;

/// <summary>
/// The note, refused-edit explanation or failed-save line, and the offer to continue an earlier block. It floats over the lowest part of the
/// workspace, just above the buttons, so it never takes room from the layout and pushes them up. It fades after a while, or goes with the cross.
/// </summary>
public sealed class MessageBar : Border
{
    private readonly TimerModel model;
    private readonly TextBlock text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13, MaxWidth = 460, VerticalAlignment = VerticalAlignment.Center, Name = "MessageText" };
    private readonly Button offer;
    private string? dismissedError, dismissedOffer;
    public MessageBar(TimerModel model)
    {
        this.model = model; Name = "MessageBar";
        // A failed save stays until a save works, so the cross hides it only until it changes.
        offer = Ui.Button("", () => model.ContinuePrevious(), "ContinuePrevious");
        offer.Classes.Add("toolButton"); offer.Classes.Add("quiet"); offer.Height = 30; offer.Padding = new Thickness(12, 0); offer.FontSize = 13;
        var close = Ui.IconButton(IconKind.Close, "Dismiss", "DismissMessage", () => { dismissedError = model.LastError; dismissedOffer = model.ContinuePreviousText; model.ExpireMessages(); Sync(); });
        var row = Ui.Horizontal(text, offer, close); row.Spacing = 12;
        Child = row; CornerRadius = new CornerRadius(12); Padding = new Thickness(14, 6, 6, 6);
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom; Margin = new Thickness(0, 0, 0, 6);
        Classes.Add("messageBar");
        model.PropertyChanged += (_, _) => Sync();
        Sync();
    }
    /// <summary>The line to show, or null when there is nothing to say.</summary>
    public string? Text => model.LastError is { } failed && failed != dismissedError ? failed : model.EditError ?? model.Notice;
    private void Sync()
    {
        if (model.LastError is null) dismissedError = null;
        var line = Text; text.Text = line ?? ""; text.IsVisible = line is not null;
        // The cross puts the offer away as well, until there is a different one (a new slip to take back).
        var continueText = model.ContinuePreviousText; if (continueText is null) dismissedOffer = null;
        if (continueText is not null && continueText == dismissedOffer) continueText = null;
        offer.IsVisible = continueText is not null; if (continueText is not null) offer.Content = continueText;
        IsVisible = line is not null || continueText is not null;
    }
}
