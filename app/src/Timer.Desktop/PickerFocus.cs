using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>Focus belongs to an open picker, then returns without taking it from a field clicked outside.</summary>
internal sealed class PickerFocus
{
    private static readonly ConditionalWeakTable<Popup, PickerFocus> Sessions = new();
    private readonly Popup host;
    private Border? card;
    private TopLevel? owner;
    private Control? returnTo;
    private int opening;

    private PickerFocus(Popup host)
    {
        this.host = host;
        host.Opened += (_, _) => Open();
        host.Closed += (_, _) => Close();
    }

    public static void Manage(Popup host, Border card)
    {
        var session = Sessions.GetValue(host, p => new PickerFocus(p));
        if (session.card is { } previous) previous.RemoveHandler(InputElement.KeyDownEvent, session.KeyDown);
        session.card = card;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        card.AddHandler(InputElement.KeyDownEvent, session.KeyDown, RoutingStrategies.Tunnel);
    }

    private void Open()
    {
        var version = ++opening;
        owner = TopLevel.GetTopLevel(host.PlacementTarget);
        returnTo = owner?.FocusManager?.GetFocusedElement() as Control ?? host.PlacementTarget;
        owner?.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        // The PopupRoot must be attached before focus can enter it.
        Dispatcher.UIThread.Post(() =>
        {
            if (!host.IsOpen || opening != version || card is null) return;
            Control? first = card is ReminderPickerCard reminder ? reminder.Picker.Strip
                : card.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("chosenDay"));
            first ??= card.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyEnabled);
            first?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (!host.IsOpen || e.Key != Key.Escape) return;
        e.Handled = true;
        host.IsOpen = false;
    }

    private void Close()
    {
        ++opening;
        var top = owner; owner = null;
        top?.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        var target = returnTo; returnTo = null;
        var closedCard = card;
        Dispatcher.UIThread.Post(() =>
        {
            if (host.IsOpen || target is null || !target.IsAttachedToVisualTree() || !target.IsEffectivelyVisible || !target.IsEffectivelyEnabled) return;
            var focused = top?.FocusManager?.GetFocusedElement();
            // An outside click or a successful Add may already have focused another control.
            if (focused is null || focused is Avalonia.Visual visual && closedCard?.IsVisualAncestorOf(visual) == true)
                target.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }
}
