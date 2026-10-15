using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Timer.Core;
using Timer.Desk;

namespace Timer.Desktop;

/// <summary>What a desk tile is: its title, its soft hue (decoration only, never a meaning colour), the cells it asks for and the page it opens in full.</summary>
public sealed record DeskTileKind(string Id, string Title, string Hue, int Columns, int Rows, string? OpensPage, int MinColumns = 1, int MinRows = 1, int MaxColumns = 4, int MaxRows = 8);

/// <summary>
/// The desk: small tiles that flow to fill the window and can be picked up by their heading and put down elsewhere.
/// Only the order is saved, because the arrangement follows from it at whatever width the window has.
/// </summary>
public sealed class DeskView : UserControl
{
    public const double CellHeight = 100, Gap = 12;
    public static readonly IReadOnlyList<DeskTileKind> Kinds = new DeskTileKind[]
    {
        new("working", "Time tracker", "#7FD1C7", 2, 2, "timeline", 1, 1, 4, 3),
        new("week", "This week", "#E8C48C", 2, 2, "overview", 1, 1, 4, 4),
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
    private readonly string? single;
    private readonly Dictionary<string, Border> floatOverlays = new();
    private (int Columns, int Rows) floatCells = (2, 2);
    /// <summary>Raised as a floating widget is resized, with its new size in cells, so its window can follow.</summary>
    public event Action<int, int>? CellsChanged;
    private readonly DeskBoard board = new();
    private readonly Dictionary<string, (Border Tile, Border Body, string? Variant)> tiles = new();
    private readonly AnalogueClock analogue; private readonly DayStrip strip;
    private readonly TextBlock digital = new() { Name = "ClockDigital", FontSize = 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "figures" } };
    private AccountChips? accountChips; private DayView? dayView;
    private readonly TextBlock stripTime = new() { Name = "ClockStripTime", FontSize = 40, FontWeight = FontWeight.Bold, Classes = { "clock" } }, clockDate = Ui.Text("", 13, "muted");
    // A control can sit in one place at a time, so a tile that rebuilds its layout first takes the shared pieces out of the old one.
    private static T Free<T>(T control) where T : Control
    {
        switch (control.Parent) { case Panel panel: panel.Children.Remove(control); break; case Decorator decorator: decorator.Child = null; break; case ContentControl content: content.Content = null; break; }
        return control;
    }
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
    private string? dragged; private Point dragStart; private DeskPlacement? dragOrigin;
    // Once the desk has been arranged every visible tile keeps its own cell, so a gap left on purpose stays a gap. Empty means tiles simply flow in order.
    private Dictionary<string, (int Column, int Row)> pins;
    private Dictionary<string, (int Column, int Row)>? provisionalPins;
    private readonly Dictionary<string, (int Columns, int Rows)> sizes;
    // The size a list tile opens at when its size has never been chosen: tall enough for what it holds. It is worked out once, as the desk is built, and is
    // never saved, so a size set by hand (which is) always wins and a tile does not change size under you as items come and go.
    private readonly Dictionary<string, (int Columns, int Rows)> preferred = new();
    private void WorkOutPreferredSizes()
    {
        void Fit(string id, int items, double perItem, double chrome)
        {
            if (sizes.ContainsKey(id)) return;
            var kind = Kinds.First(k => k.Id == id);
            var rows = DeskLayout.PreferredRows(items, perItem, chrome, kind.Rows, kind.MaxRows);
            if (rows != kind.Rows) preferred[id] = (kind.Columns, rows);
        }
        // Per item: a to-do or note line with its tag or chips under it, a line in Coming up, an account row. Chrome: the header band and margins, and the box for a new item.
        Fit("todo", model.Todos.Count, 46, 150); Fit("notes", model.NotesOn(model.SelectedDate).Count + 1, 52, 150);
        Fit("coming", model.ComingUp().Count, 40, 100); Fit("accounts", model.Accounts.Count(a => !BreakAccount.Is(a.Name)), 46, 90);
    }
    private string? resizing; private (int Columns, int Rows) resizingSize; private Point resizeStart; private Size resizeFrom;
    private string? expanded;
    private readonly ScrollViewer scroller; private readonly DayStrip timelineStrip; private readonly Border timelineHost = new();
    private readonly Dictionary<string, Control> grips = new(); private readonly Dictionary<string, Border> bands = new(), tiles0 = new(); private bool hueDark;
    /// <summary>Gives each tile the hue made for the theme in use: pale washes in a light theme, deeper tones of the same hues in a dark one (<see cref="DeskHues"/>).</summary>
    private void ApplyHues()
    {
        hueDark = model.CurrentTheme.Dark;
        foreach (var (id, tile) in tiles0)
        {
            if (IsPostIt(id)) continue; // Paper keeps its own colour.
            var wash = Color.Parse(DeskHues.Wash(id, hueDark));
            tile.Background = new SolidColorBrush(Color.FromArgb(DeskHues.WashAlphaFor(hueDark), wash.R, wash.G, wash.B));
            if (bands.TryGetValue(id, out var band)) band.Background = new SolidColorBrush(Color.FromArgb(DeskHues.BandAlphaFor(hueDark), wash.R, wash.G, wash.B));
        }
    } private readonly Dictionary<string, Border> tapes = new(); private readonly Dictionary<string, Border> slots = new(); private readonly Dictionary<string, Action<bool, bool>> slimSetters = new(); private readonly Dictionary<string, TextBlock> metas = new();
    /// <summary>Raised when a tile is opened in place or put back, with the tile's id or null, so the window can show the controls that tile needs.</summary>
    public event Action<string?>? ExpandedChanged;

