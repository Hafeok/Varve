// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>A resolved range: <c>(From, To]</c>, or <c>(From, …)</c> live when <see cref="To"/> is absent.</summary>
internal readonly record struct FeedRange(Position From, Position? To);

/// <summary>
/// The feed's and the diff's range parameters, resolved asymmetrically as
/// Delta Sharing's change data feed resolves them (ADR 0097,
/// <c>change-feed.md</c> §4): a start time to the earliest commit at or after
/// it, an end time to the latest at or before it; <c>from</c> exclusive,
/// <c>to</c> inclusive.
/// </summary>
internal static class Ranges
{
    internal static async Task<FeedRange?> ResolveAsync(HttpContext context, Dataset dataset, bool endDefaultsToHead, string? resume)
    {
        IQueryCollection query = context.Request.Query;
        Position head = dataset.Head;
        Position from;

        if (resume is not null)
        {
            if (!TryPosition(resume, out from))
            {
                await HttpProblems.BadRequest(context, "Last-Event-ID is a position.").ConfigureAwait(false);
                return null;
            }
        }
        else if (await StartAsync(context, dataset, query["from"], query["fromTime"]).ConfigureAwait(false) is Position start)
        {
            from = start;
        }
        else
        {
            return null;
        }

        StringValues to = query["to"];
        StringValues toTime = query["toTime"];

        if (to.Count + toTime.Count > 1)
        {
            await HttpProblems.BadRequest(context, "Give to or toTime, once.").ConfigureAwait(false);
            return null;
        }

        if (to.Count == 1)
        {
            if (!TryPosition(to[0], out Position end))
            {
                await HttpProblems.BadRequest(context, "to is a position.").ConfigureAwait(false);
                return null;
            }

            if (end > head)
            {
                Preconditions.Describe(context.Response, head);
                await HttpProblems.WriteAsync(context, ProblemType.PositionNotReached, "to is after the head, " + head.ToString() + ".",
                    members => members.Number("headPosition", head.Value)).ConfigureAwait(false);
                return null;
            }

            return new FeedRange(from, end);
        }

        if (toTime.Count == 1)
        {
            if (!Instants.TryParse(toTime[0], out CommitTimestamp end))
            {
                await HttpProblems.BadRequest(context, "toTime is an RFC 3339 date-time with at most seven fractional digits.").ConfigureAwait(false);
                return null;
            }

            // The latest commit at or before the instant; 0 when none is.
            return new FeedRange(from, dataset.PositionAt(end));
        }

        return new FeedRange(from, endDefaultsToHead ? head : null);
    }

    private static async Task<Position?> StartAsync(HttpContext context, Dataset dataset, StringValues from, StringValues fromTime)
    {
        if (from.Count + fromTime.Count > 1)
        {
            await HttpProblems.BadRequest(context, "Give from or fromTime, once.").ConfigureAwait(false);
            return null;
        }

        if (from.Count == 1)
        {
            if (!TryPosition(from[0], out Position start))
            {
                await HttpProblems.BadRequest(context, "from is a position.").ConfigureAwait(false);
                return null;
            }

            return start;
        }

        if (fromTime.Count == 1)
        {
            if (!Instants.TryParse(fromTime[0], out CommitTimestamp instant))
            {
                await HttpProblems.BadRequest(context, "fromTime is an RFC 3339 date-time with at most seven fractional digits.").ConfigureAwait(false);
                return null;
            }

            return StartAt(dataset, instant);
        }

        return new Position(0);
    }

    /// <summary>
    /// The exclusive start for the earliest commit at or after an instant:
    /// the greatest position strictly before it. By I5 (monotone time) that is
    /// <c>PositionAt(t − 1 tick)</c>; ticks are the store's resolution. With no
    /// commit at or after the instant it is the head, where a feed waits.
    /// </summary>
    internal static Position StartAt(Dataset dataset, CommitTimestamp instant)
    {
        DateTimeOffset value = instant.Value;
        return value.UtcTicks == DateTimeOffset.MinValue.UtcTicks
            ? new Position(0)
            : dataset.PositionAt(new CommitTimestamp(value.AddTicks(-1)));
    }

    private static bool TryPosition(string? text, out Position position)
    {
        position = default;

        if (text is null || !Instants.IsDecimal(text) || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            return false;
        }

        position = new Position(value);
        return true;
    }
}
