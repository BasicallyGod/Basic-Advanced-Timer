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
    public static Border Chip(string text, string tone, IconKind? icon = null, Control? lead = null, IconKind? trailing = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var token = tone switch { "accent" => "Accent", "late" => "Overtime", "quiet" => "Muted", _ => "Ink" };
        if (lead is not null) row.Children.Add(lead);
        if (icon is { } kind) row.Children.Add(new IconGlyph(kind) { TintKey = token == "Ink" ? null : token });
        row.Children.Add(Ink(text, token));
        if (trailing is { } end) row.Children.Add(new Viewbox { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Child = new IconGlyph(end) { TintKey = token == "Ink" ? "Muted" : token } }); // A Viewbox scales the icon: it draws on its own 18 unit grid, and a smaller size would only cut it off.
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
        var ring = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.6), VerticalAlignment = VerticalAlignment.Center };
        ring.Classes.Add("tick"); ring.Classes.Set("tickDone", note.Done);
        var button = Ui.Button("", () => notes.Toggle(note.Id), "NoteDone_" + note.Id); button.Classes.Add("pillBare"); button.Padding = new Thickness(0); button.Content = ring; button.VerticalAlignment = VerticalAlignment.Center;
        if (note.Done) ring.Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 3.9,8.6 L 7.2,11.9 L 12.9,5.1"), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Stroke = new SolidColorBrush(Colors.White), Name = "TickMark" };
        Avalonia.Automation.AutomationProperties.SetName(button, "Done: " + notes.Text(note)); ToolTip.SetTip(button, note.Done ? "Not done" : "Done");
        return button;
    }

    /// <summary>The time of a meeting as 09:15–10:00.</summary>
    public static string Span(DayNote note) => note.At is { } a && note.End is { } z ? a.ToString(@"hh\:mm") + "–" + z.ToString(@"hh\:mm") : "";

    /// <summary>Which of the offered accounts is lit, so the arrow keys can move through them and Tab or Enter take the lit one. It goes back to the first when the words change.</summary>
    public sealed class SuggestionState
    {
        public int Index { get; set; }
        public int Count { get; private set; }
        internal void Offered(int count) { Count = count; if (Index >= count) Index = 0; }
        /// <summary>Moves the light down (1) or up (-1), round the ends; false when nothing is on offer, so the key is left alone.</summary>
        public bool Move(int step) { if (Count == 0) return false; Index = (Index + step + Count) % Count; return true; }
    }

    /// <summary>
    /// A word at the end of the words that starts with # offers the accounts that match it, as buttons in <paramref name="host"/> (named <paramref name="idPrefix"/> and the
    /// account), the one in <paramref name="nav"/> lit. <paramref name="pick"/> is told the chosen account and the words with the # word taken out. Returns the lit offer,
    /// so Tab or Enter can take it (the first until an arrow key moves the light).
    /// </summary>
    public static (string Account, string Words)? ShowSuggestions(TimerModel model, string draft, StackPanel host, string idPrefix, Action<string, string> pick, SuggestionState nav)
    {
        host.Children.Clear();
        var match = System.Text.RegularExpressions.Regex.Match(draft ?? "", @"#([\p{L}\p{N}_-]*)$");
        if (!match.Success) { host.IsVisible = false; nav.Offered(0); return null; }
        var typed = match.Groups[1].Value; var rest = draft![..match.Index].TrimEnd();
        var options = model.Accounts.Where(a => !BreakAccount.Is(a.Name) && a.Name.Replace(" ", "").StartsWith(typed, StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        nav.Offered(options.Count);
        foreach (var option in options)
        {
            var button = Ui.Button("", () => pick(option.Name, rest), idPrefix + option.Name.Replace(" ", ""));
            button.Classes.Add("flat"); button.Classes.Set("suggestOn", options.IndexOf(option) == nav.Index); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Content = Ui.Horizontal(Dot(model, option.Name), new TextBlock { Text = "#" + option.Name.Replace(" ", ""), FontWeight = FontWeight.SemiBold, FontSize = 13 }, Ink(option.Name, "Muted", 13));
            host.Children.Add(button);
        }
        host.IsVisible = options.Count > 0;
        return options.Count > 0 ? (options[nav.Index].Name, rest) : null;
    }
}
