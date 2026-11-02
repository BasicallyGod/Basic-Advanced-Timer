using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Timer.Desktop;

/// <summary>A disposable visual workbench. It never receives the timer model or writes preferences.</summary>
public sealed class NeumorphicGalleryWindow : Window
{
    private static readonly string[] States = { "Ready", "Hover", "Pressed", "On", "Disabled", "Focus" };
    private readonly Dictionary<string, List<Control>> sections = new();
    private readonly Dictionary<string, Button> sectionButtons = new();
    private readonly List<ScrollViewer> paneScrolls = new();
    private readonly GalleryMaterialStudy material;
    private readonly Button currentMaterial, proposedMaterial;
    public bool IsProposed { get; private set; }

    public void SetProposed(bool proposed)
    {
        IsProposed = proposed; material.SetProposed(proposed);
        currentMaterial.Classes.Set("optOn", !proposed); proposedMaterial.Classes.Set("optOn", proposed);
    }

    public void ShowSection(string id)
    {
        if (!sections.ContainsKey(id)) return;
        foreach (var (key, controls) in sections) foreach (var control in controls) control.IsVisible = key == id;
        foreach (var (key, button) in sectionButtons) button.Classes.Set("optOn", key == id);
        foreach (var scroll in paneScrolls) scroll.Offset = default;
    }

    private void Register(string id, Control control)
    {
        if (!sections.TryGetValue(id, out var controls)) sections[id] = controls = new();
        controls.Add(control); control.IsVisible = id == "states";
    }

    public static void Open(Window owner)
    {
        if (!BuildInfo.DeveloperFeatures) return;
        if (owner.OwnedWindows.OfType<NeumorphicGalleryWindow>().FirstOrDefault() is { } existing)
        {
            existing.WindowState = WindowState.Normal; existing.Show(); existing.Activate(); return;
        }
        var gallery = new NeumorphicGalleryWindow { Topmost = owner.Topmost };
        if ((owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary) is { } screen)
        {
            var width = Math.Max(1, Math.Min(gallery.Width, screen.WorkingArea.Width / screen.Scaling - 40));
            var height = Math.Max(1, Math.Min(gallery.Height, screen.WorkingArea.Height / screen.Scaling - 60));
            gallery.MinWidth = Math.Min(gallery.MinWidth, width); gallery.MinHeight = Math.Min(gallery.MinHeight, height);
            gallery.Width = width; gallery.Height = height;
        }
        gallery.Show(owner);
    }

