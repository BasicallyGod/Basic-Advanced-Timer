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
    public IReadOnlyList<Dialog> Dialogs => open.Select(o => o.Dialog).ToList();
    public void Show(Dialog dialog)
    {
        if (open.Any(o => ReferenceEquals(o.Dialog, dialog))) return;
        dialog.Host = this; dialog.IsVisible = true;
        // A card with a cross in its corner, over a dim scrim that takes every click so nothing behind is reached.
        var close = Ui.IconButton(IconKind.Close, "Close", "CloseDialog", dialog.Close);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Margin = new Thickness(0, 10, 10, 0); close.ZIndex = 2;
        var inner = new Grid(); inner.Children.Add(dialog); inner.Children.Add(close);
        var card = new Border { Child = inner, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), Name = "DialogCard" };
        card.Classes.Add("dayCard");
        if (!double.IsNaN(dialog.Width)) card.Width = dialog.Width;
        if (!double.IsNaN(dialog.Height)) card.Height = dialog.Height;
        dialog.Width = double.NaN; dialog.Height = double.NaN; // The card has the size now, and the dialog fills it.
        var scrim = new Border { Name = "DialogScrim", Opacity = .38 };
        scrim.Classes.Add("scrim");
        var layer = new Grid(); layer.Children.Add(scrim); layer.Children.Add(card);
        layer.Tag = dialog;
        Children.Add(layer); open.Add((dialog, card)); FitCards();
        IsHitTestVisible = true;
        if (open.Count == 1) OpenChanged?.Invoke();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FocusFirst(dialog), Avalonia.Threading.DispatcherPriority.Loaded);
    }
    internal void Remove(Dialog dialog)
    {
        var index = open.FindIndex(o => ReferenceEquals(o.Dialog, dialog)); if (index < 0) return;
        var layer = Children.FirstOrDefault(c => ReferenceEquals(c.Tag, dialog));
        open.RemoveAt(index); if (layer is Panel panel) { panel.Children.Clear(); } if (layer is not null) Children.Remove(layer);
        if (open.Count == 0) { IsHitTestVisible = false; OpenChanged?.Invoke(); }
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
            card.MaxHeight = Math.Max(200, Bounds.Height - 48); card.MaxWidth = Math.Max(240, Bounds.Width - 48);
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
