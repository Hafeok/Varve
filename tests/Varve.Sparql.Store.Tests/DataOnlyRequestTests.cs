// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Sparql.Store.Tests;

/// <summary>
/// A request of <c>INSERT DATA</c> and <c>DELETE DATA</c> alone expects no
/// position (ADR 0057, amended 2026-10-08; <c>sparql-update-store.md</c> §3):
/// built at one head and landing at another, it commits what it would have
/// committed had it been evaluated there, because its delta is the request's
/// own text and the sequencer normalises it against the head it meets (I2).
/// </summary>
public class DataOnlyRequestTests
{
    /// <summary>The iterations the property runs; the 7b record states it.</summary>
    internal const int Iterations = 1_000;

    private const string Prefix = "PREFIX : <http://example.org/>\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The generator: data operations over a small vocabulary, so they collide.

    private static readonly Gen<string> Node = Gen.OneOfConst(":s0", ":s1", ":s2");
    private static readonly Gen<string> Pred = Gen.OneOfConst(":p0", ":p1");
    private static readonly Gen<string> Obj = Gen.OneOfConst(":s0", ":o0", "\"1\"", "\"x\"@en");
    private static readonly Gen<string> InGraph = Gen.OneOfConst("", "", ":g0", ":g1");

    private static readonly Gen<string> Ground = Gen.Select(Node, Pred, Obj, InGraph, Triple);

    private static readonly Gen<string> Inserted = Gen.Select(Gen.OneOf(Node, Gen.Const("_:b")), Pred, Obj, InGraph, Triple);

    // A blank node label may not be shared by two operations of one request
    // (SPARQL 1.1 Update §19.6), so each operation's labels get its index.
    private static readonly Gen<string> Request = Gen.OneOf(
            Inserted.List[1, 3].Select(t => "INSERT DATA { " + string.Join(" ", t) + " }"),
            Ground.List[1, 3].Select(t => "DELETE DATA { " + string.Join(" ", t) + " }"))
        .List[1, 3].Select(ops => Prefix + string.Join(" ;\n", ops.Select((op, i) => op.Replace("_:b", "_:b" + i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))));

    private static readonly Gen<string[]> History = Request.Array[0, 3];

