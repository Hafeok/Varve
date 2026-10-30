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
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Classification;
using Varve.Classification.Model;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Row-level access over HTTP (ADRs 0230, 0236, 0237): for generated data and
/// subject sets, a query answers what the row-scoped view holds; no response
/// on any surface carries the IRI of a node the caller sees no quad naming;
/// the feed folds to the caller's view; a row-restricted write's outcome is
/// the same whether or not the quads it cannot see are there; and the access
/// route answers an admin the quads about a subject.
/// </summary>
public partial class RowScopeTests
{
    /// <summary>Generated cases per run on the land gate; ten times as many in the nightly <c>long</c> job.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 80 : 800;

    private const string Ex = "http://ex/";
    private const string Shapes = "http://ex/shapes";
    private static readonly string[] Nodes = ["http://ex/a", "http://ex/b", "http://ex/c", "http://ex/x", "http://ex/y"];
    private static readonly string[] People = ["http://ex/a", "http://ex/b", "http://ex/c"];
    private static readonly ClassifierBinding Binding = new(Iri("http://ex/binding"), Iri(Shapes), GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded));

    private const string ShapesText =
        "GRAPH <" + Shapes + "> { <http://ex/S> <https://w3id.org/varve/ns#dataSubject> true ; <http://www.w3.org/ns/shacl#targetClass> <http://ex/Person> . "
        + "<http://ex/L> <http://www.w3.org/ns/shacl#path> <http://ex/ownedBy> ; <https://w3id.org/varve/ns#subjectLink> true . "
        + "<http://ex/P> <http://www.w3.org/ns/shacl#path> <http://ex/label> ; <https://w3id.org/varve/ns#publicLink> true . }";

    private static readonly Gen<string> Quad = Gen.Select(
        Gen.OneOfConst(Nodes),
        Gen.OneOfConst("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", Ex + "ownedBy", Ex + "knows", Ex + "label"),
        Gen.OneOfConst([.. Nodes, "lit"]),
        Gen.OneOfConst("http://ex/g/1", "http://ex/g/2", "http://ex/other"),
        (s, p, o, g) => "GRAPH <" + g + "> { <" + s + "> <" + p + "> " + (p.EndsWith("#type", StringComparison.Ordinal) ? "<http://ex/Person>" : o == "lit" ? "\"lit\"" : "<" + o + ">") + " }");

    private static readonly string[] Queries =
    [
        "SELECT ?g ?s ?p ?o WHERE { GRAPH ?g { ?s ?p ?o } }",
        "SELECT ?s ?o WHERE { GRAPH ?g { ?s <http://ex/ownedBy>+ ?o } }",
        "SELECT ?s WHERE { GRAPH <http://ex/g/1> { ?s a <http://ex/Person> } }",
        "SELECT (COUNT(*) AS ?n) WHERE { GRAPH ?g { ?s ?p ?o } }",
        "CONSTRUCT { ?s ?p ?o } WHERE { GRAPH ?g { ?s ?p ?o } }",
    ];

    [Fact]
    public async Task A_row_restricted_answer_equals_the_answer_over_the_rows_it_sees()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(Quad.List[0, 12], Gen.OneOfConst(Queries), Gen.Int[0, 7]).SampleAsync(async (quads, text, set) =>
        {
            (string name, Dataset dataset, RowScope scope) = await rows.AddAsync(quads, set);
            HttpResponseMessage response = await host.Client.SendAsync(P.Query("datasets/" + name + "/sparql", text, text.StartsWith("CONSTRUCT", StringComparison.Ordinal) ? "application/n-triples" : "application/sparql-results+json"), P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using DatasetView view = dataset.Pin();
            IQuadSource visible = RowScopedQuadSource.Wrap(view, new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), scope);
            using QueryResults expected = new Varve.Sparql.Evaluation.SparqlEvaluator(new Varve.Sparql.Evaluation.EvaluationOptions()).Evaluate(SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(text)), visible, P.Ct);
            Assert.Equal(Count(expected), Count(await response.Content.ReadAsStringAsync(P.Ct), text));
            rows.Remove(name);
        }, iter: Iterations);
    }

    /// <summary>
    /// ADR 0230 point 7's case: no response — query, Graph Store, service
    /// description, the commits feed, the diff — carries the IRI of a node
    /// that no quad the caller sees names, and the feed's fold and the diff
    /// from 0 both equal the caller's view at the head.
    /// </summary>
    [Fact]
    public async Task No_response_carries_a_hidden_iri_and_the_feed_folds_to_the_view()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(Quad.List[1, 6].List[1, 4], Gen.Int[0, 7]).SampleAsync(async (commits, set) =>
        {
            (string name, Dataset dataset, RowScope scope) = await rows.AddAsync([], set);

            foreach (List<string> commit in commits)
            {
                await CommitAsync(dataset, commit);
            }

            await rows.CatchUpAsync(name);
            using DatasetView view = dataset.Pin();
            IQuadSource visible = RowScopedQuadSource.Wrap(view, new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), scope);
            HashSet<string> seen = Lines(visible);
            HashSet<string> named = [.. seen.SelectMany(static l => l.Split(' '))];
            string[] hidden = [.. Nodes.Where(n => !named.Contains("<" + n + ">"))];

            string bodies = string.Concat(
                await ReadAsync(host, P.Query("datasets/" + name + "/sparql", Queries[0])),
                await (await host.Client.SendAsync(Get("datasets/" + name + "/graphs?graph=http%3A%2F%2Fex%2Fg%2F1", "application/n-quads"), P.Ct)).Content.ReadAsStringAsync(P.Ct),
                await ReadAsync(host, Get("datasets/" + name + "/", "text/turtle")),
                await ReadAsync(host, Get("datasets/" + name + "/diff?from=0&to=" + view.Position.Value.ToString(CultureInfo.InvariantCulture), "application/vnd.varve.delta")));

            foreach (string iri in hidden)
            {
                Assert.DoesNotContain("<" + iri + ">", bodies, StringComparison.Ordinal);
            }

            // The feed is history: it carries what the caller saw at each
            // position, so a node is hidden from it only when hidden at all.
            HashSet<string> ever = [];
            for (long p = 0; p <= view.Position.Value; p++)
            {
                using DatasetView at = await dataset.AsOfAsync(new Position(p), P.Ct);
                ever.UnionWith(Lines(RowScopedQuadSource.Wrap(at, new ShapeClassifier([Binding]).Classify(at, QuadDelta.Empty), scope)).SelectMany(static l => l.Split(' ')));
            }

            string feed = await ReadAsync(host, Get("datasets/" + name + "/commits?from=0&to=" + view.Position.Value.ToString(CultureInfo.InvariantCulture), "application/vnd.varve.delta"));
            foreach (string iri in Nodes.Where(n => !ever.Contains("<" + n + ">")))
            {
                Assert.DoesNotContain("<" + iri + ">", feed, StringComparison.Ordinal);
            }

            Assert.Equal(seen, await FoldAsync(host, "datasets/" + name + "/commits?from=0&to=" + view.Position.Value.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal(seen, await FoldAsync(host, "datasets/" + name + "/diff?from=0&to=" + view.Position.Value.ToString(CultureInfo.InvariantCulture)));
            rows.Remove(name);
        }, iter: Iterations);
    }

    /// <summary>
    /// ADR 0236's property over HTTP, the probe case: the same write, by
    /// SPARQL Update (INSERT DATA, DELETE DATA) or the Graph Store (POST,
    /// PUT, DELETE of a graph), has the same outcome over a dataset and over
    /// the same dataset without every quad the writer does not see; a
    /// refusal is the one 403 row-not-writable, and what the writer sees
    /// after the write is the same over both.
    /// </summary>
    [Fact]
    public async Task A_row_restricted_writes_outcome_never_depends_on_a_quad_it_does_not_see()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);
        Gen<Func<string, HttpRequestMessage>> update = Gen.Select(Gen.Bool, Quad.List[1, 2], (insert, quads) =>
        {
            string text = (insert ? "INSERT DATA { " : "DELETE DATA { ") + string.Join(" ", quads) + " }";
            return (Func<string, HttpRequestMessage>)(name => P.Update("datasets/" + name + "/sparql", text));
        });
        Gen<Func<string, HttpRequestMessage>> graphStore = Gen.Select(Gen.OneOfConst("POST", "PUT", "DELETE"), Gen.OneOfConst("http://ex/g/1", "http://ex/g/2"), Quad.List[0, 2], (method, graph, quads) =>
        {
            string body = string.Concat(quads.Select(static q => Triple(q) + "\n"));
            return (Func<string, HttpRequestMessage>)(name =>
            {
                HttpRequestMessage request = new(new HttpMethod(method), new Uri("datasets/" + name + "/graphs?graph=" + Uri.EscapeDataString(graph), UriKind.Relative));

                if (method != "DELETE")
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/n-triples");
                }

                return request;
            });
        });
        int refused = 0;

        await Gen.Select(Quad.List[0, 10], Gen.OneOf(update, graphStore), Gen.Int[0, 7]).SampleAsync(async (quads, write, set) =>
        {
            (string whole, Dataset dataset, RowScope scope) = await rows.AddAsync(quads, set, writes: true);
            List<string> seen;

            using (DatasetView view = dataset.Pin())
            {
                seen = [.. Lines(RowScopedQuadSource.Wrap(view, new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), scope)).Select(static l => Quoted(l))];
            }

            (string part, Dataset partial, _) = await rows.AddAsync(seen, set, writes: true);
            HttpResponseMessage first = await host.Client.SendAsync(write(whole), P.Ct);
            HttpResponseMessage second = await host.Client.SendAsync(write(part), P.Ct);
            string firstBody = await first.Content.ReadAsStringAsync(P.Ct);
            Assert.Equal(first.StatusCode, second.StatusCode);
            Assert.Equal(Problem(firstBody), Problem(await second.Content.ReadAsStringAsync(P.Ct)));

            if (first.StatusCode == HttpStatusCode.Forbidden)
            {
                Assert.Contains("row-not-writable", firstBody, StringComparison.Ordinal);
                refused++;
            }

            Assert.Equal(await SeenAsync(dataset, scope), await SeenAsync(partial, scope));
            rows.Remove(whole);
            rows.Remove(part);
        }, iter: Iterations);

        Assert.True(refused > 0, "no generated write was refused");
    }

    // The writer's view at the head, classified from scratch.
    private static async Task<HashSet<string>> SeenAsync(Dataset dataset, RowScope scope)
    {
        await Task.Yield();
        using DatasetView view = dataset.Pin();
        return Lines(RowScopedQuadSource.Wrap(view, new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), scope));
    }

    // A generated TriG quad as the N-Triples line of its triple.
    private static string Triple(string quad)
    {
        int open = quad.IndexOf('{', StringComparison.Ordinal);
        int close = quad.LastIndexOf('}');
        return quad[(open + 1)..close].Trim() + " .";
    }

    [Fact]
    public async Task A_row_restricted_read_above_the_projection_is_classification_behind()
    {
        await using Rows rows = new() { KeepUp = false };
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);
        (string name, Dataset dataset, _) = await rows.AddAsync(["GRAPH <http://ex/g/1> { <http://ex/a> a <http://ex/Person> }"], 1);
        await CommitAsync(dataset, ["GRAPH <http://ex/g/1> { <http://ex/x> <http://ex/ownedBy> <http://ex/a> }"]);

        HttpResponseMessage response = await host.Client.SendAsync(P.Query("datasets/" + name + "/sparql", Queries[0]), P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("1", response.Headers.RetryAfter?.ToString());
        Assert.Contains("classification-behind", await response.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);

        await rows.CatchUpAsync(name);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(P.Query("datasets/" + name + "/sparql", Queries[0]), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task The_access_route_answers_the_quads_about_a_subject()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);
        (string name, _, _) = await rows.AddAsync(
        [
            "GRAPH <http://ex/g/1> { <http://ex/a> a <http://ex/Person> }",
            "GRAPH <http://ex/g/1> { <http://ex/x> <http://ex/ownedBy> <http://ex/a> }",
            "GRAPH <http://ex/g/2> { <http://ex/b> a <http://ex/Person> }",
            "GRAPH <http://ex/other> { <http://ex/a> <http://ex/knows> <http://ex/b> }",
        ], 7);

        string body = await ReadAsync(host, Get("datasets/" + name + "/access?subject=http%3A%2F%2Fex%2Fa", "application/n-quads"));
        Assert.Equal(2, body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("<http://ex/x> <http://ex/ownedBy> <http://ex/a>", body, StringComparison.Ordinal);
        HttpResponseMessage history = await host.Client.SendAsync(Get("datasets/" + name + "/access?subject=http%3A%2F%2Fex%2Fa&scope=AllHistory", "application/n-quads"), P.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, history.StatusCode);
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    private static RowScope Scope(int set) =>
        RowScope.Of([new(Binding.Binding, SubjectScope.Of([.. People.Where((_, i) => (set & (1 << i)) != 0).Select(Iri)], [], [], [], ["http://ex/new/"]))]);

    private static async Task CommitAsync(Dataset dataset, List<string> quads)
    {
        if (quads.Count == 0)
        {
            return;
        }

        string text = "INSERT DATA { " + string.Join(" ", quads) + " }";
        await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
    }

    private static HttpRequestMessage Get(string path, string accept)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    private static async Task<string> ReadAsync(ProtocolTestHost host, HttpRequestMessage request)
    {
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        Assert.True(response.IsSuccessStatusCode, request.RequestUri + " answered " + (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync(P.Ct));
        return await response.Content.ReadAsStringAsync(P.Ct);
    }

    // A feed's or a diff's changes folded from nothing, as N-Quads lines.
    private static async Task<HashSet<string>> FoldAsync(ProtocolTestHost host, string path)
    {
        HttpResponseMessage response = await host.Client.SendAsync(Get(path, "application/vnd.varve.delta"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HashSet<string> state = [];

        await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(P.Ct), P.Ct))
        {
            foreach (FeedChange change in record.Changes)
            {
                if (change.Graph is { } graph && Encoding.UTF8.GetString(graph.Lexical) == Shapes)
                {
                    continue;
                }

                string line = Line(change.Subject, change.Predicate, change.Object, change.Graph);
                _ = change.Kind == FeedChangeKind.Assert ? state.Add(line) : state.Remove(line);
            }
        }

        return state;
    }

    private static HashSet<string> Lines(IQuadSource source)
    {
        HashSet<string> lines = [];
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Varve.Rdf.Quad q = cursor.Current;
            Assert.True(source.TryExternalise(q.Subject, out RdfTerm? s));
            Assert.True(source.TryExternalise(q.Predicate, out RdfTerm? p));
            Assert.True(source.TryExternalise(q.Object, out RdfTerm? o));
            RdfTerm? g = null;
            Assert.True(q.Graph.IsNone || source.TryExternalise(q.Graph, out g));

            if (g is not null && Encoding.UTF8.GetString(g.Lexical) == Shapes)
            {
                continue;
            }

            lines.Add(Line(s!, p!, o!, g));
        }

        return lines;
    }

    private static string Line(RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) =>
        Term(s) + " " + Term(p) + " " + Term(o) + " " + (g is null ? "default" : Term(g));

    private static string Term(RdfTerm term) => term.Kind == RdfTermKind.Literal ? "\"" + Encoding.UTF8.GetString(term.Lexical) + "\"" : "<" + Encoding.UTF8.GetString(term.Lexical) + ">";

    // A line back into the TriG the generator writes.
    private static string Quoted(string line)
    {
        string[] parts = line.Split(' ');
        return "GRAPH " + parts[3] + " { " + parts[0] + " " + parts[1] + " " + parts[2] + " }";
    }

    // A problem body without its request id: the same refusal has the same body.
    private static string Problem(string body) => System.Text.RegularExpressions.Regex.Replace(body, "\"instance\":\"[^\"]*\"", string.Empty);

    private static int Count(QueryResults results)
    {
        int count = 0;

        switch (results)
        {
            case SolutionResults solutions:
                while (solutions.MoveNext())
                {
                    count++;
                }

                return count;
            case TripleResults triples:
                HashSet<(RdfTerm, RdfTerm, RdfTerm)> distinct = [];

                while (triples.MoveNext())
                {
                    distinct.Add((triples.Subject, triples.Predicate, triples.Object));
                }

                return distinct.Count;
            default:
                return -1;
        }
    }

    private static int Count(string body, string text)
    {
        if (text.StartsWith("CONSTRUCT", StringComparison.Ordinal))
        {
            return body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(body);
        return json.RootElement.GetProperty("results").GetProperty("bindings").GetArrayLength();
    }

    /// <summary>The datasets, each with its caller's row scope and its classification projection.</summary>
    private sealed class Rows : IAccessScopes, IRowClassifications, IAsyncDisposable
    {
        private readonly Dictionary<string, (Dataset Dataset, CallerScope Scope, ClassificationProjection Projection)> _held = [];
        private int _next;

        internal ProtocolTestHost.Datasets Datasets { get; } = new();

        /// <summary>Whether a read catches the projection up first; off, a read above it is behind.</summary>
        internal bool KeepUp { get; init; } = true;

        internal async Task<(string Name, Dataset Dataset, RowScope Scope)> AddAsync(IReadOnlyList<string> quads, int set, bool writes = false, bool unrestricted = false)
        {
            Dataset dataset = await P.NewDatasetAsync();
            await CommitAsync(dataset, [ShapesText]);
            await CommitAsync(dataset, [.. quads]);
            RowScope scope = Scope(set);
            string name = "r" + Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture);
            CallerScope caller = unrestricted ? CallerScope.Everything : new(GraphScope.All, writes ? GraphScope.All : GraphScope.None, scope, writes ? scope : RowScope.All, AdminAccess.None);
            ClassificationProjection projection = new(dataset, Binding);
            await dataset.CatchUpAsync(projection, P.Ct);

            lock (_held)
            {
                _held[name] = (dataset, caller, projection);
            }

            Datasets.Add(name, dataset, caller);
            return (name, dataset, scope);
        }

        internal async Task CatchUpAsync(string name)
        {
            (Dataset dataset, _, ClassificationProjection projection) = Entry(name);
            await dataset.CatchUpAsync(projection, P.Ct);
        }

        internal void Remove(string name) => Datasets.Remove(name);

        /// <summary>The same dataset under a second name, for a caller who reads every graph and every row.</summary>
        internal string Alias(string name)
        {
            string alias = "u" + name;
            Datasets.Add(alias, Entry(name).Dataset, CallerScope.Everything);
            return alias;
        }

        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) =>
            dataset.Value.StartsWith('r') && TryEntry(dataset.Value, out var entry) ? entry.Scope : CallerScope.Everything;

        // The projection kept current with every write the host makes, then read at the view.
        public IRowClassification? At(DatasetName dataset, DatasetView view)
        {
            (Dataset held, _, ClassificationProjection projection) = Entry(dataset.Value);
            if (KeepUp)
            {
                held.CatchUpAsync(projection, P.Ct).AsTask().GetAwaiter().GetResult();
            }

            return projection.TryAt(view.Position, view, out IRowClassification rows) ? rows : throw new ClassificationBehindException(Binding.Binding, view.Position, projection.Position);
        }

        public IClassifier? Classifier(DatasetName dataset) => new ShapeClassifier([Binding]);

        public IReadOnlyList<TermHandle> ChangedAt(DatasetName dataset, Position position) => Entry(dataset.Value).Projection.ChangedAt(position);

        public async ValueTask DisposeAsync()
        {
            foreach ((Dataset dataset, _, _) in _held.Values)
            {
                await dataset.DisposeAsync();
            }

            await Datasets.DisposeAsync();
        }

        private (Dataset Dataset, CallerScope Scope, ClassificationProjection Projection) Entry(string name)
        {
            lock (_held)
            {
                return _held[name];
            }
        }

        private bool TryEntry(string name, out (Dataset Dataset, CallerScope Scope, ClassificationProjection Projection) entry)
        {
            lock (_held)
            {
                return _held.TryGetValue(name, out entry);
            }
        }
    }
}
