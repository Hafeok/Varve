// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// A version of the derived format (storage format §7, ADR 0072): what a
/// reader record says its reader reads (ADR 0178). Derived data is rebuilt,
/// not read for ever, so a reader of an older version finds a newer file a
/// cache miss.
/// </summary>
/// <remarks>
/// There is no conversion to or from <c>int</c> but the constructor and
/// <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct DerivedVersion : IComparable<DerivedVersion>
{
    /// <summary>A version.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is below 1.</exception>
    public DerivedVersion(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    /// <summary>Derived format 4, what a reader record that does not say reads: one written before #133.</summary>
    public static DerivedVersion Format4 { get; } = new(4);

    /// <summary>The newest derived version this build reads: 6, the checked kinds' (ADR 0213); 5 is an extent's (ADR 0177).</summary>
    public static DerivedVersion ThisBuildReads { get; } = new(6);

    /// <summary>The version's number.</summary>
    public int Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(DerivedVersion other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is older than <paramref name="right"/>.</summary>
    public static bool operator <(DerivedVersion left, DerivedVersion right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</summary>
    public static bool operator >(DerivedVersion left, DerivedVersion right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> is not newer than <paramref name="right"/>.</summary>
    public static bool operator <=(DerivedVersion left, DerivedVersion right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> is not older than <paramref name="right"/>.</summary>
    public static bool operator >=(DerivedVersion left, DerivedVersion right) => left.Value >= right.Value;
}
