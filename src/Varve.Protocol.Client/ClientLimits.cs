// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client;

/// <summary>
/// What bounds an outbound request (ADR 0103): how long the whole exchange
/// may take, and how many bytes of response are read before the answer is a
/// failure rather than a truncated result.
/// </summary>
public readonly record struct ClientLimits
{
    /// <summary>The limits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout or the cap is not positive.</exception>
    public ClientLimits(TimeSpan timeout, long maxResponseBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxResponseBytes, 0);
        Timeout = timeout;
        MaxResponseBytes = maxResponseBytes;
    }

    /// <summary>Thirty seconds, and 100 MiB of response.</summary>
    public static ClientLimits Default { get; } = new(TimeSpan.FromSeconds(30), 100L << 20);

    /// <summary>How long the exchange may take, connection to last byte.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>How many bytes of response are read before the response is a failure: a count of bytes, so that the client needs no store (ADR 0128).</summary>
    public long MaxResponseBytes { get; }

    /// <summary>
    /// The source of each logical write's <c>Idempotency-Key</c> (ADR 0246),
    /// asked once per write and reused on every retry of it, so that the
    /// write commits once: a key unique to the caller, 1 to 255 printable
    /// ASCII characters. By default, 128 random bits in lowercase hex from the
    /// operating system. A host may inject its own; null sends no key, and
    /// then no write is retried.
    /// </summary>
    public Func<string>? IdempotencyKeys { get; init; } = RandomKey;

    /// <summary>
    /// How many times a keyed write is sent again with its key after a lost
    /// response, a <c>503</c>, or <c>409 idempotency-key-in-flight</c>
    /// (ADR 0246). Three by default; a write without a key is never retried.
    /// </summary>
    public int WriteRetries { get; init; } = 3;

    // ADR 0246, ruling ClientSendsAndRetriesWithAKey: ADR 0011 bans ambient
    // randomness so that nothing in the log depends on it; the key is the
    // client's input to a write, as its body is, and the log records only
    // its hash, which no comparison of two stores replays.
#pragma warning disable RS0030 // ADR 0246: a client's key is its own input, drawn outside the store.
    private static string RandomKey() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
#pragma warning restore RS0030
}
