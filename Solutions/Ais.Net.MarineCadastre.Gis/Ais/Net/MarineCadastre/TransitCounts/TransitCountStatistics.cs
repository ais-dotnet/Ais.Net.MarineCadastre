// <copyright file="TransitCountStatistics.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Globalization;
using System.Xml.Linq;

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// Band statistics that the tool that made the raster stored in it. They cover the cells that hold
/// data. The MarineCadastre files have them. Rasters from other tools can have none.
/// </summary>
/// <param name="Count">The number of cells that hold data, or <see langword="null"/> if the file does not record it.</param>
/// <param name="Minimum">The smallest count.</param>
/// <param name="Maximum">The largest count.</param>
/// <param name="Mean">The mean count over the cells with data, or <see langword="null"/> if the file does not record it.</param>
/// <param name="StandardDeviation">The standard deviation of the counts over the cells with data, or <see langword="null"/> if the file does not record it.</param>
public sealed record TransitCountStatistics(long? Count, double Minimum, double Maximum, double? Mean, double? StandardDeviation)
{
    /// <summary>
    /// Reads the statistics from the GDAL metadata XML, if it has them.
    /// </summary>
    /// <param name="gdalMetadata">The text of the <c>GDAL_METADATA</c> tag.</param>
    /// <returns>The statistics, or <see langword="null"/> if the metadata does not record the minimum and the maximum.</returns>
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
            items.TryGetValue("STATISTICS_MEAN", out double mean) ? mean : null,
            items.TryGetValue("STATISTICS_STDDEV", out double standardDeviation) ? standardDeviation : null);
    }
}
