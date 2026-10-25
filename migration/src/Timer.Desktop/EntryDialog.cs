using Avalonia;
using Avalonia.Controls;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>Add time: an account, a start, an end and a length that follow each other, and a note. Existing blocks are edited in the Selected block card instead.</summary>
public sealed class EntryDialog : Dialog
{
    public EntryDialog(TimerModel model, DateTime start, DateTime end)
    {
        Title = "Add time";
        Width = 480; // The height follows the content, up to the window.
        var root = new StackPanel { Margin = new Thickness(24, 4, 24, 24), Spacing = 12 };
        root.Children.Add(Ui.Text(start.ToString("dddd, d MMMM yyyy"), 13, "muted"));
        var names = model.ReviewAccounts.Select(a => a.Name).Concat(model.Engine.Sessions.Select(s => s.Account)).Distinct(StringComparer.OrdinalIgnoreCase).Select(BreakAccount.Display).ToArray();
        var initialAccount = names.FirstOrDefault(n => n.Equals(BreakAccount.Display(model.SelectedAccount ?? ""), StringComparison.OrdinalIgnoreCase)) ?? names.First();
        var accounts = new ComboBox { Name = "EntryAccount", ItemsSource = names, SelectedItem = initialAccount, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        root.Children.Add(accounts);
        var times = Ui.Grid("*,24,*");
        var from = new TextBox { Name = "EntryStart", Text = start.ToString("HH:mm:ss") };
        var until = new TextBox { Name = "EntryEnd", Text = TimeText.ShowClock(end, start) };
        Ui.Put(times, from); Ui.Put(times, Ui.Dash(), 1); Ui.Put(times, until, 2); root.Children.Add(times);
        // The length and the end follow each other: type either, and the other is worked out from the start.
        var length = new TextBox { Name = "EntryLength", Width = 130, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        root.Children.Add(Ui.Horizontal(Ui.Text("Length", 13, "muted"), length));
        var hint = Ui.Text("Times are HH:mm. Length is minutes (45) or hours and minutes (1:15).", 11, "muted");
        hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap; root.Children.Add(hint);
        var note = new TextBox { Name = "EntryNote", Watermark = "Entry note", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 110 };
        root.Children.Add(note);
        var error = new TextBlock { Name = "EntryError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        var syncing = false;
        void ShowLengthFromTimes()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                length.Text = TimeText.TryParseClock(from.Text, start, false, out var a) && TimeText.TryParseClock(until.Text, start, true, out var b) && b > a ? TimeText.ShowLength(b - a) : "";
            }
            finally { syncing = false; }
        }
        ShowLengthFromTimes();
        from.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ShowLengthFromTimes(); };
        until.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ShowLengthFromTimes(); };
        length.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || syncing) return;
            if (TimeText.ParseLength(length.Text) is not { } span || !TimeText.TryParseClock(from.Text, start, false, out var begin)) return;
            var finish = begin + span;
            if (finish > start.Date.AddDays(1)) { error.Text = "That length would run past midnight."; return; }
            error.Text = ""; syncing = true;
            try { until.Text = TimeText.ShowClock(finish, start); } finally { syncing = false; }
        };
        var applied = false;
        var save = Ui.DialogButton("Save", () =>
        {
            try
            {
                if (applied) { if (model.Save()) Close(); else error.Text = model.LastError; return; }
                var previous = model.Editor.UndoState;
                if (model.AddEntry(BreakAccount.Stored((string?)accounts.SelectedItem ?? ""), TimeText.ParseClock(from.Text, start, false), TimeText.ParseClock(until.Text, start, true), note.Text ?? "")) Close();
                else
                {
                    applied = model.LastError is not null && !ReferenceEquals(previous, model.Editor.UndoState);
                    error.Text = model.LastError ?? model.EditError;
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { error.Text = "Use HH:mm or HH:mm:ss, with an end after the start."; }
        }, "SaveEntry");
        root.Children.Add(Ui.Horizontal(save, Ui.DialogButton("Cancel", Close)));
        Content = new ScrollViewer { Content = root };
    }
}
