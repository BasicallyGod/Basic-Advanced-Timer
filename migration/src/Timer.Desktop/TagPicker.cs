using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The rules behind completing a #Tag as it is typed: which tag is being typed before the caret, which accounts it could mean, and what the words become when
/// one is chosen. Kept apart from the controls so it can be tested on its own. The tag is an account's name with its spaces taken out, as everywhere else.
/// </summary>
public static class TagSuggestions
{
    private static readonly Regex Token = new(@"(?:^|\s)#([\p{L}\p{N}_-]*)$", RegexOptions.Compiled);
    private static string Squash(string name) => new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>The tag being typed just before the caret (where its # is, and the letters after it), or null when the caret is not in one.</summary>
    public static (int Start, string Typed)? Current(string? text, int caret)
    {
        text ??= ""; caret = Math.Clamp(caret, 0, text.Length);
        var match = Token.Match(text[..caret]);
        return match.Success ? (match.Groups[1].Index - 1, match.Groups[1].Value) : null;
    }

    /// <summary>The accounts whose tag starts with what was typed, in the order given. The Break account is never offered. Nothing typed offers them all.</summary>
    public static IReadOnlyList<string> Matches(string typed, IEnumerable<string> accounts, int most = 6)
        => accounts.Where(a => !BreakAccount.Is(a) && Squash(a).StartsWith(typed, StringComparison.OrdinalIgnoreCase)).Take(most).ToList();

    /// <summary>Whether the tag typed is already the whole tag of its only match, so offering it again would add nothing.</summary>
    public static bool IsComplete(string typed, IReadOnlyList<string> matches) => matches.Count == 1 && Squash(matches[0]).Equals(typed, StringComparison.OrdinalIgnoreCase);

    /// <summary>The words with the tag being typed (from its # to the caret) replaced by the account's whole tag and a space, and where the caret then goes.</summary>
    public static (string Text, int Caret) Accept(string text, int start, int caret, string account)
    {
        caret = Math.Clamp(caret, start, text.Length); var tag = NoteShorthand.TagFor(account) + " ";
        return (text[..start] + tag + text[caret..].TrimStart(' '), start + tag.Length);
    }
}

/// <summary>
/// Offers the matching accounts under (or over) a text box while a #Tag is being typed in it. The arrow keys move through them, Tab or Enter takes the chosen
/// one (the first is chosen to begin with), Escape puts the list away, and a click takes one. The list never takes the keyboard focus, so typing carries on
/// in the box. While it is open, Enter completes the tag instead of saving the note; once the tag is complete the list goes and Enter saves as before.
/// </summary>
public sealed class TagPicker
{
    private readonly TimerModel model; private readonly TextBox box; private readonly Popup popup; private readonly StackPanel rows = new();
    private IReadOnlyList<string> matches = Array.Empty<string>(); private int selected, tagStart; private bool accepting;

    private TagPicker(TextBox box, Panel host, TimerModel model, bool above)
    {
        this.box = box; this.model = model;
        var card = new Border { Child = rows, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Name = "TagPickerCard", MinWidth = 220 };
        card.Classes.Add("panel");
        popup = new Popup { Name = "TagPicker", PlacementTarget = box, Placement = above ? PlacementMode.Top : PlacementMode.Bottom, IsLightDismissEnabled = false, Child = card, Focusable = false };
        host.Children.Add(popup);
        box.PropertyChanged += (_, e) => { if (!accepting && (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)) Update(); };
        box.LostFocus += (_, _) => Close();
        box.DetachedFromVisualTree += (_, _) => Close();
        box.AddHandler(InputElement.KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Gives a text box the tag list. The list lives in <paramref name="host"/>, a panel that holds the box.</summary>
    public static TagPicker Attach(TextBox box, Panel host, TimerModel model, bool above = false) => new(box, host, model, above);

    public bool IsOpen => popup.IsOpen;
    /// <summary>The accounts being offered, in order, for tests.</summary>
    public IReadOnlyList<string> Offered => popup.IsOpen ? matches : Array.Empty<string>();
    public int Chosen => selected;

    private void Update()
    {
        if (TagSuggestions.Current(box.Text, box.CaretIndex) is not { } current) { Close(); return; }
        var found = TagSuggestions.Matches(current.Typed, model.AccountNames);
        if (found.Count == 0 || TagSuggestions.IsComplete(current.Typed, found)) { Close(); return; }
        tagStart = current.Start;
        if (!found.SequenceEqual(matches)) selected = 0;
        matches = found; Build(); popup.IsOpen = true;
    }

    private void Close() { popup.IsOpen = false; matches = Array.Empty<string>(); }

    private void Build()
    {
        rows.Children.Clear();
        for (var i = 0; i < matches.Count; i++)
        {
            var index = i; var account = model.Accounts.FirstOrDefault(a => a.Name == matches[i]);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (account is not null) line.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.Parse(Ui.Visible(account.Colour))) });
            line.Children.Add(new TextBlock { Text = account?.Display ?? matches[i], FontSize = 13 });
            var tag = Ui.Text(NoteShorthand.TagFor(matches[i]), 12, "muted"); line.Children.Add(tag);
            // A button that never takes the focus, so choosing one with the mouse leaves the caret in the box.
            var row = new Button { Name = "TagPickerRow_" + FeedPage.Key(matches[i]), Content = line, Focusable = false, Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = i == selected ? new SolidColorBrush(Ui.ThemeColour(box, "Selected", Colors.LightGray)) : Brushes.Transparent };
            row.Click += (_, _) => Accept(index);
            rows.Children.Add(row);
        }
        ((Border)popup.Child!).Width = Math.Max(220, box.Bounds.Width * .6);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (!popup.IsOpen || matches.Count == 0) return;
        switch (e.Key)
        {
            case Key.Down: selected = (selected + 1) % matches.Count; Build(); e.Handled = true; break;
            case Key.Up: selected = (selected - 1 + matches.Count) % matches.Count; Build(); e.Handled = true; break;
            case Key.Tab or Key.Enter: Accept(selected); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    /// <summary>Takes the account at this place in the list, completing the tag in the box.</summary>
    public void Accept(int index)
    {
        if (index < 0 || index >= matches.Count) return;
        var (text, caret) = TagSuggestions.Accept(box.Text ?? "", tagStart, box.CaretIndex, matches[index]);
        accepting = true; box.Text = text; box.CaretIndex = caret; accepting = false;
        Close(); box.Focus();
    }
}
