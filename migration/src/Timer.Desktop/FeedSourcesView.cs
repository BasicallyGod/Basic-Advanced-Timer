using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>Source setup stays in Settings, leaving the reading view focused on reports.</summary>
public sealed class FeedSourcesView : StackPanel
{
    private readonly TimerModel model;
    private readonly StackPanel sources = new() { Spacing = 10 };
    private readonly StackPanel editor = new() { Spacing = 8, IsVisible = false };
    private readonly TextBox name = new() { Name = "FeedSourceName", Watermark = "Source name" };
    private readonly ComboBox kind = new() { Name = "FeedSourceType", ItemsSource = new[] { "Local or synchronised folder", "Paste / import report", "RSS / Atom news feed" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox location = new() { Name = "FeedSourceLocation", Watermark = "Folder path or feed URL" };
    private readonly ComboBox account = new() { Name = "FeedSourceAccount", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox report = new() { Name = "FeedImportText", Watermark = "Paste ordinary text or Markdown here", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 160 };
    private readonly TextBlock error = new() { Name = "FeedSourceError", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock help = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private string? editing;
    private string signature = "";

    public FeedSourcesView(TimerModel model)
    {
        this.model = model; Spacing = 10;
        Children.Add(Ui.DialogButton("Add source", () => Edit(null), "FeedAddSource"));
        Children.Add(sources); Children.Add(editor);
        editor.Children.Add(name); editor.Children.Add(kind); editor.Children.Add(location);
        var browse = Ui.DialogButton("Browse folder…", () => FeedFolderPicker.Choose(this, path => location.Text = path), "FeedSourceBrowse");
        editor.Children.Add(browse); editor.Children.Add(Ui.Text("Default account (entry tags take priority)", 12, "muted")); editor.Children.Add(account);
        editor.Children.Add(report); editor.Children.Add(help); editor.Children.Add(error);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Ui.DialogButton("Save / import", Save, "FeedSourceSave"));
        actions.Children.Add(Ui.DialogButton("Cancel", () => editor.IsVisible = false, "FeedSourceCancel"));
        editor.Children.Add(actions);
        void ShowKind()
        {
            report.IsVisible = kind.SelectedIndex == 1; location.IsVisible = kind.SelectedIndex != 1; browse.IsVisible = kind.SelectedIndex == 0;
            help.Text = kind.SelectedIndex switch
            {
                0 => "Choose a folder containing .md or .txt reports. For OneDrive or Google Drive, keep this folder available offline. The timer imports while open and catches up next time it starts.",
                1 => "Paste a report and save it. Headings become entries; dates, IDs and account tags are optional. Reimporting the same report does not duplicate it.",
                _ => "Enter the publisher's RSS or Atom URL. This is a separate news source, refreshed every minute while the timer is open."
            };
        }
        kind.SelectionChanged += (_, _) => ShowKind(); ShowKind();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName != nameof(TimerModel.Elapsed)) Refresh(); }
    private void Refresh()
    {
        var list = model.FeedSources;
        var next = System.Text.Json.JsonSerializer.Serialize(list) + model.FeedImportError;
        if (next == signature) return; signature = next; sources.Children.Clear();
        foreach (var source in list)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(Ui.Text(source.Name, 14));
            var status = Ui.Text((source.Paused ? "Paused" : source.Error is null ? "Ready" : "Needs attention")
                + (source.LastSuccess is { } date ? " · Last import " + date.ToString("g") : " · Not imported yet")
                + (source.Error is null ? "" : "\n" + source.Error), 12, "muted");
            status.TextWrapping = TextWrapping.Wrap; row.Children.Add(status);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            void Button(string text, Action action) => actions.Children.Add(Ui.DialogButton(text, () => Try(action), "FeedSource_" + source.Id + "_" + text));
            Button(source.Kind == "paste" ? "Import / edit" : "Edit", () => Edit(source));
            Button(source.Paused ? "Resume" : "Pause", () => model.SaveFeedSource(source with { Paused = !source.Paused }));
            if (source.Kind != "paste") Button("Refresh", model.RefreshFeedInBackground);
            Button("Remove", () => model.RemoveFeedSource(source.Id));
            row.Children.Add(actions); sources.Children.Add(row);
        }
        var note = Ui.Text("Removing a source keeps its imported reports. Cloud delivery needs a provider that can save files unattended; verify a scheduled run before relying on it.", 12, "muted");
        note.TextWrapping = TextWrapping.Wrap; sources.Children.Add(note);
    }
    private void Edit(FeedSource? source)
    {
        editing = source?.Id; name.Text = source?.Name ?? ""; location.Text = source?.Location ?? ""; report.Text = ""; error.Text = "";
        kind.SelectedIndex = source?.Kind switch { "paste" => 1, "rss" => 2, _ => 0 }; kind.IsEnabled = source is null;
        var accounts = new[] { "No default account" }.Concat(model.AccountNames.Where(n => !Timer.Core.BreakAccount.Is(n))).ToArray();
        account.ItemsSource = accounts; account.SelectedIndex = Math.Max(0, Array.IndexOf(accounts, source?.Account));
        editor.IsVisible = true;
    }
    private void Try(Action action)
    {
        try { action(); error.Text = ""; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { error.Text = e.Message; editor.IsVisible = true; }
    }
    private void Save() => Try(() =>
    {
        var source = editing is null ? new FeedSource(Guid.NewGuid().ToString("N"), "", "", "") : model.FeedSources.Single(s => s.Id == editing);
        source = source with { Name = name.Text?.Trim() ?? "", Kind = kind.SelectedIndex switch { 1 => "paste", 2 => "rss", _ => "folder" }, Location = location.Text?.Trim() ?? "", Account = account.SelectedIndex > 0 ? account.SelectedItem as string : null };
        if (source.Kind == "paste" && !string.IsNullOrWhiteSpace(report.Text)) FeedImporters.TextReport(report.Text, source.Name, model.Engine.Now);
        model.SaveFeedSource(source); editing = source.Id;
        if (source.Kind == "paste" && !string.IsNullOrWhiteSpace(report.Text)) model.ImportFeedText(source.Id, report.Text);
        editor.IsVisible = false;
    });
}
