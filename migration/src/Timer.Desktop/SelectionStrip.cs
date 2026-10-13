using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Timer.Desktop;

/// <summary>
/// A quiet strip just above the bottom toolbar that says how many blocks are selected and what that means for merging. It is part of the layout rather
/// than a floating note, since it is selection feedback and not an alert. It has the panel colour and a hairline on top, nothing in the accent colour,
/// and a plain text button to clear the selection.
/// </summary>
public sealed class SelectionStrip : Border
{
    private readonly TimerModel model;
    private readonly TextBlock count = new() { Name = "SelectionCount", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock note = new() { Name = "SelectionNote", FontSize = 13, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    public SelectionStrip(TimerModel model)
    {
        this.model = model; Name = "SelectionStrip";
        note.Classes.Add("muted");
        var words = Ui.Horizontal(count, note); words.Spacing = 6;
        var clear = Ui.Button("Clear selection", () => model.ClearSelection(), "ClearSelection"); clear.Classes.Add("flat"); clear.FontSize = 13;
        var row = Ui.Grid("*,Auto"); Ui.Put(row, words); Ui.Put(row, clear, 1);
        IsVisible = false; Child = row; Padding = new Thickness(14, 4, 6, 4); BorderThickness = new Thickness(0, 1, 0, 0); Margin = new Thickness(0, 10, 0, 0);
        Classes.Add("selectionStrip");
        model.PropertyChanged += (_, _) => Sync();
        Sync();
    }
    /// <summary>True while blocks are selected. The window shows the strip only when its toolbar is showing too.</summary>
    public bool Wanted => model.SelectionCountText is not null;
    public void Sync()
    {
        count.Text = model.SelectionCountText ?? ""; note.Text = model.SelectionNote is { } text ? "· " + text : "";
        // Whether it shows at all also depends on the view, so the dashboard window decides that when it refreshes.
    }
}
