using Timer.Core;

namespace Timer.Desk;

/// <summary>How much room a tile has, which decides how much of itself it shows.</summary>
public enum TileForm { Tiny, Normal, Roomy }

/// <summary>A tile's id and the cells it asks for. Tiles resize only by changing these spans, never by being dragged out of shape.</summary>
public sealed record DeskTileSpec(string Id, int Columns, int Rows);

/// <summary>Where a tile sits on the desk's grid, in cells.</summary>
public sealed record DeskPlacement(string Id, int Column, int Row, int ColumnSpan, int RowSpan)
{
    public TileForm Form => ColumnSpan * RowSpan >= 6 ? TileForm.Roomy : ColumnSpan * RowSpan >= 2 ? TileForm.Normal : TileForm.Tiny;
}

/// <summary>
/// The desk's arithmetic, kept free of UI types so it can be tested. Tiles flow in order onto a grid whose column count follows the
/// window's width, so the same order gives a sensible arrangement at every size and only the order needs saving.
/// </summary>
public static class DeskLayout
{
    public const int CellWidth = 200;

    /// <summary>One column per 200 pixels of room, at least two so the smallest window still shows tiles side by side.</summary>
    public static int ColumnsFor(double width) => Math.Max(2, (int)(width / CellWidth));

    /// <summary>Places each tile, in order, in the first free spot that holds it. A tile wider than the grid is narrowed to fit.</summary>
    public static List<DeskPlacement> Pack(IEnumerable<DeskTileSpec> tiles, int columns)
    {
        columns = Math.Max(1, columns);
        var taken = new List<bool[]>();
        var placements = new List<DeskPlacement>();
        bool[] Row(int index) { while (taken.Count <= index) taken.Add(new bool[columns]); return taken[index]; }
        foreach (var tile in tiles)
        {
            var width = Math.Clamp(tile.Columns, 1, columns); var height = Math.Max(1, tile.Rows);
            for (var row = 0; ; row++)
            {
                var placed = false;
                for (var column = 0; column + width <= columns && !placed; column++)
                {
                    var free = true;
                    for (var r = row; r < row + height && free; r++) for (var c = column; c < column + width; c++) if (Row(r)[c]) { free = false; break; }
                    if (!free) continue;
                    for (var r = row; r < row + height; r++) for (var c = column; c < column + width; c++) Row(r)[c] = true;
                    placements.Add(new DeskPlacement(tile.Id, column, row, width, height)); placed = true;
                }
                if (placed) break;
            }
        }
        return placements;
    }

    public static int RowCount(IEnumerable<DeskPlacement> placements) => placements.Select(p => p.Row + p.RowSpan).DefaultIfEmpty(0).Max();

    /// <summary>Moves a tile to a position in the order, clamped to the ends. Unknown ids leave the order as it was.</summary>
    public static List<string> Move(IReadOnlyList<string> order, string id, int index)
    {
        var result = order.ToList();
        if (!result.Remove(id)) return order.ToList();
        result.Insert(Math.Clamp(index, 0, result.Count), id);
        return result;
    }

