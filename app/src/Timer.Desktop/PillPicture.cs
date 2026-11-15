using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>A fixed sample using the real display glyphs and sizing, without a timer model or recording actions.</summary>
internal sealed class PillPicture : Viewbox
{
    private readonly bool full, tall;
    private readonly int? alerts;
    private readonly double scale;
    private DisplayStyle? style;

    public PillPicture(bool full, int? alerts, double scale, bool tall)
    {
        this.full = full; this.alerts = alerts; this.scale = scale; this.tall = tall;
        IsHitTestVisible = false; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        AttachedToVisualTree += (_, _) => Build();
        ActualThemeVariantChanged += (_, _) => Build();
        Build();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (style != DisplayInk.Style) Build();
        return base.MeasureOverride(availableSize);
    }

    private void Build()
    {
        style = DisplayInk.Style;
        var width = full ? PillWindow.PillWidth : PillWindow.CompactWidth;
        var height = PillWindow.PillHeight + (tall ? LowerDisplay.HeightFor(PillLower.Strip) : 0);
        Width = width * .42 * scale; Height = height * .42 * scale;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = StatusFace.DisplayHeight };
        void Gap(double size) => row.Children.Add(new Border { Width = size });
        var led = new Border { Width = 6, Height = 14, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center };
        led.Bind(Border.BackgroundProperty, led.GetResourceObservable("DisplayGroove")); row.Children.Add(led); Gap(StatusFace.LedGap);
        row.Children.Add(new SegmentClock { Text = "04:45:00" }); Gap(StatusFace.ClockCountGap);
        var count = new StackPanel { Width = StatusFace.CountColumn, VerticalAlignment = VerticalAlignment.Center };
        var bell = new AlertBellGlyph(); bell.Show(Ui.ThemeColour(this, alerts is null ? "Muted" : "Overtime", Colors.Gray), alerts is not null);
        count.Children.Add(bell);
        count.Children.Add(new SegmentRun { Text = alerts is { } number ? number.ToString("00") : "  ", DigitHeight = StatusFace.CountDigitHeight, LitKey = "Overtime" });
        row.Children.Add(count); Gap(StatusFace.CountNameGap);
        if (full)
        {
            var name = new SegmentName { Animate = false, Colour = Ui.ThemeColour(this, "Ink", Colors.Gray) }; name.SetAccount("ACME");
            name.SetInkWindow(StatusFace.NameZone);
            row.Children.Add(new Border { Width = StatusFace.NameZone, Child = name, VerticalAlignment = VerticalAlignment.Center });
            Gap(StatusFace.NameTagGap);
        }
        var keys = new StackPanel { Name = "PreviewTransportControls", Spacing = StatusFace.ControlGap, VerticalAlignment = VerticalAlignment.Center };
        keys.Children.Add(new TransportGlyph { Width = StatusFace.ControlZone, Height = StatusFace.ControlHeight, Colour = Ui.ThemeColour(this, "Live", Colors.Green) });
        keys.Children.Add(new BreakBadgeGlyph { Width = StatusFace.ControlZone, Height = StatusFace.ControlHeight, Colour = Ui.ThemeColour(this, "Muted", Colors.Gray) });
        row.Children.Add(keys);
        var rows = new StackPanel(); rows.Children.Add(row);
        if (tall)
        {
            var strip = new StackPanel { Name = "PreviewDayStrip", Orientation = Orientation.Horizontal, Spacing = 5, Height = 40, VerticalAlignment = VerticalAlignment.Center };
            foreach (var key in new[] { "Muted", "AccentText", "BreakText", "Muted" })
            {
                var block = new Border { Width = 40, Height = 8, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
                block.Bind(Border.BackgroundProperty, block.GetResourceObservable(key)); strip.Children.Add(block);
            }
            rows.Children.Add(strip);
        }
        var well = new Border { CornerRadius = new CornerRadius(StatusFace.DisplayRadius), Padding = new Thickness(StatusFace.DisplayPadding, 0, StatusFace.RightPadding, 0) };
        well.Bind(Border.BackgroundProperty, well.GetResourceObservable("DisplayWell")); well.Child = PaperLayer.Under(well, rows);
        var bezel = new Border { Width = width, Height = height, Padding = PillWindow.BezelPadding,
            BorderThickness = new Thickness(PillWindow.EdgeThickness), CornerRadius = new CornerRadius(PillWindow.PillRadius), Child = well };
        bezel.Bind(Border.BackgroundProperty, bezel.GetResourceObservable("PillBody"));
        bezel.Bind(Border.BorderBrushProperty, bezel.GetResourceObservable("Line"));
        Child = bezel;
    }
}
