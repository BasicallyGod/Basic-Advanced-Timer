using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// Moving a reminder from the alerts card (the study "Rescheduling a reminder", option F): a line naming the week with a pair of keys that move it, the week's seven
/// days, and under them a strip of the chosen day with its blocks and meetings faintly drawn and the reminder as a pin. Pick a day, then drag the pin or press the
/// strip; it is set when the press is let go, to the five minutes. Nothing is written under the strip: the pin's own chip says the time. It replaces the reminder card
/// with its calendar, time boxes, wheel and Set, which took five or six steps for an everyday move.
/// </summary>
public sealed class ReschedulePicker : StackPanel
{
    private readonly TimerModel model;
    private readonly Action<DateTime> set;
    private readonly TextBlock range = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "RescheduleWeek" };
    private readonly UniformGrid days = new() { Columns = 7, Name = "RescheduleDays" };
    private readonly Button back;
    private int week;
    /// <summary>The strip of the chosen day, with the pin.</summary>
    public RescheduleStrip Strip { get; }
    /// <summary>The day being shown (a working day's key, as <see cref="TimerModel.TodayKey"/>).</summary>
    public DateTime Day { get; private set; }

    public ReschedulePicker(TimerModel model, DateTime? current, Action<DateTime> set)
    {
        this.model = model; this.set = set; Spacing = 8; Name = "ReschedulePicker";
        range.Bind(TextBlock.ForegroundProperty, range.GetResourceObservable("Muted"));
        back = Ui.Ctl("RescheduleWeekBack", null, IconKind.Previous, "Week before", () => MoveWeek(-1), small: true);
        var forward = Ui.Ctl("RescheduleWeekNext", null, IconKind.Next, "Week after", () => MoveWeek(1), small: true);
        var line = Ui.Grid("*,Auto"); Ui.Put(line, range); Ui.Put(line, Ui.KeyPair(back, forward, "RescheduleWeekKeys"), 1);
        Strip = new RescheduleStrip(model, at => set(at)) { Name = "RescheduleStrip" };
        Children.Add(line); Children.Add(days); Children.Add(Strip);
        // Start on the reminder's own day and time when it is still to come; otherwise half an hour from now, to the five minutes.
        var now = model.Engine.Now; var today = model.TodayKey;
        var start = current is { } c && c > now && model.DayOf(c) < today.AddDays(28) ? c : RoundUp(now.AddMinutes(30));
        Day = model.DayOf(start); week = Math.Max(0, (int)Math.Floor((Day - today).TotalDays / 7));
        Strip.Show(Day, start); Draw();
    }
    internal static DateTime RoundUp(DateTime t) { var whole = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0); var m = (5 - whole.Minute % 5) % 5; return whole.AddMinutes(m == 0 && t > whole ? 5 : m); }

    private void MoveWeek(int by)
    {
        if (week + by < 0) return;
        week += by; Choose(Day.AddDays(7 * by));
    }
    /// <summary>Shows <paramref name="day"/> on the strip, keeping the pin's time of day.</summary>
    public void Choose(DateTime day)
    {
        var offset = Strip.Pin - model.DayBegins(Day);
        Day = day.Date; week = Math.Max(0, (int)Math.Floor((Day - model.TodayKey).TotalDays / 7));
        Strip.Show(Day, model.DayBegins(Day) + offset); Draw();
    }

    private void Draw()
    {
        var british = CultureInfo.GetCultureInfo("en-GB"); var today = model.TodayKey;
        var first = today.AddDays(7 * week); var last = first.AddDays(6);
        range.Text = first.Month == last.Month ? $"{first.Day} – {last.Day} {last.ToString("MMM", british)}" : $"{first.Day} {first.ToString("MMM", british)} – {last.Day} {last.ToString("MMM", british)}";
        back.IsEnabled = week > 0;
        days.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var day = first.AddDays(i); var chosen = day == Day;
            var name = NoteParts.Ink(day.ToString("ddd", british), "Muted", 11); name.HorizontalAlignment = HorizontalAlignment.Center;
            var number = NoteParts.Ink(day.Day.ToString(british), day == today ? "AccentText" : "Ink", 14); number.FontWeight = FontWeight.SemiBold; number.HorizontalAlignment = HorizontalAlignment.Center;
            // A small dot says the day already holds time or a meeting, so a free day reads at a glance.
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, Opacity = Holds(day) ? .6 : 0, Name = "RescheduleDayDot_" + day.ToString("yyyyMMdd") };
            dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable("Muted"));
            var button = new Button { Name = "RescheduleDay_" + day.ToString("yyyyMMdd"), Content = new StackPanel { Spacing = 2, Children = { name, number, dot } }, Padding = new Thickness(0, 5, 0, 4), Margin = new Thickness(1, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
            button.Classes.Add("pillBare");
            if (chosen) { button.Classes.Add("rescheduleChosen"); button.Bind(Button.BackgroundProperty, button.GetResourceObservable("Selected")); button.Bind(Button.BorderBrushProperty, button.GetResourceObservable("AccentLit")); }
            else button.BorderBrush = Brushes.Transparent;
            Avalonia.Automation.AutomationProperties.SetName(button, day.ToString("dddd d MMMM", british));
            var pick = day; button.Click += (_, _) => Choose(pick);
            days.Children.Add(button);
        }
    }
    private bool Holds(DateTime day) => model.Engine.Sessions.Any(s => model.CountsOn(s, day) && s.Duration > TimeSpan.Zero) || model.Notes.MeetingsOn(day).Any()
        || (day == model.TodayKey && model.Engine.Active is not null);
}

