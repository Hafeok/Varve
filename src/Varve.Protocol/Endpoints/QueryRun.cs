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
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Sparql.Results;
using Varve.Store;
using Varve.Store.Log;

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

        Tracing.Operation(query switch { ConstructQuery => "CONSTRUCT", DescribeQuery => "DESCRIBE", AskQuery => "ASK", _ => "SELECT" });
        Tracing.QueryText(exchange.Options, request);
        await AnswerAsync(exchange, query!).ConfigureAwait(false);
    }

    /// <summary>Negotiates, pins, evaluates and streams a parsed query, under the regime asked or the dataset's default (ADR 0192).</summary>
    internal static async Task AnswerAsync(Exchange exchange, Query query)
    {
        HttpContext context = exchange.Context;
        EntailmentBindings bindings = exchange.Options.Entailment?.Of(exchange.Name) ?? EntailmentBindings.None;
        Microsoft.Extensions.Primitives.StringValues asked = context.Request.Headers[EntailmentHeader.Name];
        EntailmentRegime regime = bindings.Default;

        if (asked.Count > 0 && !EntailmentHeader.TryRead(asked, out regime))
        {
            await UnsupportedAsync(context, asked).ConfigureAwait(false);
            return;
        }

        string? variant = regime == EntailmentRegime.Simple ? null : EntailmentHeader.Token(regime);
        context.Response.Headers[EntailmentHeader.Name] = EntailmentHeader.Token(regime);
        bool graph = query is ConstructQuery or DescribeQuery;
        Offer<SparqlResultsFormat> results = default;
        Offer<GraphFormat> syntax = default;
        bool acceptable = graph
            ? Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Graphs, out syntax)
            : Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Results, out results);

        if (!acceptable)
        {
            await HttpProblems.NotAcceptable(context, graph
                ? "A graph result is written as N-Triples, Turtle, N-Quads, TriG, RDF/XML or JSON-LD."
                : "A solution or boolean result is written as SPARQL results JSON, XML, CSV or TSV.").ConfigureAwait(false);
            return;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position
            || await Reads.NotModifiedAsync(context, exchange.Dataset, position, variant: variant).ConfigureAwait(false))
        {
            return;
        }

        DatasetView? opened;

        try
        {
            opened = await Reads.OpenAsync(context, exchange.Dataset, position, Reads.IsAsOf(context), exchange.Options.Limits, context.RequestAborted).ConfigureAwait(false);
        }
        catch (DatasetUnavailableException failed)
        {
            await WriteUnavailableAsync(exchange, failed.Message).ConfigureAwait(false);
            return;
        }

        if (opened is not { } view)
        {
            return;
        }

        // The answer at a position is one per regime (ADR 0192 point 3).
        Preconditions.Describe(context.Response, view.Position, variant);
        context.Response.ContentType = graph ? syntax.MediaType : results.MediaType;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        SparqlEvaluator evaluator = new(regime == EntailmentRegime.Simple ? exchange.Options.Evaluation : exchange.Options.EntailedEvaluation);
        IQuadSource source = exchange.Readable(view);

        await BoundedReads.RunAsync(context, exchange.Options, async (output, cancellationToken) =>
        {
            // The rewriting reads the caller's scoped view, the TBox graphs
            // through it (ADRs 0190, 0193); simple entailment is the query as sent.
            EntailedQuery entailed = EntailmentRewriter.Rewrite(query, source, bindings.Covers, regime, schemas: bindings.Schemas, cancellationToken: cancellationToken);
            using QueryResults answer = evaluator.Evaluate(entailed.Query, entailed.Source, cancellationToken);

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
                    using (GraphOutput written = new(output, syntax.Format, exchange.Options.Limits))
                    {
                        while (triples.MoveNext())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            written.Write(triples.Subject, triples.Predicate, triples.Object);
                            await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
                        }

                        written.Complete();
                    }

                    break;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>A <c>Varve-Entailment</c> the server does not serve, or one other than simple on an update: <c>400 entailment-unsupported</c>.</summary>
    internal static Task UnsupportedAsync(HttpContext context, Microsoft.Extensions.Primitives.StringValues asked) =>
        HttpProblems.WriteAsync(context, ProblemType.EntailmentUnsupported,
            "Varve-Entailment is one of simple, rdf, rdfs and d, or one regime IRI in angle brackets; an update is evaluated under simple entailment.",
            members =>
            {
                members.String("value", EntailmentHeader.Shown(asked));
                System.Text.Json.Utf8JsonWriter json = members.Raw("supported");
                json.WriteStartArray("supported");
                foreach (string token in EntailmentHeader.Supported)
                {
                    json.WriteStringValue(token);
                }

                json.WriteEndArray();
            });

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
        long rows = 0;

        while (solutions.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(solutions, writer, names.Length);
            rows++;
            await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
        }

        writer.WriteEnd();
        Tracing.ReturnedRows(rows);
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
