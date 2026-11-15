using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Input;

namespace Timer.Desktop;

/// <summary>Keep the existing button material while exposing the native checked state.</summary>
internal sealed class CompletionButton : ToggleButton
{
    protected override Type StyleKeyOverride => typeof(Button);
    protected override AutomationPeer OnCreateAutomationPeer() => new CompletionPeer(this);
    private sealed class CompletionPeer(CompletionButton owner) : ToggleButtonAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
    }
}

/// <summary>A model-owned single choice, with a native radio role and one Tab stop per group.</summary>
internal sealed class ChoiceButton : RadioButton
{
    protected override Type StyleKeyOverride => typeof(Button);
    private readonly string selectedClass;
    protected override AutomationPeer OnCreateAutomationPeer() => new ChoicePeer(this);
    private sealed class ChoicePeer(ChoiceButton owner) : RadioButtonAutomationPeer(owner), ISelectionItemProvider
    {
        void ISelectionItemProvider.Select() { EnsureEnabled(); owner.OnClick(); }
    }

    public ChoiceButton(string selectedClass)
    {
        this.selectedClass = selectedClass;
        // Separate native groups: the model/choice owner coordinates selection, including refused changes.
        GroupName = Guid.NewGuid().ToString();
        Classes.CollectionChanged += (_, _) => Sync();
        AttachedToVisualTree += (_, _) => Sync();
    }

    public void Sync()
    {
        IsChecked = Classes.Contains(selectedClass);
        if (Parent is Panel panel)
        {
            var options = panel.Children.OfType<ChoiceButton>().ToArray();
            var tab = options.FirstOrDefault(b => b.Classes.Contains(b.selectedClass) && b.IsEffectivelyEnabled)
                ?? options.FirstOrDefault(b => b.IsEffectivelyEnabled);
            foreach (var option in options) option.IsTabStop = option == tab;
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        // The action can refuse or defer a change. Report the resulting model selection, not the click.
        Sync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End && Parent is Panel panel)
        {
            var options = panel.Children.OfType<ChoiceButton>().Where(b => b.IsEffectivelyEnabled && b.IsEffectivelyVisible).ToArray();
            var at = Array.IndexOf(options, this);
            if (at >= 0)
            {
                var next = e.Key == Key.Home ? 0 : e.Key == Key.End ? options.Length - 1
                    : (at + (e.Key is Key.Left or Key.Up ? -1 : 1) + options.Length) % options.Length;
                options[next].Focus(NavigationMethod.Directional); options[next].OnClick(); e.Handled = true; return;
            }
        }
        base.OnKeyDown(e);
    }
}
