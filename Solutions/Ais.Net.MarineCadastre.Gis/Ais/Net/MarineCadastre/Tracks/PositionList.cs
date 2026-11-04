// <copyright file="PositionList.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections;

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// A read-only view over a track's positions. It does not implement <see cref="IList{T}"/> or
/// <see cref="ICollection{T}"/> and does not expose the array, so callers cannot cast it back to
/// something they could change.
/// </summary>
/// <param name="points">The positions; the list takes ownership of the array.</param>
internal sealed class PositionList(Position[] points) : IReadOnlyList<Position>
{
    /// <inheritdoc/>
    public int Count => points.Length;

    /// <inheritdoc/>
    public Position this[int index]
    {
        get
        {
            if ((uint)index >= (uint)points.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the list.");
            }

            return points[index];
        }
    }

    /// <inheritdoc/>
    public IEnumerator<Position> GetEnumerator() => ((IEnumerable<Position>)points).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
