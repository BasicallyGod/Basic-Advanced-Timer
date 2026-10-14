using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskLayoutTests
{
    private static DeskTileSpec T(string id, int c = 1, int r = 1) => new(id, c, r);

    [Theory]
    [InlineData(300, 2)]
    [InlineData(640, 3)]
    [InlineData(1020, 5)]
    public void ColumnsFollowTheWidthWithATwoColumnFloor(double width, int columns) => Assert.Equal(columns, DeskLayout.ColumnsFor(width));

    [Fact]
    public void TilesFlowInOrderAndWrapAtTheEdge()
    {
        var p = DeskLayout.Pack(new[] { T("a"), T("b"), T("c") }, 2);
        Assert.Equal((0, 0), (p[0].Column, p[0].Row)); Assert.Equal((1, 0), (p[1].Column, p[1].Row)); Assert.Equal((0, 1), (p[2].Column, p[2].Row));
    }

    [Fact]
    public void ATallTileLeavesRoomBesideItForTheNext()
    {
        var p = DeskLayout.Pack(new[] { T("tall", 1, 2), T("b"), T("c") }, 2);
        Assert.Equal((1, 0), (p[1].Column, p[1].Row)); Assert.Equal((1, 1), (p[2].Column, p[2].Row)); Assert.Equal(2, DeskLayout.RowCount(p));
    }

    [Fact]
    public void ATileWiderThanTheGridIsNarrowedAndNoTilesOverlap()
    {
        var p = DeskLayout.Pack(new[] { T("wide", 5, 1), T("b", 2, 2), T("c", 1, 1) }, 3);
        Assert.Equal(3, p[0].ColumnSpan);
        var cells = p.SelectMany(t => Enumerable.Range(t.Column, t.ColumnSpan).SelectMany(c => Enumerable.Range(t.Row, t.RowSpan).Select(r => (c, r)))).ToList();
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void FormFollowsTheCellsATileHolds()
    {
        Assert.Equal(TileForm.Tiny, new DeskPlacement("a", 0, 0, 1, 1).Form);
        Assert.Equal(TileForm.Normal, new DeskPlacement("a", 0, 0, 2, 1).Form);
        Assert.Equal(TileForm.Roomy, new DeskPlacement("a", 0, 0, 3, 2).Form);
    }

    [Fact]
    public void MovingATileChangesOnlyTheOrder()
    {
        Assert.Equal(new[] { "b", "c", "a" }, DeskLayout.Move(new[] { "a", "b", "c" }, "a", 99));
        Assert.Equal(new[] { "c", "a", "b" }, DeskLayout.Move(new[] { "a", "b", "c" }, "c", 0));
        Assert.Equal(new[] { "a", "b" }, DeskLayout.Move(new[] { "a", "b" }, "zzz", 0));
    }

    [Fact]
    public void DroppingBetweenTilesPicksTheSlotBeforeTheOneAhead()
    {
        var p = DeskLayout.Pack(new[] { T("a"), T("b"), T("c") }, 3);
        Assert.Equal(0, DeskLayout.InsertIndexAt(p, "c", 0.2, 0.5));
        Assert.Equal(1, DeskLayout.InsertIndexAt(p, "c", 1.2, 0.5));
        Assert.Equal(2, DeskLayout.InsertIndexAt(p, "a", 2.9, 0.5));
        Assert.Equal(2, DeskLayout.InsertIndexAt(p, "a", 0.5, 3));
    }

    [Fact]
    public void ASavedOrderLosesGoneTilesAndGainsNewOnes()
        => Assert.Equal(new[] { "b", "a", "c" }, DeskLayout.Reconcile(new[] { "b", "gone", "a", "b" }, new[] { "a", "b", "c" }));

    [Theory]
    [InlineData(9, 9, 1, 1, 4, 6, 5, 4, 6)]
    [InlineData(0, 0, 2, 2, 4, 6, 5, 2, 2)]
    [InlineData(4, 3, 2, 2, 4, 6, 2, 2, 3)]
    [InlineData(3, 3, 3, 1, 4, 6, 2, 2, 3)]
    public void ASizeIsHeldToTheTilesLimitsAndTheGrid(int c, int r, int minC, int minR, int maxC, int maxR, int grid, int wantC, int wantR)
        => Assert.Equal((wantC, wantR), DeskLayout.ClampSize(c, r, minC, minR, maxC, maxR, grid));

    [Fact]
    public void ADraggedCornerSnapsToWholeCells()
    {
        // A tile two cells wide and three tall measures 2 * 200 - 12 by 3 * (124 + 12) - 12.
        Assert.Equal((2, 3), DeskLayout.SizeFromPixels(388, 396, 200, 124, 12));
        Assert.Equal((3, 3), DeskLayout.SizeFromPixels(500, 380, 200, 124, 12));
        Assert.Equal((1, 1), DeskLayout.SizeFromPixels(5, 5, 200, 124, 12));
    }

    [Fact]
    public void SizesSurviveWritingAndReadingAndBadOnesAreDropped()
    {
        var sizes = new Dictionary<string, (int Columns, int Rows)> { ["timeline"] = (3, 5), ["accounts"] = (2, 4) };
        var back = DeskLayout.ParseSizes(DeskLayout.FormatSizes(sizes));
        Assert.Equal(sizes.OrderBy(k => k.Key), back.OrderBy(k => k.Key));
        Assert.Equal(new[] { "ok" }, DeskLayout.ParseSizes(new[] { "ok:2x2", "bad", "x:0x2", "y:2xz", ":2x2", "z:99x2" }).Keys);
    }
}
