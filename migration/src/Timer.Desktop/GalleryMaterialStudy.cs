using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>Window-owned material resources. Nothing is added to Application resources or production control themes.</summary>
internal sealed class GalleryMaterialStudy
{
    private readonly Window window;
    private readonly ResourceDictionary aliases = Resources(true);
    private readonly List<ContextMenu> menus = new();
    private readonly Uri baseUri = new($"avares://{typeof(GalleryMaterialStudy).Assembly.GetName().Name}/");
    internal GalleryMaterialStudy(Window window)
    {
        this.window = window;
        window.Resources.MergedDictionaries.Add(Resources(false));
        window.Styles.Add(Styles());
    }
    private StyleInclude Styles() => new(baseUri) { Source = new Uri("/Styles/GalleryMaterialStyles.axaml", UriKind.Relative) };

    internal void CollectSamples()
    {
        foreach (var button in window.GetLogicalDescendants().OfType<Button>())
        {
            if (button.Content is IconGlyph && !button.Classes.Contains("pairFirst") && !button.Classes.Contains("pairSecond")) button.Classes.Add("galleryRound");
            if (button.ContextMenu is not { } menu || menus.Contains(menu)) continue;
            menus.Add(menu); menu.Resources.MergedDictionaries.Add(Resources(false)); menu.Styles.Add(Styles());
        }
    }
    internal void SetProposed(bool proposed)
    {
        window.Classes.Set("galleryProposal", proposed);
        if (proposed && !window.Resources.MergedDictionaries.Contains(aliases)) window.Resources.MergedDictionaries.Add(aliases);
        if (!proposed) window.Resources.MergedDictionaries.Remove(aliases);
        foreach (var menu in menus) menu.Classes.Set("galleryStudyMenu", proposed);
    }

    private static ResourceDictionary Resources(bool alias)
    {
        var result = new ResourceDictionary();
        foreach (var (theme, variant) in new[] { (AppThemes.Light, ThemeVariant.Light), (AppThemes.Dark, ThemeVariant.Dark) })
        {
            var d = new ResourceDictionary(); result.ThemeDictionaries[variant] = d;
            var light = !theme.Dark; var panel = theme.Colour("Panel");
            var well = ThemeColours.Mix(panel, light ? theme.Colour("Ink") : "#000000", light ? .035 : .13);
            var hover = ThemeColours.Mix(panel, "#FFFFFF", light ? .18 : .045);
            Color Hi(double strength) => Color.FromArgb((byte)Math.Round(255 * (light ? .88 : .065) * strength), 255, 255, 255);
            Color Lo(double strength) => Color.FromArgb((byte)Math.Round(255 * (light ? .21 : .68) * strength), light ? (byte)90 : (byte)0, light ? (byte)97 : (byte)0, light ? (byte)110 : (byte)0);
            BoxShadow Drop(double x, double y, double blur, Color colour, bool inset = false) => new() { OffsetX = x, OffsetY = y, Blur = blur, Color = colour, IsInset = inset };
            BoxShadows Pair(BoxShadow a, BoxShadow b) => new(a, new[] { b });
            var raised = Pair(Drop(-2.5, -2.5, 5, Hi(.9)), Drop(2.5, 3, 5, Lo(.8)));
            var lifted = Pair(Drop(-3, -3, 6, Hi(1)), Drop(3, 3.5, 6, Lo(.9)));
            var inset = Pair(Drop(2, 2, 4, Lo(.7), true), Drop(-2, -2, 4, Hi(.85), true));
            var compact = Pair(Drop(-1.25, -1.25, 2.5, Hi(.85)), Drop(1.25, 1.5, 2.5, Lo(.7)));
            var compactInset = Pair(Drop(1.25, 1.25, 2, Lo(.6), true), Drop(-1.25, -1.25, 2, Hi(.8), true));
            var card = Pair(Drop(-4, -4, 10, Hi(.75)), Drop(4, 5, 10, Lo(.6)));
            if (!alias)
            {
                d["StudyRaised"] = raised; d["StudyHover"] = lifted; d["StudyInset"] = inset;
                d["StudyCompact"] = compact; d["StudyCompactInset"] = compactInset; d["StudyCard"] = card;
                d["StudyPanel"] = Brush.Parse(panel); d["StudyWell"] = Brush.Parse(well); d["StudyHoverFill"] = Brush.Parse(hover);
                d["StudyEdge"] = new SolidColorBrush(light ? Color.FromArgb(12, 90, 97, 110) : Color.FromArgb(16, 255, 255, 255));
                continue;
            }
            foreach (var key in new[] { "ShadowKey", "NeoRaised" }) d[key] = raised;
            foreach (var key in new[] { "ShadowKeyHover", "NeoHover" }) d[key] = lifted;
            foreach (var key in new[] { "ShadowSunk", "NeoInset" }) d[key] = inset;
            foreach (var key in new[] { "ShadowThumb", "NeoSmallRaised", "NeoSmallHover" }) d[key] = compact;
            foreach (var key in new[] { "NeoSmallInset", "NeoSmallPressed" }) d[key] = compactInset;
            d["ShadowCard"] = card; d["ShadowPopup"] = card;
            d["Well"] = Brush.Parse(well); d["Selected"] = Brush.Parse(well); d["NeoPressed"] = Brush.Parse(well); d["KeyHover"] = Brush.Parse(hover);
            d["KeyEdge"] = new SolidColorBrush(light ? Color.FromArgb(12, 90, 97, 110) : Color.FromArgb(16, 255, 255, 255));
            foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled", "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed", "ComboBoxBackgroundDisabled" }) d[key] = Brush.Parse(well);
            foreach (var key in new[] { "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundPressed", "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPressed" }) d[key] = Brush.Parse(well);
            foreach (var key in new[] { "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemForegroundSelectedPressed" }) d[key] = Brush.Parse(theme.Colour("Accent"));
            d["SliderTrackThemeHeight"] = 6d;
            foreach (var key in new[] { "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" }) d[key] = Brush.Parse(panel);
        }
        return result;
    }
}