    public NeumorphicGalleryWindow()
    {
        material = new GalleryMaterialStudy(this);
        Title = "Neumorphic controls — Basic’s Advanced Timer"; WindowChrome.Attach(this);
        Width = 1140; Height = 760; MinWidth = 1100; MinHeight = 600;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = Ui.Grid("*", "Auto,*"); root.Margin = new Thickness(18);
        var header = new StackPanel { Spacing = 4, Margin = new Thickness(4, 0, 0, 14) };
        header.Children.Add(Ui.Heading("Neumorphic controls", 24));
        var intro = Ui.Text("Compare Current and Proposed across the same controls. These material experiments stay in this gallery.", 13, "muted"); intro.TextWrapping = TextWrapping.Wrap; header.Children.Add(intro);
        currentMaterial = NotesPage.Seg("GalleryCurrent", "Current", false, () => SetProposed(false));
        proposedMaterial = NotesPage.Seg("GalleryProposed", "Proposed", true, () => SetProposed(true));
        var mode = Ui.Horizontal(NotesPage.Switch(currentMaterial, proposedMaterial), Ui.Text("Raised keys · inset wells · one lighting direction", 12, "muted")); mode.Spacing = 14; mode.Margin = new Thickness(0, 7, 0, 3); header.Children.Add(mode);
        var choices = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, label) in NeumorphicGallerySamples.Sections)
        {
            var choice = NotesPage.Seg("GallerySection_" + id, label, id == "states", () => ShowSection(id));
            choice.Margin = new Thickness(0, 6, 6, 0); sectionButtons[id] = choice; choices.Children.Add(choice);
        }
        header.Children.Add(choices);
        Ui.Put(root, header);
        var themes = Ui.Grid("*,*"); themes.ColumnSpacing = 18; themes.MinWidth = 1064; themes.Width = 1064;
        Ui.Put(themes, Pane("Light", ThemeVariant.Light)); Ui.Put(themes, Pane("Dark", ThemeVariant.Dark), 1);
        var comparison = new ScrollViewer { Content = themes, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Horizontal scrolling measures with infinite width. Bound the comparison explicitly so wrapped captions cannot widen the panes.
        comparison.SizeChanged += (_, _) => themes.Width = Math.Max(1064, comparison.Bounds.Width);
        Ui.Put(root, comparison, 0, 1); Content = root;
        material.CollectSamples(); SetProposed(true);
    }

    private ThemeVariantScope Pane(string name, ThemeVariant variant)
    {
        var content = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        var matrix = new StackPanel { Spacing = 2 };
        var labels = Ui.Grid("82,*,*,*,*,*,*");
        for (var i = 0; i < States.Length; i++)
        {
            var label = Ui.Text(States[i], 11, "muted"); label.HorizontalAlignment = HorizontalAlignment.Center;
            Ui.Put(labels, label, i + 1);
        }
        matrix.Children.Add(labels);
        foreach (var family in new[] { "Important", "Primary", "Action", "Icon", "Filter", "Break" })
        {
            var row = Ui.Grid("82,*,*,*,*,*,*"); row.Height = 56;
            var title = Ui.Text(family, 12.5); title.VerticalAlignment = VerticalAlignment.Center; Ui.Put(row, title);
            for (var i = 0; i < States.Length; i++)
            {
                var state = States[i];
                if (state == "On" && family is "Primary" or "Action" or "Icon")
                {
                    var empty = Ui.Text("—", 12, "muted"); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
                    Ui.Put(row, empty, i + 1); continue;
                }
                var button = Sample(family, state == "On" || state == "Disabled" && family == "Important", name + "_" + family + "_" + state);
                button.IsEnabled = state != "Disabled"; button.Focusable = false; button.IsHitTestVisible = false;
                if (state is "Hover" or "Pressed" or "Focus") button.Classes.Add("preview" + state);
                var cell = new Border { Padding = new Thickness(5), Child = button }; Ui.Put(row, cell, i + 1);
            }
            matrix.Children.Add(row);
        }
        var stateCard = Card(matrix); stateCard.Name = name + "_StateMatrix"; content.Children.Add(stateCard);
        var caption = Ui.Text("Pressed is held down. On is a persistent choice. Focus is keyboard focus.", 11.5, "muted"); caption.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(caption);

        var live = new StackPanel { Spacing = 10 };
        live.Children.Add(Ui.Heading("Try it", 15));
        var status = Ui.Text("Sample controls only.", 12, "muted"); status.Name = name + "_GalleryStatus";
        var count = 0;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(Control control) { control.Margin = new Thickness(0, 0, 10, 8); actions.Children.Add(control); }
        Add(NeumorphicControls.Action("Save", () => status.Text = $"Save clicked {++count} times.", name + "_LiveAction"));
        Add(NeumorphicControls.Action("Add", () => status.Text = $"Add clicked {++count} times.", name + "_LivePrimary", primary: true));
        Add(NeumorphicControls.Icon(IconKind.Plus, "Add sample", () => status.Text = $"Icon clicked {++count} times.", name + "_LiveIcon"));
        Add(NeumorphicControls.Important(true, () => status.Text = "Importance sample changed.", name + "_LiveImportant"));
        var filter = NeumorphicControls.Toggle("Filter", false, name + "_LiveFilter");
        filter.Click += (_, _) => status.Text = filter.IsChecked == true ? "Filter sample on." : "Filter sample off."; Add(filter);
        var takeBreak = NeumorphicControls.Toggle("Break", false, name + "_LiveBreak", "break");
        takeBreak.Click += (_, _) => status.Text = takeBreak.IsChecked == true ? "Break sample on." : "Break sample off."; Add(takeBreak);
        live.Children.Add(actions); live.Children.Add(status);
        var chart = NotesPage.Seg(name + "_Chart", "Chart", true, () => { });
        var table = NotesPage.Seg(name + "_Table", "Table", false, () => { });
        chart.Click += (_, _) => { chart.Classes.Set("optOn", true); table.Classes.Set("optOn", false); };
        table.Click += (_, _) => { chart.Classes.Set("optOn", false); table.Classes.Set("optOn", true); };
        var fields = Ui.Horizontal(new TextBox { Name = name + "_SampleField", Watermark = "Existing text field", Width = 200, Height = 36, MinHeight = 36 }, NotesPage.Switch(chart, table));
        fields.Spacing = 12; live.Children.Add(fields); content.Children.Add(Card(live));

        var palette = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, token) in new[] { ("Action", "Accent"), ("Important", "ImportantStar"), ("Tracking", "Live"), ("Break", "Break"), ("Overdue", "Overtime") })
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(token));
            var swatch = Ui.Horizontal(dot, Ui.Text(label, 11.5, "muted")); swatch.Margin = new Thickness(0, 0, 14, 6); palette.Children.Add(swatch);
        }
        content.Children.Add(palette);
        content.Name = name + "_Section_states"; Register("states", content);
        var pages = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        content.Margin = new Thickness(0); pages.Children.Add(Ui.Heading(name, 21)); pages.Children.Add(content);
        foreach (var (id, _) in NeumorphicGallerySamples.Sections.Where(s => s.Id != "states"))
        {
            var section = NeumorphicGallerySamples.Build(id, name); section.Name = name + "_Section_" + id;
            Register(id, section); pages.Children.Add(section);
        }
        var scroll = new ScrollViewer { Content = pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        paneScrolls.Add(scroll);
        var pane = new Border { Name = "Gallery" + name, CornerRadius = new CornerRadius(18), Child = scroll };
        pane.Bind(Border.BackgroundProperty, pane.GetResourceObservable("Surface"));
        return new ThemeVariantScope { RequestedThemeVariant = variant, Child = pane };
    }

    private static Button Sample(string family, bool on, string name)
    {
        Button button = family switch
        {
            "Important" => NeumorphicControls.Important(on, () => { }, name),
            "Primary" => NeumorphicControls.Action("Save", () => { }, name, primary: true),
            "Action" => NeumorphicControls.Action("Save", () => { }, name),
            "Icon" => NeumorphicControls.Icon(IconKind.Plus, "Add sample", () => { }, name),
            "Break" => NeumorphicControls.Toggle("Break", on, name, "break"),
            _ => NeumorphicControls.Toggle("Filter", on, name)
        };
        if (family is not ("Important" or "Icon")) { button.Width = 54; button.Height = 36; button.Padding = new Thickness(4, 0); }
        button.HorizontalAlignment = HorizontalAlignment.Center; return button;
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderThickness = new Thickness(1) };
        card.Classes.Add("galleryCard"); card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Panel")); card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Line")); return card;
    }
}
