using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Timer.Desktop;

public enum DurationUnit { Minutes, Hours }

/// <summary>Duration syntax is shared; bare numbers retain each field's established unit.</summary>
public static class DurationText
{
    public static double? Hours(string? text, DurationUnit bareUnit, CultureInfo? culture = null, bool allowZero = false)
    {
        text = text?.Trim().ToLowerInvariant().Replace("hours", "h").Replace("hour", "h").Replace("minutes", "m").Replace("minute", "m");
        if (string.IsNullOrEmpty(text)) return null;
        culture ??= CultureInfo.CurrentCulture;
        bool Number(string value, out double number) =>
            double.TryParse(value.Trim(), NumberStyles.Float, culture, out number)
            || double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        double hours;
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length is < 2 or > 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 0 or > 59) return null;
            double seconds = 0;
            if (parts.Length == 3 && (!Number(parts[2], out seconds) || seconds < 0 || seconds >= 60)) return null;
            hours = h + m / 60d + seconds / 3600d;
        }
        else if (text.Contains('h') && !text.EndsWith('h'))
        {
            var parts = text.Split('h');
            if (parts.Length != 2 || !parts[1].EndsWith('m') || !Number(parts[0], out var h)
                || !Number(parts[1][..^1], out var m) || h < 0 || m < 0) return null;
            hours = h + m / 60d;
        }
        else
        {
            var unit = text.EndsWith('h') ? DurationUnit.Hours : text.EndsWith('m') ? DurationUnit.Minutes : bareUnit;
            if (text.EndsWith('h') || text.EndsWith('m')) text = text[..^1];
            if (!Number(text, out var number)) return null;
            hours = unit == DurationUnit.Hours ? number : number / 60d;
        }
        return double.IsFinite(hours) && (hours > 0 || allowZero && hours == 0) ? hours : null;
    }

    public static TimeSpan? Span(string? text, DurationUnit bareUnit, CultureInfo? culture = null, bool allowZero = false)
    {
        if (Hours(text, bareUnit, culture, allowZero) is not { } hours || hours > 24) return null;
        var span = TimeSpan.FromHours(hours);
        return span > TimeSpan.Zero || allowZero && hours == 0 ? span : null;
    }

    public static string ShowHours(double hours, bool decimalHours, bool showSeconds = true)
    {
        if (decimalHours) return hours.ToString("0.00", CultureInfo.CurrentCulture) + "h";
        if (hours > TimeSpan.MaxValue.TotalHours) return hours.ToString("G", CultureInfo.CurrentCulture) + "h";
        var span = TimeSpan.FromHours(hours);
        var seconds = span.Ticks % TimeSpan.TicksPerMinute;
        return $"{(long)span.TotalHours:00}:{span.Minutes:00}" + (!showSeconds || seconds == 0 ? "" : ":" + (seconds / (double)TimeSpan.TicksPerSecond).ToString("00.#######", CultureInfo.InvariantCulture));
    }
}

/// <summary>Normal text editing with a visible bare-number unit and a lossless unchanged-value path.</summary>
public sealed class DurationBox : TextBox
{
    private readonly DurationUnit bareUnit;
    private bool decimalHours;
    private readonly bool allowZero;
    private string? shown;
    private double? originalHours;
    private TimeSpan? originalSpan;
    protected override Type StyleKeyOverride => typeof(TextBox);

    public DurationBox(DurationUnit bareUnit, bool decimalHours, string accessibleName, bool allowZero = false)
    {
        this.bareUnit = bareUnit; this.decimalHours = decimalHours; this.allowZero = allowZero;
        AutomationProperties.SetName(this, accessibleName);
        AutomationProperties.SetHelpText(this, bareUnit == DurationUnit.Hours ? "Bare numbers: hours" : "Bare numbers: minutes");
        Watermark = decimalHours ? "1.50h" : "01:30";
        var unit = Ui.Text(bareUnit == DurationUnit.Hours ? "h" : "min", 11, "muted");
        unit.Margin = new Thickness(2, 0, 7, 0); unit.IsHitTestVisible = false;
        InnerRightContent = unit; VerticalContentAlignment = VerticalAlignment.Center;
        void Cue() => unit.IsVisible = !string.IsNullOrWhiteSpace(Text) && Text!.Trim().All(c => char.IsDigit(c) || c is '.' or ',' or '+' or '-' or 'e' or 'E');
        TextChanged += (_, _) => Cue(); Cue();
    }

    public DurationBox(DurationUnit bareUnit, TimerModel model, string accessibleName, bool allowZero = false)
        : this(bareUnit, model.DecimalHours, accessibleName, allowZero)
    {
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName != nameof(TimerModel.Elapsed)) SetFormat(model.DecimalHours); }
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; SetFormat(model.DecimalHours); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }

    public void SetFormat(bool value)
    {
        if (decimalHours == value) return;
        decimalHours = value; Watermark = value ? "1.50h" : "01:30";
        if (Text != shown) return; // A format preference never overwrites an unfinished edit.
        if (originalSpan is { } span) SetDuration(span); else SetHours(originalHours);
    }

    public void SetHours(double? hours)
    {
        originalHours = hours; originalSpan = null;
        shown = hours is { } h ? DurationText.ShowHours(h, decimalHours, showSeconds: false) : ""; Text = shown;
    }
    public void SetDuration(TimeSpan span)
    {
        originalHours = span.TotalHours; originalSpan = span;
        shown = DurationText.ShowHours(span.TotalHours, decimalHours); Text = shown;
    }
    public double? ReadHours() => Text == shown ? originalHours : DurationText.Hours(Text, bareUnit, allowZero: allowZero);
    public TimeSpan? ReadDuration() => Text == shown && originalSpan is { } span ? span : DurationText.Span(Text, bareUnit, allowZero: allowZero);
}
