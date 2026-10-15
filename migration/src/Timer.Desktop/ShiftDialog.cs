using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Timer.Core;

namespace Timer.Desktop;

public sealed class ShiftDialog : Dialog
{
    public ShiftDialog(TimerModel model)
    {
        Title = "Working hours";
        Width = 480; // The height follows the content, up to the window.
        // The dialog's header carries the title, so the editor has no heading of its own, only the margin round it.
        Content = new ScrollViewer { Content = new Border { Padding = new Thickness(24, 4, 24, 24), Child = new ShiftEditor(model, Close, Close) } };
    }
}

/// <summary>The working days and hours form. It sits in its own dialog and in the settings page, so it is one control with two ways of being finished.</summary>
public sealed class ShiftEditor : StackPanel
{
    /// <param name="saved">Called after the hours are saved.</param>
    /// <param name="cancelled">Called when Cancel is pressed. Without it there is no Cancel button, as on the settings page.</param>
    public ShiftEditor(TimerModel model, Action saved, Action? cancelled = null, bool heading = false, bool week = false)
    {
        Margin = new Thickness(heading ? 24 : 0); Spacing = 12;
        var root = this;
        if (heading) root.Children.Add(Ui.Heading("Working hours", 24));
        root.Children.Add(new TextBlock { Text = "Working days and shifts. Times are HH:mm, and an end earlier than the start is an overnight shift, such as 22:00 to 06:00, which belongs to the day it starts.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var entries = new List<(DayOfWeek Day, CheckBox On, TextBox Start, TextBox End)>();
        // On the settings page the week is one row of seven columns, each a day with its start and end beneath, so it takes a few lines instead of a long list.
        var weekGrid = week ? Ui.Grid("*,*,*,*,*,*,*") : null;
        var column = 0;
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var shift = model.Engine.Schedule.Shifts.GetValueOrDefault(day) ?? model.Engine.Schedule.Default;
            var on = new CheckBox { Content = week ? day.ToString()[..3] : day.ToString(), IsChecked = model.Engine.Schedule.Days.Contains(day) };
            var start = new TextBox { Text = shift.Start.ToString(@"hh\:mm"), Width = week ? double.NaN : 80 };
            var end = new TextBox { Text = shift.End.ToString(@"hh\:mm"), Width = week ? double.NaN : 80 };
            if (weekGrid is not null)
            {
                var cell = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 8, 0) };
                cell.Children.Add(on); cell.Children.Add(start); cell.Children.Add(end);
                Ui.Put(weekGrid, cell, column++);
                entries.Add((day, on, start, end)); continue;
            }
            var row = Ui.Grid("*,80,24,80");
            Ui.Put(row, on); Ui.Put(row, start, 1); Ui.Put(row, Ui.Dash(), 2); Ui.Put(row, end, 3);
            root.Children.Add(row); entries.Add((day, on, start, end));
        }
        if (weekGrid is not null) root.Children.Add(weekGrid);
        var today = new CheckBox { Content = "Also reset today to its new scheduled start/end" };
        root.Children.Add(today);
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        var buttons = Ui.Horizontal(Ui.DialogButton("Save", () =>
        {
            try
            {
                var shifts = entries.Where(e => e.On.IsChecked == true).ToDictionary(e => e.Day,
                    e => new Shift(TimeSpan.ParseExact(e.Start.Text ?? "", @"hh\:mm", CultureInfo.InvariantCulture),
                        TimeSpan.ParseExact(e.End.Text ?? "", @"hh\:mm", CultureInfo.InvariantCulture)));
                model.SetShifts(shifts, today.IsChecked == true);
                if (model.LastError is not null) { error.Text = model.LastError; return; }
                error.Text = ""; saved();
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            { error.Text = "Use HH:mm, with an end different from the start (earlier means an overnight shift), and at least one working day."; }
        }, "SaveShifts"));
        if (cancelled is not null) buttons.Children.Add(Ui.DialogButton("Cancel", cancelled));
        root.Children.Add(buttons);
    }
}
