namespace Timer.Core;

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
}
