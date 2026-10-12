using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// Pick a theme, or make your own. Choosing a theme in the list applies it. Built-in themes cannot be changed, but can be duplicated;
/// the copy can be renamed and have any colour changed, and the app shows each change as it is made. Nothing is kept until Save.
/// </summary>
public sealed class ThemesDialog : Dialog
{
    private readonly TimerModel model;
    private readonly ListBox list = new() { Name = "ThemeList" };
    private readonly StackPanel detail = new() { Spacing = 10 };
    private readonly TextBlock error = new() { Classes = { "overtime" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button duplicate, delete;
    private string shown = ""; private bool filling;
    // The theme being edited, as plain values so each change is cheap to apply.
    private string name = ""; private bool dark; private string blockStyle = ThemeBlockStyle.AsChosen; private readonly Dictionary<string, string> colours = new(); private string? original;
    private bool dirty, closeConfirmed;
    private double blockTint = ThemeBlockStyle.DefaultTint, blockEdge = ThemeBlockStyle.DefaultEdge;
    // Shown in place of a dialog when a change would throw away edits that are not saved yet.
    private readonly Border unsaved = new() { Name = "UnsavedPrompt", IsVisible = false, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0) };
    private Action? afterPrompt;

    public ThemesDialog(TimerModel model)
    {
        this.model = model;
        Title = "Themes"; Width = 800; Height = 680;
        var root = Ui.Grid("230,*", "*,Auto,Auto"); root.Margin = new Thickness(24, 4, 24, 24);
        var left = Ui.Grid("*", "Auto,*,Auto"); left.Margin = new Thickness(0, 0, 18, 0);
        Grid.SetRow(list, 1); list.Margin = new Thickness(0, 10);
        left.Children.Add(list);
        duplicate = Ui.DialogButton("Duplicate", () => Guard(() => Run(() => { model.EndThemePreview(); var copy = model.DuplicateTheme(shown); Refill(copy); })), "DuplicateTheme");
        delete = Ui.DialogButton("Delete", () => Run(() => { model.DeleteTheme(shown); Refill(model.Theme); }), "DeleteTheme");
        var buttons = Ui.Horizontal(duplicate, delete); Ui.Put(left, buttons, 0, 2);
        unsaved.Classes.Add("chip");
        Ui.Put(root, left);
        Ui.Put(root, new ScrollViewer { Content = detail, AllowAutoHide = false, Padding = new Thickness(0, 0, 14, 0) }, 1);
        var promptText = new TextBlock { Name = "UnsavedText", TextWrapping = TextWrapping.Wrap };
        var promptButtons = Ui.Horizontal(
            Ui.DialogButton("Save", () => Run(() => { SaveCurrent(); Proceed(); }), "UnsavedSave"),
            Ui.DialogButton("Discard", () => { model.EndThemePreview(); dirty = false; Proceed(); }, "UnsavedDiscard"),
            Ui.DialogButton("Keep editing", () => { afterPrompt = null; unsaved.IsVisible = false; }, "UnsavedKeep"));
        var promptStack = new StackPanel { Spacing = 8 }; promptStack.Children.Add(promptText); promptStack.Children.Add(promptButtons);
        unsaved.Child = promptStack; unsavedText = promptText;
        Grid.SetColumnSpan(unsaved, 2); Ui.Put(root, unsaved, 0, 1);
        var footer = Ui.Horizontal(Ui.DialogButton("Close", () => Close(), "CloseThemes")); footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetColumnSpan(footer, 2); Ui.Put(root, footer, 0, 2);
        Content = root;
        list.SelectionChanged += (_, _) =>
        {
            if (filling || list.SelectedItem is not string chosen || chosen == shown) return;
            // Put the highlight back on the theme being edited until it is clear whether its changes are kept.
            var stay = shown; filling = true; list.SelectedItem = stay; filling = false;
            Guard(() => Run(() => { model.EndThemePreview(); model.SetTheme(chosen); Refill(chosen); }));
        };
        // Closing without saving puts the saved look back.
        Closed += (_, _) => model.EndThemePreview();
        // Closing with edits that are not saved asks first, rather than quietly losing them.
        Closing += (_, e) =>
        {
            if (closeConfirmed || !dirty) return;
            e.Cancel = true; Guard(() => { closeConfirmed = true; Close(); });
        };
        Refill(model.Theme == ThemeLibrary.System ? model.CurrentTheme.Name : model.Theme);
    }
    private void Run(Action action)
    {
        try { error.Text = ""; action(); }
        catch (ArgumentException e) { error.Text = e.Message; }
    }
    // The list is every theme, with the built-in ones marked. The choice shown is the one in use.
    private void Refill(string select)
    {
        if (select == ThemeLibrary.System) select = model.CurrentTheme.Name; // System is Light or Dark underneath, so that one is shown.
        filling = true;
        list.ItemsSource = model.Themes.Select(t => t.Name).ToList();
        list.SelectedItem = select; filling = false;
        Show(select);
    }
    private TextBlock unsavedText = new();
    /// <summary>Runs the action now, or after the person decides what to do with edits that are not saved.</summary>
    private void Guard(Action action)
    {
        if (!dirty) { action(); return; }
        unsavedText.Text = "You have unsaved changes to " + (string.IsNullOrWhiteSpace(name) ? "this theme" : name + ". Save them first?");
        afterPrompt = action; unsaved.IsVisible = true;
    }
    private void Proceed() { var next = afterPrompt; afterPrompt = null; unsaved.IsVisible = false; next?.Invoke(); }
    private void SaveCurrent() { model.SaveTheme(Working(), original); original = name; shown = name; dirty = false; Refill(name); }
    private ThemeDefinition Working() => new(name, dark, new Dictionary<string, string>(colours), false, blockStyle, blockTint, blockEdge);
    private void Show(string themeName)
    {
        var theme = model.Themes.First(t => t.Name == themeName);
        shown = themeName; original = theme.BuiltIn ? null : theme.Name; dirty = false;
        name = theme.Name; dark = theme.Dark; blockStyle = theme.BlockStyle; blockTint = theme.BlockTint; blockEdge = theme.BlockEdge; colours.Clear();
        foreach (var token in ThemeTokens.Names) colours[token] = theme.Colour(token);
        duplicate.IsEnabled = true; delete.IsEnabled = !theme.BuiltIn;
        Rebuild(theme.BuiltIn);
    }
    // Dragging a slider reports many changes a second, and each preview repaints the whole app. So the first shows at once and the rest are
    // gathered, with the latest shown a moment later, which feels the same and keeps the slider smooth.
    private static readonly TimeSpan PreviewGap = TimeSpan.FromMilliseconds(70);
    private DateTime lastPreview = DateTime.MinValue; private Avalonia.Threading.DispatcherTimer? pendingPreview;
    private void Changed()
    {
        dirty = true; error.Text = "";
        var wait = PreviewGap - (DateTime.UtcNow - lastPreview);
        if (wait <= TimeSpan.Zero && pendingPreview is not { IsEnabled: true }) { lastPreview = DateTime.UtcNow; model.PreviewTheme(Working()); return; }
        if (pendingPreview is { IsEnabled: true }) return; // The one already waiting will show the latest values.
        pendingPreview = new Avalonia.Threading.DispatcherTimer { Interval = wait > TimeSpan.Zero ? wait : PreviewGap };
        pendingPreview.Tick += (_, _) => { pendingPreview!.Stop(); if (dirty && Host is not null) { lastPreview = DateTime.UtcNow; model.PreviewTheme(Working()); } };
        pendingPreview.Start();
    }
    private void Rebuild(bool locked)
    {
        detail.Children.Clear();
        detail.Children.Add(Ui.Heading(locked ? name + " (built in)" : "Edit theme", 20));
        if (locked) detail.Children.Add(new TextBlock { Text = "Built in. Duplicate it to make your own.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        var nameBox = new TextBox { Text = name, Name = "ThemeName", IsEnabled = !locked, MaxLength = 40, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        nameBox.PropertyChanged += (_, e) => { if (e.Property != TextBox.TextProperty) return; name = nameBox.Text ?? ""; if (!filling) dirty = true; };
        detail.Children.Add(Ui.Text("Name", 12, "muted")); detail.Children.Add(nameBox);
        var baseBox = new ComboBox { Name = "ThemeBase", ItemsSource = new[] { "Light base", "Dark base" }, SelectedIndex = dark ? 1 : 0, IsEnabled = !locked, Width = 200 };
        baseBox.SelectionChanged += (_, _) => { dark = baseBox.SelectedIndex == 1; if (!locked) Changed(); };
        detail.Children.Add(Ui.Text("Base (decides how standard controls draw)", 12, "muted")); detail.Children.Add(baseBox);
        var styleBox = new ComboBox { Name = "ThemeBlockStyle", ItemsSource = new[] { "Account colours as chosen", "Softened to suit the theme", "Tinted with a coloured edge" },
            SelectedIndex = blockStyle == ThemeBlockStyle.Tinted ? 2 : blockStyle == ThemeBlockStyle.Softened ? 1 : 0, IsEnabled = !locked, Width = 260 };
        detail.Children.Add(Ui.Text("Account colours on the timeline and week", 12, "muted")); detail.Children.Add(styleBox);
        // The tinted rules, shown only when they apply: how much of the account's colour is in the fill, and how far the edge moves towards the text colour.
        var tint = new Slider { Minimum = 0, Maximum = 1, Value = blockTint, Width = 220, IsEnabled = !locked, Name = "BlockTint" };
        var edge = new Slider { Minimum = 0, Maximum = 1, Value = blockEdge, Width = 220, IsEnabled = !locked, Name = "BlockEdge" };
        var rules = new StackPanel { Spacing = 6, IsVisible = blockStyle == ThemeBlockStyle.Tinted, Name = "BlockRules" };
        rules.Children.Add(Ui.Text("How much account colour is in the fill", 12, "muted")); rules.Children.Add(tint);
        rules.Children.Add(Ui.Text("How far the edge moves towards the text colour", 12, "muted")); rules.Children.Add(edge);
        styleBox.SelectionChanged += (_, _) =>
        {
            blockStyle = styleBox.SelectedIndex == 2 ? ThemeBlockStyle.Tinted : styleBox.SelectedIndex == 1 ? ThemeBlockStyle.Softened : ThemeBlockStyle.AsChosen;
            rules.IsVisible = blockStyle == ThemeBlockStyle.Tinted; if (!locked) Changed();
        };
        tint.PropertyChanged += (_, e) => { if (e.Property != RangeBase.ValueProperty || filling) return; blockTint = Math.Round(tint.Value, 2); if (!locked) Changed(); };
        edge.PropertyChanged += (_, e) => { if (e.Property != RangeBase.ValueProperty || filling) return; blockEdge = Math.Round(edge.Value, 2); if (!locked) Changed(); };
        detail.Children.Add(rules);
        detail.Children.Add(Ui.Text("Colours", 12, "muted"));
        var grid = Ui.Grid("200,Auto,*", string.Join(",", ThemeTokens.Names.Select(_ => "Auto"))); grid.RowSpacing = 6;
        var contrast = new StackPanel { Spacing = 3 };
        void Contrast()
        {
            contrast.Children.Clear();
            foreach (var (label, foreground, background, needed) in new[] { ("Text on the window", "Ink", "Surface", 4.5), ("Text on panels", "Ink", "Panel", 4.5), ("Quiet text on the window", "Muted", "Surface", 3.0),
                ("Tracking colour on the window", "Live", "Surface", 3.0), ("Overtime colour on the window", "Overtime", "Surface", 3.0) })
            {
                var ratio = ThemeColours.Contrast(colours[foreground], colours[background]);
                var line = new TextBlock { Text = ratio >= needed ? $"{label}: {ratio:0.0} to 1" : $"{label}: {ratio:0.0} to 1, hard to read (aim for {needed:0.#} or more)", FontSize = 12 };
                line.Classes.Add(ratio >= needed ? "muted" : "overtime"); contrast.Children.Add(line);
            }
        }
        for (var i = 0; i < ThemeTokens.Names.Length; i++)
        {
            var token = ThemeTokens.Names[i];
            Ui.Put(grid, Ui.Text(ThemeTokens.Describe(token), 14), 0, i);
            if (token == "ShadowColor")
            {
                // The shadow is black or white at some strength, so it is a strength slider rather than a colour.
                var strength = new Slider { Minimum = 0, Maximum = 255, Value = Convert.ToInt32(colours[token][1..3], 16), Width = 200, IsEnabled = !locked, Name = "ShadowStrength" };
                strength.PropertyChanged += (_, e) =>
                {
                    if (e.Property != RangeBase.ValueProperty || locked || filling) return;
                    colours[token] = "#" + ((int)strength.Value).ToString("X2") + colours[token][3..]; Changed();
                };
                Ui.Put(grid, strength, 1, i); continue;
            }
            Ui.Put(grid, ThemeColourButton(token, locked, () => { Contrast(); Changed(); }), 1, i);
        }
        detail.Children.Add(grid);
        detail.Children.Add(Ui.Text("Readability", 12, "muted")); Contrast(); detail.Children.Add(contrast);
        detail.Children.Add(error);
        if (!locked)
        {
            var save = Ui.DialogButton("Save theme", () => Run(SaveCurrent), "SaveTheme");
            var revert = Ui.DialogButton("Revert", () => Run(() => { model.EndThemePreview(); Show(shown); }), "RevertTheme");
            detail.Children.Add(Ui.Horizontal(save, revert));
        }
    }
    // A swatch that opens hue, saturation and lightness sliders and a hex box, all changing the same colour as it is dragged.
    private Button ThemeColourButton(string token, bool locked, Action changed)
    {
        var swatch = new Border { Width = 56, Height = 24, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Background = Brush.Parse(colours[token]) };
        var button = new Button { Content = swatch, Name = "Colour" + token, Padding = new Thickness(4), IsEnabled = !locked, Background = Brushes.Transparent };
        // A colour wheel (hue around it, strength out from the middle), a bar beside it for how light, and a box for the hex value. It is built the
        // first time it opens, since there are two dozen of them and most are never looked at.
        ColorSpectrum? wheel = null; ColorSlider? lightness = null; TextBox? hex = null; var syncing = false;
        void Apply(string value)
        {
            value = value.ToUpperInvariant(); if (value == colours[token]) return;
            colours[token] = value; swatch.Background = Brush.Parse(value);
            changed();
        }
        static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        void Show(Color colour)
        {
            if (wheel is null || lightness is null || hex is null) return;
            syncing = true; var hsv = colour.ToHsv(); wheel.HsvColor = hsv; lightness.HsvColor = hsv; hex.Text = Hex(colour); syncing = false;
        }
        var flyout = new Flyout();
        flyout.Opening += (_, _) =>
        {
            if (flyout.Content is not null) return;
            wheel = new ColorSpectrum { Name = "Wheel" + token, Shape = ColorSpectrumShape.Ring, Components = ColorSpectrumComponents.HueSaturation, Width = 240, Height = 240, IsEnabled = !locked };
            lightness = new ColorSlider { Name = "Lightness" + token, ColorModel = ColorModel.Hsva, ColorComponent = ColorComponent.Component3, IsAlphaVisible = false,
                Orientation = Avalonia.Layout.Orientation.Vertical, Width = 26, Height = 240, Margin = new Thickness(0, 0, 14, 0), IsEnabled = !locked };
            hex = new TextBox { Text = colours[token], Width = 120, IsEnabled = !locked, Name = "Hex" + token, HorizontalAlignment = HorizontalAlignment.Left };
            // Moving either one moves the other and the box, and typing a valid colour moves both.
            void FromPicker(object source, AvaloniaPropertyChangedEventArgs e)
            {
                if (syncing || e.Property.Name != "HsvColor" || wheel is null || lightness is null || hex is null) return;
                var hsv = ReferenceEquals(source, wheel) ? wheel.HsvColor : lightness.HsvColor;
                syncing = true; wheel.HsvColor = hsv; lightness.HsvColor = hsv; syncing = false;
                var colour = hsv.ToRgb(); hex.Text = Hex(colour); Apply(Hex(colour));
            }
            wheel.PropertyChanged += (_, e) => FromPicker(wheel, e); lightness.PropertyChanged += (_, e) => FromPicker(lightness, e);
            hex.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBox.TextProperty || syncing) return;
                var text = (hex.Text ?? "").Trim(); if (!text.StartsWith('#')) text = "#" + text;
                if (ThemeTokens.IsValid(token, text) && !text.Equals(colours[token], StringComparison.OrdinalIgnoreCase)) { Apply(text); syncing = true; var hsv = Color.Parse(text).ToHsv(); wheel.HsvColor = hsv; lightness.HsvColor = hsv; syncing = false; }
            };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { lightness, wheel } };
            flyout.Content = new StackPanel { Spacing = 10, Margin = new Thickness(6), Children = { row, hex } };
            Show(Color.Parse(colours[token]));
        };
        flyout.Opened += (_, _) => Show(Color.Parse(colours[token]));
        button.Flyout = flyout;
        return button;
    }
}
