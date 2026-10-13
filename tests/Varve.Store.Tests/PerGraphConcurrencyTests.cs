// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Per-graph optimistic concurrency (ADR 0125): serialisability per scope over
/// generated concurrent histories, and the graph-version index rebuilt from
/// the log equal to the one maintained commit by commit (I8).
/// </summary>
public sealed class PerGraphConcurrencyTests
{
    private const int Graphs = 6;

    private static RdfTerm Graph(int g) => T.Iri("g" + g);

    private static RequestTerm GraphTerm(int g) => g == 0 ? RequestTerm.None : Graph(g);

    private static readonly RdfTerm Counter = T.Iri("counter");

    private static readonly RdfTerm Value = T.Iri("value");

    /// <summary>The counter each graph holds, read from a source: 0 when absent.</summary>
    private static Dictionary<int, int> Counters(IQuadSource source)
    {
        Dictionary<int, int> counters = [];

        foreach (string quad in T.Terms(source))
        {
            string[] parts = quad.Split(' ');

            if (parts[0] != T.Render(Counter) || parts[1] != T.Render(Value))
            {
                continue;
            }

            int g = parts.Length == 3 ? 0 : Enumerable.Range(1, Graphs - 1).Single(n => parts[3] == T.Render(Graph(n)));
            counters[g] = int.Parse(parts[2][1..parts[2].IndexOf('"', 1)], CultureInfo.InvariantCulture);
        }

        return counters;
    }

    private static RdfTerm Number(int n) => T.Integer(n.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A writer that read at a position: increments the counter of every graph
    /// of its scope, from the values it read, expecting that position with that
    /// scope — the read-modify-write a scope exists to protect.
    /// </summary>
    private static CommitRequest Increment(Dictionary<int, int> read, int[] scope, long position, bool scoped)
    {
        CommitRequest request = new()
        {
            ExpectedPosition = new Position(position),
            ExpectedGraphs = scoped ? [.. scope.Select(GraphTerm)] : null,
        };

        foreach (int g in scope)
        {
            int was = read.GetValueOrDefault(g);

            if (was > 0)
            {
                request = g == 0 ? request.Retract(Counter, Value, Number(was)) : request.Retract(Counter, Value, Number(was), Graph(g));
            }

            request = g == 0 ? request.Assert(Counter, Value, Number(was + 1)) : request.Assert(Counter, Value, Number(was + 1), Graph(g));
        }

        return request;
    }

    /// <summary>
    /// The property, over generated histories: in each round several writers
    /// read at the same position, each with a scope of one to three graphs —
    /// disjoint or overlapping — and commit in an order the round draws,
    /// sometimes queued as one batch (ADR 0124), with settings commits between.
    /// A writer conflicts exactly when an earlier commit of the round, or a
    /// settings commit, touched a graph of its scope; with no scope given, it
    /// conflicts whenever anything committed after its read. No increment is
    /// lost: every counter ends at the number of commits that incremented it.
    /// </summary>
    [Theory]
    [InlineData(9201)]
    [InlineData(9202)]
    [InlineData(9203)]
    public async Task scopes_serialise_per_graph_disjoint_never_conflict_overlapping_exactly_when_touched(int seed)
    {
        int conflicts = 0, disjointCommits = 0, rounds = 0;

        for (int history = 0; history < 25; history++)
        {
            Random random = new((seed * 100) + history);
            await using Dataset dataset = await T.Open(new MemoryStorage());
            int[] increments = new int[Graphs];

            for (int round = 0; round < 12; round++)
            {
                long read = dataset.Head.Value;
                Dictionary<int, int> values;

                using (DatasetView view = dataset.Pin())
                {
                    values = Counters(view);
                }

                int writers = random.Next(2, 6);
                List<(int[] Scope, bool Scoped)> plan = [];

                for (int w = 0; w < writers; w++)
                {
                    int[] scope = [.. Enumerable.Range(0, Graphs).OrderBy(_ => random.Next()).Take(random.Next(1, 4)).Order()];
                    plan.Add((scope, random.Next(6) > 0));
                }

                bool settingsFirst = random.Next(5) == 0;
                bool batched = random.Next(2) == 0;
                List<Task<CommitResult>> answers = [];

                IDisposable? held = batched ? await dataset.HoldSequencerForTestsAsync(T.Ct) : null;

                if (settingsFirst)
                {
                    answers.Add(dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current }, new CommitMetadata { Agent = T.Iri("a"), Cause = T.Literal("s" + round) }, null, T.Ct).AsTask());
                }

                foreach ((int[] scope, bool scoped) in plan)
                {
                    Task<CommitResult> answer = dataset.CommitAsync(Increment(values, scope, read, scoped), T.Ct).AsTask();
                    answers.Add(answer);

                    if (!batched)
                    {
                        await answer;
                    }
                }

                held?.Dispose();
                CommitResult[] results = await Task.WhenAll(answers);

                // The reference: in submission order, a writer conflicts when a
                // commit before it in the round touched its scope (or anything
                // committed, without a scope).
                HashSet<int> touched = [];
                bool anything = false;
                int at = 0;

                if (settingsFirst)
                {
                    Assert.Equal(CommitOutcome.Committed, results[at++].Outcome);
                    touched.UnionWith(Enumerable.Range(0, Graphs));
                    anything = true;
                }

                foreach ((int[] scope, bool scoped) in plan)
                {
                    CommitResult result = results[at++];
                    bool expectConflict = scoped ? scope.Any(touched.Contains) : anything;
                    string where = "seed " + seed + ", history " + history + ", round " + round + ", scope " + string.Join(",", scope) + (scoped ? "" : " (no scope)") + (batched ? ", batched" : "");
                    Assert.True(expectConflict == (result.Outcome == CommitOutcome.Conflict), where + ": expected " + (expectConflict ? "Conflict" : "Committed") + ", was " + result.Outcome);

                    if (result.Outcome == CommitOutcome.Committed)
                    {
                        touched.UnionWith(scope);
                        anything = true;

                        foreach (int g in scope)
                        {
                            increments[g]++;
                        }

                        disjointCommits += plan.Count > 1 ? 1 : 0;
                    }
                    else
                    {
                        conflicts++;
                    }
                }

                // No lost update: each counter is the number of increments.
                using (DatasetView view = dataset.Pin())
                {
                    Dictionary<int, int> now = Counters(view);

                    for (int g = 0; g < Graphs; g++)
                    {
                        Assert.True(now.GetValueOrDefault(g) == increments[g], "seed " + seed + ", history " + history + ", round " + round + ": graph " + g + " holds " + now.GetValueOrDefault(g) + " after " + increments[g] + " increments");
                    }
                }

                rounds++;
            }
        }