    [Fact]
    public async Task A_data_only_request_built_at_one_head_commits_at_a_later_one()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :a :p :o . :b :p :o }");

        CommitRequest request = await BuildAsync(dataset, "DELETE DATA { :b :p :o . :c :p :o } ; INSERT DATA { :d :p :o }");
        Assert.Null(request.ExpectedPosition);

        // Another writer lands after the pin: :c appears and :a goes.
        await dataset.CommitAsync(new CommitRequest().Assert(Iri("c"), Iri("p"), Iri("o")).Retract(Iri("a"), Iri("p"), Iri("o")), Ct);

        CommitResult result = await dataset.CommitAsync(request, Ct);
        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        Assert.Equal(new Position(3), result.Position);
        Assert.Equal([":d :p :o"], State(dataset));
    }

    [Fact]
    public async Task A_data_only_request_whose_change_the_head_already_holds_is_no_change()
    {
        await using Dataset dataset = await Open();
        CommitRequest request = await BuildAsync(dataset, "INSERT DATA { :a :p :o }");
        await Run(dataset, "INSERT DATA { :a :p :o }");

        CommitResult result = await dataset.CommitAsync(request, Ct);
        Assert.Equal(CommitOutcome.NoChange, result.Outcome);
        Assert.Equal(new Position(1), dataset.Head);
    }

    [Fact]
    public async Task A_request_that_evaluates_a_pattern_still_expects_its_pin()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :a :p :o }");

        CommitRequest request = await BuildAsync(dataset, "INSERT DATA { :b :p :o } ; DELETE WHERE { ?s :p :o }");
        Assert.Equal(new Position(1), request.ExpectedPosition);

        await Run(dataset, "INSERT DATA { :c :p :o }");
        CommitResult result = await dataset.CommitAsync(request, Ct);
        Assert.Equal(CommitOutcome.Conflict, result.Outcome);
        Assert.Equal([":a :p :o", ":c :p :o"], State(dataset));
    }

    [Fact]
    public async Task An_expected_position_given_by_the_caller_is_always_honoured()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :a :p :o }");

        CommitRequest request = await BuildAsync(dataset, "INSERT DATA { :b :p :o }", new UpdateOptions { ExpectedPosition = new Position(1) });
        Assert.Equal(new Position(1), request.ExpectedPosition);

        await Run(dataset, "INSERT DATA { :c :p :o }");
        Assert.Equal(CommitOutcome.Conflict, (await dataset.CommitAsync(request, Ct)).Outcome);
        Assert.Equal(CommitOutcome.Conflict, (await Run(dataset, "INSERT DATA { :b :p :o }", new UpdateOptions { ExpectedPosition = new Position(1) })).Outcome);
        Assert.Equal(CommitOutcome.Committed, (await Run(dataset, "INSERT DATA { :b :p :o }", new UpdateOptions { ExpectedPosition = new Position(2) })).Outcome);
    }

    /// <summary>
    /// The property of the amendment: a data-only request built against a
    /// history, then landing after a contended tail, leaves the same state and
    /// the same effective delta as the request evaluated after the tail.
    /// </summary>
    [Fact]
    public async Task A_data_only_request_yields_the_same_effective_delta_at_either_end_of_a_contended_tail()
    {
        await Gen.Select(History, Request, Request.Array[1, 3]).SampleAsync(async sample =>
        {
            (string[] history, string request, string[] tail) = sample;

            await using Dataset raced = await Open();
            await using Dataset serial = await Open();

            foreach (string earlier in history)
            {
                await Run(raced, earlier);
                await Run(serial, earlier);
            }

            // Built at the head before the tail, submitted after it.
            CommitRequest built = await BuildAsync(raced, request);
            Assert.Null(built.ExpectedPosition);

            foreach (string contended in tail)
            {
                await Run(raced, contended);
                await Run(serial, contended);
            }

            CommitResult landed = await raced.CommitAsync(built, Ct);
            CommitResult evaluated = await Run(serial, request);

            Assert.Equal(evaluated.Outcome, landed.Outcome);
            Assert.Equal(serial.Head, raced.Head);
            Assert.Equal(State(serial), State(raced));

            if (landed.Outcome == CommitOutcome.Committed)
            {
                Assert.Equal(await DeltaAsync(serial), await DeltaAsync(raced));
            }
        }, iter: Iterations);
    }

    // ---- Support.

    private static string Triple(string s, string p, string o, string g) =>
        g.Length == 0 ? s + " " + p + " " + o + " ." : "GRAPH " + g + " { " + s + " " + p + " " + o + " }";

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    private static ValueTask<Dataset> Open() =>
        Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = new FixedClock() }, Ct);

    private static ValueTask<CommitResult> Run(Dataset dataset, string update, UpdateOptions? options = null) =>
        SparqlUpdate.ExecuteAsync(dataset, Parse(update), options ?? new UpdateOptions(), Ct);

    private static Update Parse(string update) =>
        SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(update.StartsWith("PREFIX", StringComparison.Ordinal) ? update : Prefix + update));

    /// <summary>One request's execution, pinned, built and released, as <see cref="SparqlUpdate"/> does it; not submitted.</summary>
    private static async Task<CommitRequest> BuildAsync(Dataset dataset, string update, UpdateOptions? options = null)
    {
        using DatasetView view = dataset.Pin();
        RequestExecution execution = new(view.Stage(), Parse(update), options ?? new UpdateOptions(), Ct);
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
            quads.Add(Line(view, cursor.Current));
        }

        return quads;
    }

    private static async Task<SortedSet<string>> DeltaAsync(Dataset dataset)
    {
        Position head = dataset.Head;
        QuadDelta delta = await dataset.DiffAsync(new Position(head.Value - 1), head, Ct);
        using DatasetView view = dataset.Pin();
        SortedSet<string> lines = new(StringComparer.Ordinal);

        foreach (Quad quad in delta.Asserted)
        {
            lines.Add("+ " + Line(view, quad));
        }

        foreach (Quad quad in delta.Retracted)
        {
            lines.Add("- " + Line(view, quad));
        }

        return lines;
    }

    private static string Line(DatasetView view, in Quad q) =>
        Text(view, q.Subject) + " " + Text(view, q.Predicate) + " " + Text(view, q.Object) + (q.Graph.IsNone ? "" : " " + Text(view, q.Graph));

    // Blank nodes are fresh per request, so they compare as blank.
    private static string Text(DatasetView source, TermHandle handle)
    {
        Assert.True(source.TryExternalise(handle, out RdfTerm? term));
        return term.Kind switch
        {
            RdfTermKind.BlankNode => "_",
            RdfTermKind.Iri => Encoding.UTF8.GetString(term.Lexical).Replace("http://example.org/", ":", StringComparison.Ordinal),
            _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\"" + (term.Language.IsEmpty ? "" : "@" + Encoding.UTF8.GetString(term.Language)),
        };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    }
}
