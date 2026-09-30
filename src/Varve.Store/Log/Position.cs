// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// A position in the log: the number of closed commits a dataset state
/// includes. Position 0 is the empty dataset; commit positions start at 1 and
/// are dense (I1).
/// </summary>
/// <remarks>
/// ADR 0065, <c>PositionIsAWrapper</c>. A position is not a count or a byte
/// offset, and a <c>long</c> could be either. It is ordered, because I1 makes
/// positions a total order, and it has no public arithmetic: the next position
/// is the sequencer's to assign, through <see cref="Next"/>, which is internal.
/// There is no conversion to or from <c>long</c> but the constructor and
/// <see cref="Value"/>, so every crossing is visible (DD0015).
/// </remarks>
public readonly record struct Position : IComparable<Position>
{
    /// <summary>A position.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public Position(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number of closed commits before and including this position.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(Position other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/>.</summary>
    public static bool operator <(Position left, Position right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/>.</summary>
    public static bool operator >(Position left, Position right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> does not come after <paramref name="right"/>.</summary>
    public static bool operator <=(Position left, Position right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> does not come before <paramref name="right"/>.</summary>
    public static bool operator >=(Position left, Position right) => left.CompareTo(right) >= 0;

    /// <summary>The position after this one: the sequencer's assignment, and nobody else's.</summary>
    internal Position Next() => new(Value + 1);
}
