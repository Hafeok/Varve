// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A writer epoch (ADR 0136): the number of claims of a dataset's writer
/// record, each claim fencing the one before it.
/// </summary>
public readonly record struct WriterEpoch : IComparable<WriterEpoch>
{
    /// <summary>The epoch <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public WriterEpoch(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(WriterEpoch other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(WriterEpoch left, WriterEpoch right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is more than <paramref name="right"/>.</summary>
    public static bool operator >(WriterEpoch left, WriterEpoch right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is not more than <paramref name="right"/>.</summary>
    public static bool operator <=(WriterEpoch left, WriterEpoch right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is not less than <paramref name="right"/>.</summary>
    public static bool operator >=(WriterEpoch left, WriterEpoch right) => left.CompareTo(right) >= 0;
}
