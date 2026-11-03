using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>Links in notes and meetings: pasting an address over selected words makes them a link, and a link is drawn as one and opens its address.</summary>
public sealed class NoteLinkTests
{
    private static void Settle(Window w) { Dispatcher.UIThread.RunJobs(); using var f = w.CaptureRenderedFrame(); }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void WordsAndLinksAreSplitInOrderAndFlattenToTheirWords()
    {
        var pieces = NoteLinks.Parse("Read the [budget notes](https://example.com/b#top) then https://example.org/x.");
        Assert.Equal(new[] { "Read the ", "budget notes", " then ", "https://example.org/x", "." }, pieces.Select(p => p.Words));
        Assert.Equal(new string?[] { null, "https://example.com/b#top", null, "https://example.org/x", null }, pieces.Select(p => p.Address)); // The full stop is not part of the address.
        Assert.Equal("Read the budget notes then https://example.org/x.", NoteLinks.Flatten("Read the [budget notes](https://example.com/b#top) then https://example.org/x."));
        Assert.Equal("No link [here] (at all)", NoteLinks.Flatten("No link [here] (at all)")); // Brackets alone are only words.
        Assert.False(NoteLinks.HasLinks("javascript:alert(1) or [x](javascript:alert(1))")); // Only web and mail addresses are links.
    }

    [Fact]
    public void AnAddressPastedOverSelectedWordsMakesThemALinkAndAnythingElsePastesAsItAlwaysDid()
    {
        Assert.Equal("[the docs](https://example.com/d)", NoteLinks.PasteReplacement("the docs", "https://example.com/d"));
        Assert.Equal("[the docs](https://example.com/d)", NoteLinks.PasteReplacement("the docs", "  https://example.com/d\r\n")); // Trailing space and a line end are not part of it.
        Assert.Equal("[a b](mailto:me@example.com)", NoteLinks.PasteReplacement("[a] [b]", "mailto:me@example.com")); // Brackets in the words would break the link.
        Assert.Equal("[old](https://new.example/)", NoteLinks.PasteReplacement("[old](https://old.example/)", "https://new.example/")); // Pasting over a link changes where it goes.
        Assert.Equal("https://example.com/d", NoteLinks.PasteReplacement("", "https://example.com/d")); // With nothing selected the address goes in as it is, and shows as a link.
        Assert.Equal("one two", NoteLinks.PasteReplacement("x", "one\r\ntwo")); // Not an address: plain text, on one line.
        Assert.Equal("see this", NoteLinks.PasteReplacement("x", "see this")); // Two words are not an address.
    }

    [AvaloniaFact]
    public void ABoxTakesTheLinkInPlaceOfTheSelectedWordsAndKeepsTheRest()
    {
        var window = new Window { Content = new TextBox { Name = "b", Text = "read the docs today" } }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var box = Find<TextBox>(window, "b"); box.SelectionStart = 5; box.SelectionEnd = 13;
            box.SelectedText = NoteLinks.PasteReplacement(box.SelectedText, "https://example.com/d");
            Assert.Equal("read [the docs](https://example.com/d) today", box.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteWithALinkIsDrawnWithAPressableLinkAndStaysWordsEverywhereElse()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        model.Notes.AddNote("Check [the budget](https://example.com/budget) before Friday", null, null);
        var note = model.Notes.All.Single(n => model.Notes.Words(n).StartsWith("Check"));
        Assert.Equal("Check the budget before Friday", model.Notes.Text(note)); Assert.Contains("](https://example.com/budget)", model.Notes.Words(note));
        Assert.Contains(model.Notes.Matching("the budget", null), n => n.Id == note.Id); // Found by its words.
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        var opened = new List<Uri>(); NoteLinks.Opener = opened.Add;
        try
        {
            var text = Find<TextBlock>(window, "NoteText_" + note.Id);
            Assert.NotNull(text.Inlines); Assert.Equal("Check the budget before Friday", string.Concat(text.Inlines!.Select(i => i is Avalonia.Controls.Documents.Run r ? r.Text : i is Avalonia.Controls.Documents.InlineUIContainer c && c.Child is Button { Content: TextBlock t } ? t.Text : "")));
            var link = text.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("noteLink"));
            Assert.Equal("https://example.com/budget", ToolTip.GetTip(link)); link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new Uri("https://example.com/budget"), Assert.Single(opened));
            // A plain note is left exactly as it was: just text.
            model.Notes.AddNote("Nothing to link", null, null); Settle(window);
            var plain = model.Notes.All.Single(n => model.Notes.Words(n) == "Nothing to link");
            Assert.Equal("Nothing to link", Find<TextBlock>(window, "NoteText_" + plain.Id).Text);
            // Opening the note to edit it shows the words as written, so the link can be changed or taken out.
            window.ShowNotes(); Settle(window);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "PageNoteText_" + note.Id && t.Inlines is { Count: > 1 });
            Find<Button>(window, "PageNoteOpen_" + note.Id).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal("Check [the budget](https://example.com/budget) before Friday", Find<TextBox>(window, "PageNoteField").Text);
        }
        finally { NoteLinks.Opener = null; window.Close(); }
    }

    [Fact]
    public void OnlyWebAndMailAddressesOpen()
    {
        Assert.True(NoteLinks.IsAddress("https://example.com")); Assert.True(NoteLinks.IsAddress("mailto:me@example.com"));
        Assert.False(NoteLinks.IsAddress("file:///c:/windows/system32/calc.exe")); Assert.False(NoteLinks.IsAddress("ftp://example.com")); Assert.False(NoteLinks.IsAddress("https://example.com and more")); Assert.False(NoteLinks.IsAddress(""));
    }
}
