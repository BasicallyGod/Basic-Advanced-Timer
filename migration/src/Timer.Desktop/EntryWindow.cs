using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Timer.Core;

namespace Timer.Desktop;

public sealed class EntryWindow : Window
{
    public EntryWindow(TimerModel model, SessionRow? row, DateTime start, DateTime end)
    {
        Title = row is null ? "Add time" : "Edit entry"; WindowChrome.Attach(this);
        Width = 480; Height = 540; CanResize = false;
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
        Ui.Put(times, from); Ui.Put(times, Ui.Dash(), 1); Ui.Put(times, until, 2); root.Children.Add(times);
        root.Children.Add(Ui.Text(editableTime ? "Start/end use HH:mm or HH:mm:ss. Time changes affect credited totals." : "Adjustments: notes only; credited time stays unchanged.", 11, "muted"));
        var note = new TextBox { Name = "EntryNote", Watermark = "Entry note", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 110, Text = row is null ? "" : model.Editor.Note(row) };
        root.Children.Add(note);
        var error = new TextBlock { Name = "EntryError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        // Asked when a later end would run into the blocks that follow: they only move if the person agrees.
        var shoveText = new TextBlock { Name = "ShoveText", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        DateTime shoveEnd = default;
        var shove = new Border { Name = "ShovePrompt", IsVisible = false, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        shove.Classes.Add("chip");
        var shoveButtons = Ui.Horizontal(
            Ui.Button("Move later blocks", () =>
            {
                shove.IsVisible = false;
                if (row is not null && model.ExtendEntry(row, shoveEnd, note.Text ?? "")) Close(); else error.Text = model.LastError ?? model.EditError;
            }, "ShoveConfirm"),
            Ui.Button("Keep editing", () => shove.IsVisible = false, "ShoveCancel"));
        var shoveStack = new StackPanel { Spacing = 8 }; shoveStack.Children.Add(shoveText); shoveStack.Children.Add(shoveButtons); shove.Child = shoveStack;
        root.Children.Add(shove);
        until.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) shove.IsVisible = false; };
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
                // Only the end later on the same account and start can shove, so that is the only case offered.
                if (row is not null && editableTime && until.Text != initialEnd && from.Text == initialStart && (string?)accounts.SelectedItem == initialAccount && Parse(until.Text, true) > row.To)
                {
                    var newEnd = Parse(until.Text, true);
                    IReadOnlyList<Timer.Storage.TimelineChange> plan;
                    try { plan = model.PlanExtend(row, newEnd); } catch (ArgumentException ex) { error.Text = ex.Message; shove.IsVisible = false; return; }
                    if (plan.Count > 1)
                    {
                        var later = plan.Where(p => !ReferenceEquals(p.Source, row)).ToList();
                        var most = later.Max(p => p.Updated.From - p.Source.From);
                        shoveEnd = newEnd; error.Text = "";
                        shoveText.Text = $"Ending this block at {(newEnd == start.Date.AddDays(1) ? "24:00" : newEnd.ToString("HH:mm:ss"))} runs into the next {(later.Count == 1 ? "block" : later.Count + " blocks")}. "
                            + $"{(later.Count == 1 ? "It" : "They")} will move later by up to {model.FormatShort(most)} so nothing overlaps. Undo puts everything back. Move {(later.Count == 1 ? "it" : "them")}?";
                        shove.IsVisible = true; return;
                    }
                }
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
        if (row is not null) buttons.Children.Add(Ui.Button("Delete", () =>
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
