// <copyright file="TransitCountStatistics.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Globalization;
using System.Xml.Linq;

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// Band statistics the producing tool stored in the raster, covering the cells that hold data. Present
/// in the MarineCadastre files; absent from rasters written without them.
/// </summary>
/// <param name="Count">How many cells hold data, or <see langword="null"/> when not recorded.</param>
/// <param name="Minimum">The smallest count.</param>
/// <param name="Maximum">The largest count.</param>
/// <param name="Mean">The mean count over cells with data.</param>
/// <param name="StandardDeviation">The standard deviation of counts over cells with data.</param>
public sealed record TransitCountStatistics(long? Count, double Minimum, double Maximum, double Mean, double StandardDeviation)
{
    /// <summary>
    /// Reads the statistics from GDAL's metadata XML, when it has them.
    /// </summary>
    /// <param name="gdalMetadata">The <c>GDAL_METADATA</c> tag text.</param>
    /// <returns>The statistics, or <see langword="null"/> when the minimum and maximum are not recorded.</returns>
    internal static TransitCountStatistics? Parse(string? gdalMetadata)
    {
        if (string.IsNullOrWhiteSpace(gdalMetadata))
        {
            return null;
        }

        Dictionary<string, double> items = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (XElement item in XDocument.Parse(gdalMetadata).Descendants("Item"))
            {
                string? name = item.Attribute("name")?.Value;
                if (name is not null && name.StartsWith("STATISTICS_", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    items[name] = value;
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (!items.TryGetValue("STATISTICS_MINIMUM", out double minimum) || !items.TryGetValue("STATISTICS_MAXIMUM", out double maximum))
        {
            return null;
        }

        return new TransitCountStatistics(
            items.TryGetValue("STATISTICS_COUNT", out double count) ? (long)count : null,
            minimum,
            maximum,
            items.GetValueOrDefault("STATISTICS_MEAN", double.NaN),
            items.GetValueOrDefault("STATISTICS_STDDEV", double.NaN));
    }
}
