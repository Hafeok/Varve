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

        // owl2-rl is answered by an OWL 2 RL closure, simply evaluated over every
        // graph's explicit and implicit parts (ADR 0197 point 6).
        bool owl2Rl = asked.Count > 0 && EntailmentHeader.IsOwl2Rl(asked);
        if (owl2Rl)
        {
            if (!ServesOwl2Rl(exchange))
            {
                await UnsupportedAsync(context, asked).ConfigureAwait(false);
                return;
            }

            regime = EntailmentRegime.Simple;
        }
        else if (asked.Count > 0 && !EntailmentHeader.TryRead(asked, out regime))
        {
            await UnsupportedAsync(context, asked).ConfigureAwait(false);
            return;
        }

        // A view is a part of the closure, which tier 1 has not (ADR 0198 point 1).
        (ClosureMode mode, DatasetSpec? dataset) = Views(query.Dataset, owl2Rl);
        if (!ReferenceEquals(dataset, query.Dataset))
        {
            if (regime != EntailmentRegime.Simple)
            {
                await UnsupportedAsync(context, asked).ConfigureAwait(false);
                return;
            }

            query = query with { Dataset = dataset };
        }

        string? variant = owl2Rl ? EntailmentHeader.Owl2RlToken : regime == EntailmentRegime.Simple ? null : EntailmentHeader.Token(regime);
        context.Response.Headers[EntailmentHeader.Name] = owl2Rl ? EntailmentHeader.Owl2RlToken : EntailmentHeader.Token(regime);
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

        // An answer under owl2-rl over a graph with a clash says so (ADR 0197 point 5).
        if (owl2Rl && Inconsistent(exchange, view))
        {
            context.Response.Headers["Varve-Inconsistent"] = "true";
        }
        context.Response.ContentType = graph ? syntax.MediaType : results.MediaType;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        SparqlEvaluator evaluator = new(regime == EntailmentRegime.Simple ? exchange.Options.Evaluation : exchange.Options.EntailedEvaluation);

        await BoundedReads.RunAsync(context, exchange.Options, async (output, cancellationToken) =>
        {
            // Composed inside the bounded read, so a classification or a closure behind is its problem (ADRs 0233, 0195).
            IQuadSource source = exchange.Readable(view, mode);

            // The rewriting reads the caller's scoped view, by graph and by row, the
            // TBox graphs through it (ADRs 0190, 0193, 0238 point 1); simple
            // entailment is the query as sent.
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

    /// <summary>
    /// Whether an OWL 2 RL closure of the dataset has a clash in a graph the
    /// caller reads: a data graph in its readable scope, or a binding's TBox
    /// conclusions, which every reader of the dataset reads. The clashes are
    /// the projection's, at its position.
    /// </summary>
    private static bool Inconsistent(Exchange exchange, DatasetView view)
    {
        if (exchange.Scope.Readable.IsEmpty)
        {
            return false;
        }

        foreach (IInferenceClosure closure in exchange.Options.InferenceClosures?.Of(exchange.Name) ?? [])
        {
            if (closure.Profile != Varve.Inference.Model.InferenceProfile.Owl2Rl)
            {
                continue;
            }

            foreach (Varve.Inference.Model.PlacedClash clash in closure.Clashes())
            {
                if (clash.Graph.IsNone ? exchange.Scope.Readable.IncludesDefault
                    : !view.TryExternalise(clash.Graph, out RdfTerm? graph) || graph.Equals(closure.TBoxConclusions) || exchange.Scope.Readable.Allows(graph))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether the dataset has an OWL 2 RL binding whose closure answers <c>owl2-rl</c> (ADR 0197 point 6).</summary>
    private static bool ServesOwl2Rl(Exchange exchange)
    {
        foreach (IInferenceClosure closure in exchange.Options.InferenceClosures?.Of(exchange.Name) ?? [])
        {
            if (closure.Profile == Varve.Inference.Model.InferenceProfile.Owl2Rl)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the view IRIs among a query's graphs ask (ADR 0198 point 1): the
    /// part the default graph and the named graphs are read in, the graphs
    /// left, and the dataset the query keeps, which is none when it names a
    /// view, since the closure source then is the dataset. A query that names
    /// no view keeps its dataset, read in the part the regime gives.
    /// </summary>
    internal static (ClosureMode Mode, DatasetSpec? Dataset) Views(DatasetSpec? dataset, bool owl2Rl)
    {
        ClosureMode plain = owl2Rl ? ClosureMode.Union : ClosureMode.Plain;
        if (dataset is null)
        {
            return (plain, dataset);
        }

        ClosurePart defaultViews = Parts(dataset.DefaultGraphs, out List<RdfTerm> defaults);
        ClosurePart namedViews = Parts(dataset.NamedGraphs, out List<RdfTerm> named);
        if (defaultViews == 0 && namedViews == 0)
        {
            return (plain, dataset);
        }

        ClosurePart rest = owl2Rl ? ClosurePart.Both : ClosurePart.Explicit;

        // §13.2: FROM NAMED alone gives an empty default graph, FROM alone no
        // named graphs; a view counts as the clause it is in.
        IReadOnlyList<RdfTerm>? defaultGraphs = dataset.DefaultGraphs.Count == 0 ? [] : defaults.Count > 0 ? defaults : null;
        IReadOnlyList<RdfTerm>? namedGraphs = dataset.NamedGraphs.Count == 0 ? [] : named.Count > 0 ? named : null;
        if (dataset.DefaultGraphs.Count == 0 && dataset.NamedGraphs.Count == 0)
        {
            defaultGraphs = null;
            namedGraphs = null;
        }

        return (new ClosureMode(defaultViews == 0 ? rest : defaultViews, namedViews == 0 ? rest : namedViews, defaultGraphs, namedGraphs), null);
    }

    private static ClosurePart Parts(AlgebraList<RdfTerm> graphs, out List<RdfTerm> others)
    {
        others = [];
        ClosurePart parts = 0;
        foreach (RdfTerm graph in graphs)
        {
            if (graph.Lexical.SequenceEqual("https://w3id.org/varve/ns#explicit"u8))
            {
                parts |= ClosurePart.Explicit;
            }
            else if (graph.Lexical.SequenceEqual("https://w3id.org/varve/ns#implicit"u8))
            {
                parts |= ClosurePart.Implicit;
            }
            else
            {
                others.Add(graph);
            }
        }

        return parts;
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
