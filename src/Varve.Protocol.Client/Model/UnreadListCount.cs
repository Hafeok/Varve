// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// The value of <c>Varve-Unread-Lists</c> (ADR 0197, amended 2026-11-26):
/// how many asserted list cells the rules leave unread, as of the answer's
/// position, in the graphs the query read and the TBox.
/// </summary>
public readonly record struct UnreadListCount
{
    /// <summary>The count <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public UnreadListCount(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The count; 0 when the header is absent.</summary>
    public int Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
