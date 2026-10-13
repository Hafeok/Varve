// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Sparql.Store.Tests;

/// <summary>
/// The scope of a request is what it asked for (ADR 0057, amended 2026-10-13):
/// over generated concurrent histories of pattern requests on disjoint and
/// overlapping graph sets — reads of graphs that do not exist yet included —
/// a request built at one head conflicts at a later one exactly when an
/// intervening commit touched a graph it read or writes, and a request that
/// commits leaves the state it would have left evaluated at that later head.
/// </summary>
public sealed class ScopeTests
{
    private const string Prefix = "PREFIX : <http://example.org/>\n";

    private const int Graphs = 5;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A request, the graphs its patterns read (null is every graph), and the graphs its templates may write.</summary>
    private sealed record Request(string Text, int[]? Read, int[] Write);

    private static string G(int g) => g == 0 ? "" : ":g" + g;

    private static string In(int g, string pattern) => g == 0 ? pattern : "GRAPH " + G(g) + " { " + pattern + " }";

    private static Request Generate(Random random)
    {
        int a = random.Next(Graphs), b = random.Next(Graphs), c = random.Next(Graphs);

        return random.Next(6) switch
        {
            // Read-modify-write of one graph's counter.
            0 => new Request(
                "DELETE { " + In(a, ":c :v ?x") + " } INSERT { " + In(a, ":c :v ?y") + " } WHERE { " + In(a, ":c :v ?x") + " BIND (?x + 1 AS ?y) }",
                [a], [a]),

            // Copy a value from one graph into another.
            1 => new Request(
                "INSERT { " + In(a, ":c :w ?x") + " } WHERE { " + In(b, ":c :v ?x") + " }",
                [b], [a]),

            // A read of a graph that may not exist: its absence is what it read.
            2 => new Request(
                "INSERT { " + In(a, ":flag :p " + c) + " } WHERE { FILTER NOT EXISTS { " + In(b, ":c :v ?x") + " } }",
                [b], [a]),

            // Every named graph: the scope is every graph.
            3 => new Request(
                "INSERT { " + In(a, ":seen :p ?g") + " } WHERE { GRAPH ?g { :c :v ?x } }",
                null, [a]),

            // GRAPH ?g confined by USING NAMED: the declared graphs, and the written one.
            4 when b != 0 && c != 0 => new Request(
                "INSERT { " + In(a, ":u :p ?x") + " } USING NAMED " + G(b) + " USING NAMED " + G(c) + " WHERE { GRAPH ?g { :c :v ?x } }",
                [b, c], [a]),

            // Clear a graph: reads it to retract what it holds.
            _ => new Request("CLEAR SILENT " + (a == 0 ? "DEFAULT" : "GRAPH " + G(a)), [a], [a]),
        };
    }

    /// <summary>An intervening commit: data into one or two graphs, which may create a graph a request read as absent.</summary>
    private static (string Text, int[] Graphs) Intervening(Random random)
    {
        int a = random.Next(Graphs), b = random.Next(Graphs);
        string text = random.Next(3) switch
        {
            0 => "INSERT DATA { " + In(a, ":c :v " + random.Next(1, 4)) + " }",
            1 => "DELETE DATA { " + In(a, ":c :v " + random.Next(1, 4)) + " } ; INSERT DATA { " + In(b, ":c :v " + random.Next(1, 4)) + " }",
            _ => "INSERT DATA { " + In(a, ":other :p " + random.Next(10)) + " }",
        };
        return (text, [a, b]);
    }

