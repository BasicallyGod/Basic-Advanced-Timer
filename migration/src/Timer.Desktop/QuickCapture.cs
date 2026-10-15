using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;
using Timer.Desk;

namespace Timer.Desktop;

/// <summary>
/// A small box to jot a note or task from anywhere, without opening the dashboard: from the pill's menu or the shortcut chosen in Settings. It takes the
/// same shorthand as Notes (a time, @day, a leading ! for a deadline, #Account), shows how it was read as you type, and saves through the same notes, so
/// nothing new is stored. Enter saves and closes, Escape closes, and so does clicking away.
/// </summary>
public sealed class QuickCaptureWindow : Window
{
    public const double CardWidth = 460;
    private readonly TimerModel model;
    private readonly TextBox input = new() { Name = "CaptureText", Watermark = "Jot it down: 14:30 Call back · ! deadline · @fri · #Account", Padding = new Thickness(12, 9), FontSize = 15 };
    private readonly TextBlock reading = Ui.Text("", 12, "muted");
    private bool closing;
    public QuickCaptureWindow(TimerModel model)
    {
        this.model = model; Name = "QuickCapture"; Title = "Quick capture"; WindowChrome.Attach(this);
        SystemDecorations = SystemDecorations.None; TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }; Background = Brushes.Transparent;
        CanResize = false; ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.Height; Width = CardWidth + 2 * Ui.ShadowMargin;
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(18, 14, 18, 14) };
        var heading = Ui.Text("Quick capture", 12, "muted"); heading.FontWeight = FontWeight.SemiBold;
        reading.TextWrapping = TextWrapping.Wrap; reading.Name = "CaptureReading";
        stack.Children.Add(heading); stack.Children.Add(input); stack.Children.Add(reading);
        var card = new Border { Child = stack, BorderThickness = new Thickness(1) }; card.Classes.Add("panel");
        var host = Ui.Shadowed(card, 18); Ui.SetShadow(host, true, null); Content = host;
        input.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Read(); };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Save(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Finish(); }
        };
        // Clicking away is as good as Escape, so the box never lingers over other work.
        Deactivated += (_, _) => Finish();
        Opened += (_, _) => { Position = Spot(); input.Focus(); };
        Read();
    }

    /// <summary>What the typed words were taken to mean, in a short line, so a mistake shows before Enter.</summary>
    public static string Describe(string? typed, DateTime now, IReadOnlyList<string> accounts)
    {
        if (string.IsNullOrWhiteSpace(typed)) return "A time, @fri for a day, ! for a deadline, #Account to link one.";
        var parts = NoteShorthand.Parse(typed, now);
        var bits = new List<string> { (parts.Day ?? now).Date == now.Date ? "Today" : (parts.Day ?? now).ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) };
        if (parts.At is { } at) bits.Add(at.ToString(@"hh\:mm"));
        if (typed.TrimStart().StartsWith('!')) bits.Add("deadline");
        if (Trinkets.FindAccountTag(typed, accounts) is { } found) bits.Add(found.Account);
        return string.Join(" · ", bits);
    }
    private void Read() => reading.Text = Describe(input.Text, model.Engine.Now, model.AccountNames);
    /// <summary>Saves what was typed as a note on today (or the day it names) and closes. Empty words just close.</summary>
    public void Save()
    {
        var typed = input.Text;
        if (!string.IsNullOrWhiteSpace(typed)) model.AddDayNote(model.Engine.Now.Date, typed);
        Finish();
    }
    private void Finish() { if (closing) return; closing = true; Close(); }
    /// <summary>Where the box opens: centred across the top third of the screen it is on, where the eye already is.</summary>
    private PixelPoint Spot()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary; if (screen is null) return Position;
        var area = screen.WorkingArea; var width = (int)(Bounds.Width * screen.Scaling);
        return new PixelPoint(area.X + Math.Max(0, (area.Width - width) / 2), area.Y + area.Height / 4);
    }
}
