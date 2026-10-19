// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// A version of the projection catalog (ADR 0160): the catalog's next set
/// sequence, which every swap advances. An extent set whose name carries
/// sequence <c>s</c> is named only by versions after <c>s</c>, and a reader
/// record announces the versions its reads hold.
/// </summary>
/// <remarks>
/// There is no conversion to or from <c>long</c> but the constructor and
/// <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct CatalogVersion : IComparable<CatalogVersion>
{
    /// <summary>A version.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public CatalogVersion(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The version that stands for every version: what a record of an unknown format holds (ADR 0160).</summary>
    public static CatalogVersion Newest { get; } = new(long.MaxValue);

    /// <summary>The version's number.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(CatalogVersion other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is older than <paramref name="right"/>.</summary>
    public static bool operator <(CatalogVersion left, CatalogVersion right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</summary>
    public static bool operator >(CatalogVersion left, CatalogVersion right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> is not newer than <paramref name="right"/>.</summary>
    public static bool operator <=(CatalogVersion left, CatalogVersion right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> is not older than <paramref name="right"/>.</summary>
    public static bool operator >=(CatalogVersion left, CatalogVersion right) => left.Value >= right.Value;
}
