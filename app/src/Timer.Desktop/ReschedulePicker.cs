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
/// The chosen day as a strip (the study "Reaching any time of day", option A, the owner's sketch): at rest it is the whole day, its beginning to its end, with the
/// working hours as a quiet green band, the meetings as thin rules, the blocks faintly in their chart colours, now as a green line on today and the reminder as a pin
/// with its time in a chip. A press jumps the pin to that part of the day and zooms the strip to the hour around it (<see cref="ZoomMinutes"/>, five-minute ticks and a
/// lit edge), with the pin kept under the pointer as the scale changes; dragging places it to the five minutes, holding near either end pans along the day, and letting
/// go sets it and goes back to the whole day. It never goes into the past. Left and Right move it five minutes, Page Up and Page Down an hour, and Enter sets it.
/// </summary>
public sealed class RescheduleStrip : Control
{
    public const double StripHeight = 62;
    /// <summary>How much of the day the strip shows while the pin is held.</summary>
    public const int ZoomMinutes = 60;
    private readonly TimerModel model;
    private readonly Action<DateTime> set;
    // The whole day; and, while the pin is held, where the hour the strip has zoomed to begins.
    private DateTime from, until, day;
    private readonly Avalonia.Threading.DispatcherTimer pan = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private double lastX;
    /// <summary>Where the pin is.</summary>
    public DateTime Pin { get; private set; }
    public DateTime From => from;
    public DateTime Until => until;
    /// <summary>Where the zoomed hour begins while the pin is held, or null at rest (the whole day).</summary>
    public DateTime? ZoomFrom { get; private set; }
    private DateTime ViewFrom => ZoomFrom ?? from;
    private DateTime ViewTo => ZoomFrom?.AddMinutes(ZoomMinutes) ?? until;

