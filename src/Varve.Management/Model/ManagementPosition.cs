// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Management.Model;

/// <summary>
/// A position of the fleet's system dataset (ADR 0163): what grants are
/// evaluated at, what a commit records it was authorised under, and a
/// management graph's version.
/// </summary>
public readonly record struct ManagementPosition
{
    /// <summary>A position: 0, the empty system dataset, or a commit's.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public ManagementPosition(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The position.</summary>
    public long Value { get; }

    /// <summary>Renders as the decimal position.</summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
