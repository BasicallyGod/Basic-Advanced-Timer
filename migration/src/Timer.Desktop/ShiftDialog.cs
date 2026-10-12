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
        Content = new ScrollViewer { Content = new ShiftEditor(model, Close, Close, heading: true) };
    }
}

/// <summary>The working days and hours form. It sits in its own dialog and in the settings page, so it is one control with two ways of being finished.</summary>
public sealed class ShiftEditor : StackPanel
{
    /// <param name="saved">Called after the hours are saved.</param>
    /// <param name="cancelled">Called when Cancel is pressed. Without it there is no Cancel button, as on the settings page.</param>
    public ShiftEditor(TimerModel model, Action saved, Action? cancelled = null, bool heading = false)
    {
        Margin = new Thickness(heading ? 24 : 0); Spacing = 12;
        var root = this;
        if (heading) root.Children.Add(Ui.Text("Working hours", 22));
        root.Children.Add(new TextBlock { Text = "Choose your working days and shifts. Times use HH:mm.\nThe day clock starts at the scheduled time; overtime starts at the end.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var entries = new List<(DayOfWeek Day, CheckBox On, TextBox Start, TextBox End)>();
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var shift = model.Engine.Schedule.Shifts.GetValueOrDefault(day) ?? model.Engine.Schedule.Default;
            var on = new CheckBox { Content = day.ToString(), IsChecked = model.Engine.Schedule.Days.Contains(day) };
            var start = new TextBox { Text = shift.Start.ToString(@"hh\:mm"), Width = 80 };
            var end = new TextBox { Text = shift.End.ToString(@"hh\:mm"), Width = 80 };
            var row = Ui.Grid("*,80,24,80");
            Ui.Put(row, on); Ui.Put(row, start, 1); Ui.Put(row, Ui.Dash(), 2); Ui.Put(row, end, 3);
            root.Children.Add(row); entries.Add((day, on, start, end));
        }
        var today = new CheckBox { Content = "Also reset today to its new scheduled start/end" };
        root.Children.Add(today);
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        var buttons = Ui.Horizontal(Ui.Button("Save", () =>
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
            { error.Text = "Use HH:mm, with an end after the start, and at least one working day."; }
        }, "SaveShifts"));
        if (cancelled is not null) buttons.Children.Add(Ui.Button("Cancel", cancelled));
        root.Children.Add(buttons);
    }
}
