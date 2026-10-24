using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>The small pieces the Notes card, the Notes page and the bell share: chips, the account dot, the round tick and the swatch of an account.</summary>
public static class NoteParts
{
    /// <summary>Text in one of the theme's colours (a token name), so it follows Light and Dark.</summary>
    public static TextBlock Ink(string text, string token, double size = 12)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(token)); return t;
    }

    /// <summary>A small pill: tone is plain, accent (a reminder), late (a reminder that has passed) or quiet (a repeat).</summary>
    public static Border Chip(string text, string tone, IconKind? icon = null, Control? lead = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var token = tone switch { "accent" => "Accent", "late" => "Overtime", "quiet" => "Muted", _ => "Ink" };
        if (lead is not null) row.Children.Add(lead);
        if (icon is { } kind) row.Children.Add(new IconGlyph(kind) { TintKey = token == "Ink" ? null : token });
        row.Children.Add(Ink(text, token));
        var chip = new Border { Child = row, CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        chip.Classes.Add("chip"); chip.Classes.Add(tone == "accent" ? "accentChip" : tone == "late" ? "lateChip" : "plainChip");
        return chip;
    }

    /// <summary>A round swatch of an account's colour (grey for none).</summary>
    public static Control Dot(TimerModel model, string? account, double size = 10)
    {
        IBrush fill = new SolidColorBrush(Colors.Gray);
        if (account is not null && model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))?.Colour is { } colour)
        {
            var look = model.BlockLook(colour);
            fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(look.Fill), 0), new GradientStop(Color.Parse(ReviewViews.DeeperEnd(look)), 1) } };
        }
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>The round tick that marks a note done (or not done).</summary>
    public static Button Tick(NotesModel notes, DayNote note)
    {
        var ring = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.6), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        ring.Classes.Add("tick"); ring.Classes.Set("tickDone", note.Done);
        var button = Ui.Button("", () => notes.Toggle(note.Id), "NoteDone_" + note.Id); button.Classes.Add("pillBare"); button.Padding = new Thickness(0); button.Content = ring; button.VerticalAlignment = VerticalAlignment.Top;
        if (note.Done) ring.Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 5.5,10.5 L 8.8,13.8 L 14.5,7"), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Stroke = new SolidColorBrush(Colors.White), Name = "TickMark" };
        Avalonia.Automation.AutomationProperties.SetName(button, "Done: " + notes.Text(note)); ToolTip.SetTip(button, note.Done ? "Not done" : "Done");
        return button;
    }

    /// <summary>The time of a meeting as 09:15–10:00.</summary>
    public static string Span(DayNote note) => note.At is { } a && note.End is { } z ? a.ToString(@"hh\:mm") + "–" + z.ToString(@"hh\:mm") : "";
}
