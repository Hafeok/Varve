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
using Varve.Turtle;
using Xunit;

namespace Varve.Sparql.Store.Tests;

/// <summary>
/// What the W3C suites do not reach (<c>sparql-update-store.md</c> §7): one
/// commit per request, the overlay between operations, conflicts, failures
/// leaving no trace, <c>LOAD</c>, validators, graph management, blank nodes.
/// </summary>
public class UpdateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Prefix = "PREFIX : <http://example.org/>\n";

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    private static ValueTask<Dataset> Open(params ICommitValidator[] validators) =>
        Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = new FixedClock(), Validators = validators }, Ct);

    private static ValueTask<CommitResult> Run(Dataset dataset, string update, UpdateOptions? options = null) =>
        SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(Prefix + update)), options ?? new UpdateOptions(), Ct);

    private static SortedSet<string> State(Dataset dataset)
    {
        using DatasetView view = dataset.Pin();
        SortedSet<string> quads = new(StringComparer.Ordinal);
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad q = cursor.Current;
            quads.Add(Text(view, q.Subject) + " " + Text(view, q.Predicate) + " " + Text(view, q.Object) + (q.Graph.IsNone ? "" : " " + Text(view, q.Graph)));
        }

        return quads;
    }

    private static string Text(DatasetView source, TermHandle handle)
    {
        Assert.True(source.TryExternalise(handle, out RdfTerm? term));
        return term.Kind switch
        {
            RdfTermKind.BlankNode => "_",
            RdfTermKind.Iri => Encoding.UTF8.GetString(term.Lexical).Replace("http://example.org/", ":", StringComparison.Ordinal),
            _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\"",
        };
    }

    [Fact]
    public async Task A_request_of_several_operations_is_one_commit_and_each_sees_the_ones_before()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :old :p :o }");

        CommitResult result = await Run(dataset, """
            INSERT DATA { :new :p "1" } ;
            DELETE WHERE { :new :p ?o } ;
            INSERT { ?s :q ?s } WHERE { ?s :p :o } ;
            INSERT DATA { :new :r :x }
            """);

        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        Assert.Equal(2, dataset.Head);
        Assert.Equal([":new :r :x", ":old :p :o", ":old :q :old"], State(dataset));
    }

    [Fact]
    public async Task A_request_with_no_net_effect_makes_no_commit()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :a :p :o }");

        CommitResult result = await Run(dataset, """
            DELETE DATA { :absent :p :o } ;
            INSERT DATA { :a :p :o } ;
            INSERT DATA { :b :p :o } ;
            DELETE DATA { :b :p :o } ;
            CLEAR GRAPH :nothing
            """);

        Assert.Equal(CommitOutcome.NoChange, result.Outcome);
        Assert.Equal(1, dataset.Head);
    }

    [Fact]
    public async Task A_conflict_is_returned_and_retried_only_when_the_caller_says_so()
    {
        await using Dataset dataset = await Open();
        Interfering source = new(dataset);
        UpdateOptions once = new() { LoadSource = source };

        CommitResult conflict = await Run(dataset, "LOAD <http://example.org/doc> ; INSERT DATA { :mine :p :o }", once);
        Assert.Equal(CommitOutcome.Conflict, conflict.Outcome);
        Assert.Equal([":theirs :p \"1\""], State(dataset));

        CommitResult retried = await Run(dataset, "LOAD <http://example.org/doc> ; INSERT DATA { :mine :p :o }", new UpdateOptions { LoadSource = source, ConflictRetries = 1 });
        Assert.Equal(CommitOutcome.Committed, retried.Outcome);
        Assert.Contains(":mine :p :o", State(dataset));
        Assert.Contains(":loaded :p :o", State(dataset));
    }

    [Fact]
    public async Task A_failing_operation_fails_the_request_and_leaves_no_trace()
    {
        await using Dataset dataset = await Open();

        SparqlUpdateException error = await Assert.ThrowsAsync<SparqlUpdateException>(async () =>
            await Run(dataset, "INSERT DATA { :fresh :p :o } ; LOAD <http://example.org/nowhere> ; INSERT DATA { :later :p :o }"));

        Assert.Equal(1, error.OperationIndex);
        Assert.Equal("LOAD", error.OperationKind);
        Assert.Equal(0, dataset.Head);
        using DatasetView view = dataset.Pin();
        Assert.False(view.TryInternalise(Iri("fresh"), out _));
    }

    [Fact]
    public async Task Load_silent_is_an_operation_with_no_effect_and_load_reads_through_the_source()
    {
        await using Dataset dataset = await Open();
        UpdateOptions options = new() { LoadSource = new Documents() };

        Assert.Equal(CommitOutcome.NoChange, (await Run(dataset, "LOAD SILENT <http://example.org/nowhere>", options)).Outcome);
        Assert.Equal(CommitOutcome.NoChange, (await Run(dataset, "LOAD SILENT <http://example.org/broken>", options)).Outcome);
        await Assert.ThrowsAsync<SparqlUpdateException>(async () => await Run(dataset, "LOAD <http://example.org/broken>", options));

        Assert.Equal(CommitOutcome.Committed, (await Run(dataset, "LOAD <http://example.org/doc> ; LOAD <http://example.org/doc> INTO GRAPH :g", options)).Outcome);

        // Each load's blank node is its own.
        Assert.Equal(["_ :p :o", "_ :p :o :g"], State(dataset));
        Assert.Equal(1, dataset.Head);
    }

    [Fact]
    public async Task A_dataset_validator_gates_the_update()
    {
        await using Dataset dataset = await Open(new NoLiterals());

        Assert.Equal(CommitOutcome.Committed, (await Run(dataset, "INSERT DATA { :a :p :o }")).Outcome);
        Assert.Equal(CommitOutcome.Rejected, (await Run(dataset, "INSERT DATA { :a :p \"x\" }")).Outcome);
        Assert.Equal(1, dataset.Head);
    }

    [Fact]
    public async Task Create_fails_on_a_graph_that_holds_a_quad_and_is_otherwise_nothing()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { GRAPH :g { :a :p :o } }");

        Assert.Equal(CommitOutcome.NoChange, (await Run(dataset, "CREATE GRAPH :empty")).Outcome);
        Assert.Equal(CommitOutcome.NoChange, (await Run(dataset, "CREATE SILENT GRAPH :g")).Outcome);
        SparqlUpdateException error = await Assert.ThrowsAsync<SparqlUpdateException>(async () => await Run(dataset, "CREATE GRAPH :g"));
        Assert.Equal("CREATE", error.OperationKind);

        // A graph created earlier in the same request by a quad exists for a later CREATE.
        await Assert.ThrowsAsync<SparqlUpdateException>(async () => await Run(dataset, "INSERT DATA { GRAPH :h { :a :p :o } } ; CREATE GRAPH :h"));
    }

    [Fact]
    public async Task Add_copy_and_move_are_quad_operations()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :d :p :o . GRAPH :g { :g :p :o } GRAPH :h { :h :p :o } }");

        await Run(dataset, "ADD :g TO :h");
        Assert.Equal([":d :p :o", ":g :p :o :g", ":g :p :o :h", ":h :p :o :h"], State(dataset));

        await Run(dataset, "COPY DEFAULT TO :h");
        Assert.Equal([":d :p :o", ":d :p :o :h", ":g :p :o :g"], State(dataset));

        await Run(dataset, "MOVE :g TO DEFAULT");
        Assert.Equal([":d :p :o :h", ":g :p :o"], State(dataset));

        Assert.Equal(CommitOutcome.NoChange, (await Run(dataset, "MOVE :h TO :h ; COPY :missing TO :missing")).Outcome);

        // A source that does not exist is an empty graph: COPY empties the target.
        await Run(dataset, "COPY :missing TO :h");
        Assert.Equal([":g :p :o"], State(dataset));
    }

    [Fact]
    public async Task With_names_the_templates_graph_and_the_wheres_default_unless_using_is_given()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :d :p :o . GRAPH :g { :g :p :o } }");

        await Run(dataset, "WITH :g INSERT { ?s :q :o } WHERE { ?s :p :o }");
        Assert.Contains(":g :q :o :g", State(dataset));
        Assert.DoesNotContain(":d :q :o :g", State(dataset));

        await Run(dataset, "WITH :g INSERT { ?s :r :o } USING <http://example.org/nothing> WHERE { ?s :p :o }");
        Assert.DoesNotContain(State(dataset), q => q.Contains(":r", StringComparison.Ordinal));

        await Run(dataset, "WITH :g DELETE { ?s ?p ?o } WHERE { ?s ?p ?o }");
        Assert.Equal([":d :p :o"], State(dataset));

        // WITH a graph that holds nothing: the dataset has an empty default graph, and one solution.
        await Run(dataset, "WITH :new INSERT { :n :p :o } WHERE {}");
        Assert.Contains(":n :p :o :new", State(dataset));
    }

    [Fact]
    public async Task Blank_nodes_are_fresh_per_request_and_per_solution()
    {
        await using Dataset dataset = await Open();

        await Run(dataset, "INSERT DATA { _:b :p _:b }");
        await Run(dataset, "INSERT DATA { _:b :p :o }");
        using (DatasetView view = dataset.Pin())
        {
            List<Quad> quads = [.. Drain(view)];
            Assert.Equal(2, quads.Count);
            Quad loop = quads.Single(q => view.TermComparer.Equals(q.Subject, q.Object));
            Quad other = quads.Single(q => !view.TermComparer.Equals(q.Subject, q.Object));
            Assert.NotEqual(loop.Subject, other.Subject);
        }

        await Run(dataset, "INSERT { _:n :from ?s } WHERE { ?s :p ?o }");
        using (DatasetView view = dataset.Pin())
        {
            Assert.True(view.TryInternalise(Iri("from"), out TermHandle from));
            HashSet<ulong> subjects = [.. Drain(view).Where(q => q.Predicate == from).Select(q => q.Subject.Value)];
            Assert.Equal(2, subjects.Count);
        }
    }

    [Fact]
    public async Task A_where_that_binds_an_existing_blank_node_reaches_that_node()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { _:x :name \"x\" }");

        await Run(dataset, "INSERT { ?n :seen true } WHERE { ?n :name \"x\" }");
        await Run(dataset, "DELETE { ?n :name ?v } WHERE { ?n :name ?v }");

        Assert.Equal(["_ :seen \"true\""], State(dataset));
    }

    [Fact]
    public async Task Illegal_constructs_in_a_template_are_skipped()
    {
        await using Dataset dataset = await Open();
        await Run(dataset, "INSERT DATA { :a :p \"lit\" }");

        CommitResult result = await Run(dataset, "INSERT { ?o :q :x . :x ?o :y . :x :q ?unbound } WHERE { :a :p ?o }");

        Assert.Equal(CommitOutcome.NoChange, result.Outcome);
    }

    private static List<Quad> Drain(DatasetView source)
    {
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        List<Quad> quads = [];

        while (cursor.MoveNext())
        {
            quads.Add(cursor.Current);
        }

        return quads;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Commits on the first load, between the pin and the commit.</summary>
    private sealed class Interfering(Dataset dataset) : ILoadSource
    {
        private bool _interfered;

        public async ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken)
        {
            if (!_interfered)
            {
                _interfered = true;
                await dataset.CommitAsync(new CommitRequest().Assert(Iri("theirs"), Iri("p"), RdfTerm.Literal("1"u8)), cancellationToken);
            }

            return LoadedDocument.Of("<http://example.org/loaded> <http://example.org/p> <http://example.org/o> .\n"u8.ToArray(), RdfSyntax.NTriples, default);
        }
    }

    private sealed class Documents : ILoadSource
    {
        public ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Encoding.UTF8.GetString(iri.Lexical) switch
            {
                "http://example.org/doc" => LoadedDocument.Of("@prefix : <http://example.org/> . _:x :p :o ."u8.ToArray(), RdfSyntax.Turtle, "http://example.org/doc"u8.ToArray()),
                "http://example.org/broken" => LoadedDocument.Of("this is not turtle"u8.ToArray(), RdfSyntax.Turtle, default),
                _ => LoadedDocument.Failed("not found"),
            });
    }

    private sealed class NoLiterals : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            foreach (Quad quad in delta.Asserted)
            {
                if (proposed.TryExternalise(quad.Object, out RdfTerm? o) && o.Kind == RdfTermKind.Literal)
                {
                    return ValidationVerdict.Reject([RdfTerm.Literal("no literals"u8)]);
                }
            }

            return ValidationVerdict.Accept();
        }
    }
}
