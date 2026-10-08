// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
                await HttpProblems.WriteAsync(context, StatusCodes.Status404NotFound, ProblemType.PositionNotReached,
                    "The position is after the head.", "The head is " + head.ToString() + ".").ConfigureAwait(false);
                return null;
            }

            return asOf.Position;
        }

        Position resolved = dataset.PositionAt(asOf.Time);

        if (resolved.Value == 0)
        {
            await HttpProblems.WriteAsync(context, StatusCodes.Status404NotFound, ProblemType.BeforeFirstCommit,
                "The time is before the first commit.").ConfigureAwait(false);
            return null;
        }

        return resolved;
    }

    /// <summary>
    /// Answers <c>If-None-Match</c>: <see langword="true"/> when the response
    /// has been written, as a <c>304</c> or a <c>400</c>. Asked before any pin
    /// is taken (ADR 0096).
    /// </summary>
    internal static async Task<bool> NotModifiedAsync(HttpContext context, Position described)
    {
        switch (Preconditions.Read(context.Request.Headers.IfNoneMatch, out Preconditions.TagList tags))
        {
            case TagCondition.Absent:
                return false;
            case TagCondition.Malformed:
                await HttpProblems.BadRequest(context, "If-None-Match names positions, as the ETag of every response does.").ConfigureAwait(false);
                return true;
            case TagCondition.Any:
            case TagCondition.Positions when tags.Contains(described):
                Preconditions.Describe(context.Response, described);
                SetVary(context.Response);
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return true;
            default:
                return false;
        }
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
        SetVary(context.Response);
        return view;
    }

    /// <summary>Whether the request carried <c>Varve-As-Of</c>.</summary>
    internal static bool IsAsOf(HttpContext context) => !StringValues.IsNullOrEmpty(context.Request.Headers[Preconditions.AsOfHeader]);

    // Authorization: a response's content is per caller once grants are by
    // graph (ADR 0106), so a cache keyed by the address alone is wrong.
    private static void SetVary(HttpResponse response) =>
        response.Headers.Vary = new StringValues([HeaderNames.Accept, Preconditions.AsOfHeader, HeaderNames.Authorization]);
}
