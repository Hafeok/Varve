// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Http;

/// <summary>
/// Opens the view a read is answered from: the pinned head, or the as-of
/// position <c>Varve-As-Of</c> selects (ADRs 0095, 0096). The view is held for
/// the response and released when the response has finished.
/// </summary>
internal static class Reads
{
    /// <summary>
    /// The position a read would describe, without pinning: the head, or the
    /// resolved as-of position. Writes the error response itself and answers
    /// <see langword="null"/> when the selector is malformed or resolves to nothing.
    /// </summary>
    internal static async Task<Position?> ResolveAsync(HttpContext context, Dataset dataset)
    {
        StringValues header = context.Request.Headers[Preconditions.AsOfHeader];

        if (StringValues.IsNullOrEmpty(header))
        {
            return dataset.Head;
        }

        if (header.Count != 1 || !AsOf.TryParse(header[0], out AsOf asOf))
        {
            await HttpProblems.BadRequest(context, "Varve-As-Of is position:<n> or time:<RFC 3339 date-time> with at most seven fractional digits.").ConfigureAwait(false);
            return null;
        }

        return await ResolveAsync(context, dataset, asOf).ConfigureAwait(false);
    }

    /// <summary>The position a selector resolves to, or the <c>404</c> written for one with none.</summary>
    internal static async Task<Position?> ResolveAsync(HttpContext context, Dataset dataset, AsOf asOf)
    {
        Position head = dataset.Head;

        if (asOf.Kind == AsOfKind.Position)
        {
            if (asOf.Position > head)
            {
                Preconditions.Describe(context.Response, head);
                await HttpProblems.WriteAsync(context, ProblemType.PositionNotReached, "The head is " + head.ToString() + ".",
                    members => members.Number("headPosition", head.Value)).ConfigureAwait(false);
                return null;
            }

            return asOf.Position;
        }

        Position resolved = dataset.PositionAt(asOf.Time);

        if (resolved.Value == 0)
        {
            await HttpProblems.WriteAsync(context, ProblemType.BeforeFirstCommit).ConfigureAwait(false);
            return null;
        }

        return resolved;
    }

    /// <summary>
    /// Answers <c>If-None-Match</c>, and <c>If-Modified-Since</c> when there is
    /// no <c>If-None-Match</c> (RFC 9110 §13.1.3): <see langword="true"/> when
    /// the response has been written, as a <c>304</c> or a <c>400</c>. Asked
    /// before any pin is taken (ADRs 0096, 0119).
    /// </summary>
    internal static async Task<bool> NotModifiedAsync(HttpContext context, Dataset dataset, Position described)
    {
        HttpRequest request = context.Request;

        switch (Preconditions.Read(request.Headers.IfNoneMatch, out Preconditions.TagList tags))
        {
            case TagCondition.Absent:
                break;
            case TagCondition.Malformed:
                await HttpProblems.BadRequest(context, "If-None-Match names positions, as the ETag of every response does.").ConfigureAwait(false);
                return true;
            case TagCondition.Any:
            case TagCondition.Positions when tags.Contains(described):
                NotModified(context, dataset, described);
                return true;
            default:
                return false;
        }

        if (request.Headers.IfModifiedSince.Count == 1
            && HeaderUtilities.TryParseDate(request.Headers.IfModifiedSince[0], out DateTimeOffset since)
            && LastModified(dataset, described) is DateTimeOffset modified
            && modified <= since)
        {
            NotModified(context, dataset, described);
            return true;
        }

        return false;
    }

    private static void NotModified(HttpContext context, Dataset dataset, Position described)
    {
        Preconditions.Describe(context.Response, described);
        DescribeFreshness(context, dataset, described);
        context.Response.StatusCode = StatusCodes.Status304NotModified;
    }

    /// <summary>
    /// The commit timestamp of <paramref name="position"/>, to the second, as
    /// <c>Last-Modified</c> carries it; none for position 0, which no commit made.
    /// </summary>
    internal static DateTimeOffset? LastModified(Dataset dataset, Position position)
    {
        if (position.Value == 0 || position > dataset.Head)
        {
            return null;
        }

        DateTimeOffset at = dataset.TimestampAt(position).Value.ToUniversalTime();
        return new DateTimeOffset(at.Ticks - (at.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    /// <summary>
    /// <c>Cache-Control</c> and <c>Last-Modified</c> for a response at
    /// <paramref name="position"/> (ADR 0119): a read selected by position is
    /// at a closed position and never changes, so it is immutable; a head
    /// read, or one selected by time, which a later commit at or before the
    /// instant could move, revalidates.
    /// </summary>
    internal static void DescribeFreshness(HttpContext context, Dataset dataset, Position position)
    {
        HttpResponse response = context.Response;
        response.Headers.CacheControl = IsClosedSelector(context) ? "private, max-age=31536000, immutable" : "no-cache";

        if (LastModified(dataset, position) is DateTimeOffset modified)
        {
            response.Headers.LastModified = HeaderUtilities.FormatDate(modified);
        }
    }

    // A Varve-As-Of of the form position:<n> names a closed position.
    private static bool IsClosedSelector(HttpContext context)
    {
        StringValues header = context.Request.Headers[Preconditions.AsOfHeader];
        return header.Count == 1 && AsOf.TryParse(header[0], out AsOf asOf) && asOf.Kind == AsOfKind.Position;
    }

    /// <summary>
    /// The view for a read at <paramref name="position"/>: a pin when that is
    /// the head, an as-of view otherwise. It is registered with the response,
    /// which disposes it after the last byte or an abort (ADR 0095). The
    /// response describes the position the view is at.
    /// </summary>
    internal static async Task<DatasetView> OpenAsync(HttpContext context, Dataset dataset, Position position, bool asOf, CancellationToken cancellationToken)
    {
        DatasetView view = asOf
            ? await dataset.AsOfAsync(position, cancellationToken).ConfigureAwait(false)
            : dataset.Pin();
        context.Response.RegisterForDispose(view);
        Preconditions.Describe(context.Response, view.Position);
        DescribeFreshness(context, dataset, view.Position);
        return view;
    }

    /// <summary>
    /// The cache headers of a representation at a closed position named in
    /// the path (ADR 0119): immutable by the model, private because the
    /// content is per caller.
    /// </summary>
    internal static void DescribeClosed(HttpContext context, Dataset dataset, Position position)
    {
        HttpResponse response = context.Response;
        response.Headers.CacheControl = "private, max-age=31536000, immutable";

        if (LastModified(dataset, position) is DateTimeOffset modified)
        {
            response.Headers.LastModified = HeaderUtilities.FormatDate(modified);
        }
    }

    /// <summary>Whether the request carried <c>Varve-As-Of</c>.</summary>
    internal static bool IsAsOf(HttpContext context) => !StringValues.IsNullOrEmpty(context.Request.Headers[Preconditions.AsOfHeader]);

}
