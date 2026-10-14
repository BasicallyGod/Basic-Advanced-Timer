using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>What a desk tile is: its title, its soft hue (decoration only, never a meaning colour), the cells it asks for and the page it opens in full.</summary>
public sealed record DeskTileKind(string Id, string Title, string Hue, int Columns, int Rows, string? OpensPage);

/// <summary>
/// The desk: small tiles that flow to fill the window and can be picked up by their heading and put down elsewhere.
/// Only the order is saved, because the arrangement follows from it at whatever width the window has.
/// </summary>
public sealed class DeskView : UserControl
{
    public const double CellHeight = 124, Gap = 12;
    public static readonly IReadOnlyList<DeskTileKind> Kinds = new DeskTileKind[]
    {
        new("working", "Working time", "#7FD1C7", 2, 1, "timeline"),
        new("accounts", "Accounts", "#A99CF5", 2, 3, null),
        new("day", "Day", "#F0C97A", 2, 1, "overview"),
    };

    private readonly TimerModel model;
    private readonly Action<string>? openPage;
    private readonly DeskBoard board = new();
    private readonly Dictionary<string, (Border Tile, Border Body, TileForm? Form)> tiles = new();
    private readonly ClockText clock = new(34, 18); private readonly TextBlock accountLine = Ui.Text("", 14), allocated = Ui.Text("", 20, "figures"), unallocated = Ui.Text("", 20, "figures");
    private List<string> order;
    private string? dragged; private Point dragStart; private int dragIndex;

    public DeskView(TimerModel model, Action<string>? openPage = null)
    {
        this.model = model; this.openPage = openPage;
        order = DeskLayout.Reconcile(model.DeskOrder, Kinds.Select(k => k.Id).ToList());
        foreach (var kind in Kinds) { var tile = BuildTile(kind); tiles[kind.Id] = tile; board.Children.Add(tile.Tile); }
        board.Owner = this;
        Name = "Desk";
        Content = new ScrollViewer { Content = board, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false };
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; RefreshLive(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }

    /// <summary>The tiles in the order they sit, as the saved order and a drag leave it.</summary>
    public IReadOnlyList<string> Order => order;
    /// <summary>Each tile's place and form at the current width.</summary>
    public IReadOnlyList<DeskPlacement> Placements => board.Placements;
    public Control? TileControl(string id) => tiles.TryGetValue(id, out var t) ? t.Tile : null;

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshLive();
    private void RefreshLive()
    {
        clock.Text = model.ClockText;
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak;
        clock.SetState(onBreak, late, model.IsTracking && !onBreak && !late);
        accountLine.Text = model.AccountLabel;
        allocated.Text = model.Format(model.Allocated); unallocated.Text = model.Format(model.Unallocated);
    }

    private (Border Tile, Border Body, TileForm? Form) BuildTile(DeskTileKind kind)
    {
        var tile = new Border { Name = "Tile_" + kind.Id, CornerRadius = new CornerRadius(18), ClipToBounds = true, Tag = kind };
        var hue = Color.Parse(kind.Hue);
        // A wash of the hue over the panel, so it reads on light and dark themes alike.
        tile.Background = new SolidColorBrush(Color.FromArgb(0x38, hue.R, hue.G, hue.B));
        var heading = Ui.Text(kind.Title, 12, "muted"); heading.VerticalAlignment = VerticalAlignment.Center;
        var head = Ui.Grid("*,Auto"); head.Margin = new Thickness(16, 10, 8, 0); head.Height = 30; head.Cursor = new Cursor(StandardCursorType.Hand); head.Background = Brushes.Transparent; head.Name = "TileHandle_" + kind.Id;
        Ui.Put(head, heading);
        if (kind.OpensPage is { } page)
        {
            var open = Ui.IconButton(IconKind.Next, "Open full page", "TileOpen_" + kind.Id, () => openPage?.Invoke(page)); open.Width = 28; open.Height = 28; Ui.Put(head, open, 1);
        }
        var body = new Border { Margin = new Thickness(16, 2, 16, 14) };
        var stack = Ui.Grid("*", "Auto,*"); Ui.Put(stack, head); Ui.Put(stack, body, row: 1); tile.Child = stack;
        head.PointerPressed += (_, e) => BeginDrag(kind.Id, e, tile);
        head.PointerMoved += (_, e) => DragTo(e);
        head.PointerReleased += (_, e) => EndDrag(e);
        head.PointerCaptureLost += (_, _) => CancelDrag();
        return (tile, body, null);
    }

    /// <summary>Fills a tile for the room it has. A small form keeps only what is essential.</summary>
    internal void Fill(string id, TileForm form)
    {
        var (tile, body, current) = tiles[id];
        if (current == form) return;
        tiles[id] = (tile, body, form);
        switch (id)
        {
            case "working":
                body.Child = form == TileForm.Tiny ? clock : new StackPanel { Spacing = 2, Children = { clock, accountLine } };
                break;
            case "day":
                body.Child = form == TileForm.Tiny ? allocated : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, Children =
                {
                    new StackPanel { Children = { Ui.Text("Allocated", 12, "muted"), allocated } }, new StackPanel { Children = { Ui.Text("Unallocated", 12, "muted"), unallocated } } } };
                break;
            case "accounts":
                body.Child = form == TileForm.Tiny
                    ? Ui.Text(model.Accounts.Count(a => !BreakAccount.Is(a.Name)) + " accounts", 14)
                    : new ScrollViewer { Content = new AccountList(model), AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                break;
        }
        RefreshLive();
    }

    private void BeginDrag(string id, PointerPressedEventArgs e, Control tile)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (TopLevel.GetTopLevel(this) is Window window) Ui.CloseMenus(window);
        dragged = id; dragStart = e.GetPosition(board); dragIndex = order.IndexOf(id);
        e.Pointer.Capture((IInputElement)e.Source!); e.Handled = true;
        tile.ZIndex = 10; tile.Opacity = .92;
    }
    private void DragTo(PointerEventArgs e)
    {
        if (dragged is null) return;
        var at = e.GetPosition(board);
        board.DragOffset = at - dragStart; board.Dragged = dragged;
        dragIndex = IndexAt(at);
        board.Provisional = DeskLayout.Move(order, dragged, dragIndex);
        board.InvalidateArrange();
    }
    private void EndDrag(PointerReleasedEventArgs e)
    {
        if (dragged is null) return;
        var id = dragged; var index = IndexAt(e.GetPosition(board));
        e.Pointer.Capture(null); Finish();
        Drop(id, index);
    }
    private void CancelDrag() { if (dragged is null) return; Finish(); board.InvalidateArrange(); }
    private void Finish()
    {
        if (dragged is { } id && tiles.TryGetValue(id, out var t)) { t.Tile.ZIndex = 0; t.Tile.Opacity = 1; }
        dragged = null; board.Dragged = null; board.Provisional = null; board.DragOffset = default;
    }
    private int IndexAt(Point at)
    {
        var cell = board.CellWidth; if (cell <= 0) return dragIndex;
        var placements = DeskLayout.Pack(order.Select(Spec), board.Columns);
        return DeskLayout.InsertIndexAt(placements, dragged!, at.X / cell, at.Y / (CellHeight + Gap));
    }
    /// <summary>Puts a tile at a place in the order, saves the order and lays the desk out again.</summary>
    public void Drop(string id, int index)
    {
        order = DeskLayout.Move(order, id, index);
        model.SetDeskOrder(order);
        board.InvalidateMeasure(); board.InvalidateArrange();
    }
    internal static DeskTileSpec Spec(string id) { var k = Kinds.First(k => k.Id == id); return new(k.Id, k.Columns, k.Rows); }
    internal IReadOnlyList<string> LayoutOrder => board.Provisional ?? order;

