// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Sparql.Results;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Graph-level authorisation through the protocol (ADR 0107), with the host's
/// scope seam answering a fixed scope: what each endpoint shows and refuses
/// for a caller who reads some graphs, and the two properties — the scoped
/// answer equals the answer over the sub-dataset of the readable graphs, and
/// no response to a caller without read on a graph carries an IRI that occurs
/// only in that graph.
/// </summary>
public class ScopeTests
{
    private const string Sparql = "datasets/d/sparql";
    private const string People = "http://ex/g/people";
    private const string Secret = "http://ex/secret";

    private static readonly string[] Everything =
    [
        "INSERT DATA { <http://ex/d1> <http://ex/p> \"default\" }",
        "INSERT DATA { GRAPH <" + People + "> { <http://ex/alice> <http://ex/p> \"people\" } }",
        "INSERT DATA { GRAPH <" + Secret + "> { <http://ex/s1> <http://ex/p> \"secret\" } }",
    ];

    [Fact]
    public async Task a_query_sees_the_readable_graphs_and_from_named_of_an_unreadable_one_is_empty_not_an_error()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, accessScopes: Fixed(Scope(People, defaultGraph: true), Scope(People, defaultGraph: true)), stopping: P.Ct);
        await FillAsync(dataset);

        string graphs = await ReadAsync(host, P.Query(Sparql, "SELECT ?g WHERE { GRAPH ?g { ?s ?p ?o } }", "text/csv"));
        Assert.Contains(People, graphs, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, graphs, StringComparison.Ordinal);

        string all = await ReadAsync(host, P.Query(Sparql, "SELECT ?o WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } }", "text/csv"));
        Assert.Contains("default", all, StringComparison.Ordinal);
        Assert.Contains("people", all, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", all, StringComparison.Ordinal);

        HttpResponseMessage from = await host.Client.SendAsync(P.Query(Sparql, "SELECT * FROM <" + Secret + "> WHERE { ?s ?p ?o }", "text/csv"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, from.StatusCode);
        Assert.Equal("s,p,o\r\n", await from.Content.ReadAsStringAsync(P.Ct));
        HttpResponseMessage named = await host.Client.SendAsync(P.Query(Sparql, "SELECT * FROM NAMED <" + Secret + "> WHERE { GRAPH ?g { ?s ?p ?o } }", "text/csv"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal("g,s,p,o\r\n", await named.Content.ReadAsStringAsync(P.Ct));
        Assert.Contains("Authorization", P.Header(from, "Vary"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task the_graph_store_answers_404_for_an_unreadable_graph_and_403_for_an_unwritable_one()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, accessScopes: Fixed(Scope(People, defaultGraph: true), Scope(People, defaultGraph: false)), stopping: P.Ct);
        await FillAsync(dataset);
        string secret = "datasets/d/graphs?graph=" + Uri.EscapeDataString(Secret);
        string people = "datasets/d/graphs?graph=" + Uri.EscapeDataString(People);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(P.Get(secret, "application/n-triples"), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(P.Get(people, "application/n-triples"), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(Put(secret), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync(secret, P.Ct)).StatusCode);

        // The default graph is readable and not writable: 403, naming it.
        HttpResponseMessage forbidden = await host.Client.SendAsync(Put("datasets/d/graphs?default"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        string problem = await forbidden.Content.ReadAsStringAsync(P.Ct);
        Assert.Contains("graph-not-writable", problem, StringComparison.Ordinal);
        Assert.Contains("\"graph\":\"default\"", problem, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Put(people), P.Ct)).StatusCode);

        // A POST to the store mints a graph the caller may not write.
        HttpRequestMessage minted = new(HttpMethod.Post, new Uri("datasets/d/graphs", UriKind.Relative)) { Content = new StringContent("<http://ex/x> <http://ex/p> <http://ex/y> .\n", Encoding.UTF8, "application/n-triples") };
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(minted, P.Ct)).StatusCode);
    }

    [Fact]
    public async Task an_update_touching_an_unwritable_graph_is_403_and_commits_nothing_and_cannot_delete_what_it_cannot_read()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, accessScopes: Fixed(Scope(People, defaultGraph: true), Scope(People, defaultGraph: false)), stopping: P.Ct);
        await FillAsync(dataset);
        Position head = dataset.Head;

        HttpResponseMessage both = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + People + "> { <http://ex/bob> <http://ex/p> 1 } . <http://ex/d2> <http://ex/p> 2 }"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, both.StatusCode);
        Assert.Contains("\"graph\":\"default\"", await both.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        Assert.Equal(head, dataset.Head);

        HttpResponseMessage secret = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + Secret + "> { <http://ex/s2> <http://ex/p> 1 } }"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, secret.StatusCode);
        Assert.Contains("\"graph\":\"" + Secret + "\"", await secret.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);

        // DELETE WHERE over every graph sees only the readable ones: the
        // secret graph is untouched, and the default graph, readable but not
        // writable, is a 403 before anything commits.
        HttpResponseMessage delete = await host.Client.SendAsync(P.Update(Sparql, "DELETE WHERE { GRAPH ?g { ?s <http://ex/p> ?o } }"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        HttpResponseMessage deleteDefault = await host.Client.SendAsync(P.Update(Sparql, "DELETE WHERE { ?s <http://ex/p> ?o }"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deleteDefault.StatusCode);
        HttpResponseMessage clear = await host.Client.SendAsync(P.Update(Sparql, "CLEAR ALL"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, clear.StatusCode);

        using DatasetView view = dataset.Pin();
        SortedSet<string> lines = P.Lines(view);
        Assert.Contains(lines, l => l.Contains("secret", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("default", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("people", StringComparison.Ordinal));
    }

    [Fact]
    public async Task the_feed_and_the_diff_show_readable_changes_only_and_settings_to_admins_only()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, accessScopes: Fixed(Scope(People, defaultGraph: false), GraphScope.None), stopping: P.Ct);
        await FillAsync(dataset);
        await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current }, new CommitMetadata { Agent = RdfTerm.Iri("http://ex/admin"u8), Cause = RdfTerm.Literal("test"u8) }, cancellationToken: P.Ct);

        HttpResponseMessage feed = await host.Client.SendAsync(P.Get("datasets/d/feed?from=0&to=4"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        List<FeedRecord> records = await P.ReadFeedAsync(feed);
        Assert.Equal([2L], records.Select(r => r.Position.Value));
        Assert.All(records.SelectMany(r => r.Changes), change => Assert.Equal(People, Encoding.UTF8.GetString(change.Graph!.Lexical)));

        HttpResponseMessage diff = await host.Client.SendAsync(P.Get("datasets/d/diff?from=0&to=4"), P.Ct);
        string text = await diff.Content.ReadAsStringAsync(P.Ct);
        Assert.Contains("people", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("default", text, StringComparison.Ordinal);

        // The service description's named graphs are the caller's.
        string description = await ReadAsync(host, P.Get("datasets/d/sparql", "text/turtle"));
        Assert.Contains(People, description, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, description, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0107's first property: for any dataset, scope and query, the answer
    /// through the scope equals the answer over the dataset holding only the
    /// scope's graphs.
    /// </summary>
    [Fact]
    public async Task the_scoped_answer_equals_the_answer_over_the_sub_dataset_of_the_readable_graphs()
    {
        string[] graphs = [People, Secret, "http://ex/g/other"];
        Gen<string> quad = Gen.Select(Gen.OneOfConst("<http://ex/a>", "<http://ex/b>", "<http://ex/c>"), Gen.OneOfConst("<http://ex/p>", "<http://ex/q>"), Gen.OneOfConst("<http://ex/a>", "\"x\"", "1", "\"y\"@en"), Gen.Int[0, 3],
            (s, p, o, g) => g == 3 ? s + " " + p + " " + o : "GRAPH <" + graphs[g] + "> { " + s + " " + p + " " + o + " }");
        Gen<string> query = Gen.OneOfConst(
            "SELECT ?s ?p ?o WHERE { ?s ?p ?o }",
            "SELECT ?g ?s ?o WHERE { GRAPH ?g { ?s ?p ?o } }",
            "SELECT ?s ?o WHERE { GRAPH <" + People + "> { ?s <http://ex/p> ?o } }",
            "SELECT ?s WHERE { GRAPH <" + Secret + "> { ?s ?p ?o } }",
            "SELECT * FROM <" + Secret + "> FROM <" + People + "> WHERE { ?s ?p ?o }",
            "SELECT * FROM NAMED <" + Secret + "> FROM NAMED <http://ex/g/other> WHERE { GRAPH ?g { ?s ?p ?o } }",
            "SELECT (COUNT(*) AS ?n) WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } }",
            "ASK { GRAPH ?g { <http://ex/a> ?p ?o } }",
            "CONSTRUCT { ?s ?p ?o } WHERE { GRAPH ?g { ?s ?p ?o } }");
        Gen<(bool, bool, bool, bool, bool)> scope = Gen.Select(Gen.Bool, Gen.Bool, Gen.Bool, Gen.Bool, Gen.Bool, (a, b, c, d, e) => (a, b, c, d, e));
        await using ProtocolTestHost.Datasets datasets = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, accessScopes: new ByDataset(datasets), stopping: P.Ct);
        int next = 0;

        await Gen.Select(quad.List[0, 10], query, scope, (q, text, s) => (q, text, s)).SampleAsync(async sample =>
        {
            (List<string> quads, string text, (bool people, bool secret, bool other, bool defaultGraph, bool prefix) which) = sample;
            List<RdfTerm> named = [];

            if (which.people)
            {
                named.Add(RdfTerm.Iri(Encoding.UTF8.GetBytes(People)));
            }

            if (which.secret)
            {
                named.Add(RdfTerm.Iri(Encoding.UTF8.GetBytes(Secret)));
            }

            if (which.other)
            {
                named.Add(RdfTerm.Iri("http://ex/g/other"u8));
            }

            GraphScope readable = GraphScope.Of([.. named], which.prefix ? ["http://ex/g/"] : [], which.defaultGraph ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded);
            await using Dataset whole = await P.NewDatasetAsync();
            await using Dataset sub = await P.NewDatasetAsync();
            await CommitAsync(whole, quads);
            await CommitAsync(sub, quads.Where(q => readable.Allows(q.StartsWith("GRAPH", StringComparison.Ordinal) ? RdfTerm.Iri(Encoding.UTF8.GetBytes(q[7..q.IndexOf('>', StringComparison.Ordinal)])) : null)).ToList());
            string name = "p" + (++next).ToString(CultureInfo.InvariantCulture);
            datasets.Add(name, whole, new CallerScope(readable, GraphScope.None, AdminAccess.None));

            HttpResponseMessage response = await host.Client.SendAsync(P.Query("datasets/" + name + "/sparql", text, text.StartsWith("CONSTRUCT", StringComparison.Ordinal) ? "application/n-triples" : "application/sparql-results+json"), P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string actual = Canonical(await response.Content.ReadAsStringAsync(P.Ct));
            using DatasetView view = sub.Pin();
            Assert.Equal(Canonical(Evaluate(text, view)), actual);
            datasets.Remove(name);
        }, iter: PropertyTests.Iterations);
    }

    /// <summary>
    /// ADR 0107's second property: no response to a caller without read on a
    /// graph — query, graph store, service description, feed, diff, with
    /// their headers and problem bodies — carries an IRI that occurs only in
    /// that graph.
    /// </summary>
    [Fact]
    public async Task no_response_to_a_caller_without_read_on_a_graph_carries_an_iri_only_that_graph_holds()
    {
        await using ProtocolTestHost.Datasets datasets = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, accessScopes: new ByDataset(datasets), stopping: P.Ct);
        Gen<int> count = Gen.Int[1, 4];
        int next = 0;

        await Gen.Select(count, count, Gen.Bool, (hidden, shown, defaultGraph) => (hidden, shown, defaultGraph)).SampleAsync(async sample =>
        {
            (int hidden, int shown, bool defaultGraph) = sample;
            string name = "l" + (++next).ToString(CultureInfo.InvariantCulture);
            await using Dataset dataset = await P.NewDatasetAsync();
            List<string> quads = [];
            List<string> marks = [];

            for (int i = 0; i < hidden; i++)
            {
                string mark = "http://hidden.example/" + name + "/" + i.ToString(CultureInfo.InvariantCulture);
                marks.Add(mark);
                quads.Add("GRAPH <" + Secret + "> { <" + mark + "> <" + mark + "#p> <" + mark + "#o> }");
            }

            for (int i = 0; i < shown; i++)
            {
                quads.Add("GRAPH <" + People + "> { <http://ex/s" + i.ToString(CultureInfo.InvariantCulture) + "> <http://ex/p> <http://ex/o> }");
                quads.Add("<http://ex/d" + i.ToString(CultureInfo.InvariantCulture) + "> <http://ex/p> <http://ex/o>");
            }

            await CommitAsync(dataset, quads);
            datasets.Add(name, dataset, new CallerScope(Scope(People, defaultGraph), Scope(People, defaultGraph), AdminAccess.None));
            string prefix = "datasets/" + name;
            HttpRequestMessage[] requests =
            [
                P.Query(prefix + "/sparql", "SELECT * WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } }"),
                P.Query(prefix + "/sparql", "SELECT * FROM <" + Secret + "> WHERE { ?s ?p ?o }", "text/csv"),
                P.Query(prefix + "/sparql", "SELECT * FROM NAMED <" + Secret + "> WHERE { GRAPH ?g { ?s ?p ?o } }", "text/tab-separated-values"),
                P.Query(prefix + "/sparql", "CONSTRUCT { ?s ?p ?o } WHERE { GRAPH ?g { ?s ?p ?o } }", "application/n-quads"),
                P.Query(prefix + "/sparql", "DESCRIBE <" + marks[0] + ">", "text/turtle"),
                P.Get(prefix + "/graphs?graph=" + Uri.EscapeDataString(Secret), "application/n-triples"),
                P.Get(prefix + "/sparql", "application/trig"),
                P.Get(prefix + "/feed?from=0&to=1"),
                P.Get(prefix + "/diff?from=0&to=1"),
                P.Update(prefix + "/sparql", "DELETE WHERE { GRAPH <" + Secret + "> { ?s ?p ?o } }"),
                P.Update(prefix + "/sparql", "INSERT DATA { GRAPH <" + Secret + "> { <http://ex/n> <http://ex/p> <http://ex/o> } }"),
                Put(prefix + "/graphs?graph=" + Uri.EscapeDataString(Secret)),
            ];

            foreach (HttpRequestMessage request in requests)
            {
                HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
                string everything = response.ToString() + await response.Content.ReadAsStringAsync(P.Ct);

                foreach (string mark in marks)
                {
                    Assert.DoesNotContain(mark, everything, StringComparison.Ordinal);
                }

                Assert.DoesNotContain("hidden.example", everything, StringComparison.Ordinal);
            }

            datasets.Remove(name);
        }, iter: 200);
    }

    // ---- Helpers.

    private static GraphScope Scope(string graph, bool defaultGraph) =>
        GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(graph))], [], defaultGraph ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded);

    private static FixedScope Fixed(GraphScope readable, GraphScope writable) => new(new CallerScope(readable, writable, AdminAccess.None));

    private static async Task FillAsync(Dataset dataset)
    {
        foreach (string text in Everything)
        {
            await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
        }
    }

    private static async Task CommitAsync(Dataset dataset, List<string> quads)
    {
        if (quads.Count == 0)
        {
            return;
        }

        string text = "INSERT DATA { " + string.Join(" ", quads.Select(q => q.EndsWith('}') ? q : q + " .")) + " }";
        await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
    }

    private static async Task<string> ReadAsync(ProtocolTestHost host, HttpRequestMessage request)
    {
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(P.Ct);
    }

    private static HttpRequestMessage Put(string path) => new(HttpMethod.Put, new Uri(path, UriKind.Relative))
    {
        Content = new StringContent("<http://ex/x> <http://ex/p> <http://ex/y> .\n", Encoding.UTF8, "application/n-triples"),
    };

    // A query answered in process over a view, in the format the server writes it in.
    private static string Evaluate(string text, DatasetView view)
    {
        System.Buffers.ArrayBufferWriter<byte> output = new();
        using QueryResults results = new SparqlEvaluator(new EvaluationOptions()).Evaluate(SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(text)), view, P.Ct);

        switch (results)
        {
            case SolutionResults solutions:
                using (SparqlResultsWriter writer = new(output, SparqlResultsFormat.Json))
                {
                    string[] names = [.. solutions.Variables.Select(v => v.Name)];
                    writer.WriteHead(names);

                    while (solutions.MoveNext())
                    {
                        writer.StartSolution();

                        for (int i = 0; i < names.Length; i++)
                        {
                            if (solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term))
                            {
                                writer.WriteBinding(i, term!);
                            }
                        }

                        writer.EndSolution();
                    }

                    writer.WriteEnd();
                }

                break;
            case BooleanResult boolean:
                using (SparqlResultsWriter writer = new(output, SparqlResultsFormat.Json))
                {
                    writer.WriteBoolean(boolean.Value);
                    writer.WriteEnd();
                }

                break;
            case TripleResults triples:
                Turtle.WriteOptions lines = new() { Syntax = Turtle.RdfSyntax.NTriples };
                byte[] line = new byte[4096];

                while (triples.MoveNext())
                {
                    int written = 0;
                    int part;
                    Assert.True(Turtle.NQuadsWriter.TryWriteTerm(triples.Subject, line.AsSpan(written), out part, in lines));
                    written += part;
                    line[written++] = (byte)' ';
                    Assert.True(Turtle.NQuadsWriter.TryWriteTerm(triples.Predicate, line.AsSpan(written), out part, in lines));
                    written += part;
                    line[written++] = (byte)' ';
                    Assert.True(Turtle.NQuadsWriter.TryWriteTerm(triples.Object, line.AsSpan(written), out part, in lines));
                    written += part;
                    line[written++] = (byte)' ';
                    line[written++] = (byte)'.';
                    line[written++] = (byte)'\n';
                    System.Buffers.BuffersExtensions.Write(output, line.AsSpan(0, written));
                }

                break;
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    // Solutions and triples are unordered: compare them as sorted lines or sorted bindings.
    private static string Canonical(string body)
    {
        if (body.StartsWith('{'))
        {
            int at = body.IndexOf("\"bindings\":[", StringComparison.Ordinal);

            if (at < 0)
            {
                return body;
            }

            string head = body[..at];
            string rows = body[(at + "\"bindings\":[".Length)..];
            rows = rows[..rows.LastIndexOf(']')];
            List<string> bindings = [];
            int depth = 0;
            int start = 0;

            for (int i = 0; i < rows.Length; i++)
            {
                depth += rows[i] == '{' ? 1 : rows[i] == '}' ? -1 : 0;

                if (depth == 0 && rows[i] == '}')
                {
                    bindings.Add(rows[start..(i + 1)]);
                    start = i + 1;

                    if (start < rows.Length && rows[start] == ',')
                    {
                        start++;
                    }
                }
            }

            bindings.Sort(StringComparer.Ordinal);
            return head + string.Join("|", bindings);
        }

        return string.Join("\n", body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
    }

    private sealed class FixedScope(CallerScope scope) : IAccessScopes
    {
        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => scope;
    }

    /// <summary>A scope per dataset name, for the properties' many datasets behind one server.</summary>
    private sealed class ByDataset(ProtocolTestHost.Datasets datasets) : IAccessScopes
    {
        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => datasets.ScopeOf(dataset.Value) ?? CallerScope.Everything;
    }
}
