using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Timer.Core;
using Timer.Desk;

namespace Timer.Desktop;

/// <summary>
/// A desk tile floating on the screen in a window of its own. The tile is the desk's own (a single-tile <see cref="DeskView"/>), so it shows the same
/// version of itself for its size in cells and stays in step with the same model; the window only carries it: moved by its header, resized in whole cells
/// from its corner, always on top unless switched off in settings, and put back on the desk with Dock.
/// </summary>
public sealed class FloatingWidgetWindow : Window
{
    private readonly TimerModel model;
    private readonly DeskView view;
    private readonly Avalonia.Threading.DispatcherTimer saveMove = new() { Interval = TimeSpan.FromMilliseconds(400) };
    public string WidgetId { get; }

    public FloatingWidgetWindow(TimerModel model, FloatingWidget state)
    {
        this.model = model; WidgetId = state.Id; Name = "Float_" + state.Id;
        var kind = DeskView.Kinds.First(k => k.Id == state.Id); Title = kind.Title; WindowChrome.Attach(this);
        SystemDecorations = SystemDecorations.None; TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }; Background = Brushes.Transparent;
        CanResize = false; ShowInTaskbar = false; Topmost = model.FloatOnTop;
        model.PropertyChanged += (_, _) => { if (Topmost != model.FloatOnTop) Topmost = model.FloatOnTop; };
        view = new DeskView(model, null, state.Id) { Margin = new Thickness(Ui.ShadowMargin) };
        // A tile's colour is a pale wash over the desk's surface, so on its own it would show whatever is behind the window. The surface goes underneath it.
        var underlay = new Border { Margin = new Thickness(Ui.ShadowMargin), CornerRadius = new CornerRadius(state.Id.StartsWith("postit", StringComparison.Ordinal) ? 8 : 22) }; underlay.Classes.Add("surface");
        Content = new Grid { Children = { underlay, view } };
        view.CellsChanged += (c, r) => { Fit(c, r); model.ResizeFloating(WidgetId, c, r); };
        view.SetCells(state.Columns, state.Rows); Fit(state.Columns, state.Rows);
        if (state.X is { } x && state.Y is { } y) { WindowStartupLocation = WindowStartupLocation.Manual; Position = new PixelPoint(x, y); }
        else WindowStartupLocation = WindowStartupLocation.Manual;
        // The position is saved a moment after the last move, not on every pixel of a drag.
        saveMove.Tick += (_, _) => { saveMove.Stop(); model.MoveFloating(WidgetId, Position.X, Position.Y); };
        PositionChanged += (_, _) => { if (IsVisible) { saveMove.Stop(); saveMove.Start(); } };
        Opened += (_, _) => { if (state.X is null || state.Y is null) { Position = FirstSpot(); } KeepOnAScreen(); };
        // Closing it any other way (Alt+F4) is the same as docking it, so it never disappears while the desk still says it floats.
        Closed += (_, _) => { saveMove.Stop(); if (!retiring) model.DockWidget(WidgetId); };
    }
    private bool retiring;
    /// <summary>Closes the window because the widget was docked or the app is closing, which must not dock it again.</summary>
    public void Retire() { retiring = true; Close(); }

    /// <summary>The window is the tile's size in cells plus room around it for its shadow.</summary>
    private void Fit(int columns, int rows)
    {
        var tile = DeskView.PixelsFor(columns, rows);
        Width = tile.Width + 2 * Ui.ShadowMargin; Height = tile.Height + 2 * Ui.ShadowMargin;
    }
    /// <summary>A new widget opens at the right of the screen it is on, stepping down for each one already there, so they never land on top of each other.</summary>
    private PixelPoint FirstSpot()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary; if (screen is null) return Position;
        var area = screen.WorkingArea; var n = Math.Max(0, model.Floating.ToList().FindIndex(f => f.Id == WidgetId));
        var width = (int)(Width * screen.Scaling);
        return new PixelPoint(Math.Max(area.X, area.Right - width - (int)(24 * screen.Scaling)), area.Y + (int)((60 + n * 48) * screen.Scaling));
    }
    /// <summary>A widget saved on a screen that is no longer there is brought back into view.</summary>
    private void KeepOnAScreen()
    {
        if (Screens.All.Count == 0) return;
        var rect = new PixelRect(Position, new PixelSize(Math.Max(1, (int)Width), Math.Max(1, (int)Height)));
        if (Screens.All.Any(s => s.WorkingArea.Intersect(rect).Width > 60 && s.WorkingArea.Intersect(rect).Height > 40)) return;
        Position = FirstSpot();
    }
}
