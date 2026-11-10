using Ais.Net.MarineCadastre.GeoTiff;
using Ais.Net.MarineCadastre.TestSupport;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.GeoTiff;

[TestClass]
public class LzwDecoderTests
{
    [TestMethod]
    public void DecodesTheTiffSpecificationExample()
    {
        // TIFF 6.0 section 13: the bytes 7 7 7 8 8 7 7 6 6 encode as Clear, 7, 258, 8, 8, 258, 6, 6, EOI in 9-bit codes.
        byte[] encoded = PackNineBitCodes([256, 7, 258, 8, 8, 258, 6, 6, 257]);
        byte[] output = new byte[9];

        LzwDecoder.Decode(encoded, output).ShouldBe(9);

        output.ShouldBe([7, 7, 7, 8, 8, 7, 7, 6, 6]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(300)]
    [DataRow(5_000)]
    [DataRow(100_000)]
    [DataRow(1_048_576)]
    public void RoundTripsDataOfEverySizeIncludingTableResets(int length)
    {
        byte[] data = new byte[length];
        Random random = new(length);
        for (int i = 0; i < length; i++)
        {
            // Mostly zeros with bursts of noise, like a sparse raster, so the table fills and resets.
            data[i] = random.Next(10) == 0 ? (byte)random.Next(256) : (byte)0;
        }

        byte[] encoded = GeoTiffBuilder.LzwEncode(data);
        byte[] output = new byte[length];

        LzwDecoder.Decode(encoded, output).ShouldBe(length);

        output.ShouldBe(data);
    }

    [TestMethod]
    public void RoundTripsHighEntropyData()
    {
        byte[] data = new byte[200_000];
        new Random(7).NextBytes(data);

        byte[] output = new byte[data.Length];
        LzwDecoder.Decode(GeoTiffBuilder.LzwEncode(data), output).ShouldBe(data.Length);

        output.ShouldBe(data);
    }

    [TestMethod]
    public void StopsWhenTheOutputIsFullEvenMidString()
    {
        byte[] data = [1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2];
        byte[] encoded = GeoTiffBuilder.LzwEncode(data);
        byte[] output = new byte[7];

        LzwDecoder.Decode(encoded, output).ShouldBe(7);

        output.ShouldBe(data[..7]);
    }

    [TestMethod]
    public void ReportsShortStreams()
    {
        byte[] encoded = GeoTiffBuilder.LzwEncode([9, 9, 9, 9]);
        byte[] output = new byte[10];

        LzwDecoder.Decode(encoded, output).ShouldBe(4);
    }

    [TestMethod]
    public void RejectsCodesBeyondTheTable()
    {
        byte[] encoded = PackNineBitCodes([256, 7, 300, 257]);

        Should.Throw<InvalidDataException>(() => LzwDecoder.Decode(encoded, new byte[16]));
    }

    private static byte[] PackNineBitCodes(int[] codes)
    {
        List<byte> bytes = [];
        ulong buffer = 0;
        int bits = 0;
        foreach (int code in codes)
        {
            buffer = (buffer << 9) | (uint)code;
            bits += 9;
            while (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        if (bits > 0)
        {
            bytes.Add((byte)(buffer << (8 - bits)));
        }

        return [.. bytes];
    }
}
