// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Iri;
using Varve.Protocol.Http;
using Varve.Rdf;
using Varve.Store;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// <c>GET /access?subject=&lt;IRI&gt;</c> (ADR 0231 point 4): access without
/// erasure mode, served by the selector over the head, to a dataset admin
/// alone, as N-Quads. Only the <c>Current</c> scope exists without key ids;
/// <c>AllHistory</c> is erasure mode's (#203).
/// </summary>
internal static class AccessEndpoint
{
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Admin).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        string? subject = context.Request.Query["subject"];
        string? scope = context.Request.Query["scope"];

        if (string.IsNullOrEmpty(subject) || !IriRef.IsAbsolute(Encoding.UTF8.GetBytes(subject)))
        {
            await HttpProblems.BadRequest(context, "An access request names its data subject as subject=<an absolute IRI>.").ConfigureAwait(false);
            return;
        }

        if (scope is not (null or "Current"))
        {
            await HttpProblems.BadRequest(context, "Without erasure mode an access request has the Current scope only; AllHistory needs key ids (ADR 0023).").ConfigureAwait(false);
            return;
        }

        DatasetView view = exchange.Dataset.Pin();
        context.Response.RegisterForDispose(view);
        IRowClassification? rows;

        try
        {
            rows = options.RowClassifications?.At(exchange.Name, view);
        }
        catch (ClassificationBehindException behind)
        {
            context.Response.Headers.RetryAfter = "1";
            await HttpProblems.WriteAsync(context, Model.ProblemType.ClassificationBehind, behind.Message, BoundedReads.ClassificationBehind(behind)).ConfigureAwait(false);
            return;
        }

        if (rows is null)
        {
            await HttpProblems.BadRequest(context, "The dataset declares no classifier binding, so no quad is about a data subject (ADR 0231).").ConfigureAwait(false);
            return;
        }

        Preconditions.Describe(context.Response, view.Position);
        context.Response.ContentType = MediaTypes.NQuads;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        IQuadCursor cursor = ClassificationSelector.Instance.Select(view, rows, RdfTerm.Iri(Encoding.UTF8.GetBytes(subject)));
        context.Response.RegisterForDispose(cursor);

        await BoundedReads.RunAsync(context, options, async (output, cancellationToken) =>
        {
            using GraphOutput graph = new(output, GraphFormat.NQuads, options.Limits);

            while (cursor.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Quad current = cursor.Current;
                graph.Write(in current, view);
                await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
            }

            graph.Complete();
        }).ConfigureAwait(false);
    }
}
