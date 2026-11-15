using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>A drawn timeline target keeps its existing pointer gestures and gains named keyboard/automation actions.</summary>
public sealed class DisplayItem : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    public Action<KeyModifiers>? Activate { get; set; }
    public bool Selected { get; set; }
    public bool CanSelect { get; set; } = true;
    public DisplayItem() { Focusable = true; FocusAdorner = DisplayFocus.For(this); }
    private void Invoke(KeyModifiers modifiers)
    {
        var root = TopLevel.GetTopLevel(this); var name = Name;
        Activate?.Invoke(modifiers);
        Dispatcher.UIThread.Post(() => root?.GetVisualDescendants().OfType<DisplayItem>().FirstOrDefault(c => c.Name == name)?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift) && ContextMenu is { } menu)
        { menu.Open(this); e.Handled = true; return; }
        if (e.Key is Key.Enter or Key.Space) { Invoke(e.KeyModifiers); e.Handled = true; return; }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(GotFocusEventArgs e) { base.OnGotFocus(e); if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) this.BringIntoView(); }
    protected override AutomationPeer OnCreateAutomationPeer() => new ItemPeer(this);
    private sealed class ItemPeer(DisplayItem owner) : ControlAutomationPeer(owner), IInvokeProvider, ISelectionItemProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        public bool IsSelected => owner.Selected;
        public ISelectionProvider? SelectionContainer => null;
        public void Invoke() { EnsureEnabled(); owner.Invoke(KeyModifiers.None); }
        public void Select() { EnsureEnabled(); if (!owner.CanSelect) throw new InvalidOperationException("This item is recording."); if (!owner.Selected) owner.Invoke(KeyModifiers.None); }
        public void AddToSelection() { EnsureEnabled(); if (!owner.CanSelect) throw new InvalidOperationException("This item is recording."); if (!owner.Selected) owner.Invoke(KeyModifiers.Shift); }
        public void RemoveFromSelection() { EnsureEnabled(); if (owner.Selected) owner.Invoke(KeyModifiers.Shift); }
    }
}

internal static class DisplayFocus
{
    public static ITemplate<Control> For(Control owner) => new FuncTemplate<Control>(() => new Outline(owner) { IsHitTestVisible = false });
    private sealed class Outline(Control owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (Bounds.Width <= 4 || Bounds.Height <= 4) return;
            var radius = owner is Border border ? Math.Max(0, border.CornerRadius.TopLeft - 2) : 5;
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Ui.ThemeColour(owner, "AccentLit", Colors.MediumPurple)), 1.5, DashStyle.Dash), new Rect(Bounds.Size).Deflate(2), radius, radius);
        }
    }
}
