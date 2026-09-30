// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// A number of bytes: a segment's length, a read's length, a segment size.
/// </summary>
/// <remarks>
/// ADR 0065, <c>StorageMemberTypes</c>. There is no conversion to or from
/// <c>long</c> but the constructor and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct ByteCount
{
    /// <summary>A number of bytes.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public ByteCount(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number of bytes.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
