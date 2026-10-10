using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Timer.Core;

namespace Timer.Desktop;

public sealed class EntryWindow : Window
{
    public EntryWindow(TimerModel model, SessionRow? row, DateTime start, DateTime end)
    {
        Title = row is null ? "Add time" : "Edit entry";
        Width = 480; Height = 460; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        var editableTime = row is null || row.IsPositioned;
        root.Children.Add(Ui.Text(Title, 22));
        root.Children.Add(Ui.Text(start.ToString("dddd, d MMMM yyyy"), 13, "muted"));
        var names = model.ReviewAccounts.Select(a => a.Name).Concat(model.Engine.Sessions.Select(s => s.Account)).Distinct(StringComparer.OrdinalIgnoreCase).Select(BreakAccount.Display).ToArray();
        var initialAccount = names.FirstOrDefault(n => n.Equals(BreakAccount.Display(row?.Account ?? model.SelectedAccount ?? ""), StringComparison.OrdinalIgnoreCase)) ?? names.First();
        var accounts = new ComboBox { Name = "EntryAccount", ItemsSource = names, SelectedItem = initialAccount, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, IsEnabled = editableTime };
        root.Children.Add(accounts);
        var times = Ui.Grid("*,24,*");
        var from = new TextBox { Name = "EntryStart", Text = start.ToString("HH:mm:ss"), IsEnabled = editableTime };
        var until = new TextBox { Name = "EntryEnd", Text = end == start.Date.AddDays(1) ? "24:00:00" : end.ToString("HH:mm:ss"), IsEnabled = editableTime };
        var initialStart = from.Text; var initialEnd = until.Text;
        Ui.Put(times, from); Ui.Put(times, Ui.Text("–"), 1); Ui.Put(times, until, 2); root.Children.Add(times);
        root.Children.Add(Ui.Text(editableTime ? "Start/end use HH:mm or HH:mm:ss. Time changes affect credited totals." : "Merged credit and adjustments: notes only; credited time stays unchanged.", 11, "muted"));
        var note = new TextBox { Name = "EntryNote", Watermark = "Entry note", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 110, Text = row is null ? "" : model.Editor.Note(row) };
        root.Children.Add(note);
        var error = new TextBlock { Name = "EntryError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        DateTime Parse(string? value, bool finish)
        {
            if (finish && value is "24:00" or "24:00:00") return start.Date.AddDays(1);
            var time = TimeSpan.ParseExact(value ?? "", new[] { @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture);
            if (time >= TimeSpan.FromDays(1)) throw new FormatException();
            return start.Date + time;
        }
        var applied = false;
        var save = Ui.Button("Save", () =>
        {
            try
            {
                if (applied) { if (model.Save()) Close(); else error.Text = model.LastError; return; }
                var previous = model.Editor.UndoState;
                if (model.EditEntry(row, row is not null && (string?)accounts.SelectedItem == initialAccount ? row.Account : BreakAccount.Stored((string?)accounts.SelectedItem ?? row?.Account ?? ""), editableTime && from.Text != initialStart ? Parse(from.Text, false) : start,
                    editableTime && until.Text != initialEnd ? Parse(until.Text, true) : end, note.Text ?? "")) Close();
                else
                {
                    applied = model.LastError is not null && !ReferenceEquals(previous, model.Editor.UndoState);
                    error.Text = model.LastError ?? model.EditError;
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { error.Text = "Use HH:mm or HH:mm:ss, with an end after the start."; }
        }, "SaveEntry");
        var buttons = Ui.Horizontal(save, Ui.Button("Cancel", Close));
        if (row is { IsMerged: false }) buttons.Children.Add(Ui.Button("Delete", () =>
        {
            if (applied) { if (model.Save()) Close(); else error.Text = model.LastError; return; }
            var previous = model.Editor.UndoState;
            if (model.DeleteEntry(row)) Close(); else
            {
                applied = model.LastError is not null && !ReferenceEquals(previous, model.Editor.UndoState);
                error.Text = model.LastError ?? model.EditError;
            }
        }, "DeleteEntry"));
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root };
    }
}
