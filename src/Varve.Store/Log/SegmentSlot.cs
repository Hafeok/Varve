// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>
/// A backend's own place in a log segment — the object-storage backend's
/// chunk number — as <see cref="ISegmentCursor"/> names it, and the catalog
/// records it (ADR 0139). Its meaning is the backend's; the store only
/// carries it.
/// </summary>
/// <remarks>
/// There is no conversion to or from <c>long</c> but the constructor and
/// <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct SegmentSlot
{
    /// <summary>A slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public SegmentSlot(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The slot's number.</summary>
    public long Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
