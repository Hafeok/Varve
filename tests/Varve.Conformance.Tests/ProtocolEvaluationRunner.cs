// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Results;
using Varve.Sparql.Results.Model;
using Varve.Store;

namespace Varve.Conformance.Tests;

/// <summary>
/// The evaluation and update suites through the protocol (ADR 0107): the
/// case's data in a store behind an in-process server whose caller holds an
/// <c>all</c> grant, the query or request <c>POST</c>ed to <c>/sparql</c>,
/// and the answer read back and compared as the in-process run compares it.
/// The scope seam, the scoped source and the writable check all lie on this
/// path, and with <c>all</c> every case must answer as before.
/// </summary>
internal static class ProtocolEvaluationRunner
{
    /// <summary>
    /// Null when the case passes over HTTP, otherwise why not. A graph
    /// result is asked for as N-Triples, or as <paramref name="graphAccept"/>
    /// (ADR 0092, amended 2026-10-12); a <c>406</c> to the latter is
    /// returned as <c>"406 " + the problem</c> for the caller to judge.
    /// </summary>
    internal static async Task<string?> RunAsync(EvaluationEntry entry, CancellationToken cancellationToken, string? graphAccept = null)
    {
        Query query = EvaluationRunner.ParseQuery(entry);
        List<(IReadOnlyList<DataQuad>, RdfTerm?)> graphs = [];

        foreach (string iri in entry.Data)
        {
            graphs.Add((EvaluationData.Quads(iri), null));
        }

        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (string iri in entry.GraphData)
        {
            if (named.Add(iri))
            {
                graphs.Add((EvaluationData.Quads(iri), RdfTerm.Iri(Encoding.UTF8.GetBytes(iri))));
            }
        }

        if (query.Dataset is { } dataset)
        {
            foreach (RdfTerm name in dataset.DefaultGraphs.ToArray().Concat(dataset.NamedGraphs.ToArray()))
            {
                string iri = Encoding.UTF8.GetString(name.Lexical);

                if (iri.StartsWith(EvaluationSuite.PublishedRoot, StringComparison.Ordinal) && EvaluationCatalogue.Exists(iri) && named.Add(iri))
                {
                    graphs.Add((EvaluationData.Quads(iri), name));
                }
            }
        }

        await using Dataset store = await EvaluationSubjects.LoadStoreAsync(graphs);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(store, clock: FixedClock.Instance, serviceHandler: new TestServiceHandler(entry.ServiceData), randomness: new SeededRandom(), stopping: cancellationToken);
        bool graph = query is ConstructQuery or DescribeQuery;
        using HttpRequestMessage request = new(HttpMethod.Post, "datasets/d/sparql?version=" + Version(entry.Version))
        {
            // The query's own IRI is its base in the harness; over HTTP the
            // request's address would be, so the base travels in the text.
            Content = new StringContent("BASE <" + entry.QueryIri + ">\n" + File.ReadAllText(EvaluationSuite.PathOf(entry.QueryIri)), Encoding.UTF8, "application/sparql-query"),
        };
        string graphType = graphAccept ?? "application/n-triples";
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(graph ? graphType : "application/sparql-results+json"));
        using HttpResponseMessage response = await host.Client.SendAsync(request, cancellationToken);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (graph && graphAccept is not null && (int)response.StatusCode == 406)
        {
            return "406 " + Encoding.UTF8.GetString(body);
        }

        if (!response.IsSuccessStatusCode)
        {
            return "the server answered " + (int)response.StatusCode + ": " + Encoding.UTF8.GetString(body);
        }

        if (entry.ResultIri is null)
        {
            return "the manifest names no result";
        }

        string resultPath = EvaluationSuite.PathOf(entry.ResultIri);
        bool graphFile = Path.GetExtension(resultPath) is ".ttl" or ".nt" or ".rdf";
        IReadOnlyList<DataQuad>? expectedGraph = graphFile ? EvaluationData.Quads(entry.ResultIri) : null;
        ResultTable? expected = ResultTable.Read(resultPath, expectedGraph);

        if (graph)
        {
            if (expectedGraph is null)
            {
                return "a graph, but the expected result is a table";
            }

            if (ProtocolRunner.Graph(graphType, body, entry.QueryIri, out List<DataQuad>? actual) is { } unparsed)
            {
                return graphType + ": " + unparsed;
            }

            if (actual!.Any(q => q.Graph is not null))
            {
                return graphType + ": a graph result names a graph";
            }

            List<DataQuad> wanted = [.. expectedGraph.Select(q => q with { Graph = null })];
            return DatasetComparison.Compare(actual!, wanted);
        }

        if (expected is null)
        {
            return "a table, but the expected result is a graph";
        }

        SparqlResultsReader reader = new(body, SparqlResultsFormat.Json);

        if (!reader.ReadHead())
        {
            return "the JSON answer does not parse: " + reader.Error.Message;
        }

        if (reader.IsBoolean)
        {
            return expected.Compare(new ResultTable { Boolean = reader.Boolean }, false, [], false);
        }

        ResultTable table = new();
        table.Variables.AddRange(reader.Variables);

        while (reader.Read())
        {
            SolutionView row = reader.Current;
            RdfTerm?[] terms = new RdfTerm?[table.Variables.Count];

            for (int i = 0; i < terms.Length; i++)
            {
                terms[i] = row.TryGet(i, out RdfTermView term) ? term.Materialise() : null;
            }

            table.Rows.Add(terms);
        }

        if (reader.Error.Kind != SparqlResultsErrorKind.None)
        {
            return "the JSON answer does not parse: " + reader.Error.Message;
        }

        (bool ordered, List<string> keys) = EvaluationRunner.OrderOf(query.Pattern);
        return expected.Compare(table, ordered, keys, entry.LaxCardinality);
    }

    /// <summary>
    /// An update request <c>POST</c>ed to a server over <paramref name="dataset"/>,
    /// with the suite's <c>LOAD</c> source. Null when the server answered
    /// <c>204</c>, otherwise the status and the problem.
    /// </summary>
    internal static async Task<string?> UpdateAsync(Dataset dataset, byte[] text, string requestIri, CancellationToken cancellationToken)
    {
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, clock: FixedClock.Instance, loadSource: new UpdateRunner.SuiteLoadSource(), randomness: new SeededRandom(), stopping: cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Post, "datasets/d/sparql")
        {
            Content = new StringContent("BASE <" + requestIri + ">\n" + Encoding.UTF8.GetString(text), Encoding.UTF8, "application/sparql-update"),
        };
        using HttpResponseMessage response = await host.Client.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode != 204)
        {
            return "the server answered " + (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync(cancellationToken);
        }

        return null;
    }

    private static string Version(SparqlVersion version) => version switch
    {
        SparqlVersion.Sparql11 => "1.1",
        SparqlVersion.Sparql12Basic => "1.2-basic",
        _ => "1.2",
    };
}
