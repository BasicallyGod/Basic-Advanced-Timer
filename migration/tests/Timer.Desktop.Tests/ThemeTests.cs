using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void EveryBuiltInThemeSetsEveryPartWithValidColoursAndAUniqueName()
    {
        Assert.Equal(ThemeLibrary.BuiltIn.Count, ThemeLibrary.BuiltIn.Select(t => t.Name.ToUpperInvariant()).Distinct().Count());
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            Assert.True(theme.BuiltIn);
            foreach (var token in ThemeTokens.Names) Assert.True(ThemeTokens.IsValid(token, theme.Colours.GetValueOrDefault(token)), $"{theme.Name} {token}");
        }
        Assert.Contains(ThemeLibrary.BuiltIn, t => t.Name == "Light" && !t.Dark); Assert.Contains(ThemeLibrary.BuiltIn, t => t.Name == "Dark" && t.Dark);
    }

    [Fact]
    public void TextIsReadableOnTheBackgroundInEveryThemeAndEveryStateTint()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Surface")) >= 7, $"{theme.Name} text on surface");
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Panel")) >= 7, $"{theme.Name} text on panel");
            Assert.True(ThemeColours.Contrast(theme.Colour("Muted"), theme.Colour("Surface")) >= 3.5, $"{theme.Name} quiet text");
            Assert.True(ThemeColours.Contrast(theme.Colour("AccentInk"), theme.Colour("Accent")) >= 4.5, $"{theme.Name} text on the accent");
            foreach (var state in new[] { ThemeDerivation.Break, ThemeDerivation.Overtime })
            {
                var variant = ThemeDerivation.Variant(theme, state);
                // The capsule is a strong colour with large, heavy text on it, so the bar is the standard 4.5 to 1 for readable text, not the 7 of the calm surfaces.
                Assert.True(ThemeColours.Contrast(variant["Ink"], variant["Surface"]) >= 4.5, $"{theme.Name} {state} text on surface");
                Assert.True(ThemeColours.Contrast(variant["Ink"], variant["Panel"]) >= 4.0, $"{theme.Name} {state} text on panel");
                Assert.True(ThemeColours.Contrast(variant["Muted"], variant["Surface"]) >= 3.0, $"{theme.Name} {state} quiet text");
            }
        }
    }

    [Fact]
    public void ThePillWearsItsStateColourWithPlainTextOnItAndLeavesTheThemesOtherColoursAlone()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            var recording = ThemeDerivation.Variant(theme, ThemeDerivation.Recording); var amber = ThemeDerivation.Variant(theme, ThemeDerivation.Break); var red = ThemeDerivation.Variant(theme, ThemeDerivation.Overtime);
            Assert.Equal(theme.Colour("PillSurface"), recording["Surface"]); Assert.Equal(theme.Colour("PillInk"), recording["Ink"]);
            Assert.Equal(theme.Colour("PillBreak"), amber["Surface"]); Assert.Equal(theme.Colour("PillOvertime"), red["Surface"]);
            // The colours that normally mean something become the pill's text colour, since the whole pill already says it.
            foreach (var variant in new[] { recording, amber, red }) foreach (var token in new[] { "Break", "Overtime" }) Assert.Equal(variant["Ink"], variant[token]);
            foreach (var token in new[] { "Accent", "AccentInk", "BudgetWarning", "ShadowColor" }) { Assert.Equal(theme.Colour(token), amber[token]); Assert.Equal(theme.Colour(token), red[token]); }
            Assert.NotEqual(amber["Surface"], red["Surface"]); Assert.NotEqual(recording["Surface"], amber["Surface"]);
            // A dark theme's overtime colour is made for text, so the pill deepens it: white text on it must still read.
            if (theme.Dark) Assert.True(ThemeColours.Hsl(red["Surface"]).L < .45, theme.Name);
        }
    }

    [Fact]
    public void ColourMathMixesConvertsAndMeasuresContrast()
    {
        Assert.Equal("#808080", ThemeColours.Mix("#000000", "#FFFFFF", 0.5)); Assert.Equal("#FF0000", ThemeColours.FromHsl(0, 1, .5)); Assert.Equal("#FFFFFF", ThemeColours.FromHsl(0, 0, 1));
        Assert.Equal(21, ThemeColours.Contrast("#000000", "#FFFFFF"), 1); Assert.Equal(1, ThemeColours.Contrast("#336699", "#336699"), 3);
        var (h, s, l) = ThemeColours.Hsl("#3A7CA5"); Assert.Equal("#3A7CA5", ThemeColours.FromHsl(h, s, l));
    }

    [Fact]
    public void YourOwnThemesSurviveSavingAndADamagedOneIsSkippedWithoutLosingTheRest()
    {
        var mineColours = ThemeLibrary.Dark.Colours.ToDictionary(c => c.Key, c => c.Value); mineColours["Surface"] = "#102030";
        var mine = new ThemeDefinition("My blue", true, mineColours, false, ThemeBlockStyle.Softened);
        var lines = new[] { ThemeStorage.ToLine(mine), "this is not json", ThemeStorage.ToLine(ThemeLibrary.Dark with { Name = "Dark" }), "{\"Name\":\"Plain\",\"Dark\":false,\"Colours\":{\"Surface\":\"nonsense\",\"Ink\":\"#112233\"}}" };
        var loaded = ThemeStorage.FromLines(lines);
        Assert.Equal(new[] { "My blue", "Plain" }, loaded.Select(t => t.Name)); // The broken line and the one reusing a built-in name are left out.
        Assert.Equal("#102030", loaded[0].Colour("Surface")); Assert.Equal(ThemeBlockStyle.Softened, loaded[0].BlockStyle); Assert.True(loaded[0].Dark); Assert.False(loaded[0].BuiltIn);
        Assert.Equal("#112233", loaded[1].Colour("Ink")); Assert.Equal(ThemeLibrary.Light.Colour("Surface"), loaded[1].Colour("Surface")); // A bad colour falls back to the base theme's.
        Assert.All(loaded, t => Assert.All(ThemeTokens.Names, n => Assert.True(ThemeTokens.IsValid(n, t.Colour(n)))));
        Assert.False(ThemeStorage.IsUsableName("System")); Assert.False(ThemeStorage.IsUsableName(" padded ")); Assert.False(ThemeStorage.IsUsableName("")); Assert.True(ThemeStorage.IsUsableName("Evening"));
    }

    [Fact]
    public void SoftenedBlocksMoveTowardsTheSurfaceAndAsChosenBlocksDoNot()
    {
        var soft = ThemeLibrary.Dark with { BlockStyle = ThemeBlockStyle.Softened };
        var colour = "#91B7D5"; var softened = ThemeDerivation.Soften(soft, colour);
        Assert.NotEqual(colour, softened); Assert.True(ThemeColours.Luminance(softened) < ThemeColours.Luminance(colour)); // Towards a dark surface.
        Assert.Equal(colour, ThemeDerivation.Soften(ThemeLibrary.Dark, colour));
        Assert.True(ThemeColours.Luminance(ThemeDerivation.Soften(ThemeLibrary.Light with { BlockStyle = ThemeBlockStyle.Softened }, colour)) > ThemeColours.Luminance(colour));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AUserThemeIsRegisteredSelectedStyledAndRemembered_AndAnUnknownNameFallsBackToSystem()
    {
        var colours = ThemeLibrary.Dark.Colours.ToDictionary(c => c.Key, c => c.Value); colours["Surface"] = "#102030"; colours["Ink"] = "#FAFAFA";
        var mine = new ThemeDefinition("Moonlit", true, colours);
        var folder = Path.Combine(Path.GetTempPath(), "timer-theme-" + Guid.NewGuid());
        try
        {
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine) with { Themes = new[] { mine } };
                var model = new TimerModel(engine, prefs, store);
                Assert.Contains(model.Themes, t => t.Name == "Moonlit"); model.SetTheme("Moonlit");
                Assert.Equal(ThemeEngine.VariantFor(mine), Avalonia.Application.Current!.RequestedThemeVariant);
                Assert.True(((Avalonia.Controls.IResourceHost)Avalonia.Application.Current).TryGetResource("Surface", ThemeEngine.VariantFor(mine), out var surface)); Assert.Equal(Avalonia.Media.Color.Parse("#102030"), ((Avalonia.Media.ISolidColorBrush)surface!).Color);
                Assert.NotEqual(ThemeEngine.VariantFor(mine), ThemeEngine.StateVariant(mine, ThemeDerivation.Break)); // The pill's break tint is its own variant.
                Assert.Throws<ArgumentException>(() => model.SetTheme("No such theme"));
                Assert.True(model.Save());
            }
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine());
                Assert.Equal("Moonlit", prefs.Theme); Assert.Equal("Moonlit", Assert.Single(prefs.Themes!).Name); Assert.Equal("#102030", prefs.Themes![0].Colour("Surface"));
            }
            // A theme name that no longer exists is not left selected.
            File.WriteAllText(Path.Combine(folder, "basics-advanced-timer.settings.json"), "{\"schemaVersion\":1,\"sections\":{\"migrationAppearance\":[\"Gone\",\"shadows=on\"]}}");
            using (var store = new Timer.Storage.TimerStore(folder)) Assert.Equal("System", store.Load(new TrackingEngine()).Theme);
        }
        finally { Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static T Find<T>(Avalonia.Controls.Control window, string name) where T : Avalonia.Controls.Control
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<T>().Single(c => c.Name == name);

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheThemeEditorDuplicatesEditsPreviewsSavesAndDeletesYourOwnThemes_AndLocksBuiltInOnes()
    {
        var model = TimerModel.CreateDemo(); var window = new ThemesDialog(model).Open(model);
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // A built-in theme is shown but cannot be edited.
            Assert.False(Find<Avalonia.Controls.Button>(window, "ColourSurface").IsEnabled); Assert.False(Find<Avalonia.Controls.TextBox>(window, "ThemeName").IsEnabled);
            Assert.DoesNotContain(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>(), b => b.Name == "SaveTheme");
            Assert.False(Find<Avalonia.Controls.Button>(window, "DeleteTheme").IsEnabled);
            var click = new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent);
            // Duplicating makes your own, editable copy and selects it.
            Find<Avalonia.Controls.Button>(window, "DuplicateTheme").RaiseEvent(click); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var copyName = model.Theme; Assert.EndsWith(" copy", copyName); Assert.False(model.Themes.Single(t => t.Name == copyName).BuiltIn);
            Assert.True(Find<Avalonia.Controls.Button>(window, "ColourSurface").IsEnabled); Assert.True(Find<Avalonia.Controls.Button>(window, "DeleteTheme").IsEnabled);
            // Typing a hex colour changes the app at once, before anything is saved.
            Edit(window, "#102030");
            var variant = ThemeEngine.VariantFor(model.Themes.Single(t => t.Name == copyName));
            Assert.True(((Avalonia.Controls.IResourceHost)Avalonia.Application.Current!).TryGetResource("Surface", variant, out var live)); Assert.Equal(Avalonia.Media.Color.Parse("#102030"), ((Avalonia.Media.ISolidColorBrush)live!).Color);
            Assert.NotEqual("#102030", model.Themes.Single(t => t.Name == copyName).Colour("Surface")); // Not kept yet.
            // Saving keeps it, with a new name.
            Find<Avalonia.Controls.TextBox>(window, "ThemeName").Text = "Evening";
            Find<Avalonia.Controls.Button>(window, "SaveTheme").RaiseEvent(click); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("Evening", model.Theme); Assert.Equal("#102030", model.Themes.Single(t => t.Name == "Evening").Colour("Surface")); Assert.DoesNotContain(model.Themes, t => t.Name == copyName);
            // A built-in name is refused with a message.
            Find<Avalonia.Controls.TextBox>(window, "ThemeName").Text = "Dark"; Find<Avalonia.Controls.Button>(window, "SaveTheme").RaiseEvent(click);
            Assert.Equal("Evening", model.Theme);
            // Deleting returns to System.
            Find<Avalonia.Controls.Button>(window, "DeleteTheme").RaiseEvent(click); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeLibrary.System, model.Theme); Assert.DoesNotContain(model.Themes, t => t.Name == "Evening");
        }
        finally { window.Close(); }
    }

    private static void Edit(ThemesDialog window, string hexColour)
    {
        // The picker is built the first time it opens, so it is opened (and put away again) before its box is typed in.
        var button = Find<Avalonia.Controls.Button>(window, "ColourSurface");
        var picker = (Avalonia.Controls.Flyout)button.Flyout!;
        if (picker.Content is null) { picker.ShowAt(button); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); picker.Hide(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants((Avalonia.Controls.Control)picker.Content!).OfType<Avalonia.Controls.TextBox>().Single(t => t.Name == "HexSurface").Text = hexColour;
    }
    private static void Press(ThemesDialog window, string name)
    {
        Find<Avalonia.Controls.Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
    private static Avalonia.Media.Color SurfaceOf(TimerModel model, string theme)
    {
        var variant = ThemeEngine.VariantFor(model.Themes.Single(t => t.Name == theme));
        Assert.True(((Avalonia.Controls.IResourceHost)Avalonia.Application.Current!).TryGetResource("Surface", variant, out var surface));
        return ((Avalonia.Media.ISolidColorBrush)surface!).Color;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingWithUnsavedEditsAsksAndDiscardPutsTheSavedLookBack()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Dark"); var copy = model.DuplicateTheme("Dark");
        var window = new ThemesDialog(model).Open(model);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Edit(window, "#FF00FF");
        window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible); Assert.True(Find<Avalonia.Controls.Border>(window, "UnsavedPrompt").IsVisible); // Asked instead of closing.
        Press(window, "UnsavedKeep");
        Assert.False(Find<Avalonia.Controls.Border>(window, "UnsavedPrompt").IsVisible); Assert.True(window.IsVisible);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF00FF"), SurfaceOf(model, copy)); // Still previewing the edit.
        window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Press(window, "UnsavedDiscard");
        Assert.False(window.IsVisible);
        Assert.Equal(Avalonia.Media.Color.Parse(ThemeLibrary.Dark.Colour("Surface")), SurfaceOf(model, copy));
        model.DeleteTheme(copy); model.SetTheme("System");
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SavingFromThePromptKeepsTheEditThenClosesOrSwitches()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Dark"); var copy = model.DuplicateTheme("Dark");
        var window = new ThemesDialog(model).Open(model);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Edit(window, "#112233");
        // Choosing another theme in the list also asks, and the edited theme stays selected until it is answered.
        var list = Find<Avalonia.Controls.ListBox>(window, "ThemeList"); list.SelectedItem = "Light"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Avalonia.Controls.Border>(window, "UnsavedPrompt").IsVisible); Assert.Equal(copy, list.SelectedItem); Assert.NotEqual("Light", model.Theme);
        Press(window, "UnsavedSave");
        Assert.Equal("#112233", model.Themes.Single(t => t.Name == copy).Colour("Surface")); Assert.Equal("Light", model.Theme);
        window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible); // Nothing left unsaved, so it closes straight away.
        model.DeleteTheme(copy); model.SetTheme("System");
    }

    [Fact]
    public void SixBuiltInsShipAndAnythingRemovedLandsOnTheNearestLightOrDark()
    {
        Assert.Equal(new[] { "Light", "Dark", "Studio", "After Hours", "Botanical", "Hawthorn" }, ThemeLibrary.BuiltIn.Select(t => t.Name));
        Assert.Equal(new[] { false, true, false, true, false, true }, ThemeLibrary.BuiltIn.Select(t => t.Dark));
        Assert.Equal("Light", ThemeLibrary.Renamed("Time Deck")); Assert.Equal("Dark", ThemeLibrary.Renamed("Time Deck Dark"));
        Assert.Equal("Hawthorn", ThemeLibrary.Renamed("Hawthorn")); Assert.Equal("Dark", ThemeLibrary.Renamed("Sage")); Assert.Equal("Light", ThemeLibrary.Renamed("Sandstone")); Assert.Equal("Dark", ThemeLibrary.Renamed("Ocean"));
        Assert.Equal("My own", ThemeLibrary.Renamed("My own"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SoftenedBlockColoursFollowTheSelectedTheme()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Dark"); var copy = model.DuplicateTheme("Dark");
        model.SaveTheme(model.Themes.Single(t => t.Name == copy) with { BlockStyle = ThemeBlockStyle.AsChosen }, copy); model.SetTheme(copy);
        Assert.Equal("#91B7D5", model.BlockColour("#91B7D5")); // As chosen.
        model.SaveTheme(model.Themes.Single(t => t.Name == copy) with { BlockStyle = ThemeBlockStyle.Softened }, copy);
        Assert.NotEqual("#91B7D5", model.BlockColour("#91B7D5"));
        model.DeleteTheme(copy); model.SetTheme("System");
    }

    [Fact]
    public void TintedBlocksKeepTheirTextReadableForEveryPresetColourAndTheEdgeKeepsTheAccountColour()
    {
        foreach (var colour in TimerModel.Palette)
        {
            var look = ThemeDerivation.Block(ThemeLibrary.Light, colour);
            Assert.True(ThemeColours.Contrast(look.Text, look.Fill) >= 4.5, $"{colour}: text {look.Text} on fill {look.Fill}");
            Assert.Equal(colour.ToUpperInvariant(), look.Edge.ToUpperInvariant()); // The light theme leaves the edge as the account's own colour.
            Assert.NotEqual(colour.ToUpperInvariant(), look.Fill.ToUpperInvariant()); // The fill is a tint, not the colour itself.
        }
        // A theme can push the edge towards its text colour, and take more or less of the account colour into the fill.
        var stronger = ThemeLibrary.Light with { BlockEdge = .5, BlockTint = .9 };
        var a = ThemeDerivation.Block(ThemeLibrary.Light, "#91B7D5"); var b = ThemeDerivation.Block(stronger, "#91B7D5");
        Assert.NotEqual(a.Edge, b.Edge); Assert.True(ThemeColours.Luminance(b.Edge) < ThemeColours.Luminance(a.Edge)); Assert.True(ThemeColours.Luminance(b.Fill) < ThemeColours.Luminance(a.Fill));
        // The older styles still work: solid blocks keep the account colour, softened ones blend towards the surface.
        Assert.Equal("#91B7D5", ThemeDerivation.Block(ThemeLibrary.Light with { BlockStyle = ThemeBlockStyle.AsChosen }, "#91B7D5").Fill);
        Assert.NotEqual("#91B7D5", ThemeDerivation.Block(ThemeLibrary.Light with { BlockStyle = ThemeBlockStyle.Softened }, "#91B7D5").Fill);
    }

    [Fact]
    public void BlockRulesSurviveSavingAndAnOlderThemeLineGetsTheDefaults()
    {
        var mine = ThemeLibrary.Light with { Name = "Mine", BuiltIn = false, BlockStyle = ThemeBlockStyle.Tinted, BlockTint = .45, BlockEdge = .1 };
        var back = ThemeStorage.FromLines(new[] { ThemeStorage.ToLine(mine) }).Single();
        Assert.Equal(ThemeBlockStyle.Tinted, back.BlockStyle); Assert.Equal(.45, back.BlockTint); Assert.Equal(.1, back.BlockEdge);
        var old = ThemeStorage.FromLines(new[] { "{\"Name\":\"Older\",\"Dark\":false,\"Colours\":{},\"BlockStyle\":\"softened\"}" }).Single();
        Assert.Equal(ThemeBlockStyle.DefaultTint, old.BlockTint); Assert.Equal(ThemeBlockStyle.DefaultEdge, old.BlockEdge); Assert.Equal(ThemeBlockStyle.Softened, old.BlockStyle);
        var wild = ThemeStorage.FromLines(new[] { ThemeStorage.ToLine(mine with { Name = "Wild", BlockTint = 9, BlockEdge = -3 }) }).Single();
        Assert.Equal(1, wild.BlockTint); Assert.Equal(0, wild.BlockEdge); // Kept within sensible limits.
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheEditorShowsTheBlockRuleSlidersForATintedThemeAndSavesThem()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Light"); var copy = model.DuplicateTheme("Light");
        var window = new ThemesDialog(model).Open(model);
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var rules = Find<Avalonia.Controls.StackPanel>(window, "BlockRules"); Assert.True(rules.IsVisible);
            var tint = Find<Avalonia.Controls.Slider>(window, "BlockTint"); Assert.Equal(.55, tint.Value, 2);
            tint.Value = .8; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(.55, model.CurrentTheme.BlockTint, 2); // Only previewed so far, not kept.
            Press(window, "SaveTheme");
            Assert.Equal(.8, model.Themes.Single(x => x.Name == copy).BlockTint, 2);
            var style = Find<Avalonia.Controls.ComboBox>(window, "ThemeBlockStyle"); style.SelectedIndex = 0; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Avalonia.Controls.StackPanel>(window, "BlockRules").IsVisible);
        }
        finally { window.Close(); model.DeleteTheme(copy); model.SetTheme("System"); }
    }
}
