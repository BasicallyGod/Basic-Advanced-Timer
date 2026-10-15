using Timer.Core;
using Timer.Desk;
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

    private static DeskTileSpec Tile(string id, int c = 2, int r = 2) => new(id, c, r);
    private static Dictionary<string, (int Column, int Row)> Pins(params (string Id, int C, int R)[] pins) => pins.ToDictionary(p => p.Id, p => (p.C, p.R));

    [Fact]
    public void PinnedTilesKeepTheirCellsAndLeaveTheGapsBetweenThem()
    {
        var placed = DeskLayout.PackPinned(new[] { Tile("a"), Tile("b") }, Pins(("a", 0, 0), ("b", 3, 4)), 6);
        Assert.Equal((0, 0), (placed[0].Column, placed[0].Row));
        Assert.Equal((3, 4), (placed[1].Column, placed[1].Row)); // Flowing would have put it at 2,0.
    }

    [Fact]
    public void ATileWithNoPinFlowsIntoTheFirstFreeSpotAroundThePinnedOnes()
    {
        var placed = DeskLayout.PackPinned(new[] { Tile("a"), Tile("loose") }, Pins(("a", 0, 0)), 4);
        Assert.Equal((2, 0), (placed[1].Column, placed[1].Row));
    }

    [Fact]
    public void ThePriorityTileKeepsItsCellAndTheOneItLandedOnMakesWay()
    {
        var pins = Pins(("a", 0, 0), ("b", 0, 0));
        var first = DeskLayout.PackPinned(new[] { Tile("a"), Tile("b") }, pins, 4, "b");
        Assert.Equal((0, 0), (first[1].Column, first[1].Row));
        Assert.NotEqual((0, 0), (first[0].Column, first[0].Row));
        Assert.False(Overlap(first[0], first[1]));
    }

    [Fact]
    public void AGrownTilePushesOnlyWhatItCoversAndNothingOverlaps()
    {
        var specs = new[] { Tile("a", 3, 2), Tile("b"), Tile("c") };
        var placed = DeskLayout.PackPinned(specs, Pins(("a", 0, 0), ("b", 2, 0), ("c", 0, 4)), 6, "a");
        Assert.Equal((0, 4), (placed[2].Column, placed[2].Row)); // Untouched, so its gap stays.
        for (var i = 0; i < placed.Count; i++) for (var j = i + 1; j < placed.Count; j++) Assert.False(Overlap(placed[i], placed[j]));
    }

    [Fact]
    public void ANarrowerWindowPullsPinsInsideTheGridWithoutOverlap()
    {
        var placed = DeskLayout.PackPinned(new[] { Tile("a"), Tile("b") }, Pins(("a", 4, 0), ("b", 6, 0)), 2);
        Assert.All(placed, p => Assert.True(p.Column + p.ColumnSpan <= 2));
        Assert.False(Overlap(placed[0], placed[1]));
    }

    private static bool Overlap(DeskPlacement x, DeskPlacement y)
        => x.Column < y.Column + y.ColumnSpan && y.Column < x.Column + x.ColumnSpan && x.Row < y.Row + y.RowSpan && y.Row < x.Row + x.RowSpan;

    [Fact]
    public void PositionsSurviveWritingAndReadingAndBadOnesAreDropped()
    {
        var pins = Pins(("timeline", 2, 3), ("day", 0, 0));
        Assert.Equal(pins.OrderBy(k => k.Key), DeskLayout.ParsePositions(DeskLayout.FormatPositions(pins)).OrderBy(k => k.Key));
        Assert.Equal(new[] { "ok" }, DeskLayout.ParsePositions(new[] { "ok:1+2", "bad", "x:-1+2", "y:1+z", ":1+2", "z:99+2" }).Keys);
    }

    private static SavedLayout Layout(string name, params string[] order) => new(name, order, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void SavingUnderAnExistingNameReplacesItInPlaceAndANewNameIsAdded()
    {
        var list = DeskLayouts.Upsert(DeskLayouts.Upsert(new List<SavedLayout>(), Layout("Focus", "a")), Layout("Plan", "b"));
        list = DeskLayouts.Upsert(list, Layout("  focus ", "c", "d"));
        Assert.Equal(new[] { "focus", "Plan" }, list.Select(l => l.Name)); Assert.Equal(new[] { "c", "d" }, list[0].Order);
        Assert.Single(DeskLayouts.Remove(list, "PLAN"));
        Assert.Equal(2, DeskLayouts.Upsert(list, Layout("   ")).Count); // An empty name saves nothing.
    }

    [Fact]
    public void NamesAreCleanedAndTheListHasALimit()
    {
        Assert.Equal("a b", DeskLayouts.CleanName("  a \t b  ")); Assert.Equal(DeskLayouts.MaxNameLength, DeskLayouts.CleanName(new string('x', 99)).Length);
        var list = new List<SavedLayout>(); for (var i = 0; i < 20; i++) list = DeskLayouts.Upsert(list, Layout("L" + i));
        Assert.Equal(DeskLayouts.MaxLayouts, list.Count);
    }

    [Fact]
    public void LayoutsSurviveWritingAndReadingAndDamageGivesNone()
    {
        var one = new SavedLayout("Focus", new[] { "working", "todo" }, new[] { "clock" }, new[] { "todo:2x4" }, new[] { "todo:2+0" });
        var back = Assert.Single(DeskLayouts.FromJson(DeskLayouts.ToJson(new[] { one })));
        Assert.Equal(one.Name, back.Name); Assert.Equal(one.Order, back.Order); Assert.Equal(one.Hidden, back.Hidden); Assert.Equal(one.Sizes, back.Sizes); Assert.Equal(one.Positions, back.Positions);
        Assert.Empty(DeskLayouts.FromJson("{not json")); Assert.Empty(DeskLayouts.FromJson(null)); Assert.Empty(DeskLayouts.FromJson("[{\"Name\":\" \"}]"));
    }

    [Fact]
    public void SavedLayoutsAndPositionsAreKeptInTheStoreAcrossRestarts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-layouts-" + Guid.NewGuid());
        try
        {
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new Timer.Desktop.TimerModel(engine, store.Load(engine), store);
                model.SaveDeskLayout(new SavedLayout("Focus", new[] { "todo" }, new[] { "clock" }, new[] { "todo:2x4" }, new[] { "todo:2+3" }));
                model.SetDeskPositions(new Dictionary<string, (int Column, int Row)> { ["day"] = (1, 2) });
            }
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new Timer.Desktop.TimerModel(engine, store.Load(engine), store);
                var layout = Assert.Single(model.DeskLayouts); Assert.Equal("Focus", layout.Name); Assert.Equal(new[] { "todo:2+3" }, layout.Positions);
                Assert.Equal((1, 2), model.DeskPositions["day"]);
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void ShortcutsReadAndWriteInOneFormAndNeedAModifier()
    {
        var spec = HotkeySpec.Parse("alt + ctrl + n")!; Assert.Equal("Ctrl+Alt+N", spec.ToString()); Assert.Equal(HotkeySpec.Control | HotkeySpec.AltMod, spec.Modifiers); Assert.Equal('N', HotkeySpec.VirtualKey(spec.Key));
        Assert.Equal("Win+F9", HotkeySpec.Parse("win+f9")!.ToString()); Assert.Equal(0x78, HotkeySpec.VirtualKey("F9"));
        Assert.Null(HotkeySpec.Parse("N")); Assert.Null(HotkeySpec.Parse("Shift+N")); // A letter alone, or with Shift alone, would take over typing.
        Assert.Null(HotkeySpec.Parse("Ctrl+Enter")); Assert.Null(HotkeySpec.Parse("Ctrl+A+B")); Assert.Null(HotkeySpec.Parse("Ctrl+F13")); Assert.Null(HotkeySpec.Parse("")); Assert.Null(HotkeySpec.Parse(null));
    }

    [Fact]
    public void AListTileAsksForRowsToFitItsItemsWithinItsUsualAndLargestSizes()
    {
        Assert.Equal(3, DeskLayout.PreferredRows(0, 46, 150, 3, 8)); Assert.Equal(3, DeskLayout.PreferredRows(2, 46, 150, 3, 8)); // Few items keep the usual size.
        Assert.Equal(4, DeskLayout.PreferredRows(6, 46, 150, 3, 8)); // 150 + 276 = 426, which is four rows of 112.
        Assert.Equal(8, DeskLayout.PreferredRows(40, 46, 150, 3, 8)); // Never past the tile's limit.
        Assert.Equal(3, DeskLayout.PreferredRows(-5, 46, 150, 3, 8)); Assert.Equal(5, DeskLayout.PreferredRows(10, 46, 150, 5, 3)); // A limit below the usual size never shrinks the tile.
    }
}
