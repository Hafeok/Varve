// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// The version of the object-store contract suite (ADR 0183): a counter from
/// 1 that rises when a case is added or made stricter, so that a store
/// verified by an older suite is verified again.
/// </summary>
public readonly record struct VerificationSuite : IComparable<VerificationSuite>
{
    /// <summary>A suite version.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is below 1.</exception>
    public VerificationSuite(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    /// <summary>The number.</summary>
    public int Value { get; }

    /// <summary>Renders as the number.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(VerificationSuite other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is the older suite.</summary>
    public static bool operator <(VerificationSuite left, VerificationSuite right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> is the newer suite.</summary>
    public static bool operator >(VerificationSuite left, VerificationSuite right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> is <paramref name="right"/> or older.</summary>
    public static bool operator <=(VerificationSuite left, VerificationSuite right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> is <paramref name="right"/> or newer.</summary>
    public static bool operator >=(VerificationSuite left, VerificationSuite right) => left.Value >= right.Value;
}
