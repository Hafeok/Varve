// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// ADR 0058: validators bound to the dataset, and the staging view with its
/// invariant — a staging handle reaches the dictionary or the log only
/// through the commit that maps it.
/// </summary>
public class StagingTests
{
    private static CommitRequest One(string subject) => new CommitRequest().Assert(T.Iri(subject), T.Iri("p"), T.Iri("o"));

    // ---- Validators bound to the dataset.

    [Fact]
    public async Task dataset_validators_run_on_every_data_commit_before_the_requests_own()
    {
        List<string> order = [];
        Recording first = new("dataset-1", order), second = new("dataset-2", order), own = new("request", order);
        await using Dataset dataset = await Dataset.OpenAsync(
            new MemoryStorage(), new DatasetOptions { Clock = ManualClock.Epoch(), Validators = [first, second] }, T.Ct);

        CommitRequest request = One("a");
        request.Validators.Add(own);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, T.Ct)).Outcome);
        Assert.Equal(["dataset-1", "dataset-2", "request"], order);

        // A plain commit, with no validator of its own, is still validated.
        order.Clear();
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(One("b"), T.Ct)).Outcome);
        Assert.Equal(["dataset-1", "dataset-2"], order);

        // A settings commit carries no delta and is not.
        order.Clear();
        await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current }, new CommitMetadata { Agent = T.Iri("me"), Cause = T.Literal("why") }, cancellationToken: T.Ct);
        Assert.Empty(order);

        // Every accepting validator's attachment is recorded.
        RecordingProjection recorder = new();
        await dataset.CatchUpAsync(recorder, T.Ct);
        Assert.Equal(3, recorder.Commits[0].Attachments.Length);
    }

    [Fact]
    public async Task a_dataset_validator_rejects_whatever_the_request_brings_and_leaves_no_trace()
    {
        await using Dataset dataset = await Dataset.OpenAsync(
            new MemoryStorage(), new DatasetOptions { Clock = ManualClock.Epoch(), Validators = [new Refusing()] }, T.Ct);

        CommitResult result = await dataset.CommitAsync(One("new"), T.Ct);

        Assert.Equal(CommitOutcome.Rejected, result.Outcome);
        Assert.Equal(0, dataset.Head);
        using DatasetView view = dataset.Pin();
        Assert.False(view.TryInternalise(T.Iri("new"), out _));
    }

    // ---- The staging view.

    [Fact]
    public async Task staging_gives_the_views_handle_to_a_held_term_and_a_provisional_one_otherwise()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("held"), T.Ct);
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();

        Assert.True(view.TryInternalise(T.Iri("held"), out TermHandle held));
        Assert.Equal(held, staging.Stage(T.Iri("held")));

        TermHandle fresh = staging.Stage(T.Iri("fresh"));
        Assert.Equal(fresh, staging.Stage(T.Iri("fresh")));
        Assert.NotEqual(held, fresh);
        Assert.False(view.TryInternalise(T.Iri("fresh"), out _));
        Assert.True(staging.TryInternalise(T.Iri("fresh"), out TermHandle again));
        Assert.Equal(fresh, again);
        Assert.True(staging.TryExternalise(fresh, out RdfTerm? back));
        Assert.Equal(T.Iri("fresh"), back);

        // A provisional handle matches nothing in the view.
        Assert.Empty(T.Drain(staging.Match(fresh, TermHandle.None, TermHandle.None, GraphPattern.Any)));

        TermHandle one = staging.StageBlank(), two = staging.StageBlank();
        Assert.NotEqual(one, two);
        Assert.True(staging.TryExternalise(one, out RdfTerm? blank));
        Assert.Equal(RdfTermKind.BlankNode, blank.Kind);
        Assert.Throws<ArgumentException>(() => staging.Stage(T.Blank("x")));
    }

    [Fact]
    public async Task an_overlay_of_a_staged_delta_sees_the_new_terms_and_one_commit_lands_them()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("held"), T.Ct);
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();

        TermHandle node = staging.StageBlank();
        TermHandle p = staging.Stage(T.Iri("p"));
        TermHandle triple = staging.StageTriple(staging.Stage(T.Iri("fresh")), p, staging.Stage(T.Literal("v")));
        Quad[] asserted =
        [
            new(node, p, staging.Stage(T.Iri("fresh"))),
            new(staging.Stage(T.Iri("fresh")), p, node),
            new(node, p, triple),
        ];
        Assert.True(view.TryInternalise(T.Iri("held"), out TermHandle held));
        Quad retracted = new(held, p, staging.Stage(T.Iri("o")));
        QuadOverlay overlay = new(staging, QuadDelta.Create(asserted, [retracted]));

        Assert.Equal(3, T.All(overlay).Count);
        Assert.Contains("<http://example.org/fresh> <http://example.org/p> _:staged1", T.Terms(overlay));

        CommitRequest request = new() { ExpectedPosition = staging.Position };

        foreach (Quad quad in asserted)
        {
            request.Assert(staging.ToRequestTerm(quad.Subject), staging.ToRequestTerm(quad.Predicate), staging.ToRequestTerm(quad.Object));
        }

        request.Retract(staging.ToRequestTerm(retracted.Subject), staging.ToRequestTerm(retracted.Predicate), staging.ToRequestTerm(retracted.Object));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, T.Ct)).Outcome);

        using DatasetView after = dataset.Pin();
        List<Quad> quads = T.All(after);
        Assert.Equal(3, quads.Count);

        // The staged blank node, used three times, is one node.
        HashSet<ulong> blanks = [.. quads.SelectMany(q => new[] { q.Subject, q.Object })
            .Where(h => after.TryExternalise(h, out RdfTerm? t) && t.Kind == RdfTermKind.BlankNode)
            .Select(h => h.Value)];
        Assert.Single(blanks);
        Assert.True(after.TryInternalise(RdfTerm.TripleTerm(T.Iri("fresh"), T.Iri("p"), T.Literal("v")), out _));
    }

    [Fact]
    public async Task a_staging_handle_given_as_an_existing_handle_fails_and_leaves_no_trace()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("held"), T.Ct);
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();
        TermHandle fresh = staging.Stage(T.Iri("fresh"));
        TermHandle blank = staging.StageBlank();

        foreach (TermHandle handle in new[] { fresh, blank })
        {
            CommitRequest request = new CommitRequest().Assert(RequestTerm.Existing(handle), T.Iri("p"), T.Iri("o"));
            await Assert.ThrowsAsync<ArgumentException>(async () => await dataset.CommitAsync(request, T.Ct));
        }

        Assert.Equal(1, dataset.Head);
        using DatasetView after = dataset.Pin();
        Assert.False(after.TryInternalise(T.Iri("fresh"), out _));
        Assert.Single(T.All(after));
    }

    [Fact]
    public async Task a_triple_term_around_a_blank_node_of_the_view_cannot_be_mapped()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Blank("x"), T.Iri("p"), T.Iri("o")), T.Ct);
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();
        TermHandle existing = T.All(view)[0].Subject;

        TermHandle triple = staging.StageTriple(existing, staging.Stage(T.Iri("p")), staging.Stage(T.Iri("new")));

        Assert.True(staging.TryExternalise(triple, out RdfTerm? term));
        Assert.Equal(RdfTermKind.TripleTerm, term.Kind);
        Assert.Throws<NotSupportedException>(() => staging.ToRequestTerm(triple));
        Assert.Throws<ArgumentException>(() => staging.StageTriple(new TermHandle(12345), existing, existing));
    }

    [Fact]
    public async Task a_staged_triple_over_held_parts_is_the_views_own()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        RdfTerm tripleTerm = RdfTerm.TripleTerm(T.Iri("s"), T.Iri("p"), T.Iri("o"));
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("a"), T.Iri("says"), tripleTerm), T.Ct);
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();

        Assert.True(view.TryInternalise(tripleTerm, out TermHandle held));
        Assert.Equal(held, staging.StageTriple(staging.Stage(T.Iri("s")), staging.Stage(T.Iri("p")), staging.Stage(T.Iri("o"))));
        Assert.Equal(held, staging.Stage(tripleTerm));
        Assert.True(staging.ToRequestTerm(held).IsExisting);
    }

    private sealed class Recording(string name, List<string> order) : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            order.Add(name);
            return ValidationVerdict.Accept(T.Literal(name));
        }
    }

    private sealed class Refusing : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta) =>
            ValidationVerdict.Reject([T.Literal("no")]);
    }
}
