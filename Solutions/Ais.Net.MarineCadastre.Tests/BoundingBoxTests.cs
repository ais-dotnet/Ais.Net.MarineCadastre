using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests;

/// <summary>
/// The checks <see cref="BoundingBox"/> makes when it is constructed, and the positional behaviour
/// they must not change.
/// </summary>
[TestClass]
public class BoundingBoxTests
{
    [TestMethod]
    public void ValidBoxesAreAccepted()
    {
        BoundingBox empty = default;
        empty.ShouldBe(new BoundingBox(0, 0, 0, 0));

        BoundingBox point = new(2, 1, 2, 1);
        point.Contains(new Position(1, 2)).ShouldBeTrue();

        BoundingBox world = new(-180, -90, 180, 90);
        world.Contains(new Position(-90, 180)).ShouldBeTrue();

        BoundingBox pugetSound = new(West: -122.6, South: 47.4, East: -122.2, North: 47.8);
        pugetSound.West.ShouldBe(-122.6);
        pugetSound.South.ShouldBe(47.4);
        pugetSound.East.ShouldBe(-122.2);
        pugetSound.North.ShouldBe(47.8);
    }

    [TestMethod]
    public void PositionalBehaviourIsUnchanged()
    {
        BoundingBox box = new(1, 2, 3, 4);

        (double west, double south, double east, double north) = box;
        (west, south, east, north).ShouldBe((1.0, 2.0, 3.0, 4.0));
        box.ShouldBe(new BoundingBox(1, 2, 3, 4));
        box.ShouldNotBe(new BoundingBox(1, 2, 3, 5));
        box.GetHashCode().ShouldBe(new BoundingBox(1, 2, 3, 4).GetHashCode());
        box.ToString().ShouldBe("BoundingBox { West = 1, South = 2, East = 3, North = 4 }");
    }

    [TestMethod]
    public void WestMustNotExceedEast()
    {
        Should.Throw<ArgumentException>(() => new BoundingBox(10, 0, 9, 0)).ParamName.ShouldBe("East");
    }

    [TestMethod]
    public void SouthMustNotExceedNorth()
    {
        Should.Throw<ArgumentException>(() => new BoundingBox(0, 10, 0, 9)).ParamName.ShouldBe("North");
    }

    [TestMethod]
    [DataRow(90.0001)]
    [DataRow(-91.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void LatitudesOutsideNinetyDegreesAreRejected(double latitude)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundingBox(0, latitude, 0, 90)).ParamName.ShouldBe("South");
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundingBox(0, -90, 0, latitude)).ParamName.ShouldBe("North");
    }

    [TestMethod]
    [DataRow(180.0001)]
    [DataRow(-181.0)]
    [DataRow(double.NaN)]
    [DataRow(double.NegativeInfinity)]
    public void LongitudesOutsideOneHundredAndEightyDegreesAreRejected(double longitude)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundingBox(longitude, 0, 180, 0)).ParamName.ShouldBe("West");
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundingBox(-180, 0, longitude, 0)).ParamName.ShouldBe("East");
    }

    [TestMethod]
    public void SwappedLatitudeAndLongitudeAreRejected()
    {
        // Puget Sound with latitude and longitude the wrong way round: South -122.6 is off the globe.
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundingBox(47.4, -122.6, 47.8, -122.2)).ParamName.ShouldBe("South");
    }

    [TestMethod]
    public void ABoundingBoxNeedsAtLeastOnePosition()
    {
        Should.Throw<ArgumentException>(() => BoundingBox.FromPositions([]));
        BoundingBox.FromPositions([new Position(1, 2)]).ShouldBe(new BoundingBox(2, 1, 2, 1));
    }

    [TestMethod]
    public void PositionsOutsideWgs84AreRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BoundingBox.FromPositions([new Position(91, 0)]));
        Should.Throw<ArgumentOutOfRangeException>(() => BoundingBox.FromPositions([new Position(0, 0), new Position(0, 181)]));
        Should.Throw<ArgumentOutOfRangeException>(() => BoundingBox.FromPositions([new Position(0, 0), new Position(double.NaN, 0)]));
    }
}
