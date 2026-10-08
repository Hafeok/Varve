// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// <c>GET /diff</c> (ADR 0097): <c>Diff(from, to)</c> of R3, the range
/// resolved as the feed resolves it, as one diff record of the delta format.
/// </summary>
internal static class DiffEndpoint
{
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        if (!Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Feeds, out Offer<FeedFraming> framing)
            || framing.Format != FeedFraming.Delta)
        {
            await HttpProblems.NotAcceptable(context, "A diff is application/vnd.varve.delta.").ConfigureAwait(false);
            return;
        }

        if (await Ranges.ResolveAsync(context, exchange.Dataset, endDefaultsToHead: true, resume: null).ConfigureAwait(false) is not FeedRange range)
        {
            return;
        }

        Position to = range.To!.Value;

        if (await Reads.NotModifiedAsync(context, to).ConfigureAwait(false))
        {
            return;
        }

        Preconditions.Describe(context.Response, to);
        context.Response.ContentType = FeedEndpoint.DeltaContentType;
        Dataset dataset = exchange.Dataset;

        await BoundedReads.RunAsync(context, options, async (output, cancellationToken) =>
        {
            QuadDelta delta = await dataset.DiffAsync(range.From, to, cancellationToken).ConfigureAwait(false);

            // The dictionary is append-only: the head names every handle a
            // diff between two earlier positions carries.
            using DatasetView names = dataset.Pin();
            DeltaLines.WriteDiff(output, range.From, to, delta, names.TryExternalise);
        }).ConfigureAwait(false);
    }
}
