// <copyright file="TransitCell.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// One cell of a MarineCadastre transit-count raster that holds data.
/// </summary>
/// <param name="Column">The cell's column at the level it was read from, counting from the west edge.</param>
/// <param name="Row">The cell's row at the level it was read from, counting from the north edge.</param>
/// <param name="Centre">The geographic position of the cell's centre.</param>
/// <param name="Transits">How many vessel transits crossed the cell in the period the raster covers.</param>
public readonly record struct TransitCell(int Column, int Row, Position Centre, uint Transits);
