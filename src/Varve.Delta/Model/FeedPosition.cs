// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Delta.Model;

/// <summary>
/// A position as the delta format writes it: the number of closed commits a
/// dataset state includes, 0 for the empty dataset (<c>change-feed.md</c> §2).
/// </summary>
/// <remarks>
/// The format's own type, so that a consumer of the feed needs no store
/// (ADR 0128). It is ordered, and has no arithmetic: a position is read from
/// the feed, never computed. There is no conversion to or from <c>long</c> but
/// the constructor and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct FeedPosition : IComparable<FeedPosition>
{
    /// <summary>A position.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public FeedPosition(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number of closed commits before and including this position.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture, as the format writes it.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(FeedPosition other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/>.</summary>
    public static bool operator <(FeedPosition left, FeedPosition right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/>.</summary>
    public static bool operator >(FeedPosition left, FeedPosition right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> does not come after <paramref name="right"/>.</summary>
    public static bool operator <=(FeedPosition left, FeedPosition right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> does not come before <paramref name="right"/>.</summary>
    public static bool operator >=(FeedPosition left, FeedPosition right) => left.CompareTo(right) >= 0;
}
