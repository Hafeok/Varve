// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// A writer epoch: the number a writer claims in the writer record and
/// carries in every chunk it creates (ADR 0136). The first is 1.
/// </summary>
public readonly record struct WriterEpoch : IComparable<WriterEpoch>
{
    /// <summary>An epoch.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is below 1.</exception>
    public WriterEpoch(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    /// <summary>The number.</summary>
    public long Value { get; }

    /// <summary>The epoch after this one.</summary>
    public WriterEpoch Next => new(checked(Value + 1));

    /// <summary>Renders as the number.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(WriterEpoch other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is the earlier epoch.</summary>
    public static bool operator <(WriterEpoch left, WriterEpoch right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> is the later epoch.</summary>
    public static bool operator >(WriterEpoch left, WriterEpoch right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> is not the later epoch.</summary>
    public static bool operator <=(WriterEpoch left, WriterEpoch right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> is not the earlier epoch.</summary>
    public static bool operator >=(WriterEpoch left, WriterEpoch right) => left.Value >= right.Value;
}
