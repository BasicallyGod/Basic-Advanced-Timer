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
/// The Feed page: updates read from files, as one stream with the newest first, like a printout from a wire. Each entry shows when it arrived, where it
/// came from, the account it is about and a couple of lines; clicking opens it in full, which marks it read when that is switched on. Entries can be
/// marked read or done, turned into a note, and filtered by what is unread, account and source. Nothing here writes to the files. The page is built once
/// and rebuilds only when something it shows has changed, keeping its place in the stream.
/// </summary>
public sealed class FeedPage : Grid
{
    private const int PageSize = 200;
    private readonly TimerModel model;
    private readonly WrapPanel filters = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel stream = new() { Spacing = 8 };
    private readonly TextBlock status = Ui.Text("", 12, "muted");
    private readonly ScrollViewer scroll;
    private readonly HashSet<string> expanded = new();
    private bool unreadOnly, showDone; private string? account, source; private int limit = PageSize;
    private string shown = "";

    public FeedPage(TimerModel model)
    {
        this.model = model; RowDefinitions = new RowDefinitions("Auto,*"); Margin = new Thickness(0, 0, 0, 4);
        var bar = Ui.Grid("*,Auto,Auto"); bar.Margin = new Thickness(0, 0, 0, 10); bar.ColumnSpacing = 10;
        Ui.Put(bar, filters);
        status.VerticalAlignment = VerticalAlignment.Center; Ui.Put(bar, status, 1);
        var refresh = Ui.DialogButton("Refresh", () => { model.RefreshFeed(); }, "FeedRefresh"); refresh.Height = 30; Ui.Put(bar, refresh, 2);
        Ui.Put(this, bar);
        scroll = new ScrollViewer { Content = stream, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Name = "FeedScroll" };
        stream.Margin = new Thickness(0, 0, 10, 0);
        var host = Ui.WithMoreBelow(scroll); SetRow(host, 1); Children.Add(host);
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

    private string Signature() => string.Join(";", model.FeedShown.Select(e => e.Id + (model.FeedIsRead(e.Id) ? "r" : "") + (model.FeedIsDone(e.Id) ? "d" : "")))
        + $"|{model.FeedFolder}|{unreadOnly}|{showDone}|{account}|{source}|{limit}|{string.Join(",", expanded.OrderBy(x => x))}|{model.FeedUpdated:s}|{string.Join(",", model.AccountNames)}|{model.Engine.Now:yyyyMMddHHmm}";

    private string? AccountOf(FeedEntry entry) => model.FeedAccountOf(entry);

    public static string Ago(DateTime now, DateTime then)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} {((int)span.TotalHours == 1 ? "hour" : "hours")} ago";
        return $"{(int)span.TotalDays} {((int)span.TotalDays == 1 ? "day" : "days")} ago";
    }

    private Button Filter(string text, string name, bool active, Action click)
    {
        var button = Ui.DialogButton(text, click, name); button.Height = 30; button.Margin = new Thickness(0, 0, 8, 6); button.Classes.Set("mainAction", active); return button;
    }

    public void Rebuild()
    {
        var offset = scroll.Offset; shown = Signature();
        filters.Children.Clear(); stream.Children.Clear();
        var all = model.FeedShown; var now = model.Engine.Now;
        status.Text = (model.FeedFolder is null ? "No folder chosen" : model.FeedUnread == 0 ? "All read" : model.FeedUnread + " unread")
            + (model.FeedUpdated is { } updated ? " · updated " + Ago(now, updated) : "");
        if (model.FeedFolder is null) { stream.Children.Add(Empty("Choose a feed folder", "Put Markdown files in a folder (a scheduled ChatGPT or Claude task can write them) and the updates show here, newest first. The files are only ever read.", true)); return; }
        if (!Directory.Exists(model.FeedFolder)) { stream.Children.Add(Empty("The folder could not be found", model.FeedFolder, true)); return; }
        if (model.FeedEntries.Count == 0) { stream.Children.Add(Empty("Nothing in the feed yet", "Entries start at a heading, with a Date: line and #AccountName tags. Settings has a prompt to give your assistant.", false)); return; }

        // Filters. Accounts and sources appear only when there is more than one to choose between.
        filters.Children.Add(Filter("Unread", "FeedFilterUnread", unreadOnly, () => { unreadOnly = !unreadOnly; Rebuild(); }));
        filters.Children.Add(Filter("Done", "FeedFilterDone", showDone, () => { showDone = !showDone; Rebuild(); }));
        var accounts = all.Select(AccountOf).Where(a => a is not null).Distinct().Cast<string>().OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        if (accounts.Count > 0) foreach (var name in accounts) { var picked = name; filters.Children.Add(Filter(NoteShorthand.TagFor(model.Accounts.FirstOrDefault(a => a.Name == name)?.Display ?? name), "FeedFilterAccount_" + Key(name), account == name, () => { account = account == picked ? null : picked; Rebuild(); })); }
        var sources = all.Select(e => e.Source).Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count > 1) foreach (var name in sources) { var picked = name; filters.Children.Add(Filter(name, "FeedFilterSource_" + Key(name), source == name, () => { source = source == picked ? null : picked; Rebuild(); })); }
        var markAll = Filter("Mark all read", "FeedMarkAllRead", false, () => { foreach (var entry in model.FeedShown) model.SetFeedRead(entry.Id, true); }); markAll.IsEnabled = model.FeedUnread > 0; filters.Children.Add(markAll);

        var list = all.Where(e => (showDone || !model.FeedIsDone(e.Id)) && (!unreadOnly || !model.FeedIsRead(e.Id)) && (account is null || AccountOf(e) == account) && (source is null || e.Source == source)).ToList();
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

    private Control Card(FeedEntry entry, DateTime now)
    {
        var id = entry.Id; var key = Key(id); var open = expanded.Contains(id); var read = model.FeedIsRead(id); var done = model.FeedIsDone(id);
        var top = new WrapPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(Pad(Ui.Text(entry.When.TimeOfDay == TimeSpan.Zero ? "" : entry.When.ToString("HH:mm"), 12, "muted")));
        top.Children.Add(Chip("FeedSource_" + key, entry.Source));
        var tagged = AccountOf(entry);
        if (tagged is not null && NoteChips.AccountChip(model, new DayNote(0, entry.When.Date, null, "#" + new string(tagged.Where(c => !char.IsWhiteSpace(c)).ToArray())), this, "FeedAccount_" + key, interactive: false) is { } chip) { chip.Margin = new Thickness(0, 0, 6, 0); top.Children.Add(chip); }
        if (entry.Urgent) { var flag = new IconGlyph(IconKind.Flag) { TintKey = "Finish", Margin = new Thickness(0, 0, 6, 0) }; ToolTip.SetTip(flag, "Needs action"); top.Children.Add(flag); }
        if (!read) { var dot = new Border { Name = "FeedUnreadDot_" + key, Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center }; dot.Classes.Add("accentEdge"); ToolTip.SetTip(dot, "Unread"); top.Children.Add(dot); }
        var headline = new TextBlock { Text = entry.Headline, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var head = new StackPanel { Spacing = 3, Background = Brushes.Transparent, Name = "FeedOpen_" + key, Cursor = new Cursor(StandardCursorType.Hand) };
        head.Children.Add(top); head.Children.Add(headline);
        if (entry.Body.Length > 0)
        {
            var body = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Name = "FeedBody_" + key }; body.Inlines!.AddRange(FeedText.Inlines(entry.Body));
            if (!open) { body.MaxLines = 2; body.TextTrimming = TextTrimming.CharacterEllipsis; }
            head.Children.Add(body);
        }
        head.Tapped += (_, _) => Toggle(id);
        var stack = new StackPanel { Spacing = 6 }; stack.Children.Add(head);
        if (open)
        {
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            Button Small(string text, string name, Action click) { var b = Ui.DialogButton(text, click, name); b.Height = 28; b.Margin = new Thickness(0, 0, 8, 0); return b; }
            buttons.Children.Add(Small(read && !done ? "Mark unread" : "Mark read", "FeedRead_" + key, () => model.SetFeedRead(id, !model.FeedIsRead(id) || model.FeedIsDone(id))));
            buttons.Children.Add(Small(done ? "Not done" : "Done", "FeedDone_" + key, () => model.SetFeedDone(id, !model.FeedIsDone(id))));
            buttons.Children.Add(Small("Add to notes", "FeedToNotes_" + key, () => model.AddFeedEntryToNotes(id)));
            buttons.Children.Add(Small("Mark above read", "FeedReadAbove_" + key, () => model.MarkFeedReadAbove(id)));
            stack.Children.Add(buttons);
        }
        var card = new Border { Name = "FeedEntry_" + key, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10), Child = stack, Opacity = done ? .5 : read ? .72 : 1 };
        card.Classes.Add("dayCard"); return card;
    }

    /// <summary>Opens an entry in full, or puts it away again. Opening marks it read when that is switched on.</summary>
    public void Toggle(string id)
    {
        if (!expanded.Remove(id)) { expanded.Add(id); if (model.FeedAutoRead) model.SetFeedRead(id, true); }
        if (Signature() != shown) Rebuild(); // Marking it read already rebuilt the page when that changed anything.
    }

    /// <summary>
    /// Shows the feed filtered to one account with an entry opened, for a click on the strip under the Day card. Opening marks it read when that is on,
    /// and the entry is brought into view.
    /// </summary>
    public void Focus(string? forAccount, string? id)
    {
        account = forAccount; source = null; unreadOnly = false; showDone = false;
        if (id is not null) { expanded.Add(id); if (model.FeedAutoRead) model.SetFeedRead(id, true); }
        Rebuild();
        if (id is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "FeedEntry_" + Key(id))?.BringIntoView());
    }

    private static Control Pad(Control control) { control.Margin = new Thickness(0, 0, 8, 0); control.VerticalAlignment = VerticalAlignment.Center; return control; }

    private static Border Chip(string name, string text)
    {
        var words = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border { Name = name, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1), Margin = new Thickness(0, 0, 6, 0), Child = words };
        chip.Classes.Add("chip"); return chip;
    }
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
