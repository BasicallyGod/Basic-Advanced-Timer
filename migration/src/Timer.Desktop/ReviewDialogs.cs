using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public class ReviewDialog : Dialog
{
    protected readonly StackPanel Root = new() { Margin = new Thickness(24, 4, 24, 24), Spacing = 12 };
    protected readonly TextBlock Error = new() { TextWrapping = TextWrapping.Wrap, Classes = { "overtime" } };
    private bool applied;
    protected ReviewDialog(string title)
    {
        Title = title; Width = 560; // The height follows the content, up to the window.
        Content = new ScrollViewer { Content = Root };
    }
    protected void Apply(TimerModel model, Func<bool> change)
    {
        try
        {
            if (applied) { if (model.Save()) Close(); else Error.Text = model.LastError; return; }
            var previous = model.Editor.UndoState;
            if (change()) Close();
            else { applied = model.LastError is not null && !ReferenceEquals(previous, model.Editor.UndoState); Error.Text = model.LastError ?? model.EditError; }
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException or InvalidOperationException or IOException)
        { Error.Text = error.Message; }
    }
    protected static TextBlock Info(string text) => new() { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
}

/// <summary>A merged block's entries, to open one of them or to ungroup them. (The week-wide dialog for grouping entries went with the Entries view; blocks are merged on the timeline.)</summary>
public sealed class GroupDialog : ReviewDialog
{
    public GroupDialog(TimerModel model, string id) : base("Grouped entries")
    {
        var week = model.WeekStart(model.SelectedDate);
        var group = model.Editor.Entries(week, week.AddDays(7)).FirstOrDefault(e => e.GroupId == id);
        Root.Children.Add(Info("Merged entries for the same account and day. Blank gaps were added as credited time. Undo reverses the added time; Ungroup keeps it."));
        foreach (var row in group?.Sources ?? System.Array.Empty<SessionRow>())
            Root.Children.Add(Ui.DialogButton($"{model.Label(row.Account)} · {row.From:HH:mm:ss} · {model.Format(row.Duration)} · {model.Editor.Note(row)}", () => { Close(); model.OpenEntry(row); }));
        Root.Children.Add(Error);
        Root.Children.Add(Ui.Horizontal(Ui.DialogButton("Ungroup", () => Apply(model, () => model.UngroupEntries(id)), "UngroupEntries"), Ui.DialogButton("Cancel", Close)));
    }
}

public sealed class TidyDialog : ReviewDialog
{
    public TidyDialog(TimerModel model) : base("Tidy day")
    {
        Root.Children.Add(Info("Review short gaps between stopped entries. Breaks, away time and running entries are excluded. Each fill adds credited time; one Undo reverses the batch."));
        var limit = new NumericUpDown { Name = "TidyLimit", Minimum = 1, Maximum = 60, Value = 5, Width = 100 };
        Root.Children.Add(Ui.Horizontal(Ui.Text("Maximum gap (minutes)", 13), limit));
        var list = new StackPanel { Spacing = 10 }; Root.Children.Add(list);
        var choices = new List<(TidyGap Gap, ComboBox Choice)>();
        void Refresh()
        {
            list.Children.Clear(); choices.Clear();
            foreach (var gap in model.Editor.TidyCandidates(model.SelectedDate, (int)(limit.Value ?? 5)))
            {
                var accounts = new[] { "Leave gap", gap.Previous, gap.Next }.Distinct().ToArray();
                var choice = new ComboBox { ItemsSource = accounts, SelectedIndex = 0, MinWidth = 180 };
                list.Children.Add(Ui.Horizontal(Ui.Text($"{gap.Start:HH:mm:ss} – {gap.End:HH:mm:ss}", 13), choice)); choices.Add((gap, choice));
            }
            if (choices.Count == 0) list.Children.Add(Ui.Text("No eligible gaps for this day.", 13, "muted"));
        }
        limit.ValueChanged += (_, _) => Refresh(); Refresh(); Root.Children.Add(Error);
        Root.Children.Add(Ui.Horizontal(Ui.DialogButton("Apply selected fills", () => Apply(model, () => model.TidyDay(choices.Where(p => p.Choice.SelectedIndex > 0)
            .Select(p => new GapChoice(p.Gap, (string)p.Choice.SelectedItem!)).ToArray(), (int)(limit.Value ?? 5))), "ApplyTidy"), Ui.DialogButton("Cancel", Close)));
    }
}
