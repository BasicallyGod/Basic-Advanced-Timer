using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;

namespace Timer.Desktop;

/// <summary>
/// Links in notes and meetings, the way Slack does them: paste a web address while some words are selected and the words become the link, and an address pasted on
/// its own (or typed) is a link too. A link is kept inside the note's own words as <c>[words](address)</c>, so it is saved, searched and edited like any other text
/// and the shared storage needs nothing new; every place that cannot draw a link shows just its words (<see cref="Flatten"/>), and the notes and meeting rows draw
/// the words as a link that opens the address when pressed (<see cref="Fill"/>). Only web and mail addresses are links.
/// </summary>
public static class NoteLinks
{
    // Either the written form, or a bare address (which stops at spaces and brackets).
    private static readonly Regex Pattern = new(@"\[(?<label>[^\[\]]+)\]\((?<url>(?:https?://|mailto:)[^\s)]+)\)|(?<bare>https?://[^\s<>\[\]()]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A stretch of a note: plain words, or words that open an address.</summary>
    public readonly record struct Piece(string Words, string? Address);

    /// <summary>The note's words split into plain stretches and links, in order.</summary>
    public static List<Piece> Parse(string text)
    {
        var pieces = new List<Piece>(); var at = 0;
        foreach (Match m in Pattern.Matches(text))
        {
            var address = m.Groups["url"].Success ? m.Groups["url"].Value : m.Groups["bare"].Value; var words = m.Groups["label"].Success ? m.Groups["label"].Value : address;
            var length = m.Length;
            // A bare address does not take the full stop or comma that ends the sentence round it.
            if (m.Groups["bare"].Success) { var trimmed = address.TrimEnd('.', ',', ';', ':', '!', '?', '"', '\''); length -= address.Length - trimmed.Length; address = words = trimmed; }
            if (m.Index > at) pieces.Add(new Piece(text[at..m.Index], null));
            pieces.Add(new Piece(words, address)); at = m.Index + length;
        }
        if (at < text.Length) pieces.Add(new Piece(text[at..], null));
        return pieces;
    }

    public static bool HasLinks(string text) => Pattern.IsMatch(text);
    /// <summary>The words only, with each link's address left out, for tooltips, the bell, the timeline's meeting cards and anywhere that cannot press a link.</summary>
    public static string Flatten(string text) => HasLinks(text) ? string.Concat(Parse(text).Select(p => p.Words)) : text;

    /// <summary>Whether this is one web or mail address and nothing else.</summary>
    public static bool IsAddress(string text)
    {
        var t = text.Trim();
        return t.Length > 0 && !t.Any(char.IsWhiteSpace) && Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    }

    /// <summary>
    /// What replaces the selected words when something is pasted: a link when an address is pasted over some selected words, otherwise the pasted text itself
    /// (on one line, since a note is one).
    /// </summary>
    public static string PasteReplacement(string selected, string pasted)
    {
        if (selected.Trim().Length > 0 && IsAddress(pasted))
        {
            var words = Parse(selected.Trim()) is [{ Address: not null } only] ? only.Words : Regex.Replace(selected.Replace('[', ' ').Replace(']', ' '), @"\s+", " ").Trim();
            return "[" + words + "](" + pasted.Trim() + ")";
        }
        return pasted.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    }

    /// <summary>Lets a box take a pasted address over selected words as a link. Anything else pastes as it always did.</summary>
    public static void AttachPaste(TextBox box)
    {
        box.AddHandler(TextBox.PastingFromClipboardEvent, async (_, e) =>
        {
            var clipboard = TopLevel.GetTopLevel(box)?.Clipboard; if (clipboard is null) return;
            e.Handled = true; // The paste is done here, after the clipboard has been read.
            var pasted = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
            if (string.IsNullOrEmpty(pasted)) return;
            box.SelectedText = PasteReplacement(box.SelectedText ?? "", pasted);
        });
    }

    /// <summary>Opens an address in the browser (or mail program) when a link is pressed. Replaced in tests, which must not open one.</summary>
    public static Action<Uri>? Opener { get; set; }
    private static void Open(string address, Control from)
    {
        if (!IsAddress(address)) return;
        var uri = new Uri(address);
        if (Opener is { } test) { test(uri); return; }
        _ = TopLevel.GetTopLevel(from)?.Launcher.LaunchUriAsync(uri);
    }

    /// <summary>Puts the words in the block: as they are when there is no link, otherwise with each link as a pressable run in the accent.</summary>
    public static void Fill(TextBlock block, string words)
    {
        if (!HasLinks(words)) { block.Text = words; return; }
        block.Text = null; block.Inlines ??= new InlineCollection();
        foreach (var piece in Parse(words))
        {
            if (piece.Address is not { } address) { block.Inlines.Add(new Run(piece.Words)); continue; }
            var label = new TextBlock { Text = piece.Words, FontSize = block.FontSize, FontWeight = block.FontWeight, TextDecorations = block.TextDecorations, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, MaxWidth = 300 }; // A very long address is cut short, not run past the card; its tooltip has all of it.
            var link = Ui.Button("", () => { }, null); link.Classes.Add("pillBare"); link.Classes.Add("noteLink"); link.Content = label; link.Padding = new Avalonia.Thickness(0);
            link.Click += (_, _) => Open(address, link);
            ToolTip.SetTip(link, address); Avalonia.Automation.AutomationProperties.SetName(link, "Link: " + piece.Words);
            link.Cursor = new Cursor(StandardCursorType.Hand);
            link.MinHeight = 0; link.MinWidth = 0; link.Margin = new Avalonia.Thickness(0, 0, 0, -block.FontSize * .24); // The box's bottom sits on the baseline, so it is dropped by the part of the letters that hang below it. // Sat on the text's own baseline, so it does not float above the words beside it.
            block.Inlines.Add(new InlineUIContainer(link) { BaselineAlignment = Avalonia.Media.BaselineAlignment.Baseline });
        }
    }
}