    /// <summary>The position in the order a tile dropped at a point would take: before the first tile whose centre is past the point in reading order.</summary>
    public static int InsertIndexAt(IReadOnlyList<DeskPlacement> placements, string dragged, double column, double row)
    {
        var others = placements.Where(p => p.Id != dragged).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            var centreRow = others[i].Row + others[i].RowSpan / 2.0; var centreColumn = others[i].Column + others[i].ColumnSpan / 2.0;
            if (row < others[i].Row || (row < others[i].Row + others[i].RowSpan && column < centreColumn)) return i;
        }
        return others.Count;
    }

    /// <summary>Reads a saved order, dropping ids that no longer exist and adding new tiles at the end so an old file still shows everything.</summary>
    public static List<string> Reconcile(IEnumerable<string>? saved, IReadOnlyList<string> known)
    {
        var result = (saved ?? Array.Empty<string>()).Where(known.Contains).Distinct().ToList();
        result.AddRange(known.Where(id => !result.Contains(id)));
        return result;
    }

    // ---- Tile sizes. A tile can be made bigger or smaller by whole cells, within limits that suit what it shows. ----

    /// <summary>Holds a size to a tile's limits and to the grid it is on, so a tile never asks for more columns than there are.</summary>
    public static (int Columns, int Rows) ClampSize(int columns, int rows, int minColumns, int minRows, int maxColumns, int maxRows, int gridColumns)
    {
        var widest = Math.Max(1, Math.Min(maxColumns, gridColumns));
        return (Math.Clamp(columns, Math.Min(minColumns, widest), widest), Math.Clamp(rows, minRows, Math.Max(minRows, maxRows)));
    }

    /// <summary>The whole-cell size nearest to a width and height in pixels, for a tile being dragged larger or smaller.</summary>
    public static (int Columns, int Rows) SizeFromPixels(double width, double height, double cellWidth, double cellHeight, double gap)
        => (Math.Max(1, (int)Math.Round((width + gap) / Math.Max(1, cellWidth))), Math.Max(1, (int)Math.Round((height + gap) / Math.Max(1, cellHeight + gap))));

    /// <summary>
    /// How many rows a list tile needs to show all of its items without scrolling: the header and the room at its foot (the box for a new item, the margins),
    /// plus a height per item, over the height of a row. Never fewer than the tile's usual rows or more than its limit, so an empty list keeps its usual size.
    /// </summary>
    public static int PreferredRows(int items, double perItem, double chrome, int usualRows, int maxRows, double rowHeight = 112)
    {
        var needed = (int)Math.Ceiling((chrome + Math.Max(0, items) * perItem) / Math.Max(1, rowHeight));
        return Math.Clamp(Math.Max(usualRows, needed), usualRows, Math.Max(usualRows, maxRows));
    }

    /// <summary>Saved sizes are written as id:ColumnsxRows. Anything that does not read that way is dropped.</summary>
    public static Dictionary<string, (int Columns, int Rows)> ParseSizes(IEnumerable<string>? saved)
    {
        var sizes = new Dictionary<string, (int, int)>();
        foreach (var entry in saved ?? Array.Empty<string>())
        {
            var parts = entry.Split(':');
            if (parts.Length == 2 && parts[1].Split('x') is [var c, var r] && int.TryParse(c, out var columns) && int.TryParse(r, out var rows) && columns is >= 1 and <= 12 && rows is >= 1 and <= 24 && parts[0].Length > 0)
                sizes[parts[0]] = (columns, rows);
        }
        return sizes;
    }
    public static List<string> FormatSizes(IReadOnlyDictionary<string, (int Columns, int Rows)> sizes) => sizes.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}:{s.Value.Columns}x{s.Value.Rows}").ToList();

    // ---- Pinned positions. Once a desk has been arranged, tiles keep the cell they were put in, so a deliberate gap stays a gap. ----

    /// <summary>
    /// Places tiles at their saved cells, then flows any without one into the first free spot around them. A pin that no longer fits (a narrower
    /// window, or a tile that grew over it) goes to the nearest free place instead; the priority tile, the one just moved or resized, is placed first so it keeps its spot and the others make way.
    /// </summary>
    public static List<DeskPlacement> PackPinned(IEnumerable<DeskTileSpec> tiles, IReadOnlyDictionary<string, (int Column, int Row)> pins, int columns, string? priority = null)
    {
        columns = Math.Max(1, columns);
        var specs = tiles.ToList();
        var taken = new List<bool[]>();
        bool[] RowOf(int index) { while (taken.Count <= index) taken.Add(new bool[columns]); return taken[index]; }
        bool Free(int column, int row, int width, int height)
        {
            for (var r = row; r < row + height; r++) for (var c = column; c < column + width; c++) if (RowOf(r)[c]) return false;
            return true;
        }
        void Take(int column, int row, int width, int height) { for (var r = row; r < row + height; r++) for (var c = column; c < column + width; c++) RowOf(r)[c] = true; }
        var placed = new Dictionary<string, DeskPlacement>();
        var pinned = specs.Where(s => pins.ContainsKey(s.Id))
            .OrderBy(s => s.Id == priority ? 0 : 1).ThenBy(s => pins[s.Id].Row).ThenBy(s => pins[s.Id].Column).ToList();
        foreach (var spec in pinned)
        {
            var width = Math.Clamp(spec.Columns, 1, columns); var height = Math.Max(1, spec.Rows);
            var (pc, pr) = pins[spec.Id]; pc = Math.Clamp(pc, 0, columns - width); pr = Math.Max(0, pr);
            var column = pc; var row = pr;
            if (!Free(pc, pr, width, height))
            {
                // The nearest free cell by distance, reading order breaking ties, searched a little past the last row in use.
                var best = double.MaxValue; var reach = Math.Max(taken.Count, pr) + height + 1;
                column = pc; row = reach;
                for (var r = 0; r <= reach; r++) for (var c = 0; c + width <= columns; c++)
                {
                    var distance = (c - pc) * (c - pc) + (r - pr) * (r - pr);
                    if (distance < best && Free(c, r, width, height)) { best = distance; column = c; row = r; }
                }
            }
            Take(column, row, width, height); placed[spec.Id] = new DeskPlacement(spec.Id, column, row, width, height);
        }
        foreach (var spec in specs.Where(s => !placed.ContainsKey(s.Id)))
        {
            var width = Math.Clamp(spec.Columns, 1, columns); var height = Math.Max(1, spec.Rows);
            for (var row = 0; !placed.ContainsKey(spec.Id); row++)
                for (var column = 0; column + width <= columns; column++)
                    if (Free(column, row, width, height)) { Take(column, row, width, height); placed[spec.Id] = new DeskPlacement(spec.Id, column, row, width, height); break; }
        }
        return specs.Select(s => placed[s.Id]).ToList();
    }

    /// <summary>The cell a tile's top left corner is nearest to, for a tile dropped at a point (in cells, fractions allowed).</summary>
    public static (int Column, int Row) CellAt(double column, double row) => (Math.Max(0, (int)Math.Round(column)), Math.Max(0, (int)Math.Round(row)));

    /// <summary>Saved positions are written as id:Column,Row. Anything that does not read that way is dropped.</summary>
    public static Dictionary<string, (int Column, int Row)> ParsePositions(IEnumerable<string>? saved)
    {
        var positions = new Dictionary<string, (int, int)>();
        foreach (var entry in saved ?? Array.Empty<string>())
        {
            // The pair is joined with a colon and a plus sign so the comma can keep separating entries.
            var parts = entry.Split(':');
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Split('+') is [var c, var r] && int.TryParse(c, out var column) && int.TryParse(r, out var row) && column is >= 0 and <= 40 && row is >= 0 and <= 200)
                positions[parts[0]] = (column, row);
        }
        return positions;
    }
    public static List<string> FormatPositions(IReadOnlyDictionary<string, (int Column, int Row)> positions) => positions.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value.Column}+{p.Value.Row}").ToList();
}
