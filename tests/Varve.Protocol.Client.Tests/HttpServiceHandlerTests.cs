// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// <c>SERVICE</c> over HTTP (ADR 0104): a query over a local dataset whose
/// <c>SERVICE</c> pattern is answered by an in-process server; the join, the
/// failures, and <c>SILENT</c>.
/// </summary>
public class HttpServiceHandlerTests
{
    private static readonly HttpClient Http = OutboundHttp.CreateClient();

    [Fact]
    public async Task A_service_pattern_is_answered_by_the_endpoint_and_joined_locally()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler handler = new(Http, C.Allowing(host), ClientLimits.Default);

        List<string> rows = Evaluate(handler,
            "SELECT ?o ?local WHERE { <http://ex/s> <http://ex/q> ?local . SERVICE <" + host.Address + "datasets/d/sparql> { <http://ex/s> <http://ex/p> ?o } } ORDER BY ?o");

        Assert.Equal(["\"1\" \"here\"", "\"2\" \"here\""], rows);
    }

    [Fact]
    public async Task A_refused_endpoint_fails_the_query_naming_it_and_silent_leaves_the_local_solutions()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler refusing = new(Http, EndpointPolicy.None, ClientLimits.Default);
        string endpoint = host.Address + "datasets/d/sparql";

        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Evaluate(refusing, "SELECT ?o WHERE { SERVICE <" + endpoint + "> { <http://ex/s> <http://ex/p> ?o } }"));
        Assert.Contains(endpoint, error.Message, StringComparison.Ordinal);
        Assert.Contains("allow-list is empty", error.Message, StringComparison.Ordinal);

        List<string> silent = Evaluate(refusing,
            "SELECT ?o ?local WHERE { <http://ex/s> <http://ex/q> ?local . SERVICE SILENT <" + endpoint + "> { <http://ex/s> <http://ex/p> ?o } }");
        Assert.Equal(["UNBOUND \"here\""], silent);
    }

    [Theory]
    [InlineData("redirect", "redirect")]
    [InlineData("json", "answered 405")]
    [InlineData("datasets/d/nowhere", "answered 404")]
    public async Task An_answer_that_is_not_results_is_a_failure(string path, string reason)
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler handler = new(Http, C.Allowing(host), ClientLimits.Default);

        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Evaluate(handler, "SELECT ?o WHERE { SERVICE <" + host.Address + path + "> { <http://ex/s> <http://ex/p> ?o } }"));
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_over_the_byte_cap_is_a_failure_not_a_truncated_answer()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler handler = new(Http, C.Allowing(host), new ClientLimits(TimeSpan.FromSeconds(30), new ByteCount(16)));

        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Evaluate(handler, "SELECT ?o WHERE { SERVICE <" + host.Address + "datasets/d/sparql> { <http://ex/s> <http://ex/p> ?o } }"));
        Assert.Contains("16-byte cap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_that_does_not_answer_in_time_is_a_failure()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler handler = new(Http, C.Allowing(host), new ClientLimits(TimeSpan.FromMilliseconds(300), new ByteCount(1 << 20)));

        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Evaluate(handler, "SELECT ?o WHERE { SERVICE <" + host.Address + "slow> { <http://ex/s> <http://ex/p> ?o } }"));
        Assert.Contains("did not answer within", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Results_xml_is_read_when_the_endpoint_answers_with_it()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler handler = new(Http, C.Allowing(host), ClientLimits.Default);

        List<string> rows = Evaluate(handler, "SELECT ?o WHERE { SERVICE <" + host.Address + "xml> { <http://ex/s> <http://ex/p> ?o } }");
        Assert.Equal(["\"from xml\""], rows);
    }

    [Fact]
    public async Task The_request_sent_is_the_pattern_as_a_select_the_endpoint_can_answer_with_the_protocol_headers()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        HttpServiceHandler handler = new(Http, C.Allowing(host), ClientLimits.Default);

        // A pattern with a graph, a filter and an optional: the serialiser's
        // job, answered by the server's parser.
        List<string> rows = Evaluate(handler,
            "SELECT ?s ?o WHERE { SERVICE <" + host.Address + "datasets/d/sparql> { GRAPH <http://ex/g> { ?s <http://ex/p> ?o } FILTER(?o = \"3\") OPTIONAL { ?s <http://ex/none> ?n } } }");
        Assert.Equal(["<http://ex/t> \"3\""], rows);
    }

    /// <summary>Evaluates over a local dataset holding <c>:s :q "here"</c>; each row's terms as N-Triples text.</summary>
    private static List<string> Evaluate(HttpServiceHandler handler, string query)
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(C.Iri("http://ex/s"), C.Iri("http://ex/q"), RdfTerm.Literal("here"u8), null);
        InMemoryDataset local = builder.ToDataset();
        EvaluationOptions options = new() { Clock = TimeProvider.System, ServiceHandler = handler };
        Query parsed = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(query));
        using QueryResults results = new SparqlEvaluator(options).Evaluate(parsed, local, C.Ct);
        SolutionResults solutions = Assert.IsType<SolutionResults>(results);
        List<string> rows = [];

        while (solutions.MoveNext())
        {
            StringBuilder row = new();

            for (int i = 0; i < solutions.Variables.Count; i++)
            {
                row.Append(i == 0 ? "" : " ").Append(solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term) ? Text(term!) : "UNBOUND");
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    private static string Text(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.Literal => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\"",
        _ => "_:" + Encoding.UTF8.GetString(term.Lexical),
    };
}