    [Theory]
    [InlineData(9401)]
    [InlineData(9402)]
    [InlineData(9403)]
    public async Task a_request_conflicts_exactly_when_an_intervening_commit_touched_its_scope(int seed)
    {
        int conflicts = 0, committed = 0, absentReads = 0;

        for (int iteration = 0; iteration < 300; iteration++)
        {
            Random random = new((seed * 1000) + iteration);
            List<string> setup = [.. Enumerable.Range(0, random.Next(0, 4)).Select(_ => Intervening(random).Text)];
            Request request = Generate(random);
            List<(string Text, int[] Graphs)> between = [.. Enumerable.Range(0, random.Next(0, 3)).Select(_ => Intervening(random))];

            // A: built at the head after the setup, submitted after the intervening commits.
            await using Dataset stale = await Open();
            await using Dataset fresh = await Open();

            foreach (string text in setup)
            {
                await Run(stale, text);
                await Run(fresh, text);
            }

            CommitRequest built = await BuildAsync(stale, request.Text);
            long before = stale.Head.Value;
            Dictionary<int, long> versions = Enumerable.Range(0, Graphs).ToDictionary(g => g, g => stale.GraphVersion(GraphTerm(g)).Value);

            foreach ((string text, _) in between)
            {
                await Run(stale, text);
                await Run(fresh, text);
            }

            // What the intervening commits touched, by the graphs' versions.
            HashSet<int> touched = [.. Enumerable.Range(0, Graphs).Where(g => stale.GraphVersion(GraphTerm(g)).Value != versions[g])];
            bool anything = stale.Head.Value != before;
            // A commit that touched what the request read must conflict; one
            // that touched neither what it read nor what it may write must
            // not; between, the delta decides, and serialisability is checked.
            bool must = request.Read is null ? anything : request.Read.Any(touched.Contains);
            bool may = must || request.Write.Any(touched.Contains);
            absentReads += request.Text.Contains("NOT EXISTS", StringComparison.Ordinal) && touched.Count > 0 ? 1 : 0;

            CommitResult result = await stale.CommitAsync(built, Ct);
            string where = "seed " + seed + ", iteration " + iteration + ": " + request.Text + " after " + string.Join(" | ", between.Select(b => b.Text)) + " (touched " + string.Join(",", touched) + ")";
            bool conflicted = result.Outcome == CommitOutcome.Conflict;
            Assert.True(!must || conflicted, where + ": a commit touched what it read, and it was " + result.Outcome);
            Assert.True(may || !conflicted, where + ": nothing it read or writes was touched, and it conflicted");

            if (result.Outcome == CommitOutcome.Conflict)
            {
                conflicts++;
                continue;
            }

            // B: the same request evaluated at that later head. Serialisable:
            // the stale one left exactly the state the fresh one leaves.
            await Run(fresh, request.Text);
            Assert.True(State(stale).SetEquals(State(fresh)), where + ": committed, and left another state than evaluating it at the head would");
            committed++;
        }

        Assert.True(conflicts > 0 && committed > 0 && absentReads > 0, "seed " + seed + ": " + conflicts + " conflicts, " + committed + " commits, " + absentReads + " absent-graph reads with a concurrent commit");
        TestContext.Current.TestOutputHelper?.WriteLine("seed " + seed + ": 300 histories, " + conflicts + " conflicts, " + committed + " serialisable commits");
    }

    /// <summary>The ADR's own case: FILTER NOT EXISTS over a graph that does not exist yet, and a commit that creates it.</summary>
    [Fact]
    public async Task a_read_of_an_absent_graph_conflicts_with_a_commit_that_creates_it()
    {
        await using Dataset dataset = await Open();
        CommitRequest built = await BuildAsync(dataset, "INSERT { GRAPH :g1 { :flag :p 1 } } WHERE { FILTER NOT EXISTS { GRAPH :h { ?s ?p ?o } } }");
        Assert.Contains(built.ExpectedGraphs!, g => g.Term is { } term && term.Equals(Iri("h")));

        await Run(dataset, "INSERT DATA { GRAPH :h { :a :b :c } }");
        Assert.Equal(CommitOutcome.Conflict, (await dataset.CommitAsync(built, Ct)).Outcome);
    }

    /// <summary>Writers of disjoint graphs, each a read-modify-write, built at one head: none conflicts.</summary>
    [Fact]
    public async Task writers_of_disjoint_graphs_do_not_conflict()
    {
        await using Dataset dataset = await Open();
        List<CommitRequest> built = [];

        for (int g = 1; g <= 4; g++)
        {
            built.Add(await BuildAsync(dataset, "DELETE { GRAPH :g" + g + " { :c :v ?x } } INSERT { GRAPH :g" + g + " { :c :v 1 } } WHERE { OPTIONAL { GRAPH :g" + g + " { :c :v ?x } } }"));
        }

        foreach (CommitRequest request in built)
        {
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, Ct)).Outcome);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    }

    private static RequestTerm GraphTerm(int g) => g == 0 ? RequestTerm.None : Iri("g" + g);

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    private static ValueTask<Dataset> Open() =>
        Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = new FixedClock() }, Ct);

    private static ValueTask<CommitResult> Run(Dataset dataset, string update) =>
        SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(Prefix + update)), new UpdateOptions(), Ct);

    private static async Task<CommitRequest> BuildAsync(Dataset dataset, string update)
    {
        using DatasetView view = dataset.Pin();
        RequestExecution execution = new(view.Stage(), SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(Prefix + update)), new UpdateOptions(), Ct);
        await execution.RunAsync();
        return execution.ToCommitRequest();
    }

    private static SortedSet<string> State(Dataset dataset)
    {
        using DatasetView view = dataset.Pin();
        SortedSet<string> quads = new(StringComparer.Ordinal);
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            quads.Add(Name(view, quad.Subject) + " " + Name(view, quad.Predicate) + " " + Name(view, quad.Object) + " " + (quad.Graph.IsNone ? "" : Name(view, quad.Graph)));
        }

        return quads;
    }

    private static string Name(DatasetView view, TermHandle handle) =>
        view.TryExternalise(handle, out RdfTerm? term) ? term.ToString() ?? "" : "?";
}