        Assert.True(conflicts > 0 && disjointCommits > 0, "the generator exercised one side only: " + conflicts + " conflicts, " + disjointCommits + " concurrent commits");
        TestContext.Current.TestOutputHelper?.WriteLine("seed " + seed + ": " + rounds + " rounds, " + conflicts + " conflicts, " + disjointCommits + " commits beside another writer");
    }

    /// <summary>
    /// I8 for the graph-version index: over generated histories of data,
    /// settings and bulk commits, checkpoints and maintenance, the version of
    /// every graph at every position — at the head and as of each position —
    /// equals the reference fold of the log, maintained commit by commit; and
    /// equals it again after reopening, after deleting the persisted index, and
    /// after deleting every derived file, when it is rebuilt from the log.
    /// </summary>
    [Theory]
    [InlineData(9301)]
    [InlineData(9302)]
    public async Task the_graph_version_index_rebuilt_from_the_log_equals_the_one_maintained(int seed)
    {
        int checkedVersions = 0;

        for (int history = 0; history < 12; history++)
        {
            Random random = new((seed * 100) + history);
            await using TemporaryDirectory directory = new(flushes: false);
            List<long[]> reference = [new long[Graphs]];
            DatasetOptions options = T.Options(maxRecordBytes: 256, segmentBytes: 4096);

            FileStorage storage = await directory.OpenAsync();
            Dataset dataset = await T.OpenOrCreate(storage, options);

            for (int step = 0; step < 30; step++)
            {
                long[] versions = (long[])reference[^1].Clone();
                int kind = random.Next(12);
                CommitResult result;

                if (kind == 0)
                {
                    result = await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = random.Next(2) == 0 ? AccessScope.Current : AccessScope.AllHistory }, new CommitMetadata { Agent = T.Iri("a"), Cause = T.Literal("s" + step) }, null, T.Ct);
                    Array.Fill(versions, result.Position.Value);
                }
                else
                {
                    CommitRequest request = new();
                    List<(bool Assert, int Graph, RdfTerm Object)> operations = [];

                    for (int op = random.Next(1, 4); op > 0; op--)
                    {
                        int g = random.Next(Graphs);
                        RdfTerm o = Number(random.Next(3));
                        bool assert = random.Next(3) > 0;
                        operations.Add((assert, g, o));
                        request = (assert, g) switch
                        {
                            (true, 0) => request.Assert(T.Iri("s"), T.Iri("p"), o),
                            (true, _) => request.Assert(T.Iri("s"), T.Iri("p"), o, Graph(g)),
                            (false, 0) => request.Retract(T.Iri("s"), T.Iri("p"), o),
                            (false, _) => request.Retract(T.Iri("s"), T.Iri("p"), o, Graph(g)),
                        };
                    }

                    // What the commit will touch: the graphs whose quads change.
                    SortedSet<string> before;

                    using (DatasetView view = dataset.Pin())
                    {
                        before = T.Terms(view);
                    }

                    if (kind == 1)
                    {
                        // A bulk load: its graphs come from its delta run (ADR 0125).
                        await using BulkLoad load = await dataset.BeginBulkLoadAsync(new BulkLoadOptions { SortRecords = 3, TermBytes = 64, Workers = 1 }, T.Ct);

                        foreach ((bool assert, int g, RdfTerm o) in operations)
                        {
                            if (assert)
                            {
                                load.Assert(T.Iri("s"), T.Iri("p"), o, g == 0 ? null : Graph(g));
                            }
                            else
                            {
                                load.Retract(T.Iri("s"), T.Iri("p"), o, g == 0 ? null : Graph(g));
                            }
                        }

                        result = await load.CommitAsync(new CommitMetadata(), T.Ct);
                    }
                    else
                    {
                        result = await dataset.CommitAsync(request, T.Ct);
                    }

                    if (result.Outcome == CommitOutcome.Committed)
                    {
                        SortedSet<string> after;

                        using (DatasetView view = dataset.Pin())
                        {
                            after = T.Terms(view);
                        }

                        foreach (string quad in after.Except(before).Concat(before.Except(after)))
                        {
                            int g = Enumerable.Range(1, Graphs - 1).FirstOrDefault(n => quad.EndsWith(" " + T.Render(Graph(n)), StringComparison.Ordinal));
                            versions[g] = result.Position.Value;
                        }
                    }
                }

                if (result.Outcome == CommitOutcome.Committed)
                {
                    reference.Add(versions);
                }

                if (random.Next(5) == 0 && dataset.Head.Value > 0)
                {
                    await dataset.CheckpointAsync(new Position(1 + random.Next((int)dataset.Head.Value)), T.Ct);
                }

                if (random.Next(4) == 0)
                {
                    await dataset.MaintainAsync(T.Ct);
                }
            }

            async Task CheckAsync(Dataset opened, string where)
            {
                long head = opened.Head.Value;
                Assert.Equal(reference.Count - 1, head);

                for (int g = 0; g < Graphs; g++)
                {
                    Assert.True(reference[(int)head][g] == opened.GraphVersion(GraphTerm(g)).Value, where + ": graph " + g + " at the head");

                    for (long p = 0; p <= head; p++)
                    {
                        long version = (await opened.GraphVersionAsync(GraphTerm(g), new Position(p), T.Ct)).Value;
                        Assert.True(reference[(int)p][g] == version, where + ": graph " + g + " as of " + p + " was " + version + ", the fold says " + reference[(int)p][g]);
                        checkedVersions++;
                    }
                }
            }

            string at = "seed " + seed + ", history " + history;
            await CheckAsync(dataset, at + ", maintained");
            await dataset.MaintainAsync(T.Ct);
            await dataset.DisposeAsync();

            foreach (string removal in new[] { "none", "index", "derived" })
            {
                if (removal == "index")
                {
                    await storage.Derived.DeleteAsync(GraphVersions.HeadName, T.Ct);
                }
                else if (removal == "derived")
                {
                    foreach (BlobName name in await storage.Derived.ListAsync(T.Ct))
                    {
                        await storage.Derived.DeleteAsync(name, T.Ct);
                    }
                }

                await using Dataset reopened = await Dataset.OpenAsync(storage, options, T.Ct);
                await CheckAsync(reopened, at + ", reopened with " + removal + " removed");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("seed " + seed + ": " + checkedVersions + " graph versions checked");
    }
}
