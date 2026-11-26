// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Varve.Delta.Model;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// Where a read is pinned (ADR 0096 point 1, ADR 0203): the head, a
/// position, or a time, which the server resolves to the greatest position
/// whose commit time is at or before it (spec 1.8 §4 I5).
/// </summary>
public readonly record struct AsOfSelector
{
    private AsOfSelector(FeedPosition? position, AsOfTime? time)
    {
        Position = position;
        Time = time;
    }

    /// <summary>The head: no <c>Varve-As-Of</c>.</summary>
    public static AsOfSelector Head => default;

    /// <summary>The position, when the selector is one.</summary>
    public FeedPosition? Position { get; }

    /// <summary>The time, when the selector is one.</summary>
    public AsOfTime? Time { get; }

    /// <summary>Whether this is the head.</summary>
    public bool IsHead => Position is null && Time is null;

    /// <summary>A position.</summary>
    public static AsOfSelector At(FeedPosition position) => new(position, null);

    /// <summary>A time, sent in UTC.</summary>
    public static AsOfSelector AtTime(AsOfTime time) => new(null, time);

    // The Varve-As-Of value, or null for the head.
    internal string? HeaderValue =>
        Position is FeedPosition position ? "position:" + position.Value.ToString(CultureInfo.InvariantCulture)
        : Time is AsOfTime time ? "time:" + time.ToString()
        : null;
}
