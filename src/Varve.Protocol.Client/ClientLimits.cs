// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.Log;

namespace Varve.Protocol.Client;

/// <summary>
/// What bounds an outbound request (ADR 0102): how long the whole exchange
/// may take, and how many bytes of response are read before the answer is a
/// failure rather than a truncated result.
/// </summary>
public readonly record struct ClientLimits
{
    /// <summary>The limits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout or the cap is not positive.</exception>
    public ClientLimits(TimeSpan timeout, ByteCount maxResponseBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxResponseBytes.Value, 0);
        Timeout = timeout;
        MaxResponseBytes = maxResponseBytes;
    }

    /// <summary>Thirty seconds, and 100 MiB of response.</summary>
    public static ClientLimits Default { get; } = new(TimeSpan.FromSeconds(30), new ByteCount(100L << 20));

    /// <summary>How long the exchange may take, connection to last byte.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>How many bytes of response are read before the response is a failure.</summary>
    public ByteCount MaxResponseBytes { get; }
}