/// <summary>
/// The chosen day as a strip: its hours, the meetings as thin rules, the blocks faintly in their chart colours, now as a green line on today, and the reminder as a pin
/// with its time in a chip. A press or a drag moves the pin to the five minutes (never into the past), and letting go sets it; Left and Right move it five minutes and
/// Enter sets it from the keyboard.
/// </summary>
public sealed class RescheduleStrip : Control
{
    public const double StripHeight = 62;
    private readonly TimerModel model;
    private readonly Action<DateTime> set;
    private DateTime from, until, day;
    private bool dragging;
    /// <summary>Where the pin is.</summary>
    public DateTime Pin { get; private set; }
    public DateTime From => from;
    public DateTime Until => until;

    public RescheduleStrip(TimerModel model, Action<DateTime> set)
    {
        this.model = model; this.set = set; Height = StripHeight; Focusable = true; Cursor = new Cursor(StandardCursorType.Cross); ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    /// <summary>Shows <paramref name="day"/>, from its working hours widened to 08:00 to 18:00, with the pin at <paramref name="pin"/> (kept inside the strip and out of the past).</summary>
    public void Show(DateTime day, DateTime pin)
    {
        this.day = day.Date; var begins = model.DayBegins(this.day); var plan = model.Engine.Plan(this.day);
        from = begins.AddHours(8); until = begins.AddHours(18);
        if (plan.Working) { if (plan.Start < from) from = Hour(plan.Start); if (plan.End > until) until = Hour(plan.End, up: true); }
        if (from < begins) from = begins; if (until > begins.AddDays(1)) until = begins.AddDays(1);
        Pin = Clamp(pin); Describe(); InvalidateVisual();
    }
    private static DateTime Hour(DateTime t, bool up = false) { var h = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0); return up && h < t ? h.AddHours(1) : h; }
    private DateTime Floor => model.Engine.Now > from ? ReschedulePicker.RoundUp(model.Engine.Now) : from;
    private DateTime Clamp(DateTime t)
    {
        var whole = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0); whole = whole.AddMinutes(Math.Round(whole.Minute / 5.0) * 5 - whole.Minute);
        if (whole > until) whole = until; if (whole < Floor) whole = Floor; return whole;
    }
    /// <summary>Whether the pin can be anywhere on this day (false for a day already over).</summary>
    public bool Open => Floor <= until;
    private void Describe() => Avalonia.Automation.AutomationProperties.SetName(this, $"Reminder at {Pin.ToString("ddd d MMM, HH:mm", CultureInfo.GetCultureInfo("en-GB"))}. Drag or press to move it");

    private double X(DateTime t) => Math.Clamp((t - from).TotalMinutes / Math.Max(1, (until - from).TotalMinutes), 0, 1) * Bounds.Width;
    private DateTime At(double x) => Clamp(from.AddMinutes(Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * (until - from).TotalMinutes));

    /// <summary>Moves the pin to the time under <paramref name="x"/>; used by the pointer and by tests.</summary>
    public void MoveTo(double x) { Pin = At(x); Describe(); InvalidateVisual(); }
    /// <summary>Sets the reminder to where the pin is.</summary>
    public void Commit() { if (Open) set(Pin); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !Open) return;
        dragging = true; e.Pointer.Capture(this); Focus(); MoveTo(e.GetPosition(this).X); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (dragging) MoveTo(e.GetPosition(this).X); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging) return;
        dragging = false; e.Pointer.Capture(null); e.Handled = true; Commit();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); dragging = false; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right) { Pin = Clamp(Pin.AddMinutes(e.Key == Key.Right ? 5 : -5)); Describe(); InvalidateVisual(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
    }

    private Color C(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height; if (w <= 0) return;
        var line = C("Line", "#D5DBE5"); var quiet = C("Muted", "#4E5A6C"); var accent = C("AccentLit", "#8A55C4");
        // Rows, top to bottom: the pin's chip, the meetings, the blocks, the hours. Each has its own, so nothing lies over anything else.
        const double laneTop = 20, footRoom = 16, meetY = 22, blockY = 28, blockH = 15;
        context.DrawRectangle(new SolidColorBrush(C("Panel", "#FFFFFF")), new Pen(new SolidColorBrush(line), 1), new Rect(.5, .5, w - 1, h - 1), 10, 10);
        var hourPen = new Pen(new SolidColorBrush(line), 1);
        var labels = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
        for (var t = from.AddHours(1); t < until; t = t.AddHours(1))
        {
            var x = Math.Round(X(t)) + .5; context.DrawLine(hourPen, new Point(x, laneTop), new Point(x, h - footRoom));
            if (t.Hour % 2 == 0)
            {
                var text = new FormattedText(t.ToString("HH"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labels, 9.5, new SolidColorBrush(quiet));
                context.DrawText(text, new Point(x - text.Width / 2, h - footRoom + 2));
            }
        }
        foreach (var m in model.Notes.MeetingsOn(day))
        {
            var a = X(ReviewViews.MeetingStart(m)); var b = X(ReviewViews.MeetingEnd(m)); if (b - a < 1) continue;
            context.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(quiet, .45)), null, new Rect(a + 1, meetY, Math.Max(1, b - a - 2), 3), 1.5, 1.5);
        }
        var now = model.Engine.Now;
        var blocks = model.Engine.Sessions.Where(s => model.CountsOn(s, day) && s.Duration > TimeSpan.Zero).Select(s => (s.Account, s.From, s.To)).ToList();
        if (day == model.TodayKey && model.Engine.Active is { } running) blocks.Add((running.Account, running.Start, now));
        foreach (var (account, start, end) in blocks)
        {
            var a = X(start); var b = X(end); if (b - a < .5) continue;
            var rect = new Rect(a + .5, blockY, Math.Max(1, b - a - 1), blockH);
            if (BreakAccount.Is(account)) context.DrawRectangle(null, new Pen(new SolidColorBrush(SoftTint.WithAlpha(C("Break", "#E08A2E"), .7)), 1.2), rect.Deflate(.6), 4, 4);
            else
            {
                var colour = model.ReviewAccounts.FirstOrDefault(x => x.Name == account) is { } found ? Color.Parse(model.ChartColour(found.Colour)) : quiet;
                context.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(colour, .55)), null, rect, 4, 4);
            }
        }
        if (now > from && now < until)
        {
            var x = X(now); context.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(C("Live", "#1F9D5A"), .75)), null, new Rect(x - 1, laneTop, 2, h - footRoom - laneTop));
        }
        if (!Open) return;
        // The pin: a stem, a dot with a ring of the card's colour, and the time in an accent chip at the top, held inside the strip near either end.
        var px = X(Pin);
        context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(px - 1, 20, 2, h - footRoom - 20));
        context.DrawEllipse(new SolidColorBrush(C("Panel", "#FFFFFF")), null, new Point(px, 20), 6, 6);
        context.DrawEllipse(new SolidColorBrush(accent), null, new Point(px, 20), 4, 4);
        var chip = new FormattedText(Pin.ToString("HH:mm"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 11, new SolidColorBrush(C("AccentInk", "#241B36")));
        var cw = chip.Width + 12; var cx = Math.Clamp(px - cw / 2, 2, w - cw - 2);
        context.DrawRectangle(new SolidColorBrush(C("Accent", "#D3B0EA")), null, new Rect(cx, 1, cw, 15), 6, 6);
        context.DrawText(chip, new Point(cx + 6, 1 + (15 - chip.Height) / 2));
    }
}
