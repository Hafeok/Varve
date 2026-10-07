// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// What bounds a request (ADR 0095): how long a query may evaluate, how long a
/// pin may be held, how many bytes a read may write, how large a request body
/// may be, and how often a live feed sends a heartbeat.
/// </summary>
public readonly record struct ProtocolLimits
{
    /// <summary>The limits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A duration or a size is not positive.</exception>
    public ProtocolLimits(TimeSpan queryTimeout, TimeSpan pinnedReadLifetime, ByteCount resultSizeCap, ByteCount maxRequestBody, TimeSpan feedHeartbeat)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(queryTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pinnedReadLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resultSizeCap.Value, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxRequestBody.Value, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(feedHeartbeat, TimeSpan.Zero);
        QueryTimeout = queryTimeout;
        PinnedReadLifetime = pinnedReadLifetime;
        ResultSizeCap = resultSizeCap;
        MaxRequestBody = maxRequestBody;
        FeedHeartbeat = feedHeartbeat;
    }

    /// <summary>
    /// The defaults the server documents: 30 seconds of evaluation, a pin held
    /// at most two minutes, 1 GiB of result, 100 MiB of request body, and a
    /// heartbeat every 15 seconds.
    /// </summary>
    public static ProtocolLimits Default { get; } = new(
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), new ByteCount(1L << 30), new ByteCount(100L << 20), TimeSpan.FromSeconds(15));

    /// <summary>How long a query may evaluate.</summary>
    public TimeSpan QueryTimeout { get; }

    /// <summary>How long a pin or an as-of view may be held, writing the response included.</summary>
    public TimeSpan PinnedReadLifetime { get; }

    /// <summary>How many bytes a read may write.</summary>
    public ByteCount ResultSizeCap { get; }

    /// <summary>How many bytes a request body may have.</summary>
    public ByteCount MaxRequestBody { get; }

    /// <summary>How often a live feed writes a heartbeat.</summary>
    public TimeSpan FeedHeartbeat { get; }
}
