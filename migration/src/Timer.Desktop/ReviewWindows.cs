using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public class ReviewWindow : Window
{
    protected readonly StackPanel Root = new() { Margin = new Thickness(24), Spacing = 12 };
    protected readonly TextBlock Error = new() { TextWrapping = TextWrapping.Wrap, Classes = { "overtime" } };
    private bool applied;
    protected ReviewWindow(string title)
    {
        Title = title; Width = 560; Height = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
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

public sealed class PositionWindow : ReviewWindow
{
    public PositionWindow(TimerModel model, CreditPosition? old = null) : base(old is null ? "Position existing credit" : "Edit credit position")
    {
        var day = old?.Start.Date ?? model.SelectedDate;
        var available = model.Editor.Unplaced(day).Where(p => p.Value > 0 || p.Key == old?.Account).Select(p => p.Key).ToList();
        if (old is not null && !available.Contains(old.Account)) available.Add(old.Account);
        Root.Children.Add(Ui.Text(day.ToString("dddd, d MMMM yyyy"), 13, "muted"));
        Root.Children.Add(Info("Place merged credit on the timeline. This uses existing credit and keeps account totals unchanged."));
        var accounts = new ComboBox { Name = "PositionAccount", ItemsSource = available, SelectedItem = old?.Account ?? available.FirstOrDefault(), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var remaining = Ui.Text("", 13, "muted");
        void Update()
        {
            var account = (string?)accounts.SelectedItem ?? "";
            var minutes = model.Editor.Unplaced(day).GetValueOrDefault(account) + (old?.Account == account ? old.Minutes : 0);
            remaining.Text = minutes.ToString("0.00", CultureInfo.InvariantCulture) + " minutes available";
        }
        accounts.SelectionChanged += (_, _) => Update(); Update(); Root.Children.Add(accounts); Root.Children.Add(remaining);
        var suggested = old?.Start ?? (model.Engine.Plan(day).Working ? model.Engine.Plan(day).Start : day.AddHours(9));
        var suggestedMinutes = old?.Minutes ?? Math.Min(30m, model.Editor.Unplaced(day).GetValueOrDefault((string?)accounts.SelectedItem ?? ""));
        if (old is null)
        {
            var spans = model.Engine.Sessions.Where(s => s.IsPositioned && s.Duration > TimeSpan.Zero && s.Day == day).Select(s => (Start: s.From, End: s.To)).ToList();
            spans.AddRange(model.Editor.Positions.Where(p => p.Start.Date == day).Select(p => (p.Start, p.End)));
            if (model.Engine.Active is { } active) spans.Add((active.Start, model.Engine.Now));
            foreach (var span in spans.OrderBy(s => s.Start))
            {
                if (span.End <= suggested) continue;
                if (span.Start >= suggested.AddMinutes((double)suggestedMinutes)) break;
                suggested = span.End;
            }
            // A saved credited duration may finish between seconds; move inward to a whole second.
            suggested = day.AddSeconds(Math.Ceiling((suggested - day).TotalSeconds));
            var cap = day.AddDays(1);
            var next = spans.Where(s => s.Start > suggested).Select(s => s.Start).Append(cap).Min();
            suggestedMinutes = Math.Max(0, Math.Min(suggestedMinutes, decimal.Floor((decimal)(next - suggested).TotalMinutes * 100m) / 100m));
        }
        var start = new TextBox { Name = "PositionStart", Width = 120, Text = suggested.ToString("HH:mm:ss"), Watermark = "Start (HH:mm)" };
        var amount = new TextBox { Name = "PositionMinutes", Width = 120, Text = suggestedMinutes.ToString("0.00", CultureInfo.InvariantCulture), Watermark = "Minutes of existing credit" };
        Root.Children.Add(Ui.Text("Start time and minutes to place", 12, "muted")); Root.Children.Add(Ui.Horizontal(start, amount));
        var note = new TextBox { Name = "PositionNote", Text = old?.Note ?? "", Watermark = "Position note", AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap }; Root.Children.Add(note); Root.Children.Add(Error);
        var buttons = Ui.Horizontal(Ui.Button("Save position", () => Apply(model, () => model.PlaceCredit((string?)accounts.SelectedItem ?? "", At(day, start.Text), decimal.Parse(amount.Text ?? "", CultureInfo.InvariantCulture), note.Text ?? "", old)), "SavePosition"), Ui.Button("Cancel", Close));
        if (old is not null) buttons.Children.Add(Ui.Button("Unplace", () => Apply(model, () => model.UnplaceCredit(old)), "UnplaceCredit"));
        Root.Children.Add(buttons);
        if (available.Count == 0) Error.Text = "No unplaced merged credit on this day.";
    }
}

public sealed class GroupWindow : ReviewWindow
{
    public GroupWindow(TimerModel model, string? id = null) : base(id is null ? "Group entries" : "Grouped entries")
    {
        Height = 620;
        var group = id is null ? null : model.Editor.Entries(model.WeekStart(model.SelectedDate), model.WeekStart(model.SelectedDate).AddDays(7)).FirstOrDefault(e => e.GroupId == id);
        var week = model.WeekStart(model.SelectedDate);
        var existing = model.Editor.Entries(week, week.AddDays(7)).Where(e => e.GroupId is not null).ToArray();
        if (id is null && existing.Length > 0)
        {
            Root.Children.Add(Ui.Text("Existing groups · open to inspect or ungroup", 13, "muted"));
            foreach (var entry in existing)
                Root.Children.Add(Ui.Button($"{entry.Day:ddd d MMM} · {entry.Account} · {entry.Sources.Count} entries", () => { var owner = Owner; Close(); if (owner is Window window) new GroupWindow(model, entry.GroupId).Show(window); }));
        }
        var grouped = existing.SelectMany(e => e.Sources).ToHashSet();
        var rows = group?.Sources ?? model.Engine.Sessions.Where(s => s.Day >= week && s.Day < week.AddDays(7) && !s.IsMerged && !grouped.Contains(s)).OrderBy(s => s.From).ToArray();
        Root.Children.Add(Info("Merge entries for the same account/day. Blank gaps are added as credited time. Overlapping or intervening blocks prevent a merge. Undo reverses the added time; Ungroup keeps it."));
        var choices = new List<(CheckBox Check, SessionRow Row)>();
        foreach (var row in rows)
        {
            if (id is not null) Root.Children.Add(Ui.Button($"{row.Account} · {row.From:HH:mm:ss} · {model.Format(row.Duration)} · {model.Editor.Note(row)}", () => { Close(); model.OpenEntry(row); }));
            else
            {
                var check = new CheckBox { Content = $"{row.Day:ddd d MMM} · {row.Account} · {row.From:HH:mm:ss} · {model.Format(row.Duration)}", Name = "GroupChoice" + model.Engine.Sessions.IndexOf(row) };
                choices.Add((check, row)); Root.Children.Add(check);
            }
        }
        Root.Children.Add(Error);
        Root.Children.Add(Ui.Horizontal(id is null
            ? Ui.Button("Group selected", () => Apply(model, () => model.GroupEntries(choices.Where(p => p.Check.IsChecked == true).Select(p => p.Row).ToArray())), "SaveGroup")
            : Ui.Button("Ungroup", () => Apply(model, () => model.UngroupEntries(id)), "UngroupEntries"), Ui.Button("Cancel", Close)));
    }
}

public sealed class TidyWindow : ReviewWindow
{
    public TidyWindow(TimerModel model) : base("Tidy day")
    {
        Height = 620;
        Root.Children.Add(Info("Review short gaps between stopped entries. Lunch, away time and running entries are excluded. Each fill adds credited time; one Undo reverses the batch."));
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

public sealed class ExportWindow : ReviewWindow
{
    public ExportWindow(TimerModel model) : base("Export week")
    {
        Height = 420;
        Root.Children.Add(Ui.Text("Week commencing " + model.WeekStart(model.SelectedDate).ToString("d MMMM yyyy"), 13));
        Root.Children.Add(Info("Creates Detail.csv and Summary.csv together. Decimal hours reconcile across entries, days and account totals. Lunch is shown separately from the working total."));
        Root.Children.Add(Info("Exports include saved entries only. Pause tracking first to include the current running interval."));
        var result = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, IsVisible = false }; Root.Children.Add(result); Root.Children.Add(Error);
        var export = Ui.Button("Export CSV files", () =>
        {
            if (model.ExportWeek()) { result.IsVisible = true; result.Text = model.ExportPath; Error.Text = null; Root.Children.Add(Ui.Text("Both files saved.", 13)); }
            else Error.Text = model.LastError ?? model.EditError;
        }, "ExportCsv");
        Root.Children.Add(Ui.Horizontal(export, Ui.Button("Close", Close)));
    }
}
