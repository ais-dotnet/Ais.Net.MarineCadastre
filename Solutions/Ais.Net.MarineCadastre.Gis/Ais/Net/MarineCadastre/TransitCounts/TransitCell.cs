// <copyright file="TransitCell.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// One cell of a MarineCadastre transit-count raster that holds data.
/// </summary>
/// <param name="Column">The column of the cell at its level, counted from the west edge.</param>
/// <param name="Row">The row of the cell at its level, counted from the north edge.</param>
/// <param name="Centre">The geographic position of the centre of the cell.</param>
/// <param name="Transits">The number of vessel transits that crossed the cell in the period of the raster.</param>
public readonly record struct TransitCell(int Column, int Row, Position Centre, uint Transits);
