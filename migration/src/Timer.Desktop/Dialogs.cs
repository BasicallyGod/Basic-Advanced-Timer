using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// A pop-up that sits on top of the dashboard, over a blurred and unclickable backdrop, like the getting started setup. It is the old pop-up window
/// without the window: set its Title, Width and Height, fill its Content, and call <see cref="Close"/> to take it away.
/// </summary>
public abstract class Dialog : ContentControl
{
    public string Title { get; protected set; } = "";
    public Control? Anchor { get; init; }
    public bool IsFlyout { get; protected set; }
    /// <summary>Raised when the dialog is about to close. Set Cancel to keep it open, such as to ask about unsaved changes first.</summary>
    public event EventHandler<System.ComponentModel.CancelEventArgs>? Closing;
    /// <summary>Raised once, after the dialog has gone.</summary>
    public event EventHandler? Closed;
    internal DialogHost? Host { get; set; }
    public void Close()
    {
        if (Host is null) return;
        var asking = new System.ComponentModel.CancelEventArgs(); Closing?.Invoke(this, asking); if (asking.Cancel) return;
        var host = Host; Host = null; host.Remove(this);
        IsVisible = false; Closed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Holds the dialogs open over the dashboard. They stack, so one opened from another goes on top of it, and Escape or the cross takes the top one away.
/// The dashboard behind blurs and cannot be clicked for as long as any dialog is open.
/// </summary>
public sealed class DialogHost : Grid
{
    private readonly List<(Dialog Dialog, Border Card)> open = new();
    public DialogHost() { IsHitTestVisible = false; SizeChanged += (_, _) => FitCards(); }
    /// <summary>Raised whenever the first dialog opens or the last one closes.</summary>
    public event Action? OpenChanged;
    public bool Any => open.Count > 0;
    public bool Modal => open.Any(o => !o.Dialog.IsFlyout);
    public IReadOnlyList<Dialog> Dialogs => open.Select(o => o.Dialog).ToList();
    public void Show(Dialog dialog)
    {
        if (open.Any(o => ReferenceEquals(o.Dialog, dialog))) return;
        dialog.Host = this; dialog.IsVisible = true;
        // A card with a header row, the title on the left and a cross on the right, above the dialog's own content. The scroll bar of a long dialog
        // therefore sits below the cross and never runs under it.
        var close = Ui.IconButton(IconKind.Close, "Close", "CloseDialog", dialog.Close);
        close.VerticalAlignment = VerticalAlignment.Center; close.Margin = new Thickness(0, 0, 10, 0);
        var title = Ui.Heading(dialog.Title, 24); title.Name = "DialogTitle"; title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(24, 0, 12, 0);
        var header = Ui.Grid("*,Auto"); header.Margin = new Thickness(0, 14, 0, 0); header.Children.Add(title); Ui.Put(header, close, 1);
        var inner = Ui.Grid("*", "Auto,*"); inner.Children.Add(header); Ui.Put(inner, dialog, row: 1);
        var card = new Border { Child = inner, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), Name = "DialogCard" };
        card.Classes.Add("dayCard");
        if (!double.IsNaN(dialog.Width)) card.Width = dialog.Width;
        if (!double.IsNaN(dialog.Height)) card.Height = dialog.Height;
        dialog.Width = double.NaN; dialog.Height = double.NaN; // The card has the size now, and the dialog fills it.
        var scrim = new Border { Name = "DialogScrim", Opacity = .38 };
        scrim.Classes.Add("scrim");
        var layer = new Grid();
        if (!dialog.IsFlyout) layer.Children.Add(scrim);
        else
        {
            layer.Background = Brushes.Transparent; // Outside clicks keep the unsaved form open.
            card.HorizontalAlignment = HorizontalAlignment.Left; card.VerticalAlignment = VerticalAlignment.Top;
            card.SizeChanged += (_, _) => FitCards();
            card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 6, Color = Color.FromArgb(45, 0, 0, 0) });
        }
        layer.Children.Add(card);
        layer.Tag = dialog;
        Children.Add(layer); open.Add((dialog, card)); FitCards();
        IsHitTestVisible = true;
        OpenChanged?.Invoke();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FocusFirst(dialog), Avalonia.Threading.DispatcherPriority.Loaded);
    }
    internal void Remove(Dialog dialog)
    {
        var index = open.FindIndex(o => ReferenceEquals(o.Dialog, dialog)); if (index < 0) return;
        var layer = Children.FirstOrDefault(c => ReferenceEquals(c.Tag, dialog));
        open.RemoveAt(index); if (layer is Panel panel) { panel.Children.Clear(); } if (layer is not null) Children.Remove(layer);
        if (open.Count == 0) IsHitTestVisible = false;
        OpenChanged?.Invoke();
        if (dialog.Anchor is { IsEffectivelyVisible: true } anchor) anchor.Focus();
    }
    /// <summary>Takes the top dialog away. Returns false when there was none, so the caller can use the key for something else.</summary>
    public bool CloseTop()
    {
        if (open.Count == 0) return false;
        open[^1].Dialog.Close(); return true;
    }
    // A card is never taller or wider than the window it sits in, so a long dialog scrolls inside itself.
    private void FitCards()
    {
        foreach (var (dialog, card) in open)
        {
            card.MaxHeight = Math.Min(dialog.MaxHeight, Math.Max(200, Bounds.Height - 48)); card.MaxWidth = Math.Max(240, Bounds.Width - 48);
            if (dialog.IsFlyout)
            {
                var anchor = dialog.Anchor;
                var point = anchor?.TranslatePoint(new Point(anchor.Bounds.Width, anchor.Bounds.Height), this) ?? new Point(24, 24);
                var width = double.IsNaN(card.Width) ? 400 : Math.Min(card.Width, card.MaxWidth);
                var height = card.Bounds.Height > 0 ? card.Bounds.Height : card.MaxHeight;
                var x = Math.Clamp(point.X + 12, 24, Math.Max(24, Bounds.Width - width - 24));
                var y = Math.Clamp(point.Y + 8, 24, Math.Max(24, Bounds.Height - height - 24));
                card.Margin = new Thickness(x, y, 0, 0);
            }
        }
    }
    private static void FocusFirst(Dialog dialog)
    {
        var first = dialog.GetVisualDescendantsOfType<Control>().FirstOrDefault(c => c is TextBox or ComboBox or CheckBox or Button && c.Focusable && c.IsEffectivelyEnabled && c.IsVisible);
        (first ?? dialog).Focus();
    }
}

internal static class DialogVisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOfType<T>(this Visual root) where T : Visual
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
