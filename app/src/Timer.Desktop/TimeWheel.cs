using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// A three-row wheel for an hour and a minute: the chosen value in the middle, the one before and the one after dimmed above and below it.
/// The hour steps by one and the minute by five (landing on multiples of five, so a minute such as 31 is never changed just by opening it). Pressing the
/// dimmed rows, the arrow keys, Page Up and Page Down, or the mouse wheel over a column move it; there is no drag, so it works the same by touch pad and keyboard.
/// It holds no state of its own beyond the two numbers, which the owner sets and reads.
/// </summary>
public sealed class TimeWheel : Border
{
    private int hour, minute;
    private readonly StackPanel hourColumn = new() { Name = "WheelHour", Spacing = 2 }, minuteColumn = new() { Name = "WheelMinute", Spacing = 2 };
    /// <summary>Raised when a press, key or wheel step moves the time.</summary>
    public event Action? Moved;

    public TimeWheel(int hour, int minute)
    {
        this.hour = hour; this.minute = minute; Name = "TimeWheel"; Focusable = true; CornerRadius = new CornerRadius(12); Padding = new Thickness(6);
        Classes.Add("trough"); // The sunk well the wheel turns in.
        AutomationProperties.SetName(this, "Time wheel");
        var colon = new TextBlock { Text = ":", FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) }; colon.Classes.Add("muted");
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 0 };
        row.Children.Add(hourColumn); row.Children.Add(colon); row.Children.Add(minuteColumn); Child = row;
        hourColumn.PointerWheelChanged += (_, e) => { Step(true, e.Delta.Y > 0 ? -1 : 1); e.Handled = true; };
        minuteColumn.PointerWheelChanged += (_, e) => { Step(false, e.Delta.Y > 0 ? -1 : 1); e.Handled = true; };
        Draw();
    }

    public int Hour => hour;
    public int Minute => minute;

    /// <summary>Puts the wheel at a time without raising <see cref="Moved"/>.</summary>
    public void Set(int newHour, int newMinute) { hour = newHour; minute = newMinute; Draw(); }

    /// <summary>The value one step in a direction from a given one: an hour wraps round 24, a minute lands on the next or previous multiple of five and wraps round 60.</summary>
    public static int Next(bool isHour, int value, int direction)
    {
        if (isHour) return (value + direction + 24) % 24;
        return direction > 0 ? (value / 5 * 5 + 5) % 60 : value % 5 == 0 ? (value - 5 + 60) % 60 : value / 5 * 5;
    }

    private void Step(bool isHour, int direction)
    {
        if (isHour) hour = Next(true, hour, direction); else minute = Next(false, minute, direction);
        Draw(); Moved?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Left and Right choose the column by moving the focus to it; Up and Down step the one that has it, the hour until the minute is chosen.
        var inMinutes = minuteColumn.IsKeyboardFocusWithin;
        switch (e.Key)
        {
            case Key.Up or Key.PageUp: Step(!inMinutes, -1); e.Handled = true; break;
            case Key.Down or Key.PageDown: Step(!inMinutes, 1); e.Handled = true; break;
            default: base.OnKeyDown(e); break;
        }
    }

    private void Draw()
    {
        Fill(hourColumn, true, hour); Fill(minuteColumn, false, minute);
    }

    private void Fill(StackPanel column, bool isHour, int value)
    {
        column.Children.Clear();
        var prefix = isHour ? "Hour" : "Minute";
        for (var offset = -1; offset <= 1; offset++)
        {
            var shown = offset == 0 ? value : Next(isHour, value, offset);
            var captured = offset;
            var button = Ui.Button(shown.ToString("00", CultureInfo.InvariantCulture), () => { if (captured != 0) Step(isHour, captured); }, "Wheel" + prefix + (offset < 0 ? "Before" : offset > 0 ? "After" : "Chosen"));
            button.Classes.Add("pillBare"); button.Classes.Add("wheelRow"); button.Classes.Set("wheelChosen", offset == 0);
            button.Width = 54; button.Height = offset == 0 ? 32 : 26; button.FontSize = offset == 0 ? 16 : 13; button.FontWeight = offset == 0 ? FontWeight.Bold : FontWeight.Normal; button.Opacity = offset == 0 ? 1 : .55;
            button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center; button.Padding = new Thickness(0);
            AutomationProperties.SetName(button, offset == 0 ? prefix + " " + shown.ToString("00", CultureInfo.InvariantCulture) : (offset < 0 ? "Earlier " : "Later ") + prefix.ToLowerInvariant() + " " + shown.ToString("00", CultureInfo.InvariantCulture));
            column.Children.Add(button);
        }
    }
}
