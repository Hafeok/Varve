// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Sparql.Results;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The query operation (SPARQL 1.1 Protocol §2.1): parse, negotiate, resolve
/// the position, pin it for the response, and stream the answer under the
/// host's limits (ADRs 0052, 0092, 0095, 0096).
/// </summary>
internal static class QueryRun
{
    internal static async Task RunAsync(Exchange exchange, SparqlRequest request)
    {
        HttpContext context = exchange.Context;

        if (!request.TryVersion(out SparqlVersion version))
        {
            await HttpProblems.BadRequest(context, "version is 1.1, 1.2-basic or 1.2.").ConfigureAwait(false);
            return;
        }

        SparqlParseOptions parse = SparqlRequest.Options(exchange, version);
        bool parsed = request.Text is { } text
            ? SparqlParser.TryParseQuery(text.AsSpan(), parse, out Query? query, out SparqlParseError error)
            : SparqlParser.TryParseQuery(request.Utf8.Span, parse, out query, out error);

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

        // §2.1.4: a protocol dataset replaces FROM and FROM NAMED.
        if (dataset is not null)
        {
            query = query! with { Dataset = dataset };
        }

        await AnswerAsync(exchange, query!).ConfigureAwait(false);
    }

    /// <summary>Negotiates, pins, evaluates and streams a parsed query.</summary>
    internal static async Task AnswerAsync(Exchange exchange, Query query)
    {
        HttpContext context = exchange.Context;
        bool graph = query is ConstructQuery or DescribeQuery;
        Offer<SparqlResultsFormat> results = default;
        Offer<RdfSyntax> syntax = default;
        bool acceptable = graph
            ? Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Graphs, out syntax)
            : Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Results, out results);

        if (!acceptable)
        {
            await HttpProblems.NotAcceptable(context, graph
                ? "A graph result is written as N-Triples, Turtle, N-Quads or TriG."
                : "A solution or boolean result is written as SPARQL results JSON, XML, CSV or TSV.").ConfigureAwait(false);
            return;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position
            || await Reads.NotModifiedAsync(context, exchange.Dataset, position).ConfigureAwait(false))
        {
            return;
        }

        DatasetView view;

        try
        {
            view = await Reads.OpenAsync(context, exchange.Dataset, position, Reads.IsAsOf(context), context.RequestAborted).ConfigureAwait(false);
        }
        catch (DatasetUnavailableException failed)
        {
            await WriteUnavailableAsync(exchange, failed.Message).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = graph ? syntax.MediaType : results.MediaType;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        SparqlEvaluator evaluator = new(exchange.Options.Evaluation);
        IQuadSource source = exchange.Readable(view);

        await BoundedReads.RunAsync(context, exchange.Options, async (output, cancellationToken) =>
        {
            using QueryResults answer = evaluator.Evaluate(query, source, cancellationToken);

            switch (answer)
            {
                case SolutionResults solutions:
                    await WriteSolutionsAsync(solutions, results.Format, output, cancellationToken).ConfigureAwait(false);
                    break;
                case BooleanResult boolean:
                    using (SparqlResultsWriter writer = new(output, results.Format))
                    {
                        writer.WriteBoolean(boolean.Value);
                        writer.WriteEnd();
                    }

                    break;
                case TripleResults triples:
                    while (triples.MoveNext())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TermLines.WriteTriple(output, triples.Subject, triples.Predicate, triples.Object);
                        await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }
        }).ConfigureAwait(false);
    }

    internal static Task WriteUnavailableAsync(Exchange exchange, string detail)
    {
        exchange.Response.Headers.RetryAfter = "1";
        return HttpProblems.WriteAsync(exchange.Context, ProblemType.Unavailable, detail);
    }

    private static async ValueTask WriteSolutionsAsync(SolutionResults solutions, SparqlResultsFormat format, ResponseOutput output, CancellationToken cancellationToken)
    {
        IReadOnlyList<Variable> variables = solutions.Variables;
        string[] names = new string[variables.Count];

        for (int i = 0; i < names.Length; i++)
        {
            names[i] = variables[i].Name;
        }

        using SparqlResultsWriter writer = new(output, format);
        writer.WriteHead(names);

        while (solutions.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(solutions, writer, names.Length);
            await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
        }

        writer.WriteEnd();
    }

    // Per row: the writer costs nothing (sparql-results.md §4), and TryGetTerm
    // names a term the store's cache holds without allocating. The allocation
    // test measures the whole request: 56 bytes a solution, the evaluator's
    // row. Not [HotPath]: TryGetTerm and the writer's members are not marked
    // in their packages, and a cold term costs its term once when the cache
    // loads it.
    private static void WriteRow(SolutionResults solutions, SparqlResultsWriter writer, int width)
    {
        writer.StartSolution();

        for (int i = 0; i < width; i++)
        {
            if (solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term))
            {
                writer.WriteBinding(i, term!);
            }
        }

        writer.EndSolution();
    }
}
