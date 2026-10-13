// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client;

/// <summary>
/// How <see cref="SparqlHttpClient.FollowAsync"/> follows (ADR 0129): the feed's
/// graph filter, and how it waits between attempts after a failure it rides
/// out, measured on <see cref="Clock"/>.
/// </summary>
public sealed class FollowOptions
{
    /// <summary>Options whose waits are measured on <paramref name="clock"/>.</summary>
    public FollowOptions(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
    }

    /// <summary>The clock the waits are measured on.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Only changes in this graph (an absolute IRI, or <c>default</c>), as the feed's <c>graph</c> parameter; null for all.</summary>
    public string? Graph { get; init; }

    /// <summary>The first wait after a failure; each further one doubles it.</summary>
    public TimeSpan FirstBackoff { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest wait between attempts.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the live tail may stay silent, not even a heartbeat, before it
    /// is taken for a dead connection and opened again. The server's heartbeat
    /// is fifteen seconds by default (ADR 0114).
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A source of jitter, answering a number in [0, 1): each wait is scaled by
    /// a factor in [0.5, 1) drawn from it, so followers that failed together do
    /// not retry together. Null waits exactly; the client draws no ambient
    /// randomness of its own (ADR 0011).
    /// </summary>
    public Func<double>? Jitter { get; init; }
}

/// <summary>
/// The follow cannot continue: the server refused it for a reason that waiting
/// will not change (ADR 0129), with the status and the problem body.
/// </summary>
public sealed class FollowRefusedException : Exception
{
    /// <summary>A refusal with <paramref name="status"/> and the problem <paramref name="problem"/>.</summary>
    public FollowRefusedException(int status, string problem)
        : base("The commits resource refused the follow: " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + problem)
    {
        Status = status;
        Problem = problem;
    }

    /// <summary>Creates one with a message.</summary>
    public FollowRefusedException(string message)
        : base(message)
    {
        Problem = string.Empty;
    }

    /// <summary>Creates one with a message and the error underneath.</summary>
    public FollowRefusedException(string message, Exception inner)
        : base(message, inner)
    {
        Problem = string.Empty;
    }

    /// <summary>Creates one with no message.</summary>
    public FollowRefusedException()
    {
        Problem = string.Empty;
    }

    /// <summary>The HTTP status.</summary>
    public int Status { get; }

    /// <summary>The response body: an RFC 9457 problem as JSON.</summary>
    public string Problem { get; }
}
