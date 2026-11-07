using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>How a <see cref="ChoiceStrip"/> draws its options.</summary>
public enum ChoiceLook
{
    /// <summary>Raised chips with a lamp that lights on the chosen one: a list the person knows by sight, such as the accounts. Past eight options the strip folds to seven and a "more" chip.</summary>
    Chips,
    /// <summary>The sunk slider with a raised thumb under the chosen option, for a few fixed words (Repeat, a budget period, Note or Meeting).</summary>
    Segments,
    /// <summary>Small chips of one or two letters in a row, for the days of the week.</summary>
    Days,
}

/// <summary>
/// A visible choice of one option from a short list, in place of a drop-down that hides its options. It keeps the small part of a combo box's surface that the app's forms use
/// (<see cref="ItemsSource"/>, <see cref="SelectedItem"/> and <see cref="SelectionChanged"/>, which also fires when the code sets the choice), so a form can swap one for the other.
/// The Left and Right arrows (Up and Down too) move the choice to the neighbouring option, as in any radio group.
/// </summary>
public sealed class ChoiceStrip : Border
{
    private List<string> items = new();
    private string? selected;
    private bool expanded;
    private readonly List<Button> buttons = new();
    public ChoiceLook Look { get; init; } = ChoiceLook.Chips;
    /// <summary>The colour (a hex string) of the dot that leads an option, or null for none: the account colours.</summary>
    public Func<string, string?>? DotFor { get; init; }
    /// <summary>The words drawn on an option when they are not the option itself (a day's first letter). The option stays its accessible name.</summary>
    public Func<string, string>? ShortLabel { get; init; }
    /// <summary>How many options a chip strip shows before it folds the rest behind a "more" chip.</summary>
    public int FoldAfter { get; init; } = 8;
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public ChoiceStrip()
    {
        Focusable = false; HorizontalAlignment = HorizontalAlignment.Stretch;
        ActualThemeVariantChanged += (_, _) => Rebuild();
        KeyDown += OnKeyDown;
    }
    public IEnumerable<string>? ItemsSource
    {
        get => items;
        set { items = value?.ToList() ?? new(); if (selected is not null && !items.Contains(selected, StringComparer.OrdinalIgnoreCase)) selected = null; Rebuild(); }
    }
    /// <summary>The chosen option (a string) or null. Setting it raises <see cref="SelectionChanged"/> when it changes, as a combo box does.</summary>
    public object? SelectedItem
    {
        get => selected;
        set
        {
            var next = value as string; if (next is not null) next = items.FirstOrDefault(i => i.Equals(next, StringComparison.OrdinalIgnoreCase)) ?? next;
            if (string.Equals(next, selected, StringComparison.Ordinal)) return;
            var old = selected; selected = next; Refresh();
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(SelectingItemsControl.SelectionChangedEvent, old is null ? Array.Empty<object>() : new object[] { old }, next is null ? Array.Empty<object>() : new object[] { next }));
        }
    }
    public int SelectedIndex
    {
        get => selected is null ? -1 : items.FindIndex(i => i.Equals(selected, StringComparison.OrdinalIgnoreCase));
        set { if (value >= 0 && value < items.Count) SelectedItem = items[value]; }
    }
    /// <summary>The option buttons, in order (not the "more" chip), for tests and for the keyboard.</summary>
    public IReadOnlyList<Button> Options => buttons;

    private void Rebuild()
    {
        buttons.Clear();
        if (Look == ChoiceLook.Segments) { Child = BuildSegments(); return; }
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = Look == ChoiceLook.Days ? 6 : 7, LineSpacing = 7 };
        var folded = Look == ChoiceLook.Chips && items.Count > FoldAfter && !expanded;
        var shown = folded ? items.Take(FoldAfter - 1).ToList() : items;
        if (folded && selected is not null && !shown.Contains(selected, StringComparer.OrdinalIgnoreCase) && items.Contains(selected, StringComparer.OrdinalIgnoreCase)) shown.Add(selected);
        foreach (var item in shown) { var chip = Chip(item); buttons.Add(chip); panel.Children.Add(chip); }
        if (folded) panel.Children.Add(MoreChip("+" + (items.Count - shown.Count) + " more", () => { expanded = true; Rebuild(); }));
        else if (Look == ChoiceLook.Chips && expanded && items.Count > FoldAfter) panel.Children.Add(MoreChip("Show fewer", () => { expanded = false; Rebuild(); }));
        Child = panel; Refresh();
    }
    private Control BuildSegments()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var item in items)
        {
            var captured = item;
            var button = NotesPage.Seg((string.IsNullOrEmpty(Name) ? "Choice" : Name) + "_" + Safe(item), item, false, () => Choose(captured));
            AutomationProperties.SetName(button, item); buttons.Add(button); row.Children.Add(button);
        }
        var track = new Border { Child = row, Height = 36, HorizontalAlignment = HorizontalAlignment.Left }; track.Classes.Add("segTrack");
        Dispatcher_Refresh(); return track;
    }
    private void Dispatcher_Refresh() => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh, Avalonia.Threading.DispatcherPriority.Loaded);
    private static string Safe(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());

    private Button Chip(string item)
    {
        var button = new Button { Name = (string.IsNullOrEmpty(Name) ? "Choice" : Name) + "_" + Safe(item), Tag = item }; button.Classes.Add("ctl"); button.Classes.Add("chip");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Border { Name = "Lamp", Width = 3, Height = 11, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) });
        if (DotFor?.Invoke(item) is { } hex) content.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Fill = Brush.Parse(hex) });
        var text = new TextBlock { Text = ShortLabel?.Invoke(item) ?? item, FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "ChipText" };
        content.Children.Add(text); button.Content = content;
        if (Look == ChoiceLook.Days) button.Padding = new Thickness(10, 0, 10, 0);
        AutomationProperties.SetName(button, item);
        button.Click += (_, _) => Choose(item);
        return button;
    }
    /// <summary>A stand-alone chip with a lamp, for several-at-once choices such as the Notes account filter: lit when on.</summary>
    public static Button LitChip(Control host, string name, string text, string? dotHex, bool on, Action click)
    {
        var button = new Button { Name = name }; button.Classes.Add("ctl"); button.Classes.Add("chip");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var lit = Ui.ThemeColour(host, "AccentLit", Colors.MediumPurple);
        content.Children.Add(new Border { Name = "Lamp", Width = 3, Height = 11, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(lit), Opacity = on ? 1 : .24,
            BoxShadow = on && SoftTint.IsDark(host) ? new BoxShadows(new BoxShadow { Blur = 7, Spread = 1, Color = SoftTint.WithAlpha(lit, .5) }) : default });
        if (dotHex is not null) content.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Fill = Brush.Parse(dotHex) });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "ChipText" });
        button.Content = content; button.Classes.Set("chipOn", on); AutomationProperties.SetName(button, text); AutomationProperties.SetHelpText(button, on ? "Chosen" : "");
        button.Click += (_, _) => click(); return button;
    }

    private Button MoreChip(string words, Action click)
    {
        var button = new Button { Name = (string.IsNullOrEmpty(Name) ? "Choice" : Name) + "_More", Content = new TextBlock { Text = words, FontSize = 12.5, FontWeight = FontWeight.SemiBold } };
        button.Classes.Add("ctl"); button.Classes.Add("chip"); button.Classes.Add("more"); button.Click += (_, _) => click(); return button;
    }
    private void Choose(string item) { SelectedItem = item; if (Look != ChoiceLook.Segments) FocusSelected(); }
    private void FocusSelected() { var i = SelectedIndex; if (i >= 0 && i < buttons.Count && IsKeyboardFocusWithin) buttons[i].Focus(); }

    /// <summary>Brings every option up to date with the choice: the lamp lights on the chosen chip, the thumb sits under the chosen segment.</summary>
    private void Refresh()
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            var on = items.Count > i && selected is not null && buttons[i].Tag is string tag ? tag.Equals(selected, StringComparison.OrdinalIgnoreCase) : (Look == ChoiceLook.Segments && i < items.Count && selected is not null && items[i].Equals(selected, StringComparison.OrdinalIgnoreCase));
            var button = buttons[i];
            if (Look == ChoiceLook.Segments) { button.Classes.Set("optOn", on); continue; }
            button.Classes.Set("chipOn", on); AutomationProperties.SetHelpText(button, on ? "Chosen" : "");
            if (button.Content is StackPanel content)
            {
                var lit = Ui.ThemeColour(this, "AccentLit", Colors.MediumPurple); var lamp = (Border)content.Children[0];
                lamp.Background = new SolidColorBrush(lit); lamp.Opacity = on ? 1 : .24;
                lamp.BoxShadow = on && SoftTint.IsDark(this) ? new BoxShadows(new BoxShadow { Blur = 7, Spread = 1, Color = SoftTint.WithAlpha(lit, .5) }) : default;
                if (content.Children[^1] is TextBlock text) text.Foreground = new SolidColorBrush(Ui.ThemeColour(this, on ? "Ink" : "Muted", Colors.Gray));
            }
        }
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (items.Count == 0 || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        var step = e.Key is Key.Left or Key.Up ? -1 : 1; var at = SelectedIndex < 0 ? (step > 0 ? -1 : 0) : SelectedIndex;
        var next = Math.Clamp(at + step, 0, items.Count - 1);
        if (next != SelectedIndex) { SelectedItem = items[next]; if (next < buttons.Count) buttons[next].Focus(); }
        e.Handled = true;
    }
}
