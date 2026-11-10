using Ais.Net.MarineCadastre.TransitCounts;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.TransitCounts;

/// <summary>
/// The checks <see cref="TransitTile"/> makes on its dimensions, its counts and its indexer.
/// </summary>
[TestClass]
public class TransitTileTests
{
    private static TransitTile Tile() => new(0, 0, 0, 0, 0, 2, 3, default, new uint[] { 1, 2, 3, 4, 5, 6 });

    [TestMethod]
    [DataRow(5)]
    [DataRow(7)]
    public void CountsMustHoldWidthTimesHeightValues(int length)
    {
        Should.Throw<ArgumentException>(() => new TransitTile(0, 0, 0, 0, 0, 2, 3, default, new uint[length])).ParamName.ShouldBe("Counts");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void WidthAndHeightMustBePositive(int size)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new TransitTile(0, 0, 0, 0, 0, size, 3, default, new uint[6])).ParamName.ShouldBe("Width");
        Should.Throw<ArgumentOutOfRangeException>(() => new TransitTile(0, 0, 0, 0, 0, 2, size, default, new uint[6])).ParamName.ShouldBe("Height");
    }

    [TestMethod]
    public void TheIndexerReadsRowMajor()
    {
        TransitTile tile = Tile();

        tile[0, 0].ShouldBe(1u);
        tile[1, 0].ShouldBe(2u);
        tile[0, 1].ShouldBe(3u);
        tile[1, 2].ShouldBe(6u);
    }

    [TestMethod]
    [DataRow(2, 0)]
    [DataRow(0, 3)]
    [DataRow(-1, 0)]
    [DataRow(0, -1)]
    public void TheIndexerRejectsCellsOutsideTheTile(int column, int row)
    {
        TransitTile tile = Tile();

        Should.Throw<ArgumentOutOfRangeException>(() => tile[column, row]);
    }

    [TestMethod]
    public void WithKeepsTheGrid()
    {
        TransitTile tile = Tile();

        TransitTile moved = tile with { Level = 1, Column = 4 };

        moved.Level.ShouldBe(1);
        moved.Column.ShouldBe(4);
        moved.Width.ShouldBe(2);
        moved.Height.ShouldBe(3);
        moved.Counts.ShouldBe(tile.Counts);
        moved[1, 2].ShouldBe(6u);
    }
}
