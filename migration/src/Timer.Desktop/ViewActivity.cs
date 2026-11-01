using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>Tracks attachment, ancestor visibility and minimisation without polling or requesting frames.</summary>
internal sealed class ViewActivity
{
    private readonly Control view;
    private readonly Action<bool> changed;
    private Visual[] ancestors = Array.Empty<Visual>();
    private Window? window;
    public bool Active { get; private set; }
    public ViewActivity(Control view, Action<bool> changed)
    {
        this.view = view; this.changed = changed;
        view.AttachedToVisualTree += Attached;
        view.DetachedFromVisualTree += Detached;
        if (TopLevel.GetTopLevel(view) is not null) Watch();
    }
    private void Attached(object? sender, VisualTreeAttachmentEventArgs e) => Watch();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) { Unwatch(); Set(false); }
    private void Watch()
    {
        Unwatch();
        ancestors = view.GetVisualAncestors().Prepend(view).ToArray();
        window = TopLevel.GetTopLevel(view) as Window;
        foreach (var visual in ancestors) visual.PropertyChanged += VisibilityChanged;
        Update();
    }
    private void Unwatch()
    {
        foreach (var visual in ancestors) visual.PropertyChanged -= VisibilityChanged;
        ancestors = Array.Empty<Visual>(); window = null;
    }
    private void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty) Update();
    }
    private void Update() => Set(ancestors.Length > 0 && ancestors.All(v => v.IsVisible) && window?.WindowState != WindowState.Minimized);
    private void Set(bool active) { if (Active == active) return; Active = active; changed(active); }
}
