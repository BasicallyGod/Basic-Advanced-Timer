using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// Simple Markdown for feed text, and nothing else: **bold**, *italic* or _italic_, and a leading - or * on a line as a bullet. Everything else stays plain words.
/// There is no HTML, no image and no link handling, because the text comes from a file the app did not write and is only ever shown.
/// </summary>
public static class FeedText
{
    private static readonly Regex Marks = new(@"\*\*(?<b>.+?)\*\*|__(?<b>.+?)__|\*(?<i>[^*\s][^*]*?)\*|(?<![\p{L}\p{N}])_(?<i>[^_\s][^_]*?)_(?![\p{L}\p{N}])", RegexOptions.Compiled);

    public static IReadOnlyList<Inline> Inlines(string text)
    {
        var result = new List<Inline>(); var first = true;
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (!first) result.Add(new LineBreak()); first = false;
            var line = raw; var lead = line.TrimStart();
            if (lead.StartsWith("- ", StringComparison.Ordinal) || lead.StartsWith("* ", StringComparison.Ordinal)) line = new string(' ', line.Length - lead.Length) + "• " + lead[2..];
            var at = 0;
            foreach (Match match in Marks.Matches(line))
            {
                if (match.Index > at) result.Add(new Run(line[at..match.Index]));
                var bold = match.Groups["b"].Success;
                result.Add(new Run(match.Groups[bold ? "b" : "i"].Value) { FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = bold ? FontStyle.Normal : FontStyle.Italic });
                at = match.Index + match.Length;
            }
            if (at < line.Length) result.Add(new Run(line[at..]));
        }
        return result;
    }

    /// <summary>The words as plain text, for tests and tooltips.</summary>
    public static string Plain(string text) => string.Concat(Inlines(text).Select(i => i is Run run ? run.Text : "\n"));
}

/// <summary>Opens the system's folder picker. A failure to show it is not an error: the folder can also be typed in Settings.</summary>
public static class FeedFolderPicker
{
    public static async void Choose(Control host, Action<string> picked)
    {
        try
        {
            if (TopLevel.GetTopLevel(host)?.StorageProvider is not { CanPickFolder: true } provider) return;
            var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the feed folder", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path) picked(path);
        }
        catch (Exception) { }
    }
}

