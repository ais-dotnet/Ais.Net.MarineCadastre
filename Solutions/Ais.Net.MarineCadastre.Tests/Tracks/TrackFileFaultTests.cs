using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.MarineCadastre.Tracks;
using Ais.Net.Models.Abstractions;

using Microsoft.Data.Sqlite;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.Tracks;

/// <summary>
/// Files that are not GeoPackages or not UTF-8, timestamps stored as real numbers, and timestamp text
/// that only the framework parser can read: each must give a documented exception or the right track.
/// </summary>
[TestClass]
public class TrackFileFaultTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 9, 52, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 15, 51, TimeSpan.Zero);
    private static readonly Position[] Miami = [new(25.79027, -80.18396), new(25.79028, -80.18398)];

    [TestMethod]
    public void AFileThatIsNotADatabaseIsReportedAsInvalidData()
    {
        string path = TempPath();
        File.WriteAllText(path, "this is not a GeoPackage, it is plain text that is long enough to fill a header page");
        try
        {
            Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(path).ToList()).Message.ShouldContain("not a GeoPackage");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnSqliteDatabaseWithoutTheGeoPackageRegistryIsReportedAsInvalidData()
    {
        string path = TempPath();
        try
        {
            Execute(path, "create table something_else (id integer primary key)");

            Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(path).ToList()).Message.ShouldContain("gpkg_contents");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ADatabaseThatStoresItsTextAsUtf16IsNotSupported()
    {
        string path = TempPath();
        try
        {
            Execute(path, "pragma encoding = 'UTF-16le'; create table something (id integer primary key)");

            Should.Throw<NotSupportedException>(() => MarineCadastreTrackReader.Read(path).ToList()).Message.ShouldContain("UTF-16");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TimestampsStoredAsRealUnixSecondsAreRead()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set start_time = {Start.ToUnixTimeSeconds()}.25, end_time = {End.ToUnixTimeSeconds()}.0""");

        VesselTrack track = MarineCadastreTrackReader.Read(package.Path).Single();

        track.StartTime.ShouldBe(Start.AddMilliseconds(250), TimeSpan.FromMilliseconds(1));
        track.EndTime.ShouldBe(End, TimeSpan.FromMilliseconds(1));
    }

    [TestMethod]
    public void NumericTimestampsAreReadTheWaySqliteReadsThemForTheTimeFilter()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set start_time = {Start.ToUnixTimeSeconds()}.5, end_time = {End.ToUnixTimeSeconds()}.5 where OBJECTID = 1""");
        package.Execute($"""update "{package.TableName}" set start_time = julianday('{Start:yyyy-MM-dd HH:mm:ss}'), end_time = julianday('{End:yyyy-MM-dd HH:mm:ss}') where OBJECTID = 2""");
        VesselTrackReaderOptions window = new() { From = Start.AddMinutes(1), To = End.AddMinutes(-1) };

        MarineCadastreTrackReader.Read(package.Path, window).Select(t => t.Id).ShouldBe([1L, 2L]);
    }

    [TestMethod]
    public void ANumericTimestampOutsideTheSupportedRangeIsReportedWithTheRow()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set end_time = 1e300""");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldContain("The end time of row 1 is outside the supported range");
    }

    [TestMethod]
    public void TimestampTextThatOnlyTheFrameworkCanReadPassesTheTimeFilter()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start.AddDays(5), End.AddDays(5), Miami));

        // SQLite's datetime() gives null for this shape, so the pre-filter must not drop the row: the
        // client-side check, which reads it with the framework parser, decides.
        package.Execute($"""update "{package.TableName}" set start_time = 'Thu, 01 Jan 2026 00:09:52 GMT', end_time = 'Thu, 01 Jan 2026 00:15:51 GMT' where OBJECTID = 1""");
        package.Execute($"""update "{package.TableName}" set start_time = 'Tue, 06 Jan 2026 00:09:52 GMT', end_time = 'Tue, 06 Jan 2026 00:15:51 GMT' where OBJECTID = 2""");
        VesselTrackReaderOptions firstDay = new() { From = Start, To = Start.AddDays(1) };

        VesselTrack track = MarineCadastreTrackReader.Read(package.Path, firstDay).Single();

        track.Id.ShouldBe(1L);
        track.StartTime.ShouldBe(Start);
        track.EndTime.ShouldBe(End);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"ais-net-tracks-{Guid.NewGuid():N}.gpkg");

    private static void Execute(string path, string sql)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
