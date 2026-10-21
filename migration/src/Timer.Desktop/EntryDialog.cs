using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Timer.Core;

namespace Timer.Desktop;

public sealed class EntryDialog : Dialog
{
    public EntryDialog(TimerModel model, SessionRow? row, DateTime start, DateTime end)
    {
        Title = row is null ? "Add time" : "Edit entry";
        Width = 480; // The height follows the content, up to the window.
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 24), Spacing = 12 };
        root.Children.Add(Ui.Text(start.ToString("dddd, d MMMM yyyy"), 13, "muted"));
        var names = model.ReviewAccounts.Select(a => a.Name).Concat(model.Engine.Sessions.Select(s => s.Account)).Distinct(StringComparer.OrdinalIgnoreCase).Select(BreakAccount.Display).ToArray();
        var initialAccount = names.FirstOrDefault(n => n.Equals(BreakAccount.Display(row?.Account ?? model.TakeEntryAccountHint() ?? model.SelectedAccount ?? ""), StringComparison.OrdinalIgnoreCase)) ?? names.First();
        var accounts = new ComboBox { Name = "EntryAccount", ItemsSource = names, SelectedItem = initialAccount, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        root.Children.Add(accounts);
        var times = Ui.Grid("*,24,*");
        var from = new TextBox { Name = "EntryStart", Text = start.ToString("HH:mm:ss") };
        var until = new TextBox { Name = "EntryEnd", Text = end == start.Date.AddDays(1) ? "24:00:00" : end.ToString("HH:mm:ss") };
        var initialStart = from.Text; var initialEnd = until.Text;
        Ui.Put(times, from); Ui.Put(times, Ui.Dash(), 1); Ui.Put(times, until, 2); root.Children.Add(times);
        // The length and the end follow each other: type either, and the other is worked out from the start.
        var length = new TextBox { Name = "EntryLength", Width = 130, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        root.Children.Add(Ui.Horizontal(Ui.Text("Length", 13, "muted"), length));
        var hint = Ui.Text("Times are HH:mm. Length is minutes (45) or hours and minutes (1:15).", 11, "muted");
        hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap; root.Children.Add(hint);
        var note = new TextBox { Name = "EntryNote", Watermark = "Entry note", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 110, Text = row is null ? "" : model.Editor.Note(row) };
        root.Children.Add(note);
        var error = new TextBlock { Name = "EntryError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        // Asked when a later end would run into the blocks that follow: they only move if the person agrees.
        var shoveText = new TextBlock { Name = "ShoveText", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        DateTime shoveEnd = default;
        var shove = new Border { Name = "ShovePrompt", IsVisible = false, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        shove.Classes.Add("chip");
        // Ticking this keeps the choice, so a longer entry moves the blocks after it without asking again. Settings has the same switch.
        var always = new CheckBox { Name = "AlwaysShove", Content = "Always move later blocks, without asking" };
        var shoveButtons = Ui.Horizontal(
            Ui.DialogButton("Move blocks", () =>
            {
                shove.IsVisible = false;
                if (always.IsChecked == true) model.SetMoveLaterAlways(true);
                if (row is not null && model.ExtendEntry(row, shoveEnd, note.Text ?? "")) Close(); else error.Text = model.LastError ?? model.EditError;
            }, "ShoveConfirm"),
            // The longer entry goes in as it is, over the top of the blocks after it. Those hours are then counted twice, which is for the person to decide.
            Ui.DialogButton("Allow overlap", () =>
            {
                shove.IsVisible = false;
                if (row is not null && model.EditEntry(row, row.Account, row.From, shoveEnd, note.Text ?? "", allowOverlap: true)) Close(); else error.Text = model.LastError ?? model.EditError;
            }, "ShoveOverlap"),
            Ui.DialogButton("Keep editing", () => shove.IsVisible = false, "ShoveCancel"));
        var shoveStack = new StackPanel { Spacing = 8 }; shoveStack.Children.Add(shoveText); shoveStack.Children.Add(always); shoveStack.Children.Add(shoveButtons); shove.Child = shoveStack;
        root.Children.Add(shove);
        until.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) shove.IsVisible = false; };
        DateTime Parse(string? value, bool finish)
        {
            if (finish && value is "24:00" or "24:00:00") return start.Date.AddDays(1);
            var time = TimeSpan.ParseExact(value ?? "", new[] { @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture);
            if (time >= TimeSpan.FromDays(1)) throw new FormatException();
            return start.Date + time;
        }
        // Minutes (45 or 7.5), H:MM or H:MM:SS.
        static TimeSpan? ParseLength(string? value)
        {
            value = value?.Trim(); if (string.IsNullOrEmpty(value)) return null;
            if (value.Contains(':'))
                return TimeSpan.TryParseExact(value, new[] { @"h\:mm", @"h\:mm\:ss", @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var span) && span > TimeSpan.Zero ? span : null;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 && minutes < 24 * 60 ? TimeSpan.FromMinutes(minutes) : null;
        }
        static string ShowLength(TimeSpan span) => span.Seconds == 0 ? $"{(int)span.TotalHours}:{span.Minutes:00}" : $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        var syncing = false;
        void ShowLengthFromTimes()
        {
            if (syncing) return;
            syncing = true;
            try { var span = Parse(until.Text, true) - Parse(from.Text, false); length.Text = span > TimeSpan.Zero ? ShowLength(span) : ""; }
            catch (Exception ex) when (ex is FormatException or OverflowException) { length.Text = ""; }
            finally { syncing = false; }
        }
        ShowLengthFromTimes();
        from.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ShowLengthFromTimes(); };
        until.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ShowLengthFromTimes(); };
        length.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || syncing) return;
            if (ParseLength(length.Text) is not { } span) return;
            DateTime begin; try { begin = Parse(from.Text, false); } catch (Exception ex) when (ex is FormatException or OverflowException) { return; }
            var finish = begin + span;
            if (finish > start.Date.AddDays(1)) { error.Text = "That length would run past midnight."; return; }
            error.Text = ""; syncing = true;
            try { until.Text = finish == start.Date.AddDays(1) ? "24:00:00" : finish.ToString("HH:mm:ss"); } finally { syncing = false; }
        };
        var applied = false;
        var save = Ui.DialogButton("Save", () =>
        {
            try
            {
                if (applied) { if (model.Save()) Close(); else error.Text = model.LastError; return; }
                // Only the end later on the same account and start can shove, so that is the only case offered.
                if (row is not null && until.Text != initialEnd && from.Text == initialStart && (string?)accounts.SelectedItem == initialAccount && Parse(until.Text, true) > row.To)
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
                            + $"{(later.Count == 1 ? "It" : "They")} can move later by up to {model.FormatShort(most)} so nothing overlaps, or this can overlap {(later.Count == 1 ? "it" : "them")}, which counts those hours twice. Undo puts everything back.";
                        // With the choice already kept, the blocks simply move.
                        if (model.MoveLaterAlways)
                        { if (model.ExtendEntry(row, newEnd, note.Text ?? "")) Close(); else error.Text = model.LastError ?? model.EditError; return; }
                        always.IsChecked = false; shove.IsVisible = true; return;
                    }
                }
                var previous = model.Editor.UndoState;
                if (model.EditEntry(row, row is not null && (string?)accounts.SelectedItem == initialAccount ? row.Account : BreakAccount.Stored((string?)accounts.SelectedItem ?? row?.Account ?? ""), from.Text != initialStart ? Parse(from.Text, false) : start,
                    until.Text != initialEnd ? Parse(until.Text, true) : end, note.Text ?? "")) Close();
                else
                {
                    applied = model.LastError is not null && !ReferenceEquals(previous, model.Editor.UndoState);
                    error.Text = model.LastError ?? model.EditError;
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { error.Text = "Use HH:mm or HH:mm:ss, with an end after the start."; }
        }, "SaveEntry");
        var buttons = Ui.Horizontal(save, Ui.DialogButton("Cancel", Close));
        if (row is not null) buttons.Children.Add(Ui.DialogButton("Delete", () =>
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
