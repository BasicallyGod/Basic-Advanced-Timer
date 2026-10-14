using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>What a desk tile is: its title, its soft hue (decoration only, never a meaning colour), the cells it asks for and the page it opens in full.</summary>
public sealed record DeskTileKind(string Id, string Title, string Hue, int Columns, int Rows, string? OpensPage, int MinColumns = 1, int MinRows = 1, int MaxColumns = 4, int MaxRows = 8);

/// <summary>
/// The desk: small tiles that flow to fill the window and can be picked up by their heading and put down elsewhere.
/// Only the order is saved, because the arrangement follows from it at whatever width the window has.
/// </summary>
public sealed class DeskView : UserControl
{
    public const double CellHeight = 124, Gap = 12;
    public static readonly IReadOnlyList<DeskTileKind> Kinds = new DeskTileKind[]
    {
        new("working", "Time tracker", "#7FD1C7", 2, 2, "timeline", 1, 1, 4, 3),
        new("week", "This week", "#E8C48C", 2, 2, "overview", 2, 2, 4, 4),
        new("deadline", "Deadline", "#EE9CB4", 2, 2, null, 1, 1, 4, 3),
        new("day", "Day", "#F0C97A", 2, 1, "overview"),
        new("strip", "Day strip", "#D9A8E8", 4, 1, "timeline", 2, 1, 4, 2),
        new("timeline", "Timeline", "#D6C3A5", 3, 4, "timeline", 2, 3, 4, 10),
        new("accounts", "Accounts", "#A99CF5", 2, 3, null),
        new("todo", "To-do", "#F4B58A", 2, 3, null),
        new("notes", "Notes", "#9ED0E6", 2, 3, null),
        new("coming", "Coming up", "#F2A7B8", 2, 2, null),
        new("postit", "Post-it", "#FFF1A8", 2, 2, null),
        new("postit2", "Post-it 2", "#E3D6FF", 2, 2, null),
        new("postit3", "Post-it 3", "#FFD3DC", 2, 2, null),
        new("pomodoro", "Pomodoro", "#D0D8A0", 2, 2, null, 1, 2, 3, 4),
        new("clock", "Clock", "#8FA8D8", 1, 2, null),
    };

