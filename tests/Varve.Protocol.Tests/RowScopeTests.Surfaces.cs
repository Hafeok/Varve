// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsCheck;
using Varve.Classification;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// ADR 0237's property surface by surface, over generated histories of
/// inserts and deletes: each answer of a row-restricted caller equals the
/// answer an unrestricted caller gets over a dataset holding exactly the
/// quads the restricted caller sees, V_p; at the head and as of a generated
/// position. The expected views are classified from scratch at each
/// position, so they are independent of the projection the host serves.
/// </summary>
public partial class RowScopeTests
{
    private static readonly string[] SurfaceQueries =
    [
        .. Queries,
        "SELECT ?s ?p ?o FROM <http://ex/g/1> FROM <http://ex/other> WHERE { ?s ?p ?o }",
        "SELECT ?g ?s ?o FROM NAMED <http://ex/g/2> FROM NAMED <http://ex/other> WHERE { GRAPH ?g { ?s ?p ?o } }",
        "SELECT ?s ?o WHERE { GRAPH ?g { ?s (<http://ex/ownedBy>|^<http://ex/knows>)* ?o } }",
        "SELECT ?s ?o WHERE { GRAPH ?g { ?s <http://ex/knows>/<http://ex/ownedBy>? ?o } }",
        "SELECT DISTINCT ?g WHERE { GRAPH ?g { ?s ?p ?o } }",
        "ASK { GRAPH ?g { <http://ex/a> <http://ex/knows> ?o } }",
        "CONSTRUCT { ?s ?p ?o } FROM <http://ex/g/1> WHERE { ?s ?p ?o }",
    ];

    private static readonly string[] SurfaceGraphs = ["http://ex/g/1", "http://ex/g/2", "http://ex/other"];

    // A history: each commit inserts generated quads or deletes some of the quads inserted before it.
    private static readonly Gen<List<(bool Delete, List<string> Quads, int[] Picks)>> History =
        Gen.Select(Gen.Int[0, 3], Quad.List[1, 4], Gen.Int[0, 1_000].Array[1, 3], (d, quads, picks) => (d == 0, quads, picks)).List[1, 5];

