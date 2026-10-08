// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The SPARQL 1.1 Service Description of a dataset's endpoint, generated from
/// what the endpoint actually does (ADRs 0092, 0096): its languages, its
/// formats, its dataset — named graphs enumerated by a SPARQL query over the
/// pinned view, never by store internals — and the event-sourced extensions
/// under <c>https://w3id.org/varve/ns#</c>.
/// </summary>
internal static class ServiceDescriptionEndpoint
{
    internal const string Sd = "http://www.w3.org/ns/sparql-service-description#";
    internal const string Formats = "http://www.w3.org/ns/formats/";
    internal const string Varve = "https://w3id.org/varve/ns#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    private static readonly Query NamedGraphs = SparqlParser.ParseQuery("SELECT DISTINCT ?g WHERE { GRAPH ?g { } }".AsSpan());

    /// <summary><c>GET /</c> and <c>HEAD /</c>.</summary>
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is { } exchange)
        {
            await WriteAsync(exchange).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the description in the syntax <c>Accept</c> chooses.</summary>
    internal static async Task WriteAsync(Exchange exchange)
    {
        HttpContext context = exchange.Context;

        if (!Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Graphs, out Offer<RdfSyntax> syntax))
        {
            await HttpProblems.NotAcceptable(context, "The service description is written as N-Triples, Turtle, N-Quads or TriG.").ConfigureAwait(false);
            return;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position
            || await Reads.NotModifiedAsync(context, position).ConfigureAwait(false))
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
            await QueryRun.WriteUnavailableAsync(exchange, failed.Message).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = syntax.MediaType;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        string service = ServiceAddress(exchange);
        SparqlEvaluator evaluator = new(exchange.Options.Evaluation);
        IQuadSource source = exchange.Readable(view);

        await BoundedReads.RunAsync(context, exchange.Options, async (output, cancellationToken) =>
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Describe(service, view.Position))
            {
                TermLines.WriteTriple(output, s, p, o);
            }

            // Federation is advertised when the host answers SERVICE (ADR
            // 0103): the handler's default refuses, and a refusing endpoint
            // claiming sd:BasicFederatedQuery would be a lie.
            if (exchange.Options.Evaluation.ServiceHandler is not RefusingServiceHandler)
            {
                TermLines.WriteTriple(output, Iri(service), Iri(Sd + "feature"), Iri(Sd + "BasicFederatedQuery"));
            }

            RdfTerm dataset = RdfTerm.BlankNode("dataset"u8);
            int index = 0;
            using QueryResults graphs = evaluator.Evaluate(NamedGraphs, source, cancellationToken);
            SolutionResults solutions = (SolutionResults)graphs;

            while (solutions.MoveNext())
            {
                if (solutions.TryGetTerm(new ColumnIndex(0), out RdfTerm? name))
                {
                    RdfTerm graph = RdfTerm.BlankNode(Encoding.UTF8.GetBytes("graph" + index.ToString(CultureInfo.InvariantCulture)));
                    index++;
                    TermLines.WriteTriple(output, dataset, Iri(Sd + "namedGraph"), graph);
                    TermLines.WriteTriple(output, graph, Iri(Rdf + "type"), Iri(Sd + "NamedGraph"));
                    TermLines.WriteTriple(output, graph, Iri(Sd + "name"), name!);
                    await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
    }

    /// <summary>The endpoint's address: <c>…/sparql</c> under the dataset's prefix.</summary>
    internal static string ServiceAddress(Exchange exchange)
    {
        string address = exchange.Address();

        if (address.EndsWith("/sparql", StringComparison.Ordinal))
        {
            return address;
        }

        return address.EndsWith('/') ? address + "sparql" : address + "/sparql";
    }

    /// <summary>The description's fixed part: everything but the named graphs.</summary>
    internal static List<(RdfTerm, RdfTerm, RdfTerm)> Describe(string service, Position position)
    {
        string prefix = service[..^"sparql".Length];
        RdfTerm self = Iri(service);
        RdfTerm type = Iri(Rdf + "type");
        RdfTerm dataset = RdfTerm.BlankNode("dataset"u8);
        RdfTerm defaultGraph = RdfTerm.BlankNode("default"u8);
        List<(RdfTerm, RdfTerm, RdfTerm)> triples =
        [
            (self, type, Iri(Sd + "Service")),
            (self, Iri(Sd + "endpoint"), self),
            (self, Iri(Sd + "supportedLanguage"), Iri(Sd + "SPARQL10Query")),
            (self, Iri(Sd + "supportedLanguage"), Iri(Sd + "SPARQL11Query")),
            (self, Iri(Sd + "supportedLanguage"), Iri(Sd + "SPARQL11Update")),
            (self, Iri(Sd + "defaultEntailmentRegime"), Iri("http://www.w3.org/ns/entailment/Simple")),
            (self, Iri(Sd + "feature"), Iri(Varve + "VersionParameter")),
            (self, Iri(Rdfs + "comment"), Literal(
                "A Varve dataset. Blank nodes are written with labels stable for the dataset's lifetime; a label sent back is a fresh node (ADR 0098). Varve-As-Of reads the past; Varve-Position and ETag name the position every response describes; If-Match on a write is its expected position.")),
        ];

        foreach (string format in (ReadOnlySpan<string>)["SPARQL_Results_JSON", "SPARQL_Results_XML", "SPARQL_Results_CSV", "SPARQL_Results_TSV", "N-Triples", "Turtle", "N-Quads", "TriG"])
        {
            triples.Add((self, Iri(Sd + "resultFormat"), Iri(Formats + format)));
        }

        foreach (string format in (ReadOnlySpan<string>)["N-Triples", "Turtle", "N-Quads", "TriG"])
        {
            triples.Add((self, Iri(Sd + "inputFormat"), Iri(Formats + format)));
        }

        triples.Add((self, Iri(Sd + "defaultDataset"), dataset));
        triples.Add((dataset, type, Iri(Sd + "Dataset")));
        triples.Add((dataset, Iri(Sd + "defaultGraph"), defaultGraph));
        triples.Add((defaultGraph, type, Iri(Sd + "Graph")));

        // The event-sourced extensions (ADRs 0096, 0097).
        triples.Add((self, type, Iri(Varve + "EventSourcedService")));
        triples.Add((self, Iri(Varve + "position"), RdfTerm.Literal(Encoding.UTF8.GetBytes(position.Value.ToString(CultureInfo.InvariantCulture)), Iri("http://www.w3.org/2001/XMLSchema#integer"))));
        triples.Add((self, Iri(Varve + "positionHeader"), Literal(Preconditions.PositionHeader)));
        triples.Add((self, Iri(Varve + "asOfHeader"), Literal(Preconditions.AsOfHeader)));
        triples.Add((self, Iri(Varve + "asOfSelector"), Iri(Varve + "PositionSelector")));
        triples.Add((self, Iri(Varve + "asOfSelector"), Iri(Varve + "TimeSelector")));
        triples.Add((self, Iri(Varve + "conditionalRequest"), Literal("If-Match")));
        triples.Add((self, Iri(Varve + "conditionalRequest"), Literal("If-None-Match")));
        triples.Add((self, Iri(Varve + "graphStore"), Iri(prefix + "graphs")));
        triples.Add((self, Iri(Varve + "changeFeed"), Iri(prefix + "feed")));
        triples.Add((self, Iri(Varve + "diff"), Iri(prefix + "diff")));
        triples.Add((self, Iri(Varve + "status"), Iri(prefix + "status")));
        triples.Add((self, Iri(Varve + "settings"), Iri(prefix + "settings")));
        triples.Add((self, Iri(Varve + "checkpoints"), Iri(prefix + "checkpoints")));
        triples.Add((self, Iri(Varve + "feedFormat"), Literal(MediaTypes.Delta + "; version=1")));
        triples.Add((self, Iri(Varve + "feedFormat"), Literal(MediaTypes.EventStream)));
        return triples;
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    private static RdfTerm Literal(string text) => RdfTerm.Literal(Encoding.UTF8.GetBytes(text));
}
