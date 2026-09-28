// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// The time a commit was closed, as the dataset's clock read it.
/// </summary>
/// <remarks>
/// ADR 0065, <c>CommitTimestampIsAWrapper</c>. Ordered, because I5 makes
/// commit timestamps monotone in position. There is no conversion to or from
/// <see cref="DateTimeOffset"/> but the constructor and <see cref="Value"/>
/// (DD0015).
/// </remarks>
public readonly record struct CommitTimestamp : IComparable<CommitTimestamp>
{
    /// <summary>A commit timestamp.</summary>
    public CommitTimestamp(DateTimeOffset value) => Value = value;

    /// <summary>The instant.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>Renders as the round-trip (<c>O</c>) form of the instant, in the invariant culture.</summary>
    public override string ToString() => Value.ToString("O", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(CommitTimestamp other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/>.</summary>
    public static bool operator <(CommitTimestamp left, CommitTimestamp right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/>.</summary>
    public static bool operator >(CommitTimestamp left, CommitTimestamp right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> does not come after <paramref name="right"/>.</summary>
    public static bool operator <=(CommitTimestamp left, CommitTimestamp right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> does not come before <paramref name="right"/>.</summary>
    public static bool operator >=(CommitTimestamp left, CommitTimestamp right) => left.CompareTo(right) >= 0;
}
