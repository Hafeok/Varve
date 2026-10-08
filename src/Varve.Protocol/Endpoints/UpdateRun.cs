// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The update operation (SPARQL 1.1 Protocol §2.2): authorised for write,
/// checked against <c>If-Match</c>, parsed, given the protocol's dataset, and
/// executed by the host's executor as one commit (ADRs 0057, 0094).
/// </summary>
internal static class UpdateRun
{
    internal static async Task RunAsync(Exchange exchange, SparqlRequest request)
    {
        HttpContext context = exchange.Context;

        if (!await exchange.AuthorizeAsync(DatasetPermissions.Write).ConfigureAwait(false))
        {
            return;
        }

        if (await Writes.CheckAsync(exchange).ConfigureAwait(false) is not { Proceed: true } plan)
        {
            return;
        }

        if (!request.TryVersion(out SparqlVersion version))
        {
            await HttpProblems.BadRequest(context, "version is 1.1, 1.2-basic or 1.2.").ConfigureAwait(false);
            return;
        }

        SparqlParseOptions parse = SparqlRequest.Options(exchange, version);
        bool parsed = request.Text is { } text
            ? SparqlParser.TryParseUpdate(text.AsSpan(), parse, out Update? update, out SparqlParseError error)
            : SparqlParser.TryParseUpdate(request.Utf8.Span, parse, out update, out error);

        if (!parsed)
        {
            await SparqlRequest.SyntaxErrorAsync(context, error).ConfigureAwait(false);
            return;
        }

        if (!request.TryDataset(out DatasetSpec? dataset, out string? invalid))
        {
            await HttpProblems.BadRequest(context, "A graph parameter is not an absolute IRI: " + invalid).ConfigureAwait(false);
            return;
        }

        if (dataset is not null)
        {
            if (!TryApply(update!, dataset, out Update? applied))
            {
                // §2.2.3: the parameters and USING or WITH together are an error.
                await HttpProblems.BadRequest(context, "using-graph-uri and using-named-graph-uri cannot be given with a USING or WITH clause.").ConfigureAwait(false);
                return;
            }

            update = applied;
        }

        CommitResult result;

        try
        {
            result = await exchange.Options.Updates
                .ExecuteAsync(exchange.Dataset, update!, Writes.Metadata(exchange), plan.Expected, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            // The executor's operation failure (an update that names a graph
            // CREATE finds, a LOAD that is refused): nothing was committed.
            await HttpProblems.WriteAsync(context, StatusCodes.Status400BadRequest, ProblemType.OperationFailed,
                "An operation of the update failed; nothing was committed.", failed.Message).ConfigureAwait(false);
            return;
        }

        // A Conflict here lost the race inside the sequencer after If-Match
        // was checked, or met a writer that came first: 409 either way (ADR 0094).
        await Writes.AnswerAsync(exchange, result).ConfigureAwait(false);
    }

    // The protocol's dataset becomes every Modify's USING; DELETE WHERE and the
    // data operations have none to take (§2.2.3).
    private static bool TryApply(Update update, DatasetSpec dataset, out Update? applied)
    {
        applied = null;
        UpdateOperation[] operations = new UpdateOperation[update.Operations.Count];

        for (int i = 0; i < operations.Length; i++)
        {
            UpdateOperation operation = update.Operations[i];

            if (operation is Modify modify)
            {
                if (modify.Using is not null || modify.With is not null)
                {
                    return false;
                }

                operation = modify with { Using = dataset };
            }

            operations[i] = operation;
        }

        applied = update with { Operations = AlgebraList.From<UpdateOperation>(operations) };
        return true;
    }
}
