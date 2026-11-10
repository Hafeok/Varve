// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// How many positions one log is behind another: a projection behind its
/// head, a follower behind the tail, a validation projection behind the
/// dataset.
/// </summary>
/// <remarks>
/// A count of positions, not a position.
/// </remarks>
public readonly record struct PositionLag : IComparable<PositionLag>
{
    /// <summary>The lag <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public PositionLag(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(PositionLag other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(PositionLag left, PositionLag right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is more than <paramref name="right"/>.</summary>
    public static bool operator >(PositionLag left, PositionLag right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is not more than <paramref name="right"/>.</summary>
    public static bool operator <=(PositionLag left, PositionLag right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is not less than <paramref name="right"/>.</summary>
    public static bool operator >=(PositionLag left, PositionLag right) => left.CompareTo(right) >= 0;
}
