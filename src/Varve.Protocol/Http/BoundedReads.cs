// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Model;
using Varve.Sparql.Evaluation;

namespace Varve.Protocol.Http;

/// <summary>Writes the body of a bounded read.</summary>
internal delegate ValueTask BodyWriter(ResponseOutput output, CancellationToken cancellationToken);

/// <summary>
/// A read bounded twice and cut visibly (ADR 0095): evaluation and the pin
/// are bounded by the host's limits on the injected clock, linked with the
/// client's abort; a cut before anything reached the client is a <c>503</c>
/// problem, after it a <c>Varve-Error</c> trailer where the response carries
/// trailers, and an aborted connection where it does not.
/// </summary>
internal static class BoundedReads
{
    internal static async Task RunAsync(HttpContext context, ProtocolOptions options, BodyWriter body)
    {
        ProtocolLimits limits = options.Limits;
        TimeSpan bound = limits.QueryTimeout < limits.PinnedReadLifetime ? limits.QueryTimeout : limits.PinnedReadLifetime;
        using CancellationTokenSource limit = new(bound, options.Clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, context.RequestAborted);
        HttpResponse response = context.Response;
        bool trailers = response.SupportsTrailers();

        if (trailers)
        {
            response.DeclareTrailer(Preconditions.ErrorTrailer);
        }

        ResponseOutput output = new(response, limits.ResultSizeCap.Value);
        ProblemType? cut = null;
        string? detail = null;

        try
        {
            await body(output, linked.Token).ConfigureAwait(false);
            await output.FlushAsync(linked.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client left. Nobody is listening for an answer; the pin is
            // released with the response.
            return;
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            cut = ProblemType.ReadLimitExceeded;
            detail = "The read reached the server's time limit.";
        }
        catch (ReadLimitExceededException error)
        {
            cut = ProblemType.ReadLimitExceeded;
            detail = error.Message;
        }
        catch (QueryEvaluationException error)
        {
            cut = ProblemType.OperationFailed;
            detail = error.Message;
        }

        if (!output.HasStarted && !response.HasStarted)
        {
            output.Discard();
            int status = cut == ProblemType.OperationFailed ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable;
            await HttpProblems.WriteAsync(context, status, cut.Value, "The read was cut.", detail).ConfigureAwait(false);
        }
        else if (trailers)
        {
            response.AppendTrailer(Preconditions.ErrorTrailer, cut.Value.Value);
        }
        else
        {
            context.Abort();
        }
    }
}