    /// <summary>A panel that sets each tile on the grid from the packed layout, with the dragged tile following the pointer.</summary>
    private sealed class DeskBoard : Panel
    {
        public DeskView? Owner;
        public List<string>? Provisional; public string? Dragged; public Vector DragOffset;
        public List<DeskPlacement> Placements { get; private set; } = new();
        public int Columns { get; private set; } = 2; public double CellWidth { get; private set; }
        private double Width0(double available) => double.IsInfinity(available) ? 640 : available;
        protected override Size MeasureOverride(Size available)
        {
            var width = Width0(available.Width); Columns = DeskLayout.ColumnsFor(width); CellWidth = width / Columns;
            var rows = DeskLayout.RowCount(DeskLayout.Pack((Owner?.order ?? new()).Select(Spec), Columns));
            foreach (var child in Children) child.Measure(new Size(CellWidth * 2, CellHeight * 3));
            return new Size(width, rows * (CellHeight + Gap) - Gap + 24);
        }
        protected override Size ArrangeOverride(Size final)
        {
            Columns = DeskLayout.ColumnsFor(final.Width); CellWidth = final.Width / Columns;
            Placements = DeskLayout.Pack((Provisional ?? Owner?.order ?? new()).Select(Spec), Columns);
            foreach (var p in Placements)
            {
                if (Children.FirstOrDefault(c => (c.Tag as DeskTileKind)?.Id == p.Id) is not { } tile) continue;
                var rect = new Rect(p.Column * CellWidth + Gap / 2, p.Row * (CellHeight + Gap) + Gap / 2, p.ColumnSpan * CellWidth - Gap, p.RowSpan * (CellHeight + Gap) - Gap);
                if (p.Id == Dragged) rect = rect.Translate(DragOffset);
                tile.Arrange(rect);
                Owner?.Fill(p.Id, p.Form);
            }
            return final;
        }
    }
}