/// <summary>
/// The Feed page: updates read from files, as one stream with the newest first, like a printout from a wire. Each entry is a plain row (when, about whom, from
/// where, the headline and a couple of lines); pressing one selects it, which marks it read when that is switched on, and opens it in full in a pane beside the
/// list with the things that can be done to it: mark it read or done, turn it into a note, mark everything above it read. The controls are two slim rows above
/// the stream (All, Unread and Done, a search box and two short lists; then how fresh it is and the page-wide actions). Nothing here writes to the files.
/// The page is built once and rebuilds only when something it shows has changed, keeping its place in the stream.
/// </summary>
public sealed class FeedPage : Grid
{
    private const int PageSize = 200;
    public const double DetailWidth = 330;
    private readonly TimerModel model;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel pickers = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox searchBox = new() { Name = "FeedSearch", Watermark = "Search the feed", Height = 30, MinHeight = 30, Padding = new Thickness(10, 4), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel stream = new() { Spacing = 0 };
    private readonly Border detail = new() { Name = "FeedDetail", IsVisible = false, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(16, 4, 4, 4), Margin = new Thickness(12, 0, 0, 0), MinWidth = 260, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock status = Ui.Text("", 12, "muted");
    private readonly ScrollViewer scroll;
    private readonly Grid contentGrid;
    private Button markAllButton = null!;
    private string tab = "all"; private string? selected, account, source; private string search = ""; private int limit = PageSize;
    private string shown = "";

    public FeedPage(TimerModel model)
    {
        this.model = model; RowDefinitions = new RowDefinitions("Auto,Auto,*"); Margin = new Thickness(0, 0, 0, 4);
        // First row: what to show. Second: how fresh it is, and the two things that act on the whole page.
        var bar = Ui.Grid("Auto,*,Auto"); bar.Margin = new Thickness(0, 0, 0, 4); bar.ColumnSpacing = 10;
        Ui.Put(bar, tabs); Ui.Put(bar, searchBox, 1); Ui.Put(bar, pickers, 2);
        searchBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && (searchBox.Text ?? "") != search) { search = searchBox.Text ?? ""; limit = PageSize; Rebuild(); } };
        Ui.Put(this, bar);
        var sub = Ui.Grid("*,Auto,Auto"); sub.Margin = new Thickness(0, 0, 0, 6); sub.ColumnSpacing = 4;
        status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(10, 0, 0, 0); Ui.Put(sub, status);
        markAllButton = Quiet("Mark all read", "FeedMarkAllRead", () => { foreach (var entry in model.FeedShown) model.SetFeedRead(entry.Id, true); }); Ui.Put(sub, markAllButton, 1);
        Ui.Put(sub, Quiet("Refresh", "FeedRefresh", () => { model.RefreshFeedInBackground(); }), 2);
        SetRow(sub, 1); Children.Add(sub);
        scroll = new ScrollViewer { Content = stream, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Name = "FeedScroll" };
        stream.Margin = new Thickness(0, 0, 10, 0);
        detail.Classes.Add("feedDetail");
        contentGrid = Ui.Grid("*,Auto", "*"); var host = Ui.WithMoreBelow(scroll); Ui.Put(contentGrid, host); Ui.Put(contentGrid, detail, 1);
        SetRow(contentGrid, 2); Children.Add(contentGrid);
        Rebuild();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; model.RefreshFeedInBackground(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
    }

    /// <summary>A name that is safe to give a control, made from an entry's ID.</summary>
    public static string Key(string id) => Regex.Replace(id, @"[^A-Za-z0-9]", "_");

    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerModel.Elapsed)) return;
        if (Signature() != shown) Rebuild();
    }

    private string Signature() => string.Join(";", model.FeedShown.Select(e => e.ToString() + (model.FeedIsRead(e.Id) ? "r" : "") + (model.FeedIsDone(e.Id) ? "d" : "")))
        + $"|{model.FeedFolder}|{tab}|{account}|{source}|{search}|{limit}|{selected}|{model.FeedUpdated:s}|{string.Join(",", model.AccountNames)}|{model.Engine.Now:yyyyMMddHHmm}";

    private string? AccountOf(FeedEntry entry) => model.FeedAccountOf(entry);

    public static string Ago(DateTime now, DateTime then)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} {((int)span.TotalHours == 1 ? "hour" : "hours")} ago";
        return $"{(int)span.TotalDays} {((int)span.TotalDays == 1 ? "day" : "days")} ago";
    }

    // A plain word that is lit when it is on, rather than an outlined button, so a row of filters does not read as a row of actions.
    private Button Filter(string text, string name, bool active, Action click)
    {
        var button = Ui.Button(text, click, name); button.Classes.Add("flat"); button.Height = 28; button.Padding = new Thickness(10, 0); button.Margin = new Thickness(0, 0, 2, 0); button.FontSize = 13;
        button.Classes.Set("filterOn", active); return button;
    }
    private Button Quiet(string text, string name, Action click)
    {
        var button = Ui.Button(text, click, name); button.Classes.Add("flat"); button.Height = 28; button.Padding = new Thickness(10, 0); button.FontSize = 13; return button;
    }
    private ComboBox Picker(string name, string all, IReadOnlyList<(string Key, string Text)> items, string? current, Action<string?> choose)
    {
        var texts = new List<string> { all }; texts.AddRange(items.Select(i => i.Text));
        var box = new ComboBox { Name = name, ItemsSource = texts, SelectedIndex = Math.Max(0, items.ToList().FindIndex(i => i.Key == current) + 1), Height = 28, MinHeight = 28, Padding = new Thickness(10, 0), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        box.SelectionChanged += (_, _) => { var index = box.SelectedIndex; var key = index <= 0 ? null : items[index - 1].Key; if (key != current) choose(key); };
        return box;
    }

    private bool Matches(FeedEntry e) => search.Length == 0 || e.Headline.Contains(search, StringComparison.OrdinalIgnoreCase) || e.Body.Contains(search, StringComparison.OrdinalIgnoreCase);

    public void Rebuild()
    {
        var offset = scroll.Offset; shown = Signature();
        tabs.Children.Clear(); pickers.Children.Clear(); stream.Children.Clear(); markAllButton.IsEnabled = false; detail.IsVisible = false; detail.Child = null;
        var all = model.FeedShown; var now = model.Engine.Now;
        status.Text = (model.FeedSources.Count == 0 && model.FeedEntries.Count == 0 ? "No sources" : model.FeedUnread == 0 ? "All read" : model.FeedUnread + " unread")
            + (model.FeedUpdated is { } updated ? " · updated " + Ago(now, updated) : "");
        if (model.FeedSources.Count == 0 && model.FeedEntries.Count == 0) { stream.Children.Add(Empty("Add a source in Settings", "Choose a folder, paste a report, or add an RSS feed in Settings → Feed. Imported reports stay available offline.", true)); return; }
        if (model.FeedEntries.Count == 0) { stream.Children.Add(Empty("Nothing in the feed yet", "Add a source in Settings or check its status there. Ordinary text and Markdown are supported.", false)); return; }

        // All and Unread leave out what is done, and Done shows only that, so an entry is in exactly one place until it is marked.
        tabs.Children.Add(Filter("All", "FeedFilterAll", tab == "all", () => { tab = "all"; Rebuild(); }));
        tabs.Children.Add(Filter(model.FeedUnread > 0 ? $"Unread {model.FeedUnread}" : "Unread", "FeedFilterUnread", tab == "unread", () => { tab = "unread"; Rebuild(); }));
        tabs.Children.Add(Filter("Done", "FeedFilterDone", tab == "done", () => { tab = "done"; Rebuild(); }));
        var accounts = all.Select(AccountOf).Where(a => a is not null).Distinct().Cast<string>().OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        if (accounts.Count > 0) pickers.Children.Add(Picker("FeedAccountFilter", "All accounts", accounts.Select(n => (n, NoteShorthand.TagFor(model.Accounts.FirstOrDefault(a => a.Name == n)?.Display ?? n))).ToList(), account, picked => { account = picked; Rebuild(); }));
        var sources = all.Select(e => e.Source).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count > 1) pickers.Children.Add(Picker("FeedSourceFilter", "All sources", sources.Select(n => (n, n)).ToList(), source, picked => { source = picked; Rebuild(); }));
        markAllButton.IsEnabled = model.FeedUnread > 0;

        var list = all.Where(e => tab switch { "done" => model.FeedIsDone(e.Id), "unread" => !model.FeedIsDone(e.Id) && !model.FeedIsRead(e.Id), _ => !model.FeedIsDone(e.Id) })
            .Where(e => (account is null || AccountOf(e) == account) && (source is null || e.Source == source) && Matches(e)).ToList();
        if (list.Count == 0) { stream.Children.Add(Empty("Nothing to show", "Every entry is hidden by the filters above.", false)); return; }
        DateTime? day = null;
        foreach (var entry in list.Take(limit))
        {
            if (day != entry.When.Date) { day = entry.When.Date; var heading = Ui.Text(DayLabel(day.Value, now), 12, "muted"); heading.FontWeight = FontWeight.SemiBold; heading.Margin = new Thickness(4, 8, 0, 0); stream.Children.Add(heading); }
            stream.Children.Add(Card(entry, now));
        }
        if (list.Count > limit)
        {
            var more = Ui.DialogButton($"Show {Math.Min(PageSize, list.Count - limit)} more", () => { limit += PageSize; Rebuild(); }, "FeedMore"); more.HorizontalAlignment = HorizontalAlignment.Center; more.Margin = new Thickness(0, 6); stream.Children.Add(more);
        }
        // The pane beside the list shows the selected entry, and is not there when nothing is selected or the entry is filtered out.
        if (selected is not null && all.FirstOrDefault(e => e.Id == selected) is { } chosen && list.Contains(chosen)) { detail.Child = Detail(chosen, now); detail.IsVisible = true; }
        // When open, the detail pane takes a proportion of available space, rather than a fixed 330px.
        // When closed its column collapses completely, giving the full width back to the list.
        contentGrid.ColumnDefinitions[0].Width = new GridLength(detail.IsVisible ? 3 : 1, GridUnitType.Star);
        contentGrid.ColumnDefinitions[1].Width = detail.IsVisible ? new GridLength(2, GridUnitType.Star) : GridLength.Auto;
        // Rebuilding keeps the place in the stream, so ticking something off does not throw the page back to the top.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => scroll.Offset = offset);
    }

    private static string DayLabel(DateTime day, DateTime now)
        => (now.Date - day.Date).Days switch { 0 => "Today", 1 => "Yesterday", _ => day.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) };

    private Control Empty(string title, string words, bool choose)
    {
        var box = new StackPanel { Spacing = 8, Margin = new Thickness(20, 16) };
        box.Children.Add(Ui.Heading(title, 18));
        var text = Ui.Text(words, 13, "muted"); text.TextWrapping = TextWrapping.Wrap; box.Children.Add(text);
        if (choose) { var pick = Ui.DialogButton("Choose folder…", () => FeedFolderPicker.Choose(this, model.SetFeedFolder), "FeedChoose"); pick.HorizontalAlignment = HorizontalAlignment.Left; box.Children.Add(pick); }
        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Child = box, Name = "FeedEmpty" }; card.Classes.Add("dayCard"); return card;
    }

    // The tag lines an entry carries (#Accountone on a line of its own) are what the account mark already says, so they are left out of the words.
    public static string WithoutTagLines(string body)
        => string.Join('\n', body.Split('\n').Where(l => !Regex.IsMatch(l.Trim(), @"^(#[A-Za-z0-9_-]+\s*)+$"))).Trim('\n', ' ');

    // One quiet line says when, about whom and from where.
    private Panel MetaLine(FeedEntry entry, string key, string when, bool wrap = false)
    {
        // In the narrow pane the line wraps instead of running under the close button.
        Panel top = wrap ? new WrapPanel { Orientation = Orientation.Horizontal } : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (when.Length > 0) top.Children.Add(Pad(Ui.Text(when, 12, "muted"), wrap));
        var tagged = AccountOf(entry);
        if (tagged is not null && NoteChips.AccountChip(model, new DayNote(0, entry.When.Date, null, "#" + new string(tagged.Where(c => !char.IsWhiteSpace(c)).ToArray())), this, "FeedAccount_" + key, interactive: false) is { } chip) top.Children.Add(Pad(chip, wrap));
        var from = Ui.Text(entry.Source, 12, "muted"); from.Name = "FeedSource_" + key; from.MaxWidth = 220; from.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(from, entry.Source); top.Children.Add(Pad(from, wrap));
        return top;
    }

    private Control Card(FeedEntry entry, DateTime now)
    {
        var id = entry.Id; var key = Key(id); var read = model.FeedIsRead(id); var done = model.FeedIsDone(id); var isSelected = selected == id;
        var when = entry.When.TimeOfDay == TimeSpan.Zero ? "" : entry.When.ToString("HH:mm");
        var headline = new TextBlock { Text = entry.Headline, FontSize = 15, FontWeight = read ? FontWeight.Medium : FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var title = Ui.Grid("*,Auto"); title.ColumnSpacing = 8; Ui.Put(title, headline);
        if (entry.Urgent) { var flag = new IconGlyph(IconKind.Flag) { TintKey = "Finish", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) }; ToolTip.SetTip(flag, "Needs action"); Ui.Put(title, flag, 1); }
        var head = new StackPanel { Spacing = 3, Background = Brushes.Transparent, Name = "FeedOpen_" + key, Cursor = new Cursor(StandardCursorType.Hand) };
        head.Children.Add(MetaLine(entry, key, when)); head.Children.Add(title);
        var words = WithoutTagLines(entry.Body);
        if (words.Length > 0)
        {
            var body = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Name = "FeedBody_" + key, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis }; body.Inlines!.AddRange(FeedText.Inlines(words)); body.Classes.Add("muted");
            head.Children.Add(body);
        }
        head.Tapped += (_, _) => Toggle(id);
        // An unread entry has a dot in the margin, where it can be scanned down without reading, instead of one more badge on every line.
        var row = Ui.Grid("18,*");
        if (!read) { var dot = new Border { Name = "FeedUnreadDot_" + key, Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) }; dot.Classes.Add("accentEdge"); ToolTip.SetTip(dot, "Unread"); Ui.Put(row, dot); }
        Ui.Put(row, head, 1);
        var card = new Border { Name = "FeedEntry_" + key, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(2, 12, 6, 12), Child = row, Opacity = done ? .5 : read ? .8 : 1 };
        card.Classes.Add("feedRow"); card.Classes.Set("feedSelected", isSelected); return card;
    }

    // The selected entry in full, with what can be done to it. The same button names as before, so they are found wherever they sit.
    private Control Detail(FeedEntry entry, DateTime now)
    {
        var id = entry.Id; var key = Key(id); var read = model.FeedIsRead(id); var done = model.FeedIsDone(id);
        var stack = new StackPanel { Spacing = 10 };
        var close = Ui.IconButton(IconKind.Close, "Close", "FeedDetailClose", () => Toggle(id)); close.Width = 26; close.Height = 26; close.HorizontalAlignment = HorizontalAlignment.Right;
        var headRow = Ui.Grid("*,Auto"); Ui.Put(headRow, MetaLine(entry, "d_" + key, entry.When.ToString(entry.When.TimeOfDay == TimeSpan.Zero ? "ddd d MMM" : "ddd d MMM, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), wrap: true)); Ui.Put(headRow, close, 1);
        stack.Children.Add(headRow);
        var title = new TextBlock { Text = entry.Headline, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "FeedDetailTitle" };
        if (entry.Urgent) { var flagged = Ui.Grid("*,Auto"); Ui.Put(flagged, title); var flag = new IconGlyph(IconKind.Flag) { TintKey = "Finish", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 4, 0, 0) }; ToolTip.SetTip(flag, "Needs action"); Ui.Put(flagged, flag, 1); stack.Children.Add(flagged); }
        else stack.Children.Add(title);
        var words = WithoutTagLines(entry.Body);
        if (words.Length > 0) { var body = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Name = "FeedDetailBody_" + key }; body.Inlines!.AddRange(FeedText.Inlines(words)); stack.Children.Add(body); }
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        Button Small(string text, string name, Action click) { var b = Ui.DialogButton(text, click, name); b.Height = 30; b.Margin = new Thickness(0, 0, 8, 8); return b; }
        buttons.Children.Add(Small(read && !done ? "Mark unread" : "Mark read", "FeedRead_" + key, () => model.SetFeedRead(id, !model.FeedIsRead(id) || model.FeedIsDone(id))));
        buttons.Children.Add(Small(done ? "Not done" : "Done", "FeedDone_" + key, () => model.SetFeedDone(id, !model.FeedIsDone(id))));
        buttons.Children.Add(Small("Add to notes", "FeedToNotes_" + key, () => model.AddFeedEntryToNotes(id)));
        buttons.Children.Add(Small("Mark above read", "FeedReadAbove_" + key, () => model.MarkFeedReadAbove(id)));
        stack.Children.Add(buttons);
        foreach (var url in FeedLinks.Find(entry.Body))
        {
            var link = Small(url, "FeedLink_" + key, () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { model.Notice = "Could not open this link."; }
            });
            link.Content = new TextBlock { Text = url, TextWrapping = TextWrapping.Wrap }; link.Height = double.NaN;
            ToolTip.SetTip(link, url); stack.Children.Add(link);
        }
        if (model.FeedOriginal(id) is { } original)
            stack.Children.Add(new Expander { Header = "Original report", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new TextBox { Text = original, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 300 } });
        return new ScrollViewer { Content = stack, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    /// <summary>Selects an entry, or clears the selection when it is already the one selected. Selecting marks it read when that is switched on.</summary>
    public void Toggle(string id)
    {
        if (selected == id) selected = null; else { selected = id; if (model.FeedAutoRead) model.SetFeedRead(id, true); }
        if (Signature() != shown) Rebuild(); // Marking it read already rebuilt the page when that changed anything.
    }

    /// <summary>
    /// Shows the feed filtered to one account with an entry selected, for a click on the strip under the Day card. Selecting marks it read when that is on,
    /// and the entry is brought into view.
    /// </summary>
    public void Focus(string? forAccount, string? id)
    {
        account = forAccount; source = null; tab = "all"; search = ""; searchBox.Text = "";
        if (id is not null) { selected = id; if (model.FeedAutoRead) model.SetFeedRead(id, true); }
        Rebuild();
        if (id is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "FeedEntry_" + Key(id))?.BringIntoView());
    }

    private static Control Pad(Control control, bool wrap = false) { control.VerticalAlignment = VerticalAlignment.Center; if (wrap) control.Margin = new Thickness(0, 0, 8, 2); return control; }
}

