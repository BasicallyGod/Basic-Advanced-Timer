using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>Isolated samples of the app's existing control families and proposed materials. No timer model.</summary>
internal static class NeumorphicGallerySamples
{
    internal static readonly (string Id, string Label)[] Sections =
    {
        ("states", "States"), ("material", "Material rules"), ("buttons", "Button families"), ("surfaces", "On surfaces"),
        ("navigation", "Menus & navigation"), ("inputs", "Ticks & inputs"), ("colours", "Colour experiments")
    };
    internal static readonly (string Name, string Hex)[] Palette =
    {
        ("Sage", "#AEB9AC"), ("Lavender", "#B3A6E0"), ("Butter", "#F0C24E"), ("Clay", "#EE7C47"),
        ("Coral", "#E3584D"), ("Ink", "#3C4252"), ("Mint", "#B7D3CB"), ("Blush", "#F0B7A8"), ("Slate", "#8892A8")
    };

    internal static Control Build(string id, string prefix) => id switch
    {
        "buttons" => Buttons(prefix), "surfaces" => Surfaces(prefix), "navigation" => Navigation(prefix),
        "inputs" => Inputs(prefix), "colours" => Colours(prefix), "material" => Material(prefix), _ => new Border()
    };
    private static StackPanel Column()
    {
        var column = new StackPanel { Spacing = 14 };
        // Keep standalone comparison captures on the same ground as the live gallery.
        column.Bind(Panel.BackgroundProperty, column.GetResourceObservable("Surface")); return column;
    }
    private static TextBlock Caption(string text) { var label = Ui.Text(text, 12, "muted"); label.TextWrapping = TextWrapping.Wrap; return label; }
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in controls) { c.Margin = new Thickness(5, 5, 9, 7); row.Children.Add(c); }
        return row;
    }
    private static Border Card(string title, params Control[] controls)
    {
        var body = new StackPanel { Spacing = 10 }; body.Children.Add(Ui.Heading(title, 15));
        foreach (var c in controls) body.Children.Add(c);
        var card = new Border { Child = body, Padding = new Thickness(14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        card.Classes.Add("galleryCard"); card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Panel")); card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Line")); return card;
    }
    private static Button Current(string prefix, string words, string? role = null, IconKind? icon = null)
    {
        var button = Ui.Ctl(prefix, words, icon, words, () => { }); if (role is not null) button.Classes.Add(role); return button;
    }
    private static Control Buttons(string p)
    {
        var body = Column(); body.Children.Add(Caption("The same action families in Current and Proposed. Compare the shared depth, edge treatment and circular icon keys without performing timer actions."));
        var disabled = Current(p + "_CurrentSaveDisabled", "Save", "primary"); disabled.IsEnabled = false;
        body.Children.Add(Card("Actions and dialog buttons", Row(Current(p + "_CurrentAdd", "Add time", "primary", IconKind.Plus), Current(p + "_CurrentSave", "Save", "primary"), disabled,
            Ui.DialogButton("Save", () => { }, p + "_DialogSave"), Ui.DialogButton("Cancel", () => { }, p + "_DialogCancel"))));
        body.Children.Add(Card("Toolbar, paired controls and icon buttons", Row(
            Ui.Ctl(p + "_Undo", null, IconKind.Undo, "Undo", () => { }), Ui.Ctl(p + "_ZoomOut", null, IconKind.Minus, "Zoom out", () => { }),
            Ui.Ctl(p + "_ZoomIn", null, IconKind.Plus, "Zoom in", () => { }), Ui.IconButton(IconKind.Gear, "Settings", p + "_Gear", () => { }),
            NeumorphicControls.Important(true, () => { }, p + "_Star")), Row(Current(p + "_Outline", "Export", "accentOutline"), Current(p + "_Small", "Today", "small"))));
        body.Children.Add(Card("Chips and local choices", Row(NoteParts.Chip("Account one", "plain"), NoteParts.Chip("Today, 14:00", "accent", IconKind.Bell),
            NoteParts.Chip("Overdue", "late", IconKind.Bell)), Row(Current(p + "_QuickTime", "In 15 minutes", "pillOpt"), Current(p + "_ChosenTime", "Tomorrow", "on"))));
        var quit = Current(p + "_Quit", "Quit timer", "quit", IconKind.Close);
        body.Children.Add(Card("Joined keys and exit action", Row(quit), Row(
            Ui.KeyPair(Ui.Ctl(p + "_PairUndo", null, IconKind.Undo, "Undo", () => { }), Ui.Ctl(p + "_PairRedo", null, IconKind.Redo, "Redo", () => { })),
            Ui.KeyPair(Ui.Ctl(p + "_PairMinus", null, IconKind.Minus, "Zoom out", () => { }), Ui.Ctl(p + "_PairPlus", null, IconKind.Plus, "Zoom in", () => { })))));
        return body;
    }
    private static Control Surfaces(string p)
    {
        var body = Column(); body.Children.Add(Caption("The same live controls on flat, raised, inset and textured surfaces. Try hover, press and keyboard focus; compare the edges and shadow clearance."));
        foreach (var surface in new[] { "Flat", "Raised", "Inset", "Textured" })
        {
            var controls = Row(NeumorphicControls.Action("Save", () => { }, p + "_" + surface + "Save"),
                NeumorphicControls.Action("Add", () => { }, p + "_" + surface + "Add", true),
                NeumorphicControls.Important(true, () => { }, p + "_" + surface + "Star"),
                new CheckBox { Content = "Done", IsChecked = true, Name = p + "_" + surface + "Tick" });
            var layers = new Grid { Children = { controls } };
            if (surface == "Textured")
            {
                var texture = new Border { IsHitTestVisible = false, Opacity = .12 };
                texture.Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(4, 4, RelativeUnit.Absolute), SpreadMethod = GradientSpreadMethod.Repeat,
                    GradientStops = { new GradientStop(Color.Parse("#8892A8"), 0), new GradientStop(Color.Parse("#8892A8"), .35), new GradientStop(Colors.Transparent, .35), new GradientStop(Colors.Transparent, 1) } };
                layers.Children.Insert(0, texture);
            }
            var well = new Border { Name = p + "_Surface" + surface, Child = layers, Padding = new Thickness(14), Margin = new Thickness(6), CornerRadius = new CornerRadius(16) };
            well.Bind(Border.BackgroundProperty, well.GetResourceObservable(surface == "Inset" ? "Well" : "Panel"));
            if (surface is "Raised" or "Inset") well.Bind(Border.BoxShadowProperty, well.GetResourceObservable(surface == "Raised" ? "ShadowThumb" : "ShadowSunk"));
            body.Children.Add(Card(surface, well));
        }
        return body;
    }
    private static Control Navigation(string p)
    {
        var body = Column(); body.Children.Add(Caption("Selection uses a recessed row or a raised thumb inside a recessed rail. All navigation stays in this gallery; dropdown and context-menu examples open real menus."));
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var tabButtons = new List<Button>();
        foreach (var (label, icon) in new[] { ("Timeline", IconKind.Timeline), ("Notes", IconKind.NotesList), ("Week overview", IconKind.Week) })
        {
            var button = Ui.Button("", () => { }, p + "_View" + label.Replace(" ", "")); button.Classes.Add("viewTab");
            button.Padding = new Thickness(10, 0); button.Content = Ui.Horizontal(new IconGlyph(icon), Ui.Text(label, 13));
            Avalonia.Automation.AutomationProperties.SetName(button, label); button.Classes.Set("selected", tabButtons.Count == 0);
            button.Click += (_, _) => { foreach (var choice in tabButtons) choice.Classes.Set("selected", choice == button); };
            tabButtons.Add(button); tabs.Children.Add(button);
        }
        var track = new Border { Name = p + "_ThreeWay", Child = tabs, HorizontalAlignment = HorizontalAlignment.Left }; track.Classes.Add("viewTabs");
        var pairs = new List<Control>();
        foreach (var (a, b) in new[] { ("Notes", "Meetings"), ("Note", "Meeting"), ("Chart", "Table") })
        {
            var first = NotesPage.Seg(p + "_Pair" + a, a, true, () => { }); var second = NotesPage.Seg(p + "_Pair" + b, b, false, () => { });
            first.Click += (_, _) => { first.Classes.Set("optOn", true); second.Classes.Set("optOn", false); };
            second.Click += (_, _) => { first.Classes.Set("optOn", false); second.Classes.Set("optOn", true); };
            pairs.Add(NotesPage.Switch(first, second));
        }
        body.Children.Add(Card("Dashboard three-way selector", track)); body.Children.Add(Card("Two-way selectors", Row(pairs.ToArray())));
        var lists = Ui.Grid("*,*"); lists.ColumnSpacing = 14;
        foreach (var (kind, column) in new[] { ("Settings", 0), ("Notes", 1) })
        {
            var list = new StackPanel { Spacing = 2 }; var choices = new List<Button>();
            foreach (var label in kind == "Settings" ? new[] { "Appearance", "Behaviour", "Working hours" } : new[] { "All", "Important", "Today" })
            {
                var button = Ui.Button(label, () => { }, p + "_Menu" + kind + label.Replace(" ", "")); button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Classes.Add(kind == "Settings" ? "settingsNav" : "vitem");
                var selectedClass = kind == "Settings" ? "selected" : "vitemOn"; button.Classes.Set(selectedClass, choices.Count == 0);
                button.Click += (_, _) => { foreach (var choice in choices) choice.Classes.Set(selectedClass, choice == button); }; choices.Add(button); list.Children.Add(button);
            }
            Ui.Put(lists, list, column);
        }
        body.Children.Add(Card("Settings and Notes menu selections", lists));
        var selection = new ComboBox { Name = p + "_MenuDropdown", ItemsSource = new[] { "All accounts", "Account one", "Account two" }, SelectedIndex = 0, Width = 190 };
        var contextButton = Current(p + "_OpenContextMenu", "Open context menu");
        var showDone = new MenuItem { Header = "Show completed", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        var menu = new ContextMenu { ItemsSource = new Control[] { new MenuItem { Header = "Add time…" }, showDone, new Separator(), new MenuItem { Header = "Paste here", IsEnabled = false } } };
        contextButton.ContextMenu = menu; contextButton.Click += (_, _) => menu.Open(contextButton);
        body.Children.Add(Card("Dropdown and context menu", Row(selection, contextButton), Caption("Open a menu to compare highlighted, checked and disabled entries. Right-click the button also opens its context menu.")));
        return body;
    }
    private static Control Inputs(string p)
    {
        var body = Column(); body.Children.Add(Caption("Native controls keep their keyboard and accessibility behaviour. Checkboxes now share the circular notes treatment, including mixed, disabled and focus states."));
        body.Children.Add(Card("Circular ticks", Row(new CheckBox { Name = p + "_TickOff", Content = "Off", IsChecked = false },
            new CheckBox { Name = p + "_TickOn", Content = "On", IsChecked = true },
            new CheckBox { Name = p + "_TickMixed", Content = "Mixed", IsThreeState = true, IsChecked = null },
            new CheckBox { Name = p + "_TickDisabled", Content = "Disabled", IsChecked = true, IsEnabled = false }),
            Row(new CheckBox { Content = "Mon", IsChecked = true }, new CheckBox { Content = "Tue", IsChecked = true }, new CheckBox { Content = "Wed" }),
            new CheckBox { Name = p + "_LongTick", Content = "Start the timer when I sign in to Windows" }));
        body.Children.Add(Card("One choice · radio buttons", Row(
            new RadioButton { Name = p + "_RadioFirst", Content = "Day", GroupName = p + "_Period", IsChecked = true },
            new RadioButton { Name = p + "_RadioSecond", Content = "Week", GroupName = p + "_Period" },
            new RadioButton { Content = "Month", GroupName = p + "_Period" },
            new RadioButton { Content = "Disabled", GroupName = p + "_DisabledPeriod", IsEnabled = false })));
        body.Children.Add(Card("On / off · inset track, raised knob", Row(
            new ToggleSwitch { Name = p + "_SwitchOff", IsChecked = false }, new ToggleSwitch { Name = p + "_SwitchOn", IsChecked = true },
            new ToggleSwitch { Name = p + "_SwitchDisabled", IsChecked = true, IsEnabled = false })));
        body.Children.Add(Card("Text, budget and time fields", new TextBox { Name = p + "_InputText", Watermark = "Write a note", Height = 36 },
            new TextBox { Watermark = "Disabled field", Height = 36, IsEnabled = false },
            Row(new TextBox { Name = p + "_Budget", Text = "37.5", Width = 100, Height = 36 }, new TextBox { Text = "09:00", Width = 100, Height = 36 })));
        body.Children.Add(Card("Dropdown and scale", Row(new ComboBox { Name = p + "_InputDropdown", ItemsSource = new[] { "Day", "Week", "Month", "Quarter" }, SelectedIndex = 1, Width = 150 }),
            new Slider { Name = p + "_Scale", Minimum = 50, Maximum = 120, Value = 90, Width = 280, HorizontalAlignment = HorizontalAlignment.Left }));
        var calendar = new SoftDatePicker { Name = p + "_Calendar", HorizontalAlignment = HorizontalAlignment.Left };
        calendar.Classes.Add("flyoutCard"); calendar.View(new DateTime(2026, 11, 2), new DateTime(2026, 11, 2));
        var chosen = Caption("Mon 2 Nov"); calendar.DayPicked += day => { calendar.View(day, day); chosen.Text = day.ToString("ddd d MMM"); };
        body.Children.Add(Card("Application date picker", calendar, chosen));
        return body;
    }
    private static Control Material(string p)
    {
        var body = Column(); body.Children.Add(Caption("One material, lit from the upper left. Size sets depth; state sets whether a control rises out of or presses into that material."));
        body.Children.Add(Card("Raised · an available action", Row(NeumorphicControls.Action("Save", () => { }, p + "_RuleRaised"), NeumorphicControls.Icon(IconKind.Plus, "Add", () => { }, p + "_RuleCompact")),
            Caption("A small key casts a tighter shadow than a card. Hover lifts it slightly; pressing turns the same light and shadow inward.")));
        body.Children.Add(Card("Inset · a value or a persistent choice", Row(new TextBox { Text = "09:00", Width = 120, Height = 36 }, NeumorphicControls.Toggle("Selected", true, p + "_RuleInset")),
            Caption("Fields and wells are recessed. Selected toggles keep their label or glyph as a second cue, so depth and colour never have to carry the state alone.")));
        var first = NotesPage.Seg(p + "_RuleFirst", "Timeline", true, () => { }); var second = NotesPage.Seg(p + "_RuleSecond", "Notes", false, () => { });
        first.Click += (_, _) => { first.Classes.Set("optOn", true); second.Classes.Set("optOn", false); };
        second.Click += (_, _) => { first.Classes.Set("optOn", false); second.Classes.Set("optOn", true); };
        body.Children.Add(Card("A raised thumb inside an inset rail", NotesPage.Switch(first, second),
            Caption("The track is the recess; the selected thumb is raised. This applies to two-way and three-way navigation, sliders and switches.")));
        body.Children.Add(Card("Colour and quiet states", Row(NeumorphicControls.Important(true, () => { }, p + "_RuleStar"), new CheckBox { Content = "Checked", IsChecked = true }),
            Caption("Gold remains a stroke star on neutral material. Primary actions may use a colour fill. Disabled actions are grey and lose elevation; keyboard focus adds a stable outline.")));
        return body;
    }
    private static Control Colours(string p)
    {
        var body = Column(); body.Children.Add(Caption("Your original palette, applied to sample buttons. Colour placement is exploratory; blue selection, green recording, amber break and red warning roles remain intact in the app."));
        foreach (var (name, hex) in Palette)
        {
            var fill = NeumorphicControls.Action(name, () => { }, p + "_Colour" + name, true);
            var ink = ThemeColours.Contrast(hex, "#000000") >= ThemeColours.Contrast(hex, "#FFFFFF") ? "#000000" : "#FFFFFF";
            fill.Resources["Accent"] = Brush.Parse(hex); fill.Resources["AccentInk"] = Brush.Parse(ink);
            var soft = NeumorphicControls.Action("Soft " + name, () => { }, p + "_Soft" + name);
            soft.Resources["Panel"] = Brush.Parse(ThemeColours.Mix(hex, "#FFFFFF", .60));
            soft.Resources["KeyHover"] = Brush.Parse(ThemeColours.Mix(hex, "#FFFFFF", .72));
            soft.Resources["NeoPressed"] = Brush.Parse(ThemeColours.Mix(hex, "#FFFFFF", .45));
            soft.Resources["Ink"] = Brush.Parse("#222631"); soft.Resources["KeyEdge"] = Brush.Parse(hex);
            body.Children.Add(Card(name + " · " + hex, Row(fill, soft)));
        }
        return body;
    }
}
