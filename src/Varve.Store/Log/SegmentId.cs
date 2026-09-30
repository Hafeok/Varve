// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// The number of a log segment in its storage.
/// </summary>
/// <remarks>
/// ADR 0065, <c>StorageMemberTypes</c>. Ordered, because ADR 0040 numbers
/// segments ascending. There is no conversion to or from <c>int</c> but the
/// constructor and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct SegmentId : IComparable<SegmentId>
{
    /// <summary>A segment id.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public SegmentId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The segment's number.</summary>
    public int Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(SegmentId other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/>.</summary>
    public static bool operator <(SegmentId left, SegmentId right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/>.</summary>
    public static bool operator >(SegmentId left, SegmentId right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> does not come after <paramref name="right"/>.</summary>
    public static bool operator <=(SegmentId left, SegmentId right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> does not come before <paramref name="right"/>.</summary>
    public static bool operator >=(SegmentId left, SegmentId right) => left.CompareTo(right) >= 0;
}
