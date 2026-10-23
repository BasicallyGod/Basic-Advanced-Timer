using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>How a reminder or a day reads in the Notes: Today, Tomorrow, or Mon 26 Oct.</summary>
public static class NoteDates
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
    public static string Day(DateTime day, DateTime today)
        => day.Date == today.Date ? "Today" : day.Date == today.Date.AddDays(1) ? "Tomorrow" : day.ToString("ddd d MMM", British);
    public static string When(DateTime when, DateTime today) => Day(when, today) + ", " + when.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// The small pop-up that sets a reminder's day and time (brief items 33 and 42): a month with past days off, quick days (Today, Tomorrow, Next Mon), hour and
/// minute boxes with steppers in five-minute steps, four preset times, a summary line that warns when the time has passed, and Remove and Set. It decides
/// nothing itself: Set and Remove hand the answer back, and the cross closes it.
/// </summary>
public sealed class ReminderPopup : Border
{
    private static readonly int[] Presets = { 9, 12, 15, 17 };
    private readonly Func<DateTime> now;
    private readonly Action<DateTime?> answer;
    private DateTime chosen, month;
    private int hour, minute;
    private readonly Grid days = new();
    private readonly TextBlock monthTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox hourBox = new() { Name = "ReminderHour", MaxLength = 2 }, minuteBox = new() { Name = "ReminderMinute", MaxLength = 2 };
    private readonly TextBlock summary = new() { Name = "ReminderSummary", FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel quickRow = new() { Orientation = Orientation.Horizontal }, presetRow = new() { Orientation = Orientation.Horizontal };
    private bool syncing;

    public ReminderPopup(Func<DateTime> now, string forText, DateTime? current, Action<DateTime?> answer, Action close, string heading = "Reminder")
    {
        this.now = now; this.answer = answer; Name = "ReminderPopup";
        var start = current ?? now().Date.AddHours(15);
        chosen = start.Date; hour = start.Hour; minute = start.Minute / 5 * 5; month = new DateTime(chosen.Year, chosen.Month, 1);
        CornerRadius = new CornerRadius(16); BorderThickness = new Thickness(1); Padding = new Thickness(12); Width = 392;
        Classes.Add("surface"); Classes.Add("flyoutCard");
        BoxShadow = BoxShadows.Parse("0 10 28 0 #29141E46");
        var column = new StackPanel { Spacing = 10 };

        var title = Ui.Grid("Auto,*,Auto"); title.ColumnSpacing = 8;
        Ui.Put(title, new TextBlock { Text = heading, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var who = new TextBlock { Text = forText, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; who.Classes.Add("muted"); Ui.Put(title, who, 1);
        var closeButton = Ui.IconButton(IconKind.Close, "Close", "ReminderClose", close); closeButton.Width = 24; closeButton.Height = 24; Ui.Put(title, closeButton, 2);
        column.Children.Add(title);

        var main = Ui.Grid("196,*"); main.ColumnSpacing = 14;
        var calendar = new StackPanel { Spacing = 4 };
        var nav = Ui.Grid("Auto,*,Auto");
        Ui.Put(nav, Small(Ui.IconButton(IconKind.Previous, "Previous month", "ReminderMonthBack", () => { month = month.AddMonths(-1); DrawDays(); })));
        Ui.Put(nav, monthTitle, 1);
        Ui.Put(nav, Small(Ui.IconButton(IconKind.Next, "Next month", "ReminderMonthForward", () => { month = month.AddMonths(1); DrawDays(); })), 2);
        calendar.Children.Add(nav);
        var heads = Ui.Grid("*,*,*,*,*,*,*");
        for (var i = 0; i < 7; i++) { var head = new TextBlock { Text = new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" }[i], FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }; head.Classes.Add("muted"); Ui.Put(heads, head, i); }
        calendar.Children.Add(heads); calendar.Children.Add(days);
        Ui.Put(main, calendar);

        var right = new StackPanel { Spacing = 6 };
        right.Children.Add(Label("Day")); right.Children.Add(quickRow);
        var timeLabel = Label("Time"); timeLabel.Margin = new Thickness(0, 4, 0, 0); right.Children.Add(timeLabel);
        var time = Ui.Horizontal(Stepper(hourBox, 1, "hour"), new TextBlock { Text = ":", FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, Stepper(minuteBox, 5, "minutes"));
        time.Spacing = 4; right.Children.Add(time); right.Children.Add(presetRow);
        Ui.Put(main, right, 1);
        column.Children.Add(main);

        var footer = Ui.Grid("*,Auto,Auto"); footer.ColumnSpacing = 8; footer.Margin = new Thickness(0, 0, 0, 0);
        Ui.Put(footer, summary);
        Ui.Put(footer, Ui.Ctl("ReminderRemove", "Remove", null, "Take the reminder away", () => answer(null), small: true), 1);
        var set = Ui.Ctl("ReminderSet", "Set", null, "Set the reminder", () => answer(Chosen), small: true); set.Classes.Add("primary"); Ui.Put(footer, set, 2);
        var rule = Ui.Rule(); rule.Margin = new Thickness(0, 2, 0, 0); column.Children.Add(rule); column.Children.Add(footer);
        Child = column;

        hourBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !syncing && int.TryParse(hourBox.Text, out var h) && h is >= 0 and <= 23) { hour = h; Redraw(false); } };
        minuteBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !syncing && int.TryParse(minuteBox.Text, out var m) && m is >= 0 and <= 59) { minute = m; Redraw(false); } };
        Redraw(true);
    }

    /// <summary>The day and time as chosen now.</summary>
    public DateTime Chosen => chosen.Date.AddHours(hour).AddMinutes(minute);

    private static Button Small(Button button) { button.Width = 24; button.Height = 24; return button; }
    private static TextBlock Label(string text) { var label = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 }; label.Classes.Add("muted"); return label; }

    private Control Stepper(TextBox box, int step, string what)
    {
        box.Width = 44; box.Height = 34; box.TextAlignment = TextAlignment.Center; box.FontSize = 18; box.FontWeight = FontWeight.Bold; box.Padding = new Thickness(0, 0, 0, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center; box.Classes.Add("clock"); AutomationProperties.SetName(box, what == "hour" ? "Hour" : "Minutes");
        Button Arrow(bool up)
        {
            var button = Ui.Button("", () => Step(what == "hour" ? up ? 1 : -1 : up ? step : -step, what), what + (up ? "Up" : "Down"));
            button.Width = 44; button.Height = 20; button.Padding = new Thickness(0); button.Classes.Add("flat"); button.Content = new Chevron { Expanded = up };
            AutomationProperties.SetName(button, (up ? "Later " : "Earlier ") + (what == "hour" ? "hour" : "by five minutes")); ToolTip.SetTip(button, (up ? "Later " : "Earlier ") + (what == "hour" ? "hour" : "by five minutes"));
            return button;
        }
        var stack = new StackPanel { Spacing = 0 }; stack.Children.Add(Arrow(true)); stack.Children.Add(box); stack.Children.Add(Arrow(false));
        return stack;
    }

    private void Step(int delta, string what)
    {
        if (what == "hour") hour = (hour + delta + 24) % 24;
        else { minute = (minute / 5 * 5 + delta + 60) % 60; }
        Redraw(true);
    }

    private void Redraw(bool writeBoxes)
    {
        if (writeBoxes) { syncing = true; hourBox.Text = hour.ToString("00", CultureInfo.InvariantCulture); minuteBox.Text = minute.ToString("00", CultureInfo.InvariantCulture); syncing = false; }
        var today = now().Date;
        DrawDays();
        quickRow.Children.Clear();
        foreach (var (name, day) in new[] { ("Today", today), ("Tomorrow", today.AddDays(1)), ("Next Mon", NextMonday(today)) })
        {
            var button = Option(name, chosen.Date == day, () => { chosen = day; month = new DateTime(day.Year, day.Month, 1); Redraw(false); }); button.Name = "ReminderDay" + name.Replace(" ", "");
            button.Margin = new Thickness(0, 0, 5, 5); quickRow.Children.Add(button);
        }
        presetRow.Children.Clear();
        foreach (var preset in Presets)
        {
            var text = preset.ToString("00", CultureInfo.InvariantCulture) + ":00";
            var button = Option(text, hour == preset && minute == 0, () => { hour = preset; minute = 0; Redraw(true); }); button.Name = "ReminderTime" + preset;
            button.Margin = new Thickness(0, 0, 5, 5); presetRow.Children.Add(button);
        }
        var late = Chosen < now();
        summary.Text = NoteDates.When(Chosen, today) + (late ? " (already past)" : "");
        summary.Classes.Set("overtimeText", late);
    }

    private static DateTime NextMonday(DateTime from) { var days = ((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7; return from.AddDays(days == 0 ? 7 : days); }

    private static Button Option(string text, bool on, Action click)
    {
        var button = Ui.Button(text, click); button.Classes.Add("ctl"); button.Classes.Add("pillOpt"); button.Classes.Set("optOn", on); button.Height = 28; button.Padding = new Thickness(9, 0); button.FontSize = 12.5;
        return button;
    }

    private void DrawDays()
    {
        var today = now().Date;
        monthTitle.Text = month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        var offset = ((int)month.DayOfWeek + 6) % 7;
        var cells = ((offset + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7) * 7;
        days.Children.Clear();
        days.ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*");
        days.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", cells / 7)));
        for (var i = 0; i < cells; i++)
        {
            var day = month.AddDays(i - offset);
            if (day.Month != month.Month) continue;
            var button = Ui.Button(day.Day.ToString(CultureInfo.InvariantCulture), () => { chosen = day; Redraw(false); }, "ReminderCell_" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            button.Classes.Add("softDay"); button.Width = double.NaN; button.Height = 26; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.FontSize = 12.5;
            button.IsEnabled = day >= today; // A day that has gone cannot be a reminder.
            button.Classes.Set("chosenDay", chosen.Date == day); button.Classes.Set("todayDay", today == day);
            AutomationProperties.SetName(button, day.ToString("d MMMM", CultureInfo.GetCultureInfo("en-GB")));
            Ui.Put(days, button, i % 7, i / 7);
        }
    }
}
