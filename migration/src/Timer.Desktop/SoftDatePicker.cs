using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Globalization;

namespace Timer.Desktop;

/// <summary>
/// Lightweight, theme-aware month picker shared by the Notes and header date flyouts.
/// Unlike the platform Calendar, it has no ruled cell borders or OS-blue selection.
/// Dates outside the month remain selectable and keyboard focus uses normal buttons.
/// </summary>
public sealed class SoftDatePicker : Border
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
    private readonly TextBlock monthTitle = Ui.Text("", 15);
    private readonly Grid days = new();
    private DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? selected;

    public event Action<DateTime>? DayPicked;

    public DateTime? SelectedDate
    {
        get => selected;
        set
        {
            if (value is not { } picked) { selected = null; RenderDays(); return; }
            selected = picked.Date;
            month = new DateTime(picked.Year, picked.Month, 1);
            RenderDays();
            DayPicked?.Invoke(picked.Date);
        }
    }

    public SoftDatePicker()
    {
        Width = 266; Padding = new Thickness(12, 10); BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14); Classes.Add("softCalendar");
        var column = new StackPanel { Spacing = 8 };
        var header = Ui.Grid("Auto,*,Auto"); header.Margin = new Thickness(2, 0, 2, 0);
        Button Nav(IconKind kind, string name, int step)
        {
            var button = Ui.IconButton(kind, step < 0 ? "Previous month" : "Next month", name, () => { month = month.AddMonths(step); RenderDays(); });
            button.Width = 30; button.Height = 30;
            return button;
        }
        Ui.Put(header, Nav(IconKind.Previous, "DateMonthBack", -1));
        monthTitle.FontWeight = FontWeight.SemiBold; monthTitle.TextAlignment = TextAlignment.Center;
        monthTitle.HorizontalAlignment = HorizontalAlignment.Center; Ui.Put(header, monthTitle, 1);
        Ui.Put(header, Nav(IconKind.Next, "DateMonthForward", 1), 2);
        column.Children.Add(header);

        var weekdays = Ui.Grid("*,*,*,*,*,*,*");
        string[] names = { "M", "T", "W", "T", "F", "S", "S" };
        for (var i = 0; i < names.Length; i++)
        {
            var label = Ui.Text(names[i], 11, "muted");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Ui.Put(weekdays, label, i);
        }
        column.Children.Add(weekdays); column.Children.Add(days);
        var today = Ui.Button("Today", () => SelectedDate = DateTime.Today, "DateChooseToday");
        today.Classes.Add("flat"); today.HorizontalAlignment = HorizontalAlignment.Right;
        today.Padding = new Thickness(10, 4); today.Height = 30;
        column.Children.Add(today);
        Child = column;
        RenderDays();
    }

    /// <summary>Position the picker without invoking the selection callback when a flyout opens.</summary>
    public void View(DateTime displayDay, DateTime? selectedDay)
    {
        selected = selectedDay?.Date;
        month = new DateTime(displayDay.Year, displayDay.Month, 1);
        RenderDays();
    }

    private void RenderDays()
    {
        monthTitle.Text = month.ToString("MMMM yyyy", British);
        var firstOffset = ((int)month.DayOfWeek + 6) % 7;
        var cells = ((firstOffset + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7) * 7;
        days.Children.Clear();
        days.ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*");
        days.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", cells / 7)));
        var first = month.AddDays(-firstOffset);
        for (var i = 0; i < cells; i++)
        {
            var day = first.AddDays(i);
            var button = Ui.Button(day.Day.ToString(CultureInfo.InvariantCulture), () => SelectedDate = day, "DateCell_" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            button.Classes.Add("softDay");
            button.Classes.Set("outsideMonth", day.Month != month.Month);
            button.Classes.Set("chosenDay", selected?.Date == day);
            button.Classes.Set("todayDay", DateTime.Today == day);
            button.FontSize = 13; button.FontWeight = selected?.Date == day ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetName(button, day.ToString("dddd d MMMM yyyy", British));
            Ui.Put(days, button, i % 7, i / 7);
        }
    }
}