    public RescheduleStrip(TimerModel model, Action<DateTime> set)
    {
        this.model = model; this.set = set; Height = StripHeight; Focusable = true; Cursor = new Cursor(StandardCursorType.Cross); ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        pan.Tick += (_, _) => PanAt(lastX);
        DetachedFromVisualTree += (_, _) => pan.Stop();
    }
    /// <summary>Shows the whole of <paramref name="day"/> with the pin at <paramref name="pin"/> (kept inside the day and out of the past).</summary>
    public void Show(DateTime day, DateTime pin)
    {
        this.day = day.Date; from = model.DayBegins(this.day); until = from.AddDays(1); ZoomFrom = null;
        Pin = Clamp(pin, snap: false); Describe(); InvalidateVisual();
    }
    private DateTime Floor => model.Engine.Now > from ? ReschedulePicker.RoundUp(model.Engine.Now) : from;
    // The last moment the pin can take: five minutes before the next day begins, so a reminder set here stays on this day.
    private DateTime Latest => until.AddMinutes(-5);
    // A pin the person moves lands on the five minutes; one the card opens on keeps a reminder's own minute (14:31 stays 14:31), as the old card did.
    private DateTime Clamp(DateTime t, bool snap = true)
    {
        var whole = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0); if (snap) whole = whole.AddMinutes(Math.Round(whole.Minute / 5.0) * 5 - whole.Minute);
        if (whole > Latest) whole = Latest; if (whole < Floor) whole = Floor; return whole;
    }
    /// <summary>Whether the pin can be anywhere on this day (false for a day already over).</summary>
    public bool Open => Floor <= Latest;
    private void Describe() => Avalonia.Automation.AutomationProperties.SetName(this, $"Reminder at {Pin.ToString("ddd d MMM, HH:mm", CultureInfo.GetCultureInfo("en-GB"))}. Drag or press to move it");

    private double X(DateTime t) => Math.Clamp((t - ViewFrom).TotalMinutes / Math.Max(1, (ViewTo - ViewFrom).TotalMinutes), 0, 1) * Bounds.Width;
    private double Fraction(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1);
    private DateTime At(double x) => Clamp(ViewFrom.AddMinutes(Fraction(x) * (ViewTo - ViewFrom).TotalMinutes));

    /// <summary>Moves the pin to the time under <paramref name="x"/> on the strip as it shows now; used by the pointer and by tests.</summary>
    public void MoveTo(double x) { Pin = At(x); Describe(); InvalidateVisual(); }
    /// <summary>Puts the pin at <paramref name="t"/> on the day showing, to the five minutes; used by tests in place of a drag.</summary>
    public void PinAt(DateTime t) { Pin = Clamp(t); Describe(); InvalidateVisual(); }
    /// <summary>
    /// A press at <paramref name="x"/>: the pin jumps to that time of the whole day, and the strip zooms to the hour round it, placed so the pin stays under the pointer.
    /// </summary>
    public void Hold(double x)
    {
        lastX = x; Pin = At(x);
        var start = Pin.AddMinutes(-Fraction(x) * ZoomMinutes);
        ZoomFrom = start < from ? from : start > until.AddMinutes(-ZoomMinutes) ? until.AddMinutes(-ZoomMinutes) : start;
        Describe(); InvalidateVisual();
    }
    /// <summary>While held, a pointer near either end pans the zoomed hour along the day (two minutes a step), and the pin follows the pointer.</summary>
    public void PanAt(double x)
    {
        lastX = x; if (ZoomFrom is not { } start) return;
        var f = Fraction(x); var by = f < .08 ? -2 : f > .92 ? 2 : 0;
        if (by != 0) { var moved = start.AddMinutes(by); ZoomFrom = moved < from ? from : moved > until.AddMinutes(-ZoomMinutes) ? until.AddMinutes(-ZoomMinutes) : moved; }
        MoveTo(x);
    }
    /// <summary>Lets go of the pin: back to the whole day, and the time is set.</summary>
    public void Release() { pan.Stop(); ZoomFrom = null; InvalidateVisual(); Commit(); }
    /// <summary>Sets the reminder to where the pin is.</summary>
    public void Commit()
    {
        // The card can stand open while the clock passes the pin (14:30 chosen, Enter pressed at 14:31), so the pin is checked against the clock as it is now, not as it
        // was when it was placed. One left behind moves to the earliest time it can be and nothing is saved, so what is set is always what the strip shows.
        if (!Open || Pin < model.Engine.Now) { if (Open) Pin = Clamp(Pin); Describe(); InvalidateVisual(); return; }
        set(Pin);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !Open) return;
        e.Pointer.Capture(this); Focus(); Hold(e.GetPosition(this).X); pan.Start(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (ZoomFrom is not null) { lastX = e.GetPosition(this).X; MoveTo(lastX); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ZoomFrom is null) return;
        e.Pointer.Capture(null); e.Handled = true; Release();
    }
    // A drag taken away (another window, Escape) goes back to the whole day without setting anything.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); if (ZoomFrom is null) return; pan.Stop(); ZoomFrom = null; InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var by = e.Key switch { Key.Left => -5, Key.Right => 5, Key.PageUp => -60, Key.PageDown => 60, _ => 0 };
        if (by != 0) { Pin = Clamp(Pin.AddMinutes(by)); Describe(); InvalidateVisual(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
    }

    private Color C(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height; if (w <= 0) return;
        var line = C("Line", "#D5DBE5"); var quiet = C("Muted", "#4E5A6C"); var accent = C("AccentLit", "#8A55C4");
        var zoomed = ZoomFrom is not null; var viewFrom = ViewFrom; var viewTo = ViewTo;
        // Rows, top to bottom: the pin's chip, the meetings, the blocks, the hours. Each has its own, so nothing lies over anything else.
        const double laneTop = 20, footRoom = 16, meetY = 22, blockY = 28, blockH = 15;
        // A lit edge while zoomed, so the change of scale reads as the strip looking closer rather than as the day changing.
        context.DrawRectangle(new SolidColorBrush(C("Panel", "#FFFFFF")), new Pen(new SolidColorBrush(zoomed ? accent : line), zoomed ? 1.5 : 1), new Rect(.75, .75, w - 1.5, h - 1.5), 10, 10);
        var plan = model.Engine.Plan(day);
        if (plan.Working && plan.End > viewFrom && plan.Start < viewTo)
        {
            var a = X(plan.Start); var b = X(plan.End);
            context.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(C("Live", "#1F9D5A"), .14)), null, new Rect(a, laneTop + 2, Math.Max(0, b - a), h - footRoom - laneTop - 2));
        }
        // Ticks and labels for the scale on show: at rest an hour a tick and a label every six hours; zoomed five minutes a tick and a label every quarter hour.
        var tick = zoomed ? 5 : 60; var label = zoomed ? 15 : 360;
        var tickPen = new Pen(new SolidColorBrush(line), 1);
        var labels = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
        var first = viewFrom.AddMinutes((tick - (viewFrom - from).TotalMinutes % tick) % tick);
        for (var t = first; t <= viewTo; t = t.AddMinutes(tick))
        {
            var minutes = (int)Math.Round((t - from).TotalMinutes); var major = minutes % label == 0;
            var x = Math.Round(X(t)) + .5;
            if (x > 1 && x < w - 1) context.DrawLine(tickPen, new Point(x, major ? laneTop : h - footRoom - 6), new Point(x, h - footRoom));
            if (!major) continue;
            var words = zoomed ? t.ToString("HH:mm") : t == until && t.Hour == 0 ? "24" : t.ToString("HH");
            var text = new FormattedText(words, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labels, 9.5, new SolidColorBrush(quiet));
            context.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 3, w - text.Width - 3), h - footRoom + 2));
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
            if (end <= viewFrom || start >= viewTo) continue;
            var a = X(start); var b = X(end); if (b - a < .5) continue;
            var rect = new Rect(a + .5, blockY, Math.Max(1, b - a - 1), blockH);
            if (BreakAccount.Is(account)) context.DrawRectangle(null, new Pen(new SolidColorBrush(SoftTint.WithAlpha(C("Break", "#E08A2E"), .7)), 1.2), rect.Deflate(.6), 4, 4);
            else
            {
                var colour = model.ReviewAccounts.FirstOrDefault(x => x.Name == account) is { } found ? Color.Parse(model.ChartColour(found.Colour)) : quiet;
                context.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(colour, .55)), null, rect, 4, 4);
            }
        }
        if (now > viewFrom && now < viewTo)
        {
            var x = X(now); context.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(C("Live", "#1F9D5A"), .75)), null, new Rect(x - 1, laneTop, 2, h - footRoom - laneTop));
        }
        if (!Open || Pin < viewFrom || Pin > viewTo) return;
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