    private readonly TimerModel model;
    private readonly Action<string>? openPage;
    private readonly DeskBoard board = new();
    private readonly Dictionary<string, (Border Tile, Border Body, TileForm? Form)> tiles = new();
    private readonly ClockText clock = new(34, 18); private readonly TextBlock accountLine = Ui.Text("", 14), allocated = Ui.Text("", 20, "figures"), unallocated = Ui.Text("", 20, "figures");
    private readonly AnalogueClock analogue; private readonly DayStrip strip;
    private PomodoroView? pomodoroView; private TrackerView? trackerView; private WeekChart? weekChart; private DeadlineView? deadlineView;
    private NotesView? notesView; private ComingUpView? comingView;
    private TodoView? todoView; private readonly Dictionary<string, TextBox> postIts = new();
    private List<string> order, hidden;
    private readonly Border drawer = new() { Name = "Drawer", Width = 236, HorizontalAlignment = HorizontalAlignment.Right, IsVisible = false, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), ZIndex = 20 };
    private readonly StackPanel chips = new() { Spacing = 8 };
    private readonly Button drawerToggle;
    private bool drawerOpen; private readonly TranslateTransform drawerSlide = new();
    private readonly Avalonia.Threading.DispatcherTimer drawerTimer = new() { Interval = TimeSpan.FromMilliseconds(16) }; private long drawerStamp;
    private const double DrawerMilliseconds = 320, ChipStagger = 30;
    private Point chipStart; private string? chipPressed; private bool chipMoved;
    private string? dragged; private Point dragStart; private int dragIndex;
    private readonly Dictionary<string, (int Columns, int Rows)> sizes;
    private string? resizing; private (int Columns, int Rows) resizingSize; private Point resizeStart; private Size resizeFrom;
    private string? expanded;
    private readonly ScrollViewer scroller; private readonly DayStrip timelineStrip; private readonly Border timelineHost = new();
    private readonly Dictionary<string, Control> grips = new(); private readonly Dictionary<string, TextBlock> metas = new();
    /// <summary>Raised when a tile is opened in place or put back, with the tile's id or null, so the window can show the controls that tile needs.</summary>
    public event Action<string?>? ExpandedChanged;

    public DeskView(TimerModel model, Action<string>? openPage = null)
    {
        this.model = model; this.openPage = openPage;
        analogue = new AnalogueClock(() => model.Engine.Now); strip = new DayStrip(model); timelineStrip = new DayStrip(model);
        sizes = new Dictionary<string, (int Columns, int Rows)>(model.DeskSizes);
        var known = Kinds.Select(k => k.Id).ToList();
        hidden = model.DeskHidden.Where(known.Contains).Distinct().ToList();
        // A desk that has never been arranged starts with one post-it out and the spare two in the drawer.
        if (model.DeskOrder.Count == 0 && model.DeskHidden.Count == 0) hidden = new List<string> { "postit2", "postit3" };
        order = DeskLayout.Reconcile(model.DeskOrder.Where(id => !hidden.Contains(id)), known.Where(id => !hidden.Contains(id)).ToList());
        foreach (var kind in Kinds) { var tile = BuildTile(kind); tiles[kind.Id] = tile; board.Children.Add(tile.Tile); }
        board.Owner = this; board.Lively = !model.Calm;
        Name = "Desk";
        // A button above the board opens the drawer, a panel down the right edge that holds the tiles put away.
        drawerToggle = Ui.Button("", () => SetDrawer(!drawerOpen), "DrawerToggle");
        drawerToggle.Classes.Add("toolButton"); drawerToggle.Classes.Add("quiet"); drawerToggle.HorizontalAlignment = HorizontalAlignment.Right; drawerToggle.Height = 30; drawerToggle.Padding = new Thickness(12, 0); drawerToggle.Margin = new Thickness(0, 0, 4, 4);
        drawer.RenderTransform = drawerSlide; drawerTimer.Tick += (_, _) => StepDrawer();
        drawer.Classes.Add("panel"); drawer.Margin = new Thickness(0, 4, 4, 4); drawer.Padding = new Thickness(14);
        var drawerStack = new StackPanel { Spacing = 10 };
        drawerStack.Children.Add(Ui.Text("Put away", 12, "muted")); drawerStack.Children.Add(chips);
        drawer.Child = drawerStack; drawer.BoxShadow = BoxShadows.Parse("-6 0 24 0 #30000000");
        // The Drawer button is handed to the window, which sets it in the header beside the title, so the board gets the room a row of its own would take.
        var frame = Ui.Grid("*", "*");
        scroller = new ScrollViewer { Content = board, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false };
        // An opened tile fills the room the board has, so the board needs to know it.
        scroller.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty && expanded is not null) board.InvalidateMeasure(); };
        // A faint field of dots sits behind the tiles, so the desk reads as a surface rather than a blank page.
        Ui.Put(frame, new DotField());
        Ui.Put(frame, scroller);
        Ui.Put(frame, drawer);
        Content = frame;
        SyncDrawer();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; RefreshLive(); };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; };
    }

    /// <summary>Whether tiles glide, lift and bounce (Lively) or just move (Calm).</summary>
    public bool Lively { get => board.Lively; set { board.Lively = value; board.InvalidateArrange(); } }
    /// <summary>The tiles in the order they sit, as the saved order and a drag leave it.</summary>
    public IReadOnlyList<string> Order => order;
    /// <summary>The button that opens and closes the drawer. The window sets it in its header.</summary>
    public Button DrawerButton => drawerToggle;
    public bool DrawerOpen => drawerOpen;
    public void SetDrawer(bool open)
    {
        if (open == drawerOpen) return;
        drawerOpen = open;
        if (!Lively) { drawer.IsVisible = open; drawerSlide.X = 0; foreach (var chip in chips.Children) { chip.Opacity = 1; chip.RenderTransform = null; } return; }
        // The panel slides in from the edge and its chips follow one after another. The first step is taken on the first tick, so where no ticks come it simply appears.
        drawer.IsVisible = true; drawerStamp = Environment.TickCount64; drawerTimer.Start();
    }
    private void StepDrawer()
    {
        var elapsed = (double)(Environment.TickCount64 - drawerStamp);
        double Ease(double t) { t = Math.Clamp(t, 0, 1); return 1 - Math.Pow(1 - t, 3); }
        var shown = drawerOpen ? Ease(elapsed / DrawerMilliseconds) : 1 - Ease(elapsed / (DrawerMilliseconds * .8));
        drawerSlide.X = (1 - shown) * (drawer.Width + 16);
        var index = 0;
        foreach (var chip in chips.Children)
        {
            var local = drawerOpen ? Ease((elapsed - index * ChipStagger) / 240) : shown; index++;
            chip.Opacity = local; chip.RenderTransform = new TranslateTransform((1 - local) * 28, 0);
        }
        var finished = elapsed >= DrawerMilliseconds + chips.Children.Count * ChipStagger;
        if (!finished) return;
        drawerTimer.Stop(); drawerSlide.X = 0;
        foreach (var chip in chips.Children) { chip.Opacity = 1; chip.RenderTransform = null; }
        drawer.IsVisible = drawerOpen;
    }
    /// <summary>The tiles in the drawer.</summary>
    public IReadOnlyList<string> Hidden => hidden;
    /// <summary>Each tile's place and form at the current width.</summary>
    public IReadOnlyList<DeskPlacement> Placements => board.Placements;
    /// <summary>The tile opened to fill the desk, or null.</summary>
    public string? Expanded => expanded;
    /// <summary>The size a tile has now, in cells: the one being dragged, the one saved, or its own default, held to its limits.</summary>
    public (int Columns, int Rows) SizeOf(string id)
    {
        var kind = Kinds.First(k => k.Id == id);
        var (c, r) = resizing == id ? resizingSize : sizes.TryGetValue(id, out var saved) ? saved : (kind.Columns, kind.Rows);
        return DeskLayout.ClampSize(c, r, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, board.Columns);
    }
    internal DeskTileSpec SpecFor(string id) { var (c, r) = SizeOf(id); return new(id, c, r); }
    /// <summary>Makes a tile larger or smaller by whole cells, within what suits it, and remembers the size.</summary>
    public void Resize(string id, int columns, int rows)
    {
        var kind = Kinds.First(k => k.Id == id);
        sizes[id] = DeskLayout.ClampSize(columns, rows, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, 12);
        model.SetDeskSizes(sizes); board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>Opens a tile in place: it grows to fill the desk and the others step aside. It closes with its button, or Escape (handled by the window, after dialogs).</summary>
    public void Expand(string id)
    {
        if (expanded == id || !tiles.ContainsKey(id) || hidden.Contains(id)) return;
        SetDrawer(false); expanded = id; SyncDrawer(); board.InvalidateMeasure(); board.InvalidateArrange(); ExpandedChanged?.Invoke(id);
    }
    public void Collapse()
    {
        if (expanded is null) return;
        expanded = null; SyncDrawer(); board.InvalidateMeasure(); board.InvalidateArrange(); ExpandedChanged?.Invoke(null);
    }
    public Control? TileControl(string id) => tiles.TryGetValue(id, out var t) ? t.Tile : null;

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RefreshLive();
        // The rows are redrawn only when something other than the clock changed, so a tick never interrupts the to-do list.
        if (e.PropertyName != nameof(TimerModel.Elapsed)) { RebuildTimeline(); todoView?.Rebuild(); notesView?.Rebuild(); comingView?.Rebuild(); PaintPostIt(); }
    }
    /// <summary>Redraws the timeline tile, keeping where it was scrolled to, as the dashboard does for its own timeline.</summary>
    private void RebuildTimeline()
    {
        if (!tiles.TryGetValue("timeline", out var t) || tiles["timeline"].Form is null or TileForm.Tiny) return;
        var old = timelineHost.Child as Control;
        var scroll = old is ScrollViewer direct ? direct : old?.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = model.TimelineOffset is double fromModel ? new Vector(0, fromModel) : scroll?.Tag is Vector pending ? pending : scroll?.Offset;
        var fresh = ReviewViews.Timeline(model);
        if (offset is { } kept) { var next = fresh is ScrollViewer d ? d : fresh.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault(); if (next is not null) next.Tag = kept; }
        timelineHost.Child = fresh;
    }
    private static IconKind IconFor(string id) => id switch
    {
        "working" or "clock" or "pomodoro" => IconKind.Clock, "day" => IconKind.Table, "strip" or "timeline" => IconKind.Timeline, "accounts" => IconKind.Entries,
        "todo" => IconKind.Check, "notes" => IconKind.Note, "coming" => IconKind.Today, "week" => IconKind.Bars, "deadline" => IconKind.Flag, _ => IconKind.Edit
    };
    /// <summary>The quiet figure at the right of each tile's header.</summary>
    private void RefreshMeta()
    {
        void Set(string id, string text) { if (metas.TryGetValue(id, out var m)) m.Text = text; }
        var blocks = strip.Blocks().Count; var todos = model.Trinkets.Todos;
        Set("strip", $"{blocks} block{(blocks == 1 ? "" : "s")}"); Set("timeline", $"{blocks} block{(blocks == 1 ? "" : "s")}");
        Set("todo", todos.Count == 0 ? "" : $"{todos.Count(t => t.Done)} of {todos.Count} done");
        var coming = model.ComingUp().Count; Set("coming", coming == 0 ? "" : $"{coming} to come");
        var notes = model.NotesOn(model.SelectedDate).Count; Set("notes", notes == 0 ? "" : $"{notes} note{(notes == 1 ? "" : "s")}");
        Set("accounts", $"{model.Accounts.Count(a => !BreakAccount.Is(a.Name))} accounts");
        if (weekChart is not null) Set("week", model.Format(weekChart.Total()));
        Set("pomodoro", model.Pomodoro.Completed == 0 ? "" : $"{model.Pomodoro.Completed} done");
    }
    private static bool IsPostIt(string id) => id.StartsWith("postit", StringComparison.Ordinal);
    private static int PostItIndex(string id) => id == "postit" ? 0 : int.Parse(id[6..]) - 1;
    private void PaintPostIt()
    {
        foreach (var (id, t) in tiles.Where(t => IsPostIt(t.Key)))
            t.Tile.Background = new SolidColorBrush(PostItView.Paper(model.Trinkets.PostIts[PostItIndex(id)].Colour));
    }
    private void RefreshLive()
    {
        clock.Text = model.ClockText;
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak;
        clock.SetState(onBreak, late, model.IsTracking && !onBreak && !late);
        accountLine.Text = model.AccountLabel; analogue.InvalidateVisual(); strip.InvalidateVisual(); pomodoroView?.Refresh(); trackerView?.Refresh(); deadlineView?.Refresh(); weekChart?.InvalidateVisual(); RefreshMeta();
        allocated.Text = model.Format(model.Allocated); unallocated.Text = model.Format(model.Unallocated);
    }

    private (Border Tile, Border Body, TileForm? Form) BuildTile(DeskTileKind kind)
    {
        var tile = new Border { Name = "Tile_" + kind.Id, CornerRadius = new CornerRadius(22), ClipToBounds = true, Tag = kind };
        var hue = Color.Parse(kind.Hue);
        // A wash of the hue over the panel, so it reads on light and dark themes alike, with a soft shadow to lift the tile off the desk.
        tile.Background = new SolidColorBrush(Color.FromArgb(0x38, hue.R, hue.G, hue.B));
        tile.BoxShadow = BoxShadows.Parse("0 4 14 0 #18000000");
        // The header: a round badge in the tile's hue with its icon, the title, a quiet figure, and the grip to pick the tile up by.
        // The buttons (open, open in place, put away) appear when the pointer is over the tile, so they do not crowd the header at rest.
        var badge = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Child = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(hue), VerticalAlignment = VerticalAlignment.Center,
            Child = new IconGlyph(IconFor(kind.Id)) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
        var heading = new TextBlock { Text = kind.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Ui.Text("", 12, "muted"); meta.VerticalAlignment = VerticalAlignment.Center; meta.Name = "TileMeta_" + kind.Id; metas[kind.Id] = meta;
        var head = Ui.Grid("Auto,*,Auto,Auto,Auto"); head.ColumnSpacing = 10; head.Margin = new Thickness(14, 0, 8, 0); head.Cursor = new Cursor(StandardCursorType.Hand); head.Background = Brushes.Transparent; head.Name = "TileHandle_" + kind.Id;
        Ui.Put(head, badge); Ui.Put(head, heading, 1); Ui.Put(head, meta, 2);
        var buttons = Ui.Horizontal(); buttons.Spacing = 2; buttons.Opacity = 0; buttons.IsHitTestVisible = false;
        if (kind.OpensPage is { } page)
        {
            var open = Ui.IconButton(IconKind.Next, "Open full page", "TileOpen_" + kind.Id, () => openPage?.Invoke(page)); open.Width = 28; open.Height = 28; buttons.Children.Add(open);
        }
        var expand = Ui.IconButton(IconKind.FitDay, "Open in place", "TileExpand_" + kind.Id, () => { if (expanded == kind.Id) Collapse(); else Expand(kind.Id); }); expand.Width = 28; expand.Height = 28; buttons.Children.Add(expand);
        var put = Ui.IconButton(IconKind.Close, "Put away in the drawer", "TileHide_" + kind.Id, () => Hide(kind.Id)); put.Width = 28; put.Height = 28; buttons.Children.Add(put);
        if (IsPostIt(kind.Id))
        {
            // Paper stays light whatever the theme, and a button changes its colour.
            var paint = Ui.IconButton(IconKind.Edit, "Change colour", "PostItColour" + (kind.Id == "postit" ? "" : (PostItIndex(kind.Id) + 1).ToString()), () => model.CyclePostItColour(PostItIndex(kind.Id))); paint.Width = 28; paint.Height = 28; buttons.Children.Insert(0, paint);
        }
        Ui.Put(head, buttons, 3);
        var dots = new IconGlyph(IconKind.Grip) { TintKey = "Muted", VerticalAlignment = VerticalAlignment.Center, Opacity = .8, IsHitTestVisible = false }; Ui.Put(head, dots, 4);
        tile.PointerEntered += (_, _) => { buttons.Opacity = 1; buttons.IsHitTestVisible = true; };
        tile.PointerExited += (_, _) => { if (resizing is null && dragged is null) { buttons.Opacity = 0; buttons.IsHitTestVisible = false; } };
        var band = new Border { Height = 52, Background = new SolidColorBrush(Color.FromArgb(0x30, hue.R, hue.G, hue.B)), Child = head };
        var body = new Border { Margin = new Thickness(16, 10, 16, 14) };
        var stack = Ui.Grid("*", "Auto,*"); Ui.Put(stack, band); Ui.Put(stack, body, row: 1);
        Control content = IsPostIt(kind.Id) ? new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Child = stack } : stack;
        // A grip in the bottom right corner makes the tile larger or smaller by whole cells.
        var grip = new Border { Name = "TileResize_" + kind.Id, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M15,5 L5,15 M15,10 L10,15"), Stroke = new SolidColorBrush(Colors.Gray), StrokeThickness = 1.6, Opacity = .6, StrokeLineCap = PenLineCap.Round, Margin = new Thickness(2) } };
        grip.PointerPressed += (_, e) => BeginResize(kind.Id, e, tile);
        grip.PointerMoved += (_, e) => ResizeTo(e);
        grip.PointerReleased += (_, e) => EndResize(e);
        grip.PointerCaptureLost += (_, _) => CancelResize();
        grips[kind.Id] = grip;
        var layers = new Grid(); layers.Children.Add(content); layers.Children.Add(grip);
        tile.Child = layers;
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
            case "working": body.Child = trackerView = new TrackerView(model, form == TileForm.Tiny); break;
            case "week": body.Child = weekChart ??= new WeekChart(model); weekChart.InvalidateVisual(); break;
            case "deadline": body.Child = deadlineView = new DeadlineView(model, form == TileForm.Tiny); break;
            case "day":
                body.Child = form == TileForm.Tiny ? allocated : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, Children =
                {
                    new StackPanel { Children = { Ui.Text("Allocated", 12, "muted"), allocated } }, new StackPanel { Children = { Ui.Text("Unallocated", 12, "muted"), unallocated } } } };
                break;
            case "timeline": timelineStrip.Labels = true; body.Child = form == TileForm.Tiny ? timelineStrip : timelineHost; if (form != TileForm.Tiny) RebuildTimeline(); break;
            case "pomodoro": body.Child = pomodoroView = new PomodoroView(model, form == TileForm.Tiny); break;
            case "clock": body.Child = analogue; break;
            case "strip": strip.Labels = form != TileForm.Tiny; body.Child = strip; strip.InvalidateVisual(); break;
            case "notes": body.Child = notesView = new NotesView(model, form == TileForm.Tiny); break;
            case "coming": body.Child = new ScrollViewer { Content = comingView = new ComingUpView(model), AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; break;
            case "todo": body.Child = todoView = new TodoView(model, form == TileForm.Tiny); break;
            case "postit": case "postit2": case "postit3":
                if (!postIts.TryGetValue(id, out var note)) postIts[id] = note = PostItView.Build(model, PostItIndex(id), false, id == "postit" ? "PostItText" : "PostItText" + (PostItIndex(id) + 1));
                body.Child = note; PaintPostIt(); break;
            case "accounts":
                body.Child = form == TileForm.Tiny
                    ? Ui.Text(model.Accounts.Count(a => !BreakAccount.Is(a.Name)) + " accounts", 14)
                    : new ScrollViewer { Content = new AccountList(model), AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                break;
        }
        RefreshLive();
    }

    private void BeginResize(string id, PointerPressedEventArgs e, Control tile)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || expanded is not null) return;
        resizing = id; resizingSize = SizeOf(id); resizeStart = e.GetPosition(board); resizeFrom = tile.Bounds.Size;
        e.Pointer.Capture((IInputElement)e.Source!); e.Handled = true;
    }
    private void ResizeTo(PointerEventArgs e)
    {
        if (resizing is not { } id) return;
        var at = e.GetPosition(board);
        var (c, r) = DeskLayout.SizeFromPixels(resizeFrom.Width + at.X - resizeStart.X, resizeFrom.Height + at.Y - resizeStart.Y, board.CellWidth, CellHeight, Gap);
        var kind = Kinds.First(k => k.Id == id);
        var size = DeskLayout.ClampSize(c, r, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, board.Columns);
        if (size == resizingSize) return;
        resizingSize = size; board.InvalidateMeasure(); board.InvalidateArrange();
    }
    private void EndResize(PointerReleasedEventArgs e)
    {
        if (resizing is not { } id) return;
        var size = resizingSize; e.Pointer.Capture(null); resizing = null;
        Resize(id, size.Columns, size.Rows);
    }
    private void CancelResize() { if (resizing is null) return; resizing = null; board.InvalidateMeasure(); board.InvalidateArrange(); }

    private void BeginDrag(string id, PointerPressedEventArgs e, Control tile)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (TopLevel.GetTopLevel(this) is Window window) Ui.CloseMenus(window);
        dragged = id; dragStart = e.GetPosition(board); dragIndex = order.IndexOf(id);
        e.Pointer.Capture((IInputElement)e.Source!); e.Handled = true;
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
        var overDrawer = drawerOpen && drawer.Bounds.Contains(e.GetPosition((Visual?)drawer.Parent));
        e.Pointer.Capture(null); Finish();
        if (overDrawer) Hide(id); else Drop(id, index);
    }
    private void CancelDrag() { if (dragged is null) return; Finish(); board.InvalidateArrange(); }
    private void Finish()
    {
        dragged = null; board.Dragged = null; board.Provisional = null; board.DragOffset = default;
    }
    private int IndexAt(Point at)
    {
        var cell = board.CellWidth; if (cell <= 0) return dragIndex;
        var placements = DeskLayout.Pack(order.Select(SpecFor), board.Columns);
        return DeskLayout.InsertIndexAt(placements, dragged!, at.X / cell, at.Y / (CellHeight + Gap));
    }
    /// <summary>Puts a tile at a place in the order, saves the order and lays the desk out again.</summary>
    public void Drop(string id, int index)
    {
        order = DeskLayout.Move(order, id, index);
        model.SetDeskOrder(order);
        board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>Puts a tile away in the drawer. Nothing is lost: it keeps its data and comes back when it is put on the desk again.</summary>
    public void Hide(string id)
    {
        if (!order.Remove(id)) return;
        hidden.Add(id); Save(); SyncDrawer(); board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>Brings a tile from the drawer onto the desk at a place in the order.</summary>
    public void Show(string id, int index = int.MaxValue)
    {
        if (!hidden.Remove(id)) return;
        order.Insert(Math.Clamp(index, 0, order.Count), id); Save(); SyncDrawer(); board.InvalidateMeasure(); board.InvalidateArrange();
    }
    private void Save() { model.SetDeskOrder(order); model.SetDeskHidden(hidden); }
    private void SyncDrawer()
    {
        foreach (var kind in Kinds) { tiles[kind.Id].Tile.IsVisible = !hidden.Contains(kind.Id) && (expanded is null || expanded == kind.Id); grips[kind.Id].IsVisible = expanded is null; }
        drawerToggle.Content = hidden.Count == 0 ? "Drawer" : $"Drawer ({hidden.Count})";
        chips.Children.Clear();
        if (hidden.Count == 0) chips.Children.Add(Ui.Text("Nothing put away. Drag a tile here, or use its cross.", 12, "muted"));
        foreach (var id in hidden)
        {
            var kind = Kinds.First(k => k.Id == id); var hue = Color.Parse(kind.Hue);
            var chip = new Border { Name = "DrawerChip_" + id, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Cursor = new Cursor(StandardCursorType.Hand), Background = new SolidColorBrush(Color.FromArgb(0x40, hue.R, hue.G, hue.B)), Child = Ui.Text(kind.Title, 14) };
            chip.PointerPressed += (_, e) => { chipPressed = id; chipStart = e.GetPosition(this); chipMoved = false; e.Pointer.Capture(chip); e.Handled = true; };
            chip.PointerMoved += (_, e) => { if (chipPressed is not null && (e.GetPosition(this) - chipStart).X is var dx && Math.Abs(dx) + Math.Abs((e.GetPosition(this) - chipStart).Y) > 8) chipMoved = true; };
            chip.PointerReleased += (_, e) =>
            {
                if (chipPressed is not { } pressed) return;
                chipPressed = null; e.Pointer.Capture(null);
                // A plain click brings the tile back at the end. Dragging it out onto the board drops it where it lands.
                var at = e.GetPosition(board); var overBoard = chipMoved && new Rect(board.Bounds.Size).Contains(at) && !drawer.Bounds.Contains(e.GetPosition((Visual?)drawer.Parent));
                Show(pressed, overBoard ? DropIndexFor(pressed, at) : int.MaxValue);
            };
            chips.Children.Add(chip);
        }
    }
    private int DropIndexFor(string id, Point at)
    {
        var cell = board.CellWidth; if (cell <= 0) return int.MaxValue;
        var specs = order.Concat(new[] { id }).Select(SpecFor);
        return DeskLayout.InsertIndexAt(DeskLayout.Pack(specs, board.Columns), id, at.X / cell, at.Y / (CellHeight + Gap));
    }
    internal IReadOnlyList<string> LayoutOrder => board.Provisional ?? order;

    /// <summary>
    /// A panel that sets each tile on the grid from the packed layout, with the dragged tile following the pointer. Tiles glide to their
    /// places on a light spring (a little overshoot, so a drop settles with a bounce), and the one in hand is lifted and tilted with the way it is moving.
    /// With Lively off the tiles simply jump, which is also what the headless tests see because nothing ticks there.
    /// </summary>
    private sealed class DeskBoard : Panel
    {
        private sealed class Glide { public double X, Y, W, H, VX, VY, VW, VH; }
        public DeskView? Owner;
        public List<string>? Provisional; public string? Dragged; public Vector DragOffset;
        public bool Lively = true;
        public List<DeskPlacement> Placements { get; private set; } = new();
        public int Columns { get; private set; } = 2; public double CellWidth { get; private set; }
        private readonly Dictionary<string, Glide> glides = new(); private readonly Dictionary<string, Rect> targets = new();
        private readonly Avalonia.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private string? lifted; private double lift, liftTarget, tilt, lastX;

        public DeskBoard() { Margin = new Thickness(6, 2, 6, 10); // Room for the tiles' shadows, which the scroll view would otherwise cut off.
            timer.Tick += (_, _) => Step(); DetachedFromVisualTree += (_, _) => timer.Stop(); }
        private static double Width0(double available) => double.IsInfinity(available) ? 640 : available;
        protected override Size MeasureOverride(Size available)
        {
            var width = Width0(available.Width); Columns = DeskLayout.ColumnsFor(width); CellWidth = width / Columns;
            if (Owner?.expanded is not null)
            {
                // An opened tile fills what the scroll view shows.
                var room = Math.Max(240, Owner.scroller.Bounds.Height);
                foreach (var child in Children) child.Measure(new Size(width, room));
                return new Size(width, room);
            }
            var rows = DeskLayout.RowCount(DeskLayout.Pack((Owner?.order ?? new()).Select(o => Owner!.SpecFor(o)), Columns));
            foreach (var child in Children) child.Measure(new Size(CellWidth * 2, CellHeight * 3));
            return new Size(width, rows * (CellHeight + Gap) - Gap + 24);
        }
        protected override Size ArrangeOverride(Size final)
        {
            Columns = DeskLayout.ColumnsFor(final.Width); CellWidth = final.Width / Columns;
            Placements = Owner?.expanded is { } open
                ? new List<DeskPlacement> { new(open, 0, 0, Columns, 99) } // Roomy, since it has the whole desk.
                : DeskLayout.Pack((Provisional ?? Owner?.order ?? new()).Select(o => Owner!.SpecFor(o)), Columns);
            targets.Clear();
            foreach (var p in Placements)
            {
                if (Children.FirstOrDefault(c => (c.Tag as DeskTileKind)?.Id == p.Id) is not { } tile) continue;
                var rect = Owner?.expanded is not null ? new Rect(Gap / 2, Gap / 2, Math.Max(1, final.Width - Gap), Math.Max(1, final.Height - Gap))
                    : new Rect(p.Column * CellWidth + Gap / 2, p.Row * (CellHeight + Gap) + Gap / 2, p.ColumnSpan * CellWidth - Gap, p.RowSpan * (CellHeight + Gap) - Gap);
                targets[p.Id] = rect;
                if (!glides.TryGetValue(p.Id, out var glide)) glides[p.Id] = glide = new Glide { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height };
                if (p.Id == Dragged)
                {
                    // The tile in hand sticks to the pointer, and its spring starts from there so letting go carries on smoothly.
                    var held = rect.Translate(DragOffset); glide.X = held.X; glide.Y = held.Y; glide.W = held.Width; glide.H = held.Height; glide.VX = glide.VY = glide.VW = glide.VH = 0;
                    if (Dragged != lifted) { lifted = Dragged; lastX = DragOffset.X; }
                    tilt += (Math.Clamp((DragOffset.X - lastX) * .35, -3.5, 3.5) - tilt) * .4; lastX = DragOffset.X; liftTarget = Lively ? 1 : 0; // The lift eases in, in Step, so picking a tile up does not pop.
                }
                else if (!Lively) { glide.X = rect.X; glide.Y = rect.Y; glide.W = rect.Width; glide.H = rect.Height; }
                tile.Arrange(new Rect(glide.X, glide.Y, Math.Max(1, glide.W), Math.Max(1, glide.H)));
                Owner?.Fill(p.Id, p.Form);
            }
            ApplyLift();
            if (Lively && (Dragged is not null || Unsettled() || lift > .01) && !timer.IsEnabled) timer.Start();
            return final;
        }
        private bool Unsettled() => targets.Any(t => glides.TryGetValue(t.Key, out var g) && (Math.Abs(g.X - t.Value.X) > .4 || Math.Abs(g.Y - t.Value.Y) > .4 || Math.Abs(g.W - t.Value.Width) > .4 || Math.Abs(g.H - t.Value.Height) > .4 || Math.Abs(g.VX) + Math.Abs(g.VY) > .4));
        private static void Spring(ref double pos, ref double velocity, double target)
        {
            velocity = (velocity + (target - pos) * .16) * .72; pos += velocity;
            if (Math.Abs(target - pos) < .3 && Math.Abs(velocity) < .3) { pos = target; velocity = 0; }
        }
        private void Step()
        {
            foreach (var (id, rect) in targets)
            {
                if (id == Dragged || !glides.TryGetValue(id, out var g)) continue;
                Spring(ref g.X, ref g.VX, rect.X); Spring(ref g.Y, ref g.VY, rect.Y); Spring(ref g.W, ref g.VW, rect.Width); Spring(ref g.H, ref g.VH, rect.Height);
            }
            if (Dragged is null) { lift *= .8; tilt *= .8; if (lift < .01) { lift = 0; tilt = 0; } }
            else { lift += (liftTarget - lift) * .2; if (liftTarget - lift < .01) lift = liftTarget; }
            InvalidateArrange();
            if (Dragged is null && lift == 0 && !Unsettled()) { timer.Stop(); lifted = null; ApplyLift(); }
        }
        // The held tile grows a little and leans the way it is being carried, and sits above its neighbours until it has settled.
        private void ApplyLift()
        {
            foreach (var child in Children)
            {
                var held = lifted is not null && (child.Tag as DeskTileKind)?.Id == lifted && (lift > 0 || Dragged == lifted);
                child.ZIndex = held ? 10 : 0; child.Opacity = held ? .97 : 1;
                child.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
                child.RenderTransform = held ? new TransformGroup { Children = { new ScaleTransform(1 + .025 * lift, 1 + .025 * lift), new RotateTransform(tilt * lift) } } : null;
            }
        }
    }
}

/// <summary>A faint field of dots in the theme's line colour, drawn behind the desk's tiles.</summary>
public sealed class DotField : Control
{
    public DotField() { IsHitTestVisible = false; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var brush = new SolidColorBrush(Ui.ThemeColour(this, "Line", Colors.LightGray), .55);
        for (var x = 12.0; x < Bounds.Width; x += 24) for (var y = 12.0; y < Bounds.Height; y += 24) context.DrawEllipse(brush, null, new Point(x, y), 1.1, 1.1);
    }
}