    [Fact]
    public async Task Every_query_answers_what_the_caller_sees_at_the_position_read()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(History, Gen.OneOfConst(SurfaceQueries), Gen.Int[0, 7], Gen.Int[0, 1_000]).SampleAsync(async (history, text, set, pick) =>
        {
            (string name, Dataset dataset, RowScope scope) = await HistoryAsync(rows, history, set);
            long p = 1 + (pick % dataset.Head.Value);
            HashSet<string> seen = await VisibleAsync(dataset, scope, p);
            (string part, _, _) = await rows.AddAsync([.. seen.Select(Quoted)], set, unrestricted: true);

            string accept = text.StartsWith("CONSTRUCT", StringComparison.Ordinal) ? "application/n-triples" : "application/sparql-results+json";
            string asOf = "position:" + p.ToString(CultureInfo.InvariantCulture);
            string restricted = await ReadAsync(host, P.Query("datasets/" + name + "/sparql", text, accept, asOf));
            string expected = await ReadAsync(host, P.Query("datasets/" + part + "/sparql", text, accept));
            Assert.Equal(Canonical(expected, accept), Canonical(restricted, accept));
            rows.Remove(name);
            rows.Remove(part);
        }, iter: Iterations);
    }

    /// <summary>
    /// Graph Store <c>GET</c> of every graph, at a generated position: the
    /// quads of that graph the caller sees, an empty graph when it holds
    /// quads none of which are seen, <c>404</c> only when it holds none.
    /// </summary>
    [Fact]
    public async Task A_graph_store_get_answers_the_quads_of_the_graph_the_caller_sees()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(History, Gen.Int[0, 7], Gen.Int[0, 1_000]).SampleAsync(async (history, set, pick) =>
        {
            (string name, Dataset dataset, RowScope scope) = await HistoryAsync(rows, history, set);
            long p = 1 + (pick % dataset.Head.Value);
            HashSet<string> seen = await VisibleAsync(dataset, scope, p);
            HashSet<string> held = await StateAsync(dataset, p);

            foreach (string graph in SurfaceGraphs)
            {
                HttpRequestMessage get = Get("datasets/" + name + "/graphs?graph=" + Uri.EscapeDataString(graph), "application/n-triples");
                get.Headers.TryAddWithoutValidation("Varve-As-Of", "position:" + p.ToString(CultureInfo.InvariantCulture));
                HttpResponseMessage response = await host.Client.SendAsync(get, P.Ct);
                string suffix = " <" + graph + ">";

                if (!held.Any(l => l.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                    continue;
                }

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                HashSet<string> expected = [.. seen.Where(l => l.EndsWith(suffix, StringComparison.Ordinal)).Select(l => l[..^suffix.Length] + " .")];
                Assert.Equal(expected, Triples(await response.Content.ReadAsStringAsync(P.Ct)));
            }

            rows.Remove(name);
        }, iter: Iterations);
    }

    /// <summary>
    /// The feed, commit by commit: each delivered record at p carries
    /// exactly (V_p \ V_{p−1}, V_{p−1} \ V_p), reclassified quads included,
    /// and a data commit whose delivered delta is empty is not delivered.
    /// </summary>
    [Fact]
    public async Task Each_feed_record_is_the_delta_of_the_callers_view()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(History, Gen.Int[0, 7]).SampleAsync(async (history, set) =>
        {
            (string name, Dataset dataset, RowScope scope) = await HistoryAsync(rows, history, set);
            long head = dataset.Head.Value;
            List<HashSet<string>> views = [[]];

            for (long p = 1; p <= head; p++)
            {
                views.Add(await VisibleAsync(dataset, scope, p));
            }

            Dictionary<long, (HashSet<string> Asserted, HashSet<string> Retracted)> records = await RecordsAsync(host, "datasets/" + name + "/commits?from=0&to=" + head.ToString(CultureInfo.InvariantCulture));

            for (long p = 2; p <= head; p++)
            {
                HashSet<string> gained = [.. views[(int)p].Except(views[(int)p - 1])];
                HashSet<string> lost = [.. views[(int)p - 1].Except(views[(int)p])];

                if (gained.Count == 0 && lost.Count == 0)
                {
                    Assert.False(records.ContainsKey(p), "position " + p.ToString(CultureInfo.InvariantCulture) + " delivered, with nothing of the caller's view changed");
                    continue;
                }

                Assert.True(records.TryGetValue(p, out (HashSet<string> Asserted, HashSet<string> Retracted) record), "position " + p.ToString(CultureInfo.InvariantCulture) + " not delivered");
                Assert.Equal(gained, record.Asserted);
                Assert.Equal(lost, record.Retracted);
            }

            rows.Remove(name);
        }, iter: Iterations);
    }

    /// <summary>
    /// The diff over a generated pair of positions is exactly
    /// (V_{P₂} \ V_{P₁}, V_{P₁} \ V_{P₂}); and <c>/commits/{p}</c> is the
    /// commit's own delta, additions judged at p and retractions at p − 1.
    /// </summary>
    [Fact]
    public async Task The_diff_and_one_commit_answer_the_callers_view_of_them()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(History, Gen.Int[0, 7], Gen.Int[0, 1_000], Gen.Int[0, 1_000]).SampleAsync(async (history, set, a, b) =>
        {
            (string name, Dataset dataset, RowScope scope) = await HistoryAsync(rows, history, set);
            long head = dataset.Head.Value;
            long from = a % (head + 1);
            long to = from + (b % (head - from + 1));
            HashSet<string> earlier = from == 0 ? [] : await VisibleAsync(dataset, scope, from);
            HashSet<string> later = to == 0 ? [] : await VisibleAsync(dataset, scope, to);

            (HashSet<string> asserted, HashSet<string> retracted) = Union(await RecordsAsync(host, "datasets/" + name + "/diff?from=" + from.ToString(CultureInfo.InvariantCulture) + "&to=" + to.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal([.. later.Except(earlier)], asserted);
            Assert.Equal([.. earlier.Except(later)], retracted);

            for (long p = 2; p <= head; p++)
            {
                HashSet<string> before = await StateAsync(dataset, p - 1);
                HashSet<string> after = await StateAsync(dataset, p);
                HashSet<string> seenBefore = await VisibleAsync(dataset, scope, p - 1);
                HashSet<string> seenAfter = await VisibleAsync(dataset, scope, p);

                (HashSet<string> added, HashSet<string> removed) = Union(await RecordsAsync(host, "datasets/" + name + "/commits/" + p.ToString(CultureInfo.InvariantCulture)));
                Assert.Equal([.. after.Except(before).Where(seenAfter.Contains)], added);
                Assert.Equal([.. before.Except(after).Where(seenBefore.Contains)], removed);
            }

            rows.Remove(name);
        }, iter: Iterations);
    }

    /// <summary>The service description lists the graphs as it does for a caller who reads every row (ADR 0237 point 3).</summary>
    [Fact]
    public async Task The_service_description_is_the_one_every_graph_reader_gets()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, stopping: P.Ct);

        await Gen.Select(History, Gen.Int[0, 7]).SampleAsync(async (history, set) =>
        {
            (string name, _, _) = await HistoryAsync(rows, history, set);
            string alias = rows.Alias(name);
            string restricted = await ReadAsync(host, Get("datasets/" + name + "/", "application/n-triples"));
            string everything = await ReadAsync(host, Get("datasets/" + alias + "/", "application/n-triples"));
            HashSet<string> wanted = Triples(everything.Replace("/" + alias + "/", "/" + name + "/", StringComparison.Ordinal));
            HashSet<string> got = Triples(restricted);
            Assert.True(wanted.SetEquals(got), "only for every row: " + string.Join("\n", wanted.Except(got)) + "\nonly restricted: " + string.Join("\n", got.Except(wanted)));
            rows.Remove(name);
            rows.Remove(alias);
        }, iter: Iterations);
    }

    /// <summary>
    /// ADR 0237 point 6: a row-restricted caller sees no validation report
    /// graph by query or Graph Store, and no commit it reads carries a
    /// report attachment, on the commits resource, the feed or the diff. A
    /// caller who reads every row sees both.
    /// </summary>
    [Fact]
    public async Task A_row_restricted_caller_reads_no_report_graph_and_no_report_attachment()
    {
        await using Rows rows = new();
        (string name, Dataset dataset, _) = await rows.AddAsync(["GRAPH <http://ex/g/1> { <http://ex/a> a <http://ex/Person> }"], 1);
        await CommitAsync(dataset, ["GRAPH <" + ReportShapes + "> { <http://ex/R> <" + Sh + "targetSubjectsOf> <http://ex/age> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/age> ; <" + Sh + "datatype> <http://www.w3.org/2001/XMLSchema#integer> ] }"]);
        await CommitAsync(dataset, ["GRAPH <http://ex/g/1> { <http://ex/a> <http://ex/age> \"old\" }"]);

        // A commit carrying a report attachment, as a report binding's validator attaches one (ADR 0173).
        RdfTerm attachment = RdfTerm.TripleTerm(Iri("urn:report"), Iri("https://w3id.org/varve/ns#affectedReport"), Iri(ReportGraph));
        await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(
            dataset,
            SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes("INSERT DATA { GRAPH <http://ex/g/1> { <http://ex/a> <http://ex/knows> <http://ex/a> } }")),
            new Varve.Sparql.Store.UpdateOptions { Metadata = new CommitMetadata { Attachments = [attachment] } },
            P.Ct);
        await rows.CatchUpAsync(name);
        string alias = rows.Alias(name);

        ValidationReport reports = new(new Varve.Shacl.Store.ValidationProjection(dataset, new Varve.Shacl.Store.Model.ShapesBinding(Iri("urn:report"), Iri(ReportShapes), GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded), Varve.Shacl.Store.Model.ValidationMode.Report)));
        await dataset.CatchUpAsync(reports.Projection, P.Ct);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, validationReports: reports, stopping: P.Ct);

        string head = dataset.Head.Value.ToString(CultureInfo.InvariantCulture);
        string focus = "SELECT ?focus WHERE { GRAPH <" + ReportGraph + "> { ?r <" + Sh + "focusNode> ?focus } }";
        string[] commits = ["/commits/" + head, "/commits?from=0&to=" + head, "/diff?from=0&to=" + head];

        // The caller who reads every row: the report, and the attachment.
        Assert.Contains("http://ex/a", await ReadAsync(host, P.Query("datasets/" + alias + "/sparql", focus, "text/csv")), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("datasets/" + alias + "/graphs?graph=" + Uri.EscapeDataString(ReportGraph), "application/n-triples"), P.Ct)).StatusCode);
        Assert.Contains("affectedReport", await ReadAsync(host, Get("datasets/" + alias + commits[0], "application/vnd.varve.delta")), StringComparison.Ordinal);

        // The row-restricted caller: neither, though it reads the focus's row.
        Assert.Equal("focus\r\n", await ReadAsync(host, P.Query("datasets/" + name + "/sparql", focus, "text/csv")));
        Assert.DoesNotContain(ReportGraph, await ReadAsync(host, P.Query("datasets/" + name + "/sparql", "SELECT DISTINCT ?g WHERE { GRAPH ?g { ?s ?p ?o } }", "text/csv")), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(Get("datasets/" + name + "/graphs?graph=" + Uri.EscapeDataString(ReportGraph), "application/n-triples"), P.Ct)).StatusCode);

        foreach (string path in commits)
        {
            Assert.DoesNotContain("affectedReport", await ReadAsync(host, Get("datasets/" + name + path, "application/vnd.varve.delta")), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The term-lookup contract (ADR 0022, amended; #191) over the read the
    /// protocol serves a row-restricted caller: the row-scoped source over the
    /// graph scope over a pinned view, classified by the projection at its
    /// position, and below its floor, where the classification is computed on
    /// demand from scratch. Neither denies a term a quad it shows holds,
    /// whatever it has classified so far.
    /// </summary>
    [Fact]
    public async Task The_row_scoped_read_never_denies_a_term_it_has_not_classified_yet()
    {
        await using Rows rows = new();
        (_, Dataset dataset, RowScope scope) = await rows.AddAsync(
        [
            "GRAPH <http://ex/g/1> { <http://ex/a> a <http://ex/Person> }",
            "GRAPH <http://ex/g/1> { <http://ex/x> <http://ex/ownedBy> <http://ex/a> }",
            "GRAPH <http://ex/g/1> { <http://ex/x> <http://ex/label> \"shown\" }",
            "GRAPH <http://ex/g/2> { <http://ex/b> a <http://ex/Person> }",
            "GRAPH <http://ex/g/2> { <http://ex/y> <http://ex/ownedBy> <http://ex/b> }",
            "GRAPH <http://ex/other> { <http://ex/c> <http://ex/knows> <http://ex/y> }",
        ], 1);
        await CommitAsync(dataset, ["GRAPH <http://ex/g/1> { <http://ex/x> <http://ex/knows> <http://ex/a> }"]);
        using DatasetView view = dataset.Pin();

        IQuadSource AtTheProjection()
        {
            ClassificationProjection projection = new(dataset, Binding);
            dataset.CatchUpAsync(projection, P.Ct).AsTask().GetAwaiter().GetResult();
            Assert.True(projection.TryAt(view.Position, view, out IRowClassification classified));
            return RowScopedQuadSource.Wrap(GraphScopedQuadSource.Wrap(view, GraphScope.All), classified, scope);
        }

        IQuadSource BelowTheFloor()
        {
            ClassificationProjection projection = new(dataset, Binding);
            using DatasetView head = dataset.Pin();
            projection.ResetAsync(head, P.Ct).AsTask().GetAwaiter().GetResult();
            DatasetView earlier = dataset.AsOfAsync(new Position(view.Position.Value - 1), P.Ct).AsTask().GetAwaiter().GetResult();
            Assert.True(projection.TryAt(earlier.Position, earlier, out IRowClassification scratch));
            return RowScopedQuadSource.Wrap(GraphScopedQuadSource.Wrap(earlier, GraphScope.All), scratch, scope);
        }

        RdfTerm hidden = Iri("http://ex/y");
        IReadOnlyCollection<RdfTerm> current = QuadSourceContract.Holds(AtTheProjection, [hidden]);
        Assert.Contains(Iri("http://ex/x"), current);
        Assert.DoesNotContain(Iri("http://ex/b"), current);
        Assert.Contains(Iri("http://ex/x"), QuadSourceContract.Holds(BelowTheFloor, [hidden]));
    }

    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string ReportShapes = "http://ex/report-shapes";
    private const string ReportGraph = "https://w3id.org/varve/ns#validation/urn%3Areport";

    // A dataset with the classifier's shapes at position 1 and the history after it.
    private static async Task<(string Name, Dataset Dataset, RowScope Scope)> HistoryAsync(Rows rows, List<(bool Delete, List<string> Quads, int[] Picks)> history, int set)
    {
        (string name, Dataset dataset, RowScope scope) = await rows.AddAsync([], set);
        List<string> inserted = [];

        foreach ((bool delete, List<string> quads, int[] picks) in history)
        {
            if (delete && inserted.Count > 0)
            {
                string text = "DELETE DATA { " + string.Join(" ", picks.Select(i => inserted[i % inserted.Count]).Distinct()) + " }";
                await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
            }
            else
            {
                await CommitAsync(dataset, quads);
                inserted.AddRange(quads);
            }
        }

        await rows.CatchUpAsync(name);
        return (name, dataset, scope);
    }

    // V_p: the quads the caller sees at p, classified from scratch there.
    private static async Task<HashSet<string>> VisibleAsync(Dataset dataset, RowScope scope, long p)
    {
        using DatasetView at = await dataset.AsOfAsync(new Position(p), P.Ct);
        return Lines(RowScopedQuadSource.Wrap(at, new ShapeClassifier([Binding]).Classify(at, QuadDelta.Empty), scope));
    }

    // Every quad at p, the shapes graph aside.
    private static async Task<HashSet<string>> StateAsync(Dataset dataset, long p)
    {
        using DatasetView at = await dataset.AsOfAsync(new Position(p), P.Ct);
        return Lines(at);
    }

    // A feed's, a diff's or a commit's records by position, the shapes graph aside.
    private static async Task<Dictionary<long, (HashSet<string> Asserted, HashSet<string> Retracted)>> RecordsAsync(ProtocolTestHost host, string path)
    {
        HttpResponseMessage response = await host.Client.SendAsync(Get(path, "application/vnd.varve.delta"), P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, path + " answered " + (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync(P.Ct));
        Dictionary<long, (HashSet<string>, HashSet<string>)> records = [];

        await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(P.Ct), P.Ct))
        {
            HashSet<string> asserted = [];
            HashSet<string> retracted = [];

            foreach (FeedChange change in record.Changes)
            {
                if (change.Graph is { } graph && Encoding.UTF8.GetString(graph.Lexical) == Shapes)
                {
                    continue;
                }

                _ = (change.Kind == FeedChangeKind.Assert ? asserted : retracted).Add(Line(change.Subject, change.Predicate, change.Object, change.Graph));
            }

            records[record.Position.Value] = (asserted, retracted);
        }

        return records;
    }

    private static (HashSet<string> Asserted, HashSet<string> Retracted) Union(Dictionary<long, (HashSet<string> Asserted, HashSet<string> Retracted)> records)
    {
        HashSet<string> asserted = [];
        HashSet<string> retracted = [];

        foreach ((HashSet<string> a, HashSet<string> r) in records.Values)
        {
            asserted.UnionWith(a);
            retracted.UnionWith(r);
        }

        return (asserted, retracted);
    }

    // N-Triples lines as a set, whitespace normalised to the line form above.
    private static HashSet<string> Triples(string body) =>
        [.. body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    // A query answer as a sorted multiset of solutions, or of triples.
    private static List<string> Canonical(string body, string accept)
    {
        if (accept == "application/n-triples")
        {
            return [.. Triples(body).Order(StringComparer.Ordinal)];
        }

        using JsonDocument json = JsonDocument.Parse(body);

        if (json.RootElement.TryGetProperty("boolean", out JsonElement boolean))
        {
            return ["boolean=" + boolean.GetBoolean().ToString(CultureInfo.InvariantCulture)];
        }

        List<string> solutions = [];

        foreach (JsonElement binding in json.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            solutions.Add(string.Join("|", binding.EnumerateObject().Select(static v => v.Name + "=" + v.Value.GetRawText()).Order(StringComparer.Ordinal)));
        }

        solutions.Sort(StringComparer.Ordinal);
        return solutions;
    }

    /// <summary>One report projection as its report graph, as the server's adapter is.</summary>
    private sealed class ValidationReport(Varve.Shacl.Store.ValidationProjection projection) : IValidationReports, IValidationReportGraph
    {
        internal Varve.Shacl.Store.ValidationProjection Projection => projection;

        public RdfTerm Binding => projection.Binding.Binding;

        public RdfTerm Graph => projection.ReportGraph;

        public GraphScope Validates => projection.Binding.Validates;

        public IReadOnlyList<IValidationReportGraph> Of(Model.DatasetName dataset) => [this];

        public bool TryReportAt(Position position, out Varve.Shacl.ValidationOutcome outcome) => projection.TryReportAt(position, out outcome);

        public Varve.Shacl.ValidationOutcome ReportAt(Position position) =>
            projection.TryReportAt(position, out Varve.Shacl.ValidationOutcome outcome) ? outcome : throw new ValidationBehindException(Binding, position, projection.Position);
    }
}
