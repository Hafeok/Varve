// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Rdf;

/// <summary>
/// A number of quads: what a <see cref="CardinalityEstimate"/> counts.
/// </summary>
/// <remarks>
/// ADR 0065. A count of quads is not a position, a byte length or an index,
/// and a <c>long</c> could be any of them. It is in <c>Varve.Rdf</c> because a
/// cardinality is a property of a quad source, which layer 1 defines (ADR
/// 0049). There is no conversion to or from <c>long</c> but the constructor
/// and <see cref="Value"/>, so every crossing is visible (DD0015).
/// </remarks>
public readonly record struct QuadCount
{
    /// <summary>A count of quads.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public QuadCount(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The count.</summary>
    public long Value { get; }
}