/// <summary>
/// A slim card under the Day card with up to two unread feed items about the account being tracked, so what the client's updates say is in view when work on
/// them starts. It is there only while an account is running and has unread items, and is gone on a break. Clicking an item opens the feed filtered to that
/// account with the item open; the cross marks it read without opening it. Like the pages, it listens to the model itself and rebuilds only when it changed.
/// </summary>
public sealed class FeedStrip : Border
{
    public const int Shown = 2;
    private readonly TimerModel model; private readonly Action<string, string> open; private string shown = "\u0001";

    public FeedStrip(TimerModel model, Action<string, string> open)
    {
        this.model = model; this.open = open; Name = "FeedStrip"; IsVisible = false;
        CornerRadius = new CornerRadius(14); BorderThickness = new Thickness(1); Padding = new Thickness(14, 8, 8, 8); Margin = new Thickness(12, 0, 16, 6);
        Classes.Add("dayCard");
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; Update(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
    }

    /// <summary>The account the strip speaks of: the one running, and none on a break or when nothing is running.</summary>
    private string? Account => model.IsTracking && !model.IsOnBreak ? model.ActiveAccount : null;

    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName != nameof(TimerModel.Elapsed)) Update(); }

    private static string When(FeedEntry entry, DateTime now)
        => entry.When.Date == now.Date ? (entry.When.TimeOfDay == TimeSpan.Zero ? "Today" : entry.When.ToString("HH:mm")) : entry.When.Date == now.Date.AddDays(-1) ? "Yest." : entry.When.ToString("d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));

    public void Update()
    {
        var account = Account; var items = account is null ? Array.Empty<FeedEntry>() : model.FeedUnreadFor(account);
        var signature = account + "|" + string.Join(",", items.Select(i => i.Id)) + "|" + model.Engine.Now.ToString("yyyyMMdd");
        if (signature == shown) return; shown = signature;
        if (account is null || items.Count == 0) { IsVisible = false; Child = null; return; }
        var known = model.Accounts.FirstOrDefault(a => a.Name == account); var now = model.Engine.Now;
        var stack = new StackPanel { Spacing = 2 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (known is not null) head.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.Parse(Ui.Visible(known.Colour))) });
        var title = Ui.Text("From the feed · " + (known?.Display ?? account), 12, "muted"); title.FontWeight = FontWeight.SemiBold; head.Children.Add(title);
        stack.Children.Add(head);
        foreach (var entry in items.Take(Shown))
        {
            var id = entry.Id; var key = FeedPage.Key(id); var row = Ui.Grid("*,Auto");
            var line = Ui.Grid("Auto,*,Auto"); line.ColumnSpacing = 6;
            Control lead = entry.Urgent ? new IconGlyph(IconKind.Flag) { TintKey = "Finish", VerticalAlignment = VerticalAlignment.Center } : new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0) , Opacity = .6 };
            if (lead is Border dot) dot.Classes.Add("accentEdge");
            Ui.Put(line, lead);
            var headline = new TextBlock { Text = entry.Headline, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; Ui.Put(line, headline, 1);
            var when = Ui.Text(When(entry, now), 11, "muted"); when.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, when, 2);
            var button = Ui.Button("", () => open(account, id), "FeedStripItem_" + key);
            button.Content = line; button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Padding = new Thickness(0, 4); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            ToolTip.SetTip(button, entry.Headline);
            Ui.Put(row, button);
            var read = Ui.IconButton(IconKind.Close, "Mark read", "FeedStripRead_" + key, () => model.SetFeedRead(id, true)); read.Width = 22; read.Height = 22; Ui.Put(row, read, 1);
            stack.Children.Add(row);
        }
        if (items.Count > Shown) { var more = Ui.Text($"+{items.Count - Shown} more", 11, "muted"); more.Name = "FeedStripMore"; more.Margin = new Thickness(11, 0, 0, 0); stack.Children.Add(more); }
        Child = stack; IsVisible = true;
    }
}

/// <summary>Links open only after a click; arbitrary schemes, embedded images and HTML are never executed.</summary>
public static class FeedLinks
{
    public static IReadOnlyList<string> Find(string text) => Regex.Matches(text, @"https?://[^\s<>\)\]""']+", RegexOptions.IgnoreCase)
        .Select(m => m.Value.TrimEnd('.', ',', ';')).Where(FeedImporters.SafeUrl).Distinct().Take(30).ToArray();
}
