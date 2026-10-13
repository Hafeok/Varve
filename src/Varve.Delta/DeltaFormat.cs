// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Delta;

/// <summary>
/// A version of <c>application/vnd.varve.delta</c> (<c>docs/spec/change-feed.md</c>),
/// under the read-forever rule (ADR 0128): every release reads every version
/// up to <see cref="Current"/>, and a version's meaning never changes.
/// </summary>
/// <remarks>
/// Ordered, as <c>FormatVersion</c> of the store is. There is no conversion to
/// or from <c>int</c> but the constructor and <see cref="Value"/> (DD0015).
/// The release descriptor's <c>delta-format</c> line is checked against
/// <see cref="Current"/> by reading this file (ADR 0102, amended 2026-10-13).
/// </remarks>
public readonly record struct DeltaFormat : IComparable<DeltaFormat>
{
    /// <summary>A format version.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not positive.</exception>
    public DeltaFormat(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    /// <summary>The version this build writes, and the newest it reads: version 1.</summary>
    public static DeltaFormat Current => new(1);

    /// <summary>The media type without its parameter.</summary>
    public const string MediaTypeName = "application/vnd.varve.delta";

    /// <summary>The version number.</summary>
    public int Value { get; }

    /// <summary>The media type of this version, with its <c>version</c> parameter.</summary>
    public string MediaType => MediaTypeName + "; version=" + Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(DeltaFormat other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is older than <paramref name="right"/>.</summary>
    public static bool operator <(DeltaFormat left, DeltaFormat right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</summary>
    public static bool operator >(DeltaFormat left, DeltaFormat right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is not newer than <paramref name="right"/>.</summary>
    public static bool operator <=(DeltaFormat left, DeltaFormat right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is not older than <paramref name="right"/>.</summary>
    public static bool operator >=(DeltaFormat left, DeltaFormat right) => left.CompareTo(right) >= 0;
}