    /// <summary>
    /// The desk, or with <paramref name="only"/> a single tile on its own for a floating widget: the same tile, built by the same code and showing
    /// the same version of itself for its size in cells, with no board, drawer or arranging around it.
    /// </summary>
    public DeskView(TimerModel model, Action<string>? openPage = null, string? only = null)
    {
        this.model = model; this.openPage = openPage; single = only;
        analogue = new AnalogueClock(() => model.Engine.Now); strip = new DayStrip(model); timelineStrip = new DayStrip(model);
        sizes = new Dictionary<string, (int Columns, int Rows)>(model.DeskSizes);
        var known = Kinds.Select(k => k.Id).ToList();
        if (single is null) WorkOutPreferredSizes();
        hidden = model.DeskHidden.Where(known.Contains).Distinct().ToList();
        pins = model.DeskPositions.Where(p => known.Contains(p.Key) && !hidden.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        // A desk that has never been arranged starts with one post-it out and the spare two in the drawer.
        if (model.DeskOrder.Count == 0 && model.DeskHidden.Count == 0) hidden = new List<string> { "postit2", "postit3" };
        order = DeskLayout.Reconcile(model.DeskOrder.Where(id => !hidden.Contains(id)), known.Where(id => !hidden.Contains(id)).ToList());
        foreach (var kind in Kinds)
        {
            if (single is not null && kind.Id != single) continue;
            var tile = BuildTile(kind); tiles[kind.Id] = tile; if (single is null) board.Children.Add(tile.Tile);
        }
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
        // Arrange mode turns the desk's editing controls on, and the Drawer button comes with it; the window sets both in its header.
        arrangeButton = Ui.Button("Arrange", () => SetArranging(!arranging), "ArrangeToggle");
        arrangeButton.Classes.Add("toolButton"); arrangeButton.Classes.Add("quiet"); arrangeButton.Height = 30; arrangeButton.Padding = new Thickness(12, 0);
        // Tidy up closes the gaps by letting the tiles flow in reading order again. It is offered only while arranging, and only when there is something pinned to undo.
        tidyButton = Ui.Button("Tidy up", TidyUp, "TidyDesk"); ToolTip.SetTip(tidyButton, "Close the gaps");
        tidyButton.Classes.Add("toolButton"); tidyButton.Classes.Add("quiet"); tidyButton.Height = 30; tidyButton.Padding = new Thickness(12, 0); tidyButton.IsVisible = false;
        // Layouts keeps named arrangements: apply one, save the desk as it is now, or delete one. Offered only while arranging, like the other editing controls.
        layoutsButton = Ui.Button("Layouts", () => { }, "LayoutsToggle"); layoutsButton.Classes.Add("toolButton"); layoutsButton.Classes.Add("quiet"); layoutsButton.Height = 30; layoutsButton.Padding = new Thickness(12, 0); layoutsButton.IsVisible = false;
        layoutsFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight }; layoutsFlyout.Opening += (_, _) => layoutsFlyout.Content = BuildLayoutsPanel(); layoutsButton.Flyout = layoutsFlyout;
        drawerToggle.IsVisible = false; HeaderTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { layoutsButton, tidyButton, drawerToggle, arrangeButton } };
        // The Drawer button is handed to the window, which sets it in the header beside the title, so the board gets the room a row of its own would take.
        var frame = Ui.Grid("*", "*");
        scroller = new ScrollViewer { Content = board, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false };
        // An opened tile fills the room the board has, so the board needs to know it.
        scroller.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty && expanded is not null) board.InvalidateMeasure(); };
        // A faint field of dots sits behind the tiles, so the desk reads as a surface rather than a blank page.
        Ui.Put(frame, scroller);
        Ui.Put(frame, drawer);
        Content = frame;
        ApplyHues();
        if (single is not null) { Content = tiles[single].Tile; }
        else { SyncDrawer(); RefreshFloating(); }
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
    /// <summary>Shows a single-tile desk at a size in cells, which decides the version of the tile it shows (as the desk's own placement does).</summary>
    public void SetCells(int columns, int rows)
    {
        if (single is null) return;
        var kind = Kinds.First(k => k.Id == single);
        var (c, r) = DeskLayout.ClampSize(columns, rows, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, 12);
        floatCells = (c, r);
        Fill(single, new DeskPlacement(single, 0, 0, c, r));
    }
    /// <summary>The size in cells a tile would float at: the size it has on the desk.</summary>
    public (int Columns, int Rows) CellsFor(string id) => SizeOf(id);
    /// <summary>The window size, in pixels, a tile of this many cells takes when floating (without the room for its shadow).</summary>
    public static Size PixelsFor(int columns, int rows) => new(columns * DeskLayout.CellWidth - Gap, rows * (CellHeight + Gap) - Gap);
    /// <summary>The tiles whose cells are on the desk but whose content is floating in a window of its own show a quiet placeholder instead.</summary>
    private void RefreshFloating() { foreach (var (id, overlay) in floatOverlays) overlay.IsVisible = model.IsFloating(id); }
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
        var (c, r) = resizing == id ? resizingSize : sizes.TryGetValue(id, out var saved) ? saved : preferred.TryGetValue(id, out var fits) ? fits : (kind.Columns, kind.Rows);
        return DeskLayout.ClampSize(c, r, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, board.Columns);
    }
    internal DeskTileSpec SpecFor(string id) { var (c, r) = SizeOf(id); return new(id, c, r); }
    /// <summary>Where each of these tiles sits at the current width: at its own cell once arranged (the tile being moved or resized first, so the others make way), otherwise flowing in order.</summary>
    internal List<DeskPlacement> PackFor(IEnumerable<string> ids)
    {
        var specs = ids.Select(SpecFor).ToList(); var use = provisionalPins ?? pins;
        return use.Count == 0 ? DeskLayout.Pack(specs, board.Columns) : DeskLayout.PackPinned(specs, use, board.Columns, dragged ?? resizing ?? lastMoved);
    }
    private string? lastMoved;
    /// <summary>Fixes every visible tile where it is now, so what follows moves one tile and leaves the rest alone.</summary>
    private void PinAll() { if (pins.Count == 0) foreach (var p in PackFor(order)) pins[p.Id] = (p.Column, p.Row); }
    /// <summary>Settles the layout after a change: the moved tile keeps its place, the tiles it landed on make way, and the result is what gets saved.</summary>
    private void Settle(string? moved)
    {
        lastMoved = moved;
        var placed = PackFor(order); lastMoved = null;
        pins = placed.ToDictionary(p => p.Id, p => (p.Column, p.Row));
        // The order follows what the eye reads, so Tidy up and an older build flow the tiles the way they look.
        order = placed.OrderBy(p => p.Row).ThenBy(p => p.Column).Select(p => p.Id).ToList();
        model.SetDeskOrder(order); model.SetDeskPositions(pins);
        tidyButton.IsVisible = arranging && pins.Count > 0;
        board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>Lets the tiles flow in reading order again, closing every gap.</summary>
    public void TidyUp()
    {
        if (pins.Count == 0) return;
        var placed = PackFor(order);
        order = placed.OrderBy(p => p.Row).ThenBy(p => p.Column).Select(p => p.Id).ToList();
        pins = new(); provisionalPins = null; model.SetDeskOrder(order); model.SetDeskPositions(pins); tidyButton.IsVisible = false;
        board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>The desk as it is now, as a layout that can be saved by name. It holds the arrangement only, never what is inside the tiles.</summary>
    public SavedLayout CaptureLayout(string name)
        => new(name, order.ToList(), hidden.ToList(), DeskLayout.FormatSizes(sizes), DeskLayout.FormatPositions(pins));
    /// <summary>Brings a saved layout onto the desk. Tiles the layout does not know about are put at the end, and ones that no longer exist are ignored, so an old layout still works.</summary>
    public void ApplyLayout(SavedLayout layout)
    {
        Collapse();
        var known = Kinds.Select(k => k.Id).ToList();
        hidden = layout.Hidden.Where(known.Contains).Distinct().ToList();
        order = DeskLayout.Reconcile(layout.Order.Where(id => !hidden.Contains(id)), known.Where(id => !hidden.Contains(id)).ToList());
        sizes.Clear(); foreach (var (id, size) in DeskLayout.ParseSizes(layout.Sizes)) if (known.Contains(id)) sizes[id] = size;
        pins = DeskLayout.ParsePositions(layout.Positions).Where(p => known.Contains(p.Key) && !hidden.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        provisionalPins = null; model.SetDeskSizes(sizes); Save(); SyncDrawer();
        tidyButton.IsVisible = arranging && pins.Count > 0; board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>
    /// Three ready-made arrangements to start from: Focus (the tracker, to-do, a countdown and a post-it), Planning (the timeline with notes and what is coming)
    /// and Everything (every tile at its usual size). They are not saved layouts, so they cannot be deleted or counted against the twelve.
    /// </summary>
    public static readonly IReadOnlyList<SavedLayout> Starters = new[]
    {
        StarterLayout("Focus", ("working", 2, 2), ("todo", 2, 3), ("pomodoro", 2, 2), ("deadline", 2, 1), ("postit", 2, 2)),
        StarterLayout("Planning", ("timeline", 3, 4), ("notes", 2, 3), ("coming", 2, 2), ("deadline", 2, 2), ("day", 2, 1)),
        StarterLayout("Everything", Kinds.Where(k => k.Id is not ("postit2" or "postit3")).Select(k => (k.Id, k.Columns, k.Rows)).ToArray()),
    };
    private static SavedLayout StarterLayout(string name, params (string Id, int Columns, int Rows)[] tiles)
    {
        var shown = tiles.Select(t => t.Id).ToList();
        // Sizes are written only where they differ from the tile's own, so a starter never fights a size the person likes by default.
        var sizes = tiles.Where(t => Kinds.First(k => k.Id == t.Id) is var k && (k.Columns, k.Rows) != (t.Columns, t.Rows)).ToDictionary(t => t.Id, t => (t.Columns, t.Rows));
        return new SavedLayout(name, shown, Kinds.Select(k => k.Id).Where(id => !shown.Contains(id)).ToList(), DeskLayout.FormatSizes(sizes), Array.Empty<string>());
    }
    public Control BuildLayoutsPanel()
    {
        var panel = new StackPanel { Name = "LayoutsPanel", Spacing = 6, Width = 260 };
        panel.Children.Add(Ui.Text("Start from", 12, "muted"));
        var starters = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var starter in Starters)
        {
            var apply = Ui.Button(starter.Name, () => { ApplyLayout(starter); layoutsFlyout.Hide(); }, "LayoutStarter_" + starter.Name); apply.Classes.Add("toolButton"); apply.Classes.Add("quiet"); apply.Margin = new Thickness(0, 0, 6, 6);
            starters.Children.Add(apply);
        }
        panel.Children.Add(starters);
        panel.Children.Add(Ui.Text("Saved layouts", 12, "muted"));
        if (model.DeskLayouts.Count == 0) panel.Children.Add(Ui.Text("None yet. Arrange the desk, then save it here.", 12, "muted"));
        foreach (var layout in model.DeskLayouts)
        {
            var name = layout.Name; var row = Ui.Grid("*,Auto");
            var apply = Ui.Button(name, () => { ApplyLayout(layout); layoutsFlyout.Hide(); }, "LayoutApply_" + name); apply.Classes.Add("toolButton"); apply.Classes.Add("quiet"); apply.HorizontalAlignment = HorizontalAlignment.Stretch; apply.HorizontalContentAlignment = HorizontalAlignment.Left;
            var bin = Ui.IconButton(IconKind.Trash, "Delete this layout", "LayoutDelete_" + name, () => { model.DeleteDeskLayout(name); layoutsFlyout.Content = BuildLayoutsPanel(); }); bin.Width = 28; bin.Height = 28;
            Ui.Put(row, apply); Ui.Put(row, bin, 1); panel.Children.Add(row);
        }
        var input = new TextBox { Name = "LayoutName", Watermark = "Name for this layout", MaxLength = DeskLayouts.MaxNameLength, Margin = new Thickness(0, 6, 0, 0) };
        void Save() { var typed = DeskLayouts.CleanName(input.Text); if (typed.Length == 0) return; model.SaveDeskLayout(CaptureLayout(typed)); layoutsFlyout.Content = BuildLayoutsPanel(); }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Save(); } };
        panel.Children.Add(input);
        var save = Ui.DialogButton("Save current desk", Save, "LayoutSave"); panel.Children.Add(save);
        if (model.DeskLayouts.Count >= DeskLayouts.MaxLayouts) panel.Children.Add(Ui.Text("Saving under a new name needs one deleted first.", 12, "muted"));
        return panel;
    }
    /// <summary>The cell a tile has been put in, or null while the desk still flows in order.</summary>
    public (int Column, int Row)? PinOf(string id) => pins.TryGetValue(id, out var cell) ? cell : null;
    public bool HasGaps => pins.Count > 0;
    /// <summary>Makes a tile larger or smaller by whole cells, within what suits it, and remembers the size.</summary>
    public void Resize(string id, int columns, int rows)
    {
        var kind = Kinds.First(k => k.Id == id);
        if (hidden.Contains(id)) { sizes[id] = DeskLayout.ClampSize(columns, rows, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, 12); model.SetDeskSizes(sizes); return; }
        PinAll(); // Everything else stays where it is, and only what the larger tile lands on makes way.
        sizes[id] = DeskLayout.ClampSize(columns, rows, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, 12);
        model.SetDeskSizes(sizes); Settle(id);
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
        if (model.CurrentTheme.Dark != hueDark) ApplyHues();
        if (single is null && e.PropertyName != nameof(TimerModel.Elapsed)) RefreshFloating();
        RefreshLive();
        // The rows are redrawn only when something other than the clock changed, so a tick never interrupts the to-do list.
        if (e.PropertyName != nameof(TimerModel.Elapsed)) { RebuildTimeline(); todoView?.Rebuild(); notesView?.Rebuild(); comingView?.Rebuild(); PaintPostIt(); }
    }
    /// <summary>Redraws the timeline tile, keeping where it was scrolled to, as the dashboard does for its own timeline.</summary>
    private void RebuildTimeline()
    {
        if (!tiles.TryGetValue("timeline", out var t) || tiles["timeline"].Variant is null or "tiny") return;
        var old = timelineHost.Child as Control;
        var scroll = old is ScrollViewer direct ? direct : old?.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = model.TimelineOffset is double fromModel ? new Vector(0, fromModel) : scroll?.Tag is Vector pending ? pending : scroll?.Offset;
        var fresh = ReviewViews.Timeline(model);
        if (offset is { } kept) { var next = fresh is ScrollViewer d ? d : fresh.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault(); if (next is not null) next.Tag = kept; }
        timelineHost.Child = fresh; Ui.OwnWheel(fresh);
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
        var blocks = strip.Blocks().Count; var todos = model.Todos;
        Set("strip", $"{blocks} block{(blocks == 1 ? "" : "s")}"); Set("timeline", $"{blocks} block{(blocks == 1 ? "" : "s")}");
        Set("todo", todos.Count == 0 ? "" : $"{todos.Count(t => t.Done)} of {todos.Count} done");
        var coming = model.ComingUp().Count; Set("coming", coming == 0 ? "" : $"{coming} to come");
        var notes = model.NotesOn(model.SelectedDate).Count; Set("notes", notes == 0 ? "" : $"{notes} note{(notes == 1 ? "" : "s")}");
        Set("accounts", $"{model.Accounts.Count(a => !BreakAccount.Is(a.Name))} accounts");
        if (weekChart is not null) Set("week", model.Format(weekChart.Total()));
        { var f = DayFigures.For(model); Set("day", f.Fraction is { } share ? $"{share * 100:0}%" : ""); }
        Set("pomodoro", model.Pomodoro.Completed == 0 ? "" : $"{model.Pomodoro.Completed} done");
    }
    private static bool IsPostIt(string id) => id.StartsWith("postit", StringComparison.Ordinal);
    private static int PostItIndex(string id) => id == "postit" ? 0 : int.Parse(id[6..]) - 1;
    private void PaintPostIt()
    {
        foreach (var (id, t) in tiles.Where(t => IsPostIt(t.Key)))
            { var note = model.Trinkets.PostIts[PostItIndex(id)]; t.Tile.Background = new SolidColorBrush(PostItView.Paper(note.Colour)); if (tapes.TryGetValue(id, out var tape)) tape.IsVisible = note.Tape; }
    }
    private void RefreshLive()
    {
        var now = model.Engine.Now;
        analogue.InvalidateVisual(); digital.Text = now.ToString("HH:mm"); stripTime.Text = digital.Text; clockDate.Text = now.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        accountChips?.Refresh(); dayView?.Refresh(); strip.InvalidateVisual(); pomodoroView?.Refresh(); trackerView?.Refresh(); deadlineView?.Refresh(); weekChart?.InvalidateVisual(); RefreshMeta();
    }

    // What changes when the desk goes into arrange mode, kept per tile so every tile follows the same rules.
    private readonly Dictionary<string, Action> chrome = new();
    private bool arranging;
    /// <summary>Whether the desk is being rearranged. At rest the tiles show no grips, resize corners or put-away crosses, so the desk feels settled.</summary>
    public bool Arranging => arranging;
    public Panel HeaderTools { get; }
    private readonly Button arrangeButton, tidyButton, layoutsButton; private readonly Flyout layoutsFlyout;
    public void SetArranging(bool on)
    {
        if (on == arranging) return;
        arranging = on; if (!on) { SetDrawer(false); CancelDrag(); CancelResize(); }
        arrangeButton.Content = on ? "Done" : "Arrange"; arrangeButton.Classes.Set("mainAction", on); drawerToggle.IsVisible = on; layoutsButton.IsVisible = on; tidyButton.IsVisible = on && pins.Count > 0;
        foreach (var refresh in chrome.Values) refresh();
    }
    private static double RestAngle(string id) => id switch { "postit" => -1.6, "postit2" => 1.3, "postit3" => -0.8, _ => 0 };

    private (Border Tile, Border Body, string? Variant) BuildTile(DeskTileKind kind)
    {
        var tile = new Border { Name = "Tile_" + kind.Id, CornerRadius = new CornerRadius(IsPostIt(kind.Id) ? 8 : 22), ClipToBounds = !IsPostIt(kind.Id), Tag = kind };
        var hue = Color.Parse(kind.Hue); var post = IsPostIt(kind.Id);
        // A wash of the tile's hue over the surface, set by ApplyHues for the theme in use, with a soft shadow to lift the tile off the desk.
        tiles0[kind.Id] = tile;
        tile.BoxShadow = BoxShadows.Parse(post ? "0 6 16 0 #34000000" : "0 4 14 0 #18000000");
        var hovered = false;
        var buttons = Ui.Horizontal(); buttons.Spacing = 2; buttons.Opacity = 0; buttons.IsHitTestVisible = false;
        // A floating widget has only the buttons that suit a window of its own: its own, and Dock to put it back on the desk.
        if (single is null && kind.OpensPage is { } page)
        {
            var open = Ui.IconButton(IconKind.Next, "Open full page", "TileOpen_" + kind.Id, () => openPage?.Invoke(page)); open.Width = 28; open.Height = 28; buttons.Children.Add(open);
        }
        if (single is null)
        {
            var expand = Ui.IconButton(IconKind.FitDay, "Open in place", "TileExpand_" + kind.Id, () => { if (expanded == kind.Id) Collapse(); else Expand(kind.Id); }); expand.Width = 28; expand.Height = 28; buttons.Children.Add(expand);
            var lift = Ui.IconButton(IconKind.Float, "Float on screen", "TileFloat_" + kind.Id, () => { var (c, r) = SizeOf(kind.Id); model.FloatWidget(kind.Id, c, r); }); lift.Width = 28; lift.Height = 28; buttons.Children.Add(lift);
        }
        else
        {
            // The full page opens in the dashboard, which comes up for it, since a floating widget has no dashboard around it.
            if (kind.OpensPage is { } fullPage) { var open = Ui.IconButton(IconKind.Next, "Open full page", "TileOpen_" + kind.Id, () => model.RequestPage(fullPage)); open.Width = 28; open.Height = 28; buttons.Children.Add(open); }
            var dock = Ui.IconButton(IconKind.Dock, "Back on the desk", "TileDock_" + kind.Id, () => model.DockWidget(kind.Id)); dock.Width = 28; dock.Height = 28; buttons.Children.Add(dock);
        }
        var put = Ui.IconButton(IconKind.Close, "Put away in the drawer", "TileHide_" + kind.Id, () => Hide(kind.Id)); put.Width = 28; put.Height = 28; put.IsVisible = false; if (single is null) buttons.Children.Add(put);
        if (post)
        {
            // A button changes the paper's colour.
            var paint = Ui.IconButton(IconKind.Edit, "Change colour", "PostItColour" + (kind.Id == "postit" ? "" : (PostItIndex(kind.Id) + 1).ToString()), () => model.CyclePostItColour(PostItIndex(kind.Id))); paint.Width = 28; paint.Height = 28; buttons.Children.Insert(0, paint);
            var tapeButton = Ui.IconButton(IconKind.Copy, "Tape on or off", "PostItTape" + (kind.Id == "postit" ? "" : (PostItIndex(kind.Id) + 1).ToString()), () => model.TogglePostItTape(PostItIndex(kind.Id))); tapeButton.Width = 28; tapeButton.Height = 28; buttons.Children.Insert(1, tapeButton);
        }
        // A grip in the bottom right corner makes the tile larger or smaller by whole cells. It shows only while arranging.
        var grip = new Border { Name = "TileResize_" + kind.Id, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner), IsVisible = false,
            Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M15,5 L5,15 M15,10 L10,15"), Stroke = new SolidColorBrush(Colors.Gray), StrokeThickness = 1.6, Opacity = .6, StrokeLineCap = PenLineCap.Round, Margin = new Thickness(2) } };
        if (single is not null) grip.IsVisible = true; // A floating widget can always be resized, in whole cells like a tile on the desk.
        grip.PointerPressed += (_, e) => BeginResize(kind.Id, e, tile);
        grip.PointerMoved += (_, e) => ResizeTo(e);
        grip.PointerReleased += (_, e) => EndResize(e);
        grip.PointerCaptureLost += (_, _) => CancelResize();
        grips[kind.Id] = grip;
        Border body; Control content; Border handle; Control? meta = null; IconGlyph? dots = null;
        var layers = new Grid();
        if (post)
        {
            // A post-it is an informal object, not a structured tile: no header band, a strip of tape across the top and a slight tilt. The top of the paper is its handle.
            handle = new Border { Name = "TileHandle_" + kind.Id, Height = 34, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
            buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.VerticalAlignment = VerticalAlignment.Top; buttons.Margin = new Thickness(0, 4, 6, 0);
            var tape = new Border { Width = 84, Height = 22, Background = new SolidColorBrush(Colors.White, .55), BorderBrush = new SolidColorBrush(Colors.Black, .06), BorderThickness = new Thickness(0, 0, 0, 1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -9, 0, 0), RenderTransform = new RotateTransform(-3), IsHitTestVisible = false };
            tapes[kind.Id] = tape; body = new Border { Margin = new Thickness(18, 22, 18, 16) };
            var paper = new Grid(); paper.Children.Add(body); paper.Children.Add(handle); paper.Children.Add(buttons); paper.Children.Add(tape);
            content = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Child = paper };
        }
        else
        {
            // The header: a round badge in the tile's hue with its icon, the title, a quiet figure, and the grip to pick the tile up by (arrange mode only).
            var badge = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Child = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(hue), VerticalAlignment = VerticalAlignment.Center,
                Child = new IconGlyph(IconFor(kind.Id)) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
            var heading = new TextBlock { Text = kind.Title, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var figure = Ui.Text("", 13, "muted"); figure.VerticalAlignment = VerticalAlignment.Center; figure.Name = "TileMeta_" + kind.Id; metas[kind.Id] = figure; meta = figure;
            // A figure that is the point of the tile (the week's total) is set bold in the text colour; the rest stay quiet.
            if (kind.Id == "week") { figure.Classes.Remove("muted"); figure.FontSize = 18; figure.FontWeight = FontWeight.Bold; figure.Classes.Add("clock"); }
            var head = Ui.Grid("Auto,Auto,*,Auto,Auto"); head.ColumnSpacing = 10; head.Margin = new Thickness(14, 0, 8, 0); head.VerticalAlignment = VerticalAlignment.Center; head.Background = Brushes.Transparent; head.Name = "TileHandle_" + kind.Id; // Centred in its band, whose height is fixed.
            handle = new Border { Child = head, Background = Brushes.Transparent }; // The drag target is the whole header.
            Ui.Put(head, badge); Ui.Put(head, heading, 1);
            // In a slim tile, one row high, the content sits here in the header's own row instead of under it.
            var slot = new Border { Name = "TileInline_" + kind.Id, IsVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right }; Ui.Put(head, slot, 2); slots[kind.Id] = slot;
            // The figure and the buttons share one cell at the right, the figure at rest and the buttons under the pointer, so the header's right edge
            // is the same on every tile whatever it holds and the grip always sits at the same distance from the corner.
            figure.HorizontalAlignment = HorizontalAlignment.Right; buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.VerticalAlignment = VerticalAlignment.Center;
            var cluster = new Grid { VerticalAlignment = VerticalAlignment.Center }; cluster.Children.Add(figure); cluster.Children.Add(buttons); Ui.Put(head, cluster, 3);
            dots = new IconGlyph(IconKind.Grip) { TintKey = "Muted", VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, IsVisible = false }; Ui.Put(head, dots, 4);
            var band = new Border { Height = 52, Child = handle }; bands[kind.Id] = band;
            body = new Border { Margin = new Thickness(16, 10, 16, 14) };
            var stack = Ui.Grid("*", "Auto,*"); Ui.Put(stack, band); Ui.Put(stack, body, row: 1);
            content = stack;
            // A tile one row high has no room for a header and a body, so it becomes a slim bar: the content moves up beside the title (or beside the badge alone when one column wide),
            // and the hover buttons float over its corner instead of holding a place in the row.
            slimSetters[kind.Id] = (slim, titled) =>
            {
                band.Height = slim ? double.NaN : 52; stack.RowDefinitions = new RowDefinitions(slim ? "*,0" : "Auto,*");
                body.IsVisible = !slim; slot.IsVisible = slim; heading.IsVisible = !slim || titled; figure.IsVisible = !slim;
                if (slim && cluster.Parent == head) { head.Children.Remove(cluster); Grid.SetColumn(cluster, 0); cluster.HorizontalAlignment = HorizontalAlignment.Right; cluster.VerticalAlignment = VerticalAlignment.Top; cluster.Margin = new Thickness(0, 6, 6, 0); layers.Children.Add(cluster); }
                else if (!slim && cluster.Parent == layers) { layers.Children.Remove(cluster); cluster.Margin = default; cluster.HorizontalAlignment = HorizontalAlignment.Stretch; cluster.VerticalAlignment = VerticalAlignment.Center; Ui.Put(head, cluster, 3); }
            };
        }
        layers.Children.Insert(0, content); layers.Children.Add(grip);
        if (single is null)
        {
            // While the tile floats, its place on the desk is kept and says so, with a way back.
            var away = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            away.Children.Add(Ui.Text("Floating on screen", 13, "muted"));
            var dockHere = Ui.DialogButton("Dock", () => model.DockWidget(kind.Id), "TileDockHere_" + kind.Id); dockHere.HorizontalAlignment = HorizontalAlignment.Center; dockHere.Height = 30; away.Children.Add(dockHere);
            var overlay = new Border { Name = "TileFloating_" + kind.Id, IsVisible = false, CornerRadius = new CornerRadius(post ? 8 : 22), Child = away }; overlay.Classes.Add("panel");
            layers.Children.Add(overlay); floatOverlays[kind.Id] = overlay;
        }
        tile.Child = layers;
        void Refresh()
        {
            var shown = arranging || hovered;
            buttons.Opacity = shown ? 1 : 0; buttons.IsHitTestVisible = shown; if (meta is not null) meta.Opacity = shown ? 0 : 1;
            put.IsVisible = arranging; grip.IsVisible = single is not null || (arranging && expanded is null); if (dots is not null) dots.IsVisible = arranging;
            handle.Cursor = arranging ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            tile.BorderBrush = arranging ? new SolidColorBrush(Colors.Gray, .45) : null; tile.BorderThickness = arranging ? new Thickness(1.5) : default;
        }
        chrome[kind.Id] = Refresh;
        tile.PointerEntered += (_, _) => { hovered = true; Refresh(); };
        tile.PointerExited += (_, _) => { if (resizing is null && dragged is null) { hovered = false; Refresh(); } };
        handle.PointerPressed += (_, e) =>
        {
            // A floating widget is carried by its header, as the pill is, with the window moving rather than a tile on the board.
            if (single is not null)
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window) { Ui.CloseMenus(window); window.BeginMoveDrag(e); e.Handled = true; }
                return;
            }
            BeginDrag(kind.Id, e, tile);
        };
        handle.PointerMoved += (_, e) => DragTo(e);
        handle.PointerReleased += (_, e) => EndDrag(e);
        handle.PointerCaptureLost += (_, _) => CancelDrag();
        return (tile, body, null);
    }

    /// <summary>Fills a tile for the room it has. A small form keeps only what is essential.</summary>
    /// <summary>
    /// What a tile shows follows from the room it has, not just how much of it: a clock that is one row high becomes a digital strip, a deadline that is
    /// one cell becomes a bare countdown. The key names which version of the tile fits the placement, and the tile is redrawn only when it changes.
    /// </summary>
    private static readonly HashSet<string> SlimKinds = new() { "working", "day", "deadline", "clock", "strip", "accounts", "week", "coming" };
    internal static string VariantFor(string id, DeskPlacement p)
    {
        var area = p.ColumnSpan * p.RowSpan;
        // One row high, a tile whose content is a line or two becomes a slim bar: the title and the content share the row.
        if (p.RowSpan == 1 && SlimKinds.Contains(id)) return p.ColumnSpan >= 2 ? "slim2" : "slim1";
        return id switch
        {
            "clock" => area >= 6 ? "roomy" : "face",
            "week" => area >= 9 ? "large" : p.Form.ToString().ToLowerInvariant(),
            _ => p.Form.ToString().ToLowerInvariant(),
        };
    }
    internal void Fill(string id, DeskPlacement placement)
    {
        var (tile, body, current) = tiles[id]; var variant = VariantFor(id, placement); var form = placement.Form;
        if (current == variant) return;
        tiles[id] = (tile, body, variant);
        var slim = variant.StartsWith("slim", StringComparison.Ordinal); var wide = variant == "slim2";
        if (slimSetters.TryGetValue(id, out var setSlim)) setSlim(slim, wide);
        Border target = body; if (slim) body.Child = null; // What the tile showed below its header goes when it moves up into the header row.
        if (slots.TryGetValue(id, out var inline)) { if (slim) { target = inline; inline.HorizontalAlignment = id == "strip" ? HorizontalAlignment.Stretch : HorizontalAlignment.Right; } else inline.Child = null; }
        switch (id)
        {
            case "working": target.Child = trackerView = new TrackerView(model, form, slim); break;
            case "day": target.Child = dayView = new DayView(model, form, slim); break;
            case "deadline": target.Child = deadlineView = new DeadlineView(model, form, slim); break;
            case "week":
                weekChart ??= new WeekChart(model); Free(weekChart);
                weekChart.Mode = slim ? TileForm.Tiny : form; weekChart.FullLegend = variant == "large"; weekChart.Width = slim ? (wide ? 170 : 100) : double.NaN; weekChart.Height = slim ? 56 : double.NaN;
                target.Child = weekChart; weekChart.InvalidateVisual(); break;
            case "timeline": timelineStrip.Labels = true; target.Child = form == TileForm.Tiny ? timelineStrip : timelineHost; if (form != TileForm.Tiny) RebuildTimeline(); break;
            case "pomodoro": target.Child = pomodoroView = new PomodoroView(model, form == TileForm.Tiny); break;
            case "clock":
                {
                    // One row high it is just the time beside the title; with two rows it is the face with the time under it; with more it adds the date.
                    if (slim)
                    {
                        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
                        clockDate.Margin = new Thickness(0, 0, 0, 4); clockDate.VerticalAlignment = VerticalAlignment.Bottom; line.Children.Add(Free(stripTime)); if (wide) line.Children.Add(Free(clockDate)); target.Child = line; break;
                    }
                    var roomy = variant == "roomy"; var face = Ui.Grid("*", roomy ? "*,Auto,Auto" : "*,Auto"); Ui.Put(face, Free(analogue));
                    clockDate.Margin = new Thickness(0, 6, 0, 0); clockDate.VerticalAlignment = VerticalAlignment.Center;
                    if (roomy) { clockDate.HorizontalAlignment = HorizontalAlignment.Center; Ui.Put(face, Free(clockDate), row: 1); }
                    digital.Margin = new Thickness(0, 2, 0, 0); Ui.Put(face, Free(digital), row: roomy ? 2 : 1);
                    target.Child = face; break;
                }
            case "strip": strip.Labels = !slim; strip.Height = slim ? 30 : double.NaN; strip.MinWidth = slim ? 80 : 0; strip.VerticalAlignment = slim ? VerticalAlignment.Center : VerticalAlignment.Stretch; target.Child = strip; strip.InvalidateVisual(); break;
            case "notes": target.Child = notesView = new NotesView(model, form == TileForm.Tiny); break;
            case "coming":
                comingView = new ComingUpView(model, slim || form == TileForm.Tiny);
                target.Child = slim ? comingView : new ScrollViewer { Content = comingView, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; break;
            case "todo": target.Child = todoView = new TodoView(model, form == TileForm.Tiny); break;
            case "postit": case "postit2": case "postit3":
                if (!postIts.TryGetValue(id, out var note)) postIts[id] = note = PostItView.Build(model, PostItIndex(id), false, id == "postit" ? "PostItText" : "PostItText" + (PostItIndex(id) + 1));
                target.Child = note; PaintPostIt(); break;
            case "accounts":
                // One row high the accounts are chips with their names and times (or, one cell wide, the account in hand); with room, the list, and with more, the list with budget bars.
                target.Child = slim ? accountChips = new AccountChips(model, single: !wide)
                    : new ScrollViewer { Content = new AccountList(model, reorderable: true, bars: form == TileForm.Roomy), AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                break;
        }
        // A list inside a tile keeps the wheel while the pointer is over it, so scrolling it never moves the desk behind.
        if (target.Child is Control built) Ui.OwnWheel(built);
        RefreshLive();
    }

    private void BeginResize(string id, PointerPressedEventArgs e, Control tile)
    {
        if ((single is null && !arranging) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || expanded is not null) return;
        resizing = id; resizingSize = single is not null ? floatCells : SizeOf(id); resizeStart = e.GetPosition(single is not null ? this : board); resizeFrom = tile.Bounds.Size;
        e.Pointer.Capture((IInputElement)e.Source!); e.Handled = true;
    }
    private void ResizeTo(PointerEventArgs e)
    {
        if (resizing is not { } id) return;
        var at = e.GetPosition(single is not null ? this : board);
        var (c, r) = DeskLayout.SizeFromPixels(resizeFrom.Width + at.X - resizeStart.X, resizeFrom.Height + at.Y - resizeStart.Y, single is not null ? DeskLayout.CellWidth : board.CellWidth, CellHeight, Gap);
        var kind = Kinds.First(k => k.Id == id);
        var size = DeskLayout.ClampSize(c, r, kind.MinColumns, kind.MinRows, kind.MaxColumns, kind.MaxRows, single is not null ? 12 : board.Columns);
        if (size == resizingSize) return;
        resizingSize = size;
        if (single is not null) { floatCells = size; SetCells(size.Columns, size.Rows); CellsChanged?.Invoke(size.Columns, size.Rows); return; }
        board.InvalidateMeasure(); board.InvalidateArrange();
    }
    private void EndResize(PointerReleasedEventArgs e)
    {
        if (resizing is not { } id) return;
        var size = resizingSize; e.Pointer.Capture(null); resizing = null;
        if (single is not null) return; // A floating widget has already followed the pointer, cell by cell.
        Resize(id, size.Columns, size.Rows);
    }
    private void CancelResize() { if (resizing is null) return; resizing = null; board.InvalidateMeasure(); board.InvalidateArrange(); }

    private void BeginDrag(string id, PointerPressedEventArgs e, Control tile)
    {
        if (!arranging || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; // Tiles are picked up only while arranging.
        if (TopLevel.GetTopLevel(this) is Window window) Ui.CloseMenus(window);
        dragged = id; dragStart = e.GetPosition(board); dragOrigin = board.Placements.FirstOrDefault(p => p.Id == id);
        e.Pointer.Capture((IInputElement)e.Source!); e.Handled = true;
    }
    private void DragTo(PointerEventArgs e)
    {
        if (dragged is null) return;
        var at = e.GetPosition(board);
        board.DragOffset = at - dragStart; board.Dragged = dragged; board.DragOrigin = dragOrigin;
        // The tile's top left corner decides the cell it would land in; the others show where they would make way.
        if (CellFor(at) is { } cell)
        {
            var preview = pins.Count == 0 ? board.Placements.ToDictionary(p => p.Id, p => (p.Column, p.Row)) : new Dictionary<string, (int Column, int Row)>(pins);
            preview[dragged] = cell; provisionalPins = preview;
        }
        board.InvalidateArrange();
    }
    private void EndDrag(PointerReleasedEventArgs e)
    {
        if (dragged is null) return;
        var id = dragged; var cell = CellFor(e.GetPosition(board));
        var overDrawer = drawerOpen && drawer.Bounds.Contains(e.GetPosition((Visual?)drawer.Parent));
        e.Pointer.Capture(null); Finish();
        if (overDrawer) Hide(id); else if (cell is { } at) DropAt(id, at.Column, at.Row); else board.InvalidateArrange();
    }
    private void CancelDrag() { if (dragged is null) return; Finish(); board.InvalidateArrange(); }
    private void Finish()
    {
        dragged = null; dragOrigin = null; provisionalPins = null; board.Dragged = null; board.DragOrigin = null; board.DragOffset = default;
    }
    /// <summary>The cell a dragged tile's top left corner is over, from where it was picked up and how far the pointer has moved.</summary>
    private (int Column, int Row)? CellFor(Point at)
    {
        var cell = board.CellWidth; if (cell <= 0 || dragOrigin is not { } origin) return null;
        var (column, row) = DeskLayout.CellAt(origin.Column + (at.X - dragStart.X) / cell, origin.Row + (at.Y - dragStart.Y) / (CellHeight + Gap));
        return (column, row);
    }
    /// <summary>Puts a tile in a cell. Tiles already there make way to the nearest free place, and every other tile stays where it was.</summary>
    public void DropAt(string id, int column, int row)
    {
        if (hidden.Contains(id) || !order.Contains(id)) return;
        PinAll(); pins[id] = (column, row); Settle(id);
    }
    /// <summary>Moves a tile to a place in the reading order and lets the tiles flow again from there. The older, order-only way to rearrange.</summary>
    public void Drop(string id, int index)
    {
        order = DeskLayout.Move(order, id, index); pins = new(); tidyButton.IsVisible = false;
        model.SetDeskOrder(order); model.SetDeskPositions(pins);
        board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>Puts a tile away in the drawer. Nothing is lost: it keeps its data and comes back when it is put on the desk again.</summary>
    public void Hide(string id)
    {
        if (!order.Contains(id)) return;
        PinAll(); order.Remove(id); pins.Remove(id); // The others stay where they are, so the tile's place is left empty.
        hidden.Add(id); Save(); SyncDrawer(); Settle(null);
    }
    /// <summary>Brings a tile from the drawer onto the desk at a place in the order.</summary>
    public void Show(string id, int index = int.MaxValue)
    {
        if (!hidden.Remove(id)) return;
        order.Insert(Math.Clamp(index, 0, order.Count), id); model.SetDeskHidden(hidden); SyncDrawer();
        // On an arranged desk the tile goes into the first free place, without moving anything else.
        if (pins.Count > 0) { Settle(null); return; }
        Save(); board.InvalidateMeasure(); board.InvalidateArrange();
    }
    /// <summary>Brings a tile from the drawer onto the desk at the cell it was dropped on.</summary>
    public void ShowAt(string id, int column, int row)
    {
        if (!hidden.Remove(id)) return;
        PinAll(); order.Add(id); model.SetDeskHidden(hidden); SyncDrawer(); pins[id] = (column, row); Settle(id);
    }
    private void Save() { model.SetDeskOrder(order); model.SetDeskHidden(hidden); model.SetDeskPositions(pins); }
    private void SyncDrawer()
    {
        foreach (var kind in Kinds) { tiles[kind.Id].Tile.IsVisible = !hidden.Contains(kind.Id) && (expanded is null || expanded == kind.Id); grips[kind.Id].IsVisible = arranging && expanded is null; }
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
                if (overBoard && board.CellWidth > 0) ShowAt(pressed, Math.Max(0, (int)(at.X / board.CellWidth)), Math.Max(0, (int)(at.Y / (CellHeight + Gap))));
                else Show(pressed);
            };
            chips.Children.Add(chip);
        }
    }
    internal IReadOnlyList<string> LayoutOrder => order;

    /// <summary>
    /// A panel that sets each tile on the grid from the packed layout, with the dragged tile following the pointer. Tiles glide to their
    /// places on a light spring (a little overshoot, so a drop settles with a bounce), and the one in hand is lifted and tilted with the way it is moving.
    /// With Lively off the tiles simply jump, which is also what the headless tests see because nothing ticks there.
    /// </summary>
    private sealed class DeskBoard : Panel
    {
        private sealed class Glide { public double X, Y, W, H, VX, VY, VW, VH; }
        public DeskView? Owner;
        public string? Dragged; public Vector DragOffset; public DeskPlacement? DragOrigin;
        public bool Lively = true;
        public List<DeskPlacement> Placements { get; private set; } = new();
        public int Columns { get; private set; } = 2; public double CellWidth { get; private set; }
        private readonly Dictionary<string, Glide> glides = new(); private readonly Dictionary<string, Rect> targets = new();
        private readonly Avalonia.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private string? lifted; private double lift, liftTarget, tilt, lastX;

        public DeskBoard() { Margin = new Thickness(6, 2, 6, 10); // Room for the tiles' shadows, which the scroll view would otherwise cut off.
            // The dots sit behind the tiles, in this panel so they scroll with them, and are left out wherever a tile covers them, so a tile's own surface stays clean.
            Children.Add(dots); timer.Tick += (_, _) => Step(); DetachedFromVisualTree += (_, _) => timer.Stop(); }
        private readonly DotField dots = new();
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
            var rows = DeskLayout.RowCount(Owner?.PackFor(Owner.order) ?? new());
            foreach (var child in Children) child.Measure(new Size(CellWidth * 2, CellHeight * 3));
            return new Size(width, rows * (CellHeight + Gap) - Gap + 24);
        }
        protected override Size ArrangeOverride(Size final)
        {
            Columns = DeskLayout.ColumnsFor(final.Width); CellWidth = final.Width / Columns;
            Placements = Owner?.expanded is { } open
                ? new List<DeskPlacement> { new(open, 0, 0, Columns, 99) } // Roomy, since it has the whole desk.
                : Owner?.PackFor(Owner.order) ?? new();
            targets.Clear(); var holes = new List<Rect>();
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
                    // The tile in hand is measured from where it was picked up, not from the cell it would land in, which moves as it is carried.
                    var from = DragOrigin is { } o ? new Rect(o.Column * CellWidth + Gap / 2, o.Row * (CellHeight + Gap) + Gap / 2, rect.Width, rect.Height) : rect;
                    var held = from.Translate(DragOffset); glide.X = held.X; glide.Y = held.Y; glide.W = held.Width; glide.H = held.Height; glide.VX = glide.VY = glide.VW = glide.VH = 0;
                    if (Dragged != lifted) { lifted = Dragged; lastX = DragOffset.X; }
                    tilt += (Math.Clamp((DragOffset.X - lastX) * .35, -3.5, 3.5) - tilt) * .4; lastX = DragOffset.X; liftTarget = Lively ? 1 : 0; // The lift eases in, in Step, so picking a tile up does not pop.
                }
                else if (!Lively) { glide.X = rect.X; glide.Y = rect.Y; glide.W = rect.Width; glide.H = rect.Height; }
                var placed = new Rect(glide.X, glide.Y, Math.Max(1, glide.W), Math.Max(1, glide.H)); tile.Arrange(placed); holes.Add(placed);
                Owner?.Fill(p.Id, p);
            }
            dots.Arrange(new Rect(final)); dots.Holes = holes; dots.InvalidateVisual();
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
                var rest = RestAngle((child.Tag as DeskTileKind)?.Id ?? ""); // Post-its keep their slight tilt at rest and when held.
                child.RenderTransform = held ? new TransformGroup { Children = { new ScaleTransform(1 + .025 * lift, 1 + .025 * lift), new RotateTransform(rest + tilt * lift) } } : rest != 0 ? new RotateTransform(rest) : null;
            }
        }
    }
}

/// <summary>A faint field of dots in the theme's line colour, drawn behind the desk's tiles.</summary>
public sealed class DotField : Control
{
    public List<Rect> Holes { get; set; } = new();
    public DotField() { IsHitTestVisible = false; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var brush = new SolidColorBrush(Ui.ThemeColour(this, "Line", Colors.LightGray), .55);
        for (var x = 12.0; x < Bounds.Width; x += 24)
            for (var y = 12.0; y < Bounds.Height; y += 24)
            {
                var at = new Point(x, y); var covered = false;
                foreach (var hole in Holes) if (hole.Inflate(3).Contains(at)) { covered = true; break; }
                if (!covered) context.DrawEllipse(brush, null, at, 1.1, 1.1);
            }
    }
}