/// <summary>
/// The card a reminder is set in everywhere it hangs under a button (Details, the Notes card's bell, the Notes page): its heading and what it is for, the same days and
/// strip as Reschedule, and under them Remove (only while there is a reminder to take off) and Cancel. Letting go of the pin is the answer, so there is no Set; the
/// caller decides whether that saves at once (Details) or is kept for a note still being typed (the add lines). The meeting's day keeps <see cref="ReminderPopup"/>'s
/// day-only month, since it has no time to pick.
/// </summary>
public sealed class ReminderPickerCard : Border
{
    public const double CardWidth = 300;
    /// <summary>The days and the strip.</summary>
    public ReschedulePicker Picker { get; }

    public ReminderPickerCard(TimerModel model, string forText, DateTime? current, Action<DateTime?> answer, Action close)
    {
        Name = "ReminderPicker"; CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1); Padding = new Thickness(12); Width = CardWidth;
        Classes.Add("surface"); Classes.Add("flyoutCard"); BoxShadow = BoxShadows.Parse("0 10 28 0 #29141E46");
        var column = new StackPanel { Spacing = 10 };
        var title = Ui.Grid("Auto,*,Auto"); title.ColumnSpacing = 8;
        Ui.Put(title, new TextBlock { Text = "Reminder", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var who = new TextBlock { Text = forText, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; who.Classes.Add("muted"); Ui.Put(title, who, 1);
        var cross = Ui.IconButton(IconKind.Close, "Close", "ReminderClose", close); cross.Width = 24; cross.Height = 24; Ui.Put(title, cross, 2);
        Picker = new ReschedulePicker(model, current, at => answer(at));
        // Remove only when there is something to remove, at the left; Cancel at the right, where Reschedule's is.
        var foot = Ui.Grid("Auto,*,Auto");
        if (current is not null) { var remove = Ui.Ctl("ReminderRemove", "Remove", null, "Remove", () => answer(null), small: true); remove.Padding = new Thickness(14, 0); Ui.Put(foot, remove); }
        var cancel = Ui.Button("", close, "ReminderCancel"); cancel.Classes.Add("pillBare"); cancel.Padding = new Thickness(4, 2);
        var words = NoteParts.Ink("Cancel", "Muted", 12.5); words.FontWeight = FontWeight.SemiBold; cancel.Content = words; cancel.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(cancel, "Cancel"); Ui.Put(foot, cancel, 2);
        column.Children.Add(title); column.Children.Add(Picker); column.Children.Add(foot);
        Child = column;
    }
}
