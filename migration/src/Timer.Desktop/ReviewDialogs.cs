using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public class ReviewDialog : Dialog
{
    protected readonly StackPanel Root = new() { Margin = new Thickness(24), Spacing = 12 };
    protected readonly TextBlock Error = new() { TextWrapping = TextWrapping.Wrap, Classes = { "overtime" } };
    private bool applied;
    protected ReviewDialog(string title)
    {
        Title = title; Width = 560; // The height follows the content, up to the window.
        Root.Children.Add(Ui.Text(title, 22)); Content = new ScrollViewer { Content = Root };
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
    protected static DateTime At(DateTime day, string? value)
        => day.Date + TimeSpan.ParseExact(value ?? "", new[] { @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture);
}

public sealed class GroupDialog : ReviewDialog
{
    public GroupDialog(TimerModel model, string? id = null) : base(id is null ? "Group entries" : "Grouped entries")
    {
        var group = id is null ? null : model.Editor.Entries(model.WeekStart(model.SelectedDate), model.WeekStart(model.SelectedDate).AddDays(7)).FirstOrDefault(e => e.GroupId == id);
        var week = model.WeekStart(model.SelectedDate);
        var existing = model.Editor.Entries(week, week.AddDays(7)).Where(e => e.GroupId is not null).ToArray();
        if (id is null && existing.Length > 0)
        {
            Root.Children.Add(Ui.Text("Existing groups · open to inspect or ungroup", 13, "muted"));
            foreach (var entry in existing)
                Root.Children.Add(Ui.Button($"{entry.Day:ddd d MMM} · {model.Label(entry.Account)} · {entry.Sources.Count} entries", () => { var host = Host; Close(); host?.Show(new GroupDialog(model, entry.GroupId)); }));
        }
        var grouped = existing.SelectMany(e => e.Sources).ToHashSet();
        var rows = group?.Sources ?? model.Engine.Sessions.Where(s => s.Day >= week && s.Day < week.AddDays(7) && !grouped.Contains(s)).OrderBy(s => s.From).ToArray();
        Root.Children.Add(Info("Merge entries for the same account/day. Blank gaps are added as credited time. Overlapping or intervening blocks prevent a merge. Undo reverses the added time; Ungroup keeps it."));
        var choices = new List<(CheckBox Check, SessionRow Row)>();
        foreach (var row in rows)
        {
            if (id is not null) Root.Children.Add(Ui.Button($"{model.Label(row.Account)} · {row.From:HH:mm:ss} · {model.Format(row.Duration)} · {model.Editor.Note(row)}", () => { Close(); model.OpenEntry(row); }));
            else
            {
                var check = new CheckBox { Content = $"{row.Day:ddd d MMM} · {model.Label(row.Account)} · {row.From:HH:mm:ss} · {model.Format(row.Duration)}", Name = "GroupChoice" + model.Engine.Sessions.IndexOf(row) };
                choices.Add((check, row)); Root.Children.Add(check);
            }
        }
        Root.Children.Add(Error);
        Root.Children.Add(Ui.Horizontal(id is null
            ? Ui.Button("Group selected", () => Apply(model, () => model.GroupEntries(choices.Where(p => p.Check.IsChecked == true).Select(p => p.Row).ToArray())), "SaveGroup")
            : Ui.Button("Ungroup", () => Apply(model, () => model.UngroupEntries(id)), "UngroupEntries"), Ui.Button("Cancel", Close)));
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
        Root.Children.Add(Ui.Horizontal(Ui.Button("Apply selected fills", () => Apply(model, () => model.TidyDay(choices.Where(p => p.Choice.SelectedIndex > 0)
            .Select(p => new GapChoice(p.Gap, (string)p.Choice.SelectedItem!)).ToArray(), (int)(limit.Value ?? 5))), "ApplyTidy"), Ui.Button("Cancel", Close)));
    }
}

public sealed class ExportDialog : ReviewDialog
{
    public ExportDialog(TimerModel model) : base("Export hours")
    {
        var week = new RadioButton { Name = "ExportWeekChoice", GroupName = "ExportPeriod", IsChecked = true, Content = "Week commencing " + model.WeekStart(model.SelectedDate).ToString("d MMMM yyyy") };
        var month = new RadioButton { Name = "ExportMonthChoice", GroupName = "ExportPeriod", Content = "Whole month of " + model.SelectedDate.ToString("MMMM yyyy") };
        Root.Children.Add(Ui.Text("What to export", 13)); Root.Children.Add(week); Root.Children.Add(month);
        Root.Children.Add(Info("Creates Detail.csv and Summary.csv together. Decimal hours reconcile across entries, days and account totals. Breaks are shown separately from the working total."));
        Root.Children.Add(Info("Exports include saved entries only. Pause tracking first to include the current running interval."));
        var result = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, IsVisible = false }; Root.Children.Add(result); Root.Children.Add(Error);
        var export = Ui.Button("Export CSV files", () =>
        {
            if (model.ExportWeek(month.IsChecked == true)) { result.IsVisible = true; result.Text = model.ExportPath; Error.Text = null; Root.Children.Add(Ui.Text("Both files saved.", 13)); }
            else Error.Text = model.LastError ?? model.EditError;
        }, "ExportCsv");
        Root.Children.Add(Ui.Horizontal(export, Ui.Button("Close", Close)));
    }
}
