// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>Worked examples of the store's behaviour, one rule each, beside the properties.</summary>
public class BehaviourTests
{
    private static CommitRequest One(string subject, RdfTerm? graph = null) =>
        graph is null
            ? new CommitRequest().Assert(T.Iri(subject), T.Iri("p"), T.Iri("o"))
            : new CommitRequest().Assert(T.Iri(subject), T.Iri("p"), T.Iri("o"), graph);

    // --- T1 ---------------------------------------------------------------------

    [Fact]
    public async Task a_request_that_changes_nothing_is_no_change_and_leaves_no_trace()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("s"), T.Ct);

        CommitResult result = await dataset.CommitAsync(new CommitRequest()
            .Assert(T.Iri("s"), T.Iri("p"), T.Iri("o"))              // already present
            .Retract(T.Iri("absent"), T.Iri("p"), T.Iri("o"))        // not present
            .Assert(T.Iri("new"), T.Iri("p"), T.Iri("fresh"))        // asserted, then
            .Retract(T.Iri("new"), T.Iri("p"), T.Iri("fresh")), T.Ct); // retracted

        Assert.Equal(CommitOutcome.NoChange, result.Outcome);
        Assert.Equal(1, result.Position);
        Assert.Equal(1, dataset.Head);

        using DatasetView view = dataset.Pin();
        Assert.False(view.TryInternalise(T.Iri("new"), out _));
        Assert.False(view.TryInternalise(T.Iri("fresh"), out _));
    }

    [Fact]
    public async Task a_stale_expected_position_is_a_conflict_carrying_the_head()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("a"), T.Ct);
        await dataset.CommitAsync(One("b"), T.Ct);

        CommitResult result = await dataset.CommitAsync(new CommitRequest { ExpectedPosition = 1 }.Assert(T.Iri("c"), T.Iri("p"), T.Iri("o")), T.Ct);

        Assert.Equal(CommitOutcome.Conflict, result.Outcome);
        Assert.Equal(2, result.Position);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(new CommitRequest { ExpectedPosition = 2 }.Assert(T.Iri("c"), T.Iri("p"), T.Iri("o")), T.Ct)).Outcome);
    }

    [Fact]
    public async Task a_blank_label_is_a_new_node_in_every_request_and_a_handle_is_the_same_node()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Blank("x"), T.Iri("p"), T.Iri("o")).Assert(T.Blank("x"), T.Iri("q"), T.Iri("o")), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Blank("x"), T.Iri("p"), T.Iri("o")), T.Ct);

        using DatasetView view = dataset.Pin();
        List<Quad> quads = T.All(view);
        Assert.Equal(3, quads.Count);
        Assert.Equal(2, quads.Select(q => q.Subject).Distinct().Count());

        TermHandle first = quads.First(q => view.TryExternalise(q.Predicate, out RdfTerm? p) && p.Equals(T.Iri("q"))).Subject;

        // Re-sending the externalised label is a new node; the handle is the old one.
        Assert.True(view.TryExternalise(first, out RdfTerm? label));
        await dataset.CommitAsync(new CommitRequest().Assert(label, T.Iri("r"), T.Iri("o")), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(RequestTerm.Existing(first), T.Iri("s"), T.Iri("o")), T.Ct);

        using DatasetView after = dataset.Pin();
        Assert.Equal(3, T.All(after).Select(q => q.Subject).Distinct().Count());
        Assert.Contains(new Quad(first, After(after, "s"), After(after, "o")), T.All(after));
    }

    private static TermHandle After(DatasetView view, string local) =>
        view.TryInternalise(T.Iri(local), out TermHandle handle) ? handle : throw new InvalidOperationException(local);

    [Fact]
    public async Task a_handle_the_dataset_never_issued_is_refused_and_leaves_no_trace()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("s"), T.Ct);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await dataset.CommitAsync(new CommitRequest().Assert(RequestTerm.Existing(new TermHandle(999)), T.Iri("new"), T.Iri("o")), T.Ct));

        Assert.Equal(1, dataset.Head);
        using DatasetView view = dataset.Pin();
        Assert.False(view.TryInternalise(T.Iri("new"), out _));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(One("t"), T.Ct)).Outcome);
    }

    [Fact]
    public async Task an_inline_literal_has_no_entry_and_a_non_canonical_one_is_a_different_term()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        RecordingProjection recorder = new();

        await dataset.CommitAsync(new CommitRequest()
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("1"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("01"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Boolean("true"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Lang("chat", "EN"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Lang("chat", "en")), T.Ct);
        await dataset.CatchUpAsync(recorder, T.Ct);

        using DatasetView view = dataset.Pin();
        Assert.Equal(4, T.All(view).Count); // @EN and @en are one term

        string[] allocated = [.. recorder.Commits[0].Allocations.ToArray().Select(a => T.Render(a.Term))];
        Assert.Contains(T.Render(T.Integer("01")), allocated);
        Assert.DoesNotContain(T.Render(T.Integer("1")), allocated);
        Assert.DoesNotContain(T.Render(T.Boolean("true")), allocated);

        Assert.True(view.TryInternalise(T.Integer("1"), out TermHandle one));
        Assert.True(view.TryInternalise(T.Integer("01"), out TermHandle leading));
        Assert.False(view.TermComparer.Equals(one, leading));
        Assert.True(view.TryExternalise(one, out RdfTerm? back));
        Assert.Equal(T.Integer("1"), back);
    }

    [Fact]
    public async Task a_validator_sees_the_proposed_state_with_its_fresh_terms_and_an_attachment_is_recorded()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("old"), T.Ct);
        Inspecting validator = new();

        CommitRequest request = new CommitRequest().Assert(T.Iri("new"), T.Iri("p"), T.Iri("o")).Retract(T.Iri("old"), T.Iri("p"), T.Iri("o"));
        request.Validators.Add(validator);
        CommitResult result = await dataset.CommitAsync(request, T.Ct);

        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        Assert.Equal(["<http://example.org/new> <http://example.org/p> <http://example.org/o>"], validator.Seen);

        RecordingProjection recorder = new();
        await dataset.CatchUpAsync(recorder, T.Ct);
        using DatasetView view = dataset.Pin();
        Assert.True(view.TryExternalise(recorder.Commits[1].Attachments.Span[0], out RdfTerm? attachment));
        Assert.Equal(T.Iri("report"), attachment);
    }

    private sealed class Inspecting : ICommitValidator
    {
        public List<string> Seen { get; } = [];

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            Seen.AddRange(T.Terms(proposed));
            return ValidationVerdict.Accept(T.Iri("report"));
        }
    }

    [Fact]
    public async Task a_settings_change_needs_an_agent_and_a_cause_and_is_a_fold()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current }, new CommitMetadata { Agent = T.Iri("me") }, cancellationToken: T.Ct));

        await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current }, new CommitMetadata { Agent = T.Iri("me"), Cause = T.Literal("why") }, cancellationToken: T.Ct);
        await dataset.CommitAsync(One("s"), T.Ct);
        await dataset.ChangeSettingsAsync(new SettingsChange(), new CommitMetadata { Agent = T.Iri("me"), Cause = T.Literal("nothing") }, cancellationToken: T.Ct);

        Assert.Equal(3, dataset.Head);
        Assert.Equal(AccessScope.AllHistory, (await dataset.SettingsAtAsync(0, T.Ct)).DefaultAccessScope);
        Assert.Equal(AccessScope.Current, (await dataset.SettingsAtAsync(1, T.Ct)).DefaultAccessScope);
        Assert.Equal(AccessScope.Current, dataset.Settings.DefaultAccessScope);
    }

    // --- §7: the failed state ------------------------------------------------------

    [Fact]
    public async Task a_default_projection_that_throws_fails_the_dataset_until_it_is_rebuilt()
    {
        MemoryStorage storage = new();
        DatasetOptions options = T.Options();
        await using Dataset dataset = await Dataset.OpenAsync(storage, options, T.Ct);
        await dataset.CommitAsync(One("a"), T.Ct);

        options.DefaultProjectionFault = position => position == 2;

        // The commit stands: a derived artefact cannot veto a durable fact.
        CommitResult committed = await dataset.CommitAsync(One("b"), T.Ct);
        Assert.Equal(CommitOutcome.Committed, committed.Outcome);
        Assert.Equal(2, committed.Position);
        Assert.True(dataset.IsFailed);

        // Pin() and T1 fail explicitly; nothing waits.
        Assert.Throws<DatasetUnavailableException>(() => dataset.Pin());
        CommitResult refused = await dataset.CommitAsync(One("c"), T.Ct);
        Assert.Equal(CommitOutcome.Unavailable, refused.Outcome);
        Assert.NotNull(refused.Reason);

        // As-of reads come from the log and a checkpoint, not the default projection.
        using (DatasetView asOf = await dataset.AsOfAsync(2, T.Ct))
        {
            Assert.Equal(2, T.All(asOf).Count);
        }

        // A rebuild that fails again leaves it failed; one that succeeds clears it.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await dataset.RebuildDefaultProjectionAsync(T.Ct));
        Assert.True(dataset.IsFailed);

        options.DefaultProjectionFault = null;
        await dataset.RebuildDefaultProjectionAsync(T.Ct);
        Assert.False(dataset.IsFailed);

        using DatasetView view = dataset.Pin();
        Assert.Equal(2, T.All(view).Count);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(One("c"), T.Ct)).Outcome);
    }

    // --- §8 and ADR 0046: subscriptions ------------------------------------------------

    [Fact]
    public async Task a_filtered_subscription_skips_what_it_filters_out_but_never_settings_or_erasure()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        RdfTerm g = T.Iri("g");
        await dataset.CommitAsync(One("a", g), T.Ct);                                    // 1: in g
        await dataset.CommitAsync(One("b"), T.Ct);                                       // 2: default graph
        await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current }, new CommitMetadata { Agent = T.Iri("me"), Cause = T.Iri("why") }, cancellationToken: T.Ct); // 3
        await dataset.AppendErasureAsync(7, new CommitMetadata { Agent = T.Iri("me") }, T.Ct); // 4: the test seam
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("c"), T.Iri("p"), T.Iri("o"), g).Assert(T.Iri("d"), T.Iri("p"), T.Iri("o")), T.Ct); // 5: both

        using DatasetView pin = dataset.Pin();
        Assert.True(pin.TryInternalise(g, out TermHandle gh));
        List<Commit> delivered = await Take(dataset.Subscribe(0, SubscriptionFilter.ForGraph(GraphPattern.Named(gh)), T.Ct), 4);

        Assert.Equal([1L, 3, 4, 5], delivered.Select(c => c.Position));
        Assert.Equal([CommitKind.Data, CommitKind.Settings, CommitKind.Erasure, CommitKind.Data], delivered.Select(c => c.Kind));
        Assert.Equal(1, delivered[3].Delta.Asserted.Length);
        Assert.True(delivered[1].Delta.IsEmpty && delivered[2].Delta.IsEmpty);

        // The filtered commit carries only the allocations its filtered delta mentions.
        string[] allocated = [.. delivered[3].Allocations.ToArray().Select(a => T.Render(a.Term))];
        Assert.Contains(T.Render(T.Iri("c")), allocated);
        Assert.DoesNotContain(T.Render(T.Iri("d")), allocated);

        // The consumer owns its position: resuming from 3 resumes at 4.
        Assert.Equal([4L, 5], (await Take(dataset.Subscribe(3, SubscriptionFilter.ForGraph(GraphPattern.Named(gh)), T.Ct), 2)).Select(c => c.Position));
    }

    [Fact]
    public async Task a_subscription_at_the_head_waits_for_the_next_commit()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("a"), T.Ct);

        Task<List<Commit>> waiting = Take(dataset.Subscribe(1, SubscriptionFilter.All, T.Ct), 1);
        await Task.Delay(50, T.Ct);
        Assert.False(waiting.IsCompleted);

        await dataset.CommitAsync(One("b"), T.Ct);
        List<Commit> got = await waiting.WaitAsync(TimeSpan.FromSeconds(10), T.Ct);
        Assert.Equal(2, got.Single().Position);
    }

    private static async Task<List<Commit>> Take(IAsyncEnumerable<Commit> feed, int count)
    {
        List<Commit> taken = [];

        await foreach (Commit commit in feed)
        {
            taken.Add(commit);

            if (taken.Count == count)
            {
                break;
            }
        }

        return taken;
    }

    // --- T2 and I7 --------------------------------------------------------------------

    /// <summary>
    /// I7: a checkpoint at <c>P</c> plus the log tail to <c>Q</c> equals full
    /// replay to <c>Q</c> — for every <c>P</c>, with that checkpoint alone
    /// beside the log.
    /// </summary>
    [Fact]
    public async Task a_checkpoint_plus_the_tail_equals_full_replay()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                long head = harness.Model.Head;
                List<byte[]> log = await LogPropertyTests.SegmentsAsync(harness.Storage);

                for (long p = 1; p <= head; p++)
                {
                    await harness.Dataset.CheckpointAsync(p, T.Ct);
                    string name = (await harness.Storage.Derived.ListAsync(T.Ct)).Single();
                    ReadOnlyMemory<byte> blob = await harness.Storage.Derived.GetRangeAsync(name, 0, int.MaxValue, T.Ct);
                    await harness.Dataset.DropCheckpointAsync(p, T.Ct);

                    MemoryStorage alone = MemoryStorage.FromSegments(log.Select(s => (ReadOnlyMemory<byte>)s), [new(name, blob)]);
                    await using Dataset opened = await Dataset.OpenAsync(alone, harness.Options, T.Ct);
                    Assert.Equal([p], opened.Checkpoints);

                    for (long q = p; q <= head; q++)
                    {
                        using DatasetView view = await opened.AsOfAsync(q, T.Ct);
                        Harness.Same(Harness.Rendered(harness.Model.History[(int)q]), harness.Rendered(view), "checkpoint " + p + " + tail to " + q);
                    }

                    using DatasetView pinned = opened.Pin();
                    Harness.Same(Harness.Rendered(harness.Model.Graph), harness.Rendered(pinned), "rebuilt from checkpoint " + p);
                }
            },
            iter: 60,
            print: script => script.ToString());
    }

    [Fact]
    public async Task a_checkpoint_copied_beside_another_log_is_ignored()
    {
        MemoryStorage first = new();
        await using (Dataset dataset = await T.Open(first))
        {
            await dataset.CommitAsync(One("a"), T.Ct);
            await dataset.CheckpointAsync(1, T.Ct);
        }

        MemoryStorage second = new();
        await using (Dataset dataset = await T.Open(second))
        {
            await dataset.CommitAsync(One("different"), T.Ct);
        }

        string name = (await first.Derived.ListAsync(T.Ct)).Single();
        ReadOnlyMemory<byte> blob = await first.Derived.GetRangeAsync(name, 0, int.MaxValue, T.Ct);
        MemoryStorage mixed = MemoryStorage.FromSegments(await LogPropertyTests.SegmentsAsync(second) is { } log ? log.Select(s => (ReadOnlyMemory<byte>)s) : [], [new(name, blob)]);

        await using Dataset opened = await T.Open(mixed);
        Assert.Empty(opened.Checkpoints);
        using DatasetView view = opened.Pin();
        Assert.Equal(["<http://example.org/different> <http://example.org/p> <http://example.org/o>"], T.Terms(view));
    }

    // --- R1 ---------------------------------------------------------------------------

    [Fact]
    public async Task a_pinned_read_does_not_see_terms_allocated_after_it()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("a"), T.Ct);
        using DatasetView pinned = dataset.Pin();
        await dataset.CommitAsync(One("later"), T.Ct);

        Assert.False(pinned.TryInternalise(T.Iri("later"), out _));
        using DatasetView now = dataset.Pin();
        Assert.True(now.TryInternalise(T.Iri("later"), out TermHandle later));
        Assert.False(pinned.TryExternalise(later, out _));
    }

    // --- §6: source-supplied equality with private terms, against a stub ----------------

    [Fact]
    public void a_readable_private_term_equals_a_canonical_one_by_value_and_a_shredded_one_only_itself()
    {
        ulong canonical = 5;
        ulong privateReadable = (2UL << 62) | 1;
        ulong privateOther = (2UL << 62) | 2;
        ulong shredded = (2UL << 62) | 3;
        ulong blank = (1UL << 62) | 1;
        RdfTerm emil = T.Literal("Emil");

        StoreTermComparer comparer = StoreTermComparer.ByValue(
            new StubPrivates(new() { [privateReadable] = emil, [privateOther] = emil }),
            id => id == canonical ? emil : null);

        TermHandle c = new(canonical), p1 = new(privateReadable), p2 = new(privateOther), s = new(shredded), b = new(blank);

        Assert.True(comparer.Equals(p1, c));
        Assert.True(comparer.Equals(c, p2));
        Assert.True(comparer.Equals(p1, p2));
        Assert.Equal(comparer.GetHashCode(c), comparer.GetHashCode(p1));
        Assert.True(comparer.Equals(s, s));
        Assert.False(comparer.Equals(s, c));
        Assert.False(comparer.Equals(s, new TermHandle((2UL << 62) | 4)));
        Assert.False(comparer.Equals(b, c));
        Assert.True(StoreTermComparer.ById.Equals(c, c) && !StoreTermComparer.ById.Equals(p1, c));
    }

    private sealed class StubPrivates(Dictionary<ulong, RdfTerm> readable) : IPrivateTermValues
    {
        public bool TryValue(ulong id, [MaybeNullWhen(false)] out RdfTerm term) => readable.TryGetValue(id, out term);
    }

    // --- R2, R3 edges ------------------------------------------------------------------

    [Fact]
    public async Task reads_outside_the_log_are_refused_and_diff_runs_both_ways()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("a"), T.Ct);
        await dataset.CommitAsync(One("b"), T.Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await dataset.AsOfAsync(3, T.Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await dataset.DiffAsync(0, 3, T.Ct));

        QuadDelta forward = await dataset.DiffAsync(0, 2, T.Ct);
        QuadDelta backward = await dataset.DiffAsync(2, 0, T.Ct);
        Assert.Equal(2, forward.Asserted.Length);
        Assert.Equal(forward.Inverse(), backward);
        Assert.True((await dataset.DiffAsync(1, 1, T.Ct)).IsEmpty);
    }

    [Fact]
    public async Task a_projection_that_is_fed_twice_applies_once()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(One("a"), T.Ct);
        await dataset.CommitAsync(One("b"), T.Ct);
        QuadSetProjection projection = new();

        await dataset.CatchUpAsync(projection, T.Ct);
        await foreach (Commit commit in dataset.Subscribe(0, SubscriptionFilter.All, T.Ct))
        {
            await projection.ApplyAsync(commit, CancellationToken.None);

            if (commit.Position == 2)
            {
                break;
            }
        }

        Assert.Equal(2, projection.Quads.Count);
        Assert.Equal(2, projection.Position);
    }
}
