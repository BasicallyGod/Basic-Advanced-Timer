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

/// <summary>The day-only calendar used for meetings. Timed reminders use ReminderPickerCard.</summary>
public sealed class ReminderPopup : Border
{
    private readonly Func<DateTime> now;
    private DateTime chosen, month;
    private readonly Grid days = new();
    private readonly TextBlock monthTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summary = new() { Name = "ReminderSummary", FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    public const double DayOnlyWide = 222;

    public ReminderPopup(Func<DateTime> now, string forText, DateTime? current, Action<DateTime?> answer, Action close, string heading = "Meeting day")
    {
        this.now = now; Name = "ReminderPopup";
        var start = current ?? now().Date;
        chosen = start.Date; month = new DateTime(chosen.Year, chosen.Month, 1);
        CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1); Padding = new Thickness(12); Width = DayOnlyWide;
        Classes.Add("surface"); Classes.Add("flyoutCard");
        BoxShadow = BoxShadows.Parse("0 10 28 0 #29141E46");
        var column = new StackPanel { Spacing = 10 };

        var title = Ui.Grid("Auto,*,Auto"); title.ColumnSpacing = 8;
        Ui.Put(title, new TextBlock { Text = heading, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var who = new TextBlock { Text = forText, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; who.Classes.Add("muted"); Ui.Put(title, who, 1);
        var closeButton = Ui.IconButton(IconKind.Close, "Close", "ReminderClose", close); closeButton.Width = 24; closeButton.Height = 24; Ui.Put(title, closeButton, 2);
        column.Children.Add(title);

        var calendar = new StackPanel { Spacing = 4, Name = "ReminderMonthPanel", HorizontalAlignment = HorizontalAlignment.Center, Width = 196 };
        var nav = Ui.Grid("Auto,*,Auto");
        Ui.Put(nav, Small(Ui.IconButton(IconKind.Previous, "Previous month", "ReminderMonthBack", () => { month = month.AddMonths(-1); DrawDays(); })));
        Ui.Put(nav, monthTitle, 1);
        Ui.Put(nav, Small(Ui.IconButton(IconKind.Next, "Next month", "ReminderMonthForward", () => { month = month.AddMonths(1); DrawDays(); })), 2);
        calendar.Children.Add(nav);
        var heads = Ui.Grid("*,*,*,*,*,*,*");
        for (var i = 0; i < 7; i++) { var head = new TextBlock { Text = new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" }[i], FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }; head.Classes.Add("muted"); Ui.Put(heads, head, i); }
        calendar.Children.Add(heads); calendar.Children.Add(days);
        column.Children.Add(calendar);

        var footer = Ui.Grid("*,Auto,Auto"); summary.HorizontalAlignment = HorizontalAlignment.Left; footer.ColumnSpacing = 8; footer.Margin = new Thickness(0, 0, 0, 0);
        var set = Ui.Ctl("ReminderSet", "Set", null, "Set", () => answer(Chosen), small: true); set.Classes.Add("primary"); Ui.Put(footer, set, 2); footer.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
        var rule = Ui.Rule(); rule.Margin = new Thickness(0, 2, 0, 0); column.Children.Add(rule);
        Ui.Put(footer, summary, 0);
        column.Children.Add(footer);
        Child = column;

        Redraw();
    }

    /// <summary>
    /// Sets the card in a pop-up with room round it for its whole shadow, and lifts the pop-up by that room so the card still sits where it did. A pop-up's window
    /// clips anything drawn past its edge, so a shadow with no room showed as flat grey corners beyond the card's rounded ones.
    /// </summary>
    public static T Roomy<T>(T card, Avalonia.Controls.Primitives.Popup host) where T : Border
    {
        card.Margin = HangingCard.Room; card.BoxShadow = HangingCard.Shadow;
        host.VerticalOffset = -HangingCard.Room.Top; host.HorizontalOffset = 0;
        return card;
    }

    public DateTime Chosen => chosen.Date;

    private static Button Small(Button button) { button.Width = 24; button.Height = 24; return button; }
    private void Redraw()
    {
        DrawDays();
        summary.Text = NoteDates.Day(chosen, now().Date);
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
            var button = Ui.Button(day.Day.ToString(CultureInfo.InvariantCulture), () => { chosen = day; Redraw(); }, "ReminderCell_" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            button.Classes.Add("softDay"); button.Width = double.NaN; button.Height = 26; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.FontSize = 12.5;
            button.Classes.Set("chosenDay", chosen.Date == day);
            button.Classes.Set("weekendDay", day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
            button.Content = SoftDatePicker.DayContent(day.Day.ToString(CultureInfo.InvariantCulture), 12.5);
            AutomationProperties.SetName(button, day.ToString("d MMMM", CultureInfo.GetCultureInfo("en-GB")));
            Ui.Put(days, button, i % 7, i / 7);
        }
    }
}
