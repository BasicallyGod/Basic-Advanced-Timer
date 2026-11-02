using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Styling;

namespace Timer.Desktop;

/// <summary>Standard buttons and toggles with one isolated visual theme. Only importance stars are migrated in this pilot.</summary>
public static class NeumorphicControls
{
    private static T Configure<T>(T button, string name, bool compact = false) where T : Button
    {
        button.Name = name; button.Classes.Add("neo"); if (compact) button.Classes.Add("compact");
        button.Theme = (ControlTheme)Application.Current!.FindResource(button is ToggleButton ? "NeumorphicToggle" : "NeumorphicButton")!;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private static void Label(Button button, string words)
    {
        var text = new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, button.GetObservable(Button.ForegroundProperty));
        button.Content = text; AutomationProperties.SetName(button, words);
    }

    public static Button Action(string words, Action click, string name, bool primary = false)
    {
        var button = Configure(new Button(), name); if (primary) button.Classes.Add("primary"); Label(button, words);
        button.Click += (_, _) => click(); return button;
    }

    public static Button Icon(IconKind kind, string tip, Action click, string name)
    {
        var button = Configure(new Button(), name, compact: true); button.Content = new IconGlyph(kind);
        AutomationProperties.SetName(button, tip); ToolTip.SetTip(button, tip); button.Click += (_, _) => click(); return button;
    }

    public static ToggleButton Toggle(string words, bool on, string name, string? role = null)
    {
        var button = Configure(new ToggleButton { IsChecked = on }, name); if (role is not null) button.Classes.Add(role);
        Label(button, words); return button;
    }

    public static ToggleButton Important(bool on, Action click, string name, double size = 28)
    {
        var button = Configure(new ToggleButton { IsChecked = on }, name, compact: true);
        button.Classes.Add("important"); button.Width = button.Height = size;
        void Sync()
        {
            var marked = button.IsChecked == true;
            button.Content = new IconGlyph(marked ? IconKind.StarFilled : IconKind.Star) { TintKey = marked ? "ImportantStar" : "Muted" };
            var tip = marked ? "Not important" : "Mark important";
            AutomationProperties.SetName(button, tip); ToolTip.SetTip(button, tip);
        }
        button.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Sync(); };
        // Model callbacks still own the stored value. A routed Click used by existing callers is delivered exactly once too.
        button.Click += (_, _) => click(); Sync(); return button;
    }
}
