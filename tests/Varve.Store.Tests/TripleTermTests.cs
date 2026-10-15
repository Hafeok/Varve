// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>Triple terms as the store's sources hand them out, by value and by handle (ADR 0121).</summary>
public class TripleTermTests
{
    private static RdfTerm Triple(bool blankInside) =>
        RdfTerm.TripleTerm(blankInside ? T.Blank("b") : T.Iri("ts"), T.Iri("tp"), T.Literal("to"));

    /// <summary>
    /// A validator reads the pending source (spec T1 step 5): a triple term
    /// the commit creates is that triple term there, as it is in the store
    /// once the commit lands. #109 suspected otherwise from
    /// <c>PendingSource</c>'s constructor alone; the resolver fills a final
    /// triple allocation's term before the pending source reads it, and this
    /// keeps it so.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_validator_externalises_a_fresh_triple_term_as_the_store_does_after_the_commit(bool blankInside)
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        Externalising validator = new();

        CommitResult result = await dataset.CommitAsync(
            new CommitRequest { Validators = [validator] }.Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside)), T.Ct);

        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        using DatasetView view = dataset.Pin();
        Quad stored = Assert.Single(T.All(view));
        Assert.True(view.TryExternalise(stored.Object, out RdfTerm? after));
        Assert.Equal(RdfTermKind.TripleTerm, after.Kind);
        Assert.Equal(after, validator.Seen);
    }

    // A store over memory or over runs flushed to derived/ (ADR 0070), with
    // the triple term <<( _:b tp "to" )>> at position 1, its blank node also
    // a subject, and a second triple term allocated at position 2.
    private static async Task<Dataset> TwoPositions(bool flushed)
    {
        DatasetOptions options = new() { Clock = ManualClock.Epoch(), MemtableLimit = new QuadCount(1), Maintenance = MaintenanceMode.Off };
        Dataset dataset = flushed ? await Dataset.CreateAsync(new MemoryStorage(), T.Id, options, T.Ct) : await T.Open(new MemoryStorage());

        await dataset.CommitAsync(
            new CommitRequest()
                .Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside: true))
                .Assert(T.Blank("b"), T.Iri("name"), T.Literal("b")),
            T.Ct);

        if (flushed)
        {
            await dataset.MaintainAsync(T.Ct);
        }

        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("later"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("a"), T.Iri("tp"), T.Literal("later"))), T.Ct);
        return dataset;
    }

    private static TermHandle ObjectOf(IQuadSource source, string subject)
    {
        Assert.True(source.TryInternalise(T.Iri(subject), out TermHandle s));
        return T.Drain(source.Match(s, TermHandle.None, TermHandle.None, GraphPattern.Any)).Single().Object;
    }

    /// <summary>
    /// A triple term with a blank node inside answers its components as the
    /// handles the store gave out, at the head, through a pinned view and at
    /// an as-of position alike: the dictionary is append-only (ADR 0121,
    /// #87). A triple term allocated after a view's position is one that view
    /// does not know, and it answers false where the head answers.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_triple_terms_components_are_the_stores_handles_at_the_head_pinned_and_as_of(bool flushed)
    {
        await using Dataset dataset = await TwoPositions(flushed);
        using DatasetView head = dataset.Pin();
        using DatasetView asOf = await dataset.AsOfAsync(new Position(1), T.Ct);

        // The handles the store gave out: the blank node as a subject, the rest by value.
        Assert.True(head.TryInternalise(T.Iri("name"), out TermHandle name));
        TermHandle blank = T.Drain(head.Match(TermHandle.None, name, TermHandle.None, GraphPattern.Any)).Single().Subject;
        Assert.True(head.TryInternalise(T.Iri("tp"), out TermHandle tp));
        Assert.True(head.TryInternalise(T.Literal("to"), out TermHandle to));
        TermHandle triple = ObjectOf(head, "r");
        TermHandle later = ObjectOf(head, "later");

        Assert.Equal(new Position(2), head.Position);
        Assert.Equal(new Position(1), asOf.Position);

        foreach (IQuadSource view in new IQuadSource[] { head, asOf })
        {
            Assert.Equal(triple, ObjectOf(view, "r"));
            Assert.True(view.TryGetTripleTermComponents(triple, out TermHandle s, out TermHandle p, out TermHandle o));
            Assert.Equal((blank, tp, to), (s, p, o));

            // Not a triple term: a blank node, an IRI, a literal.
            Assert.False(view.TryGetTripleTermComponents(blank, out _, out _, out _));
            Assert.False(view.TryGetTripleTermComponents(tp, out _, out _, out _));
            Assert.False(view.TryGetTripleTermComponents(to, out _, out _, out _));
        }

        // The boundary: position 2's triple term, unknown at position 1.
        Assert.True(head.TryGetTripleTermComponents(later, out TermHandle ls, out _, out _));
        Assert.True(head.TryInternalise(T.Iri("a"), out TermHandle a));
        Assert.Equal(a, ls);
        Assert.False(asOf.TryGetTripleTermComponents(later, out _, out _, out _));
    }

    /// <summary>
    /// A pinned view answers what it answered when it was pinned, after a
    /// later commit allocates another triple term (ADR 0015, 0121).
    /// </summary>
    [Fact]
    public async Task a_pinned_view_answers_the_same_after_a_later_commit_and_false_for_its_triple_term()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside: true)), T.Ct);
        using DatasetView pinned = dataset.Pin();
        TermHandle triple = ObjectOf(pinned, "r");
        Assert.True(pinned.TryGetTripleTermComponents(triple, out TermHandle s, out TermHandle p, out TermHandle o));

        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("later"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("a"), T.Iri("tp"), T.Literal("later"))), T.Ct);
        using DatasetView head = dataset.Pin();
        TermHandle later = ObjectOf(head, "later");

        Assert.True(pinned.TryGetTripleTermComponents(triple, out TermHandle s2, out TermHandle p2, out TermHandle o2));
        Assert.Equal((s, p, o), (s2, p2, o2));
        Assert.True(head.TryGetTripleTermComponents(triple, out TermHandle s3, out TermHandle p3, out TermHandle o3));
        Assert.Equal((s, p, o), (s3, p3, o3));
        Assert.Equal(TermIds.Blank(1), s.Value);

        Assert.True(head.TryGetTripleTermComponents(later, out _, out _, out _));
        Assert.False(pinned.TryGetTripleTermComponents(later, out _, out _, out _));
    }

    /// <summary>
    /// The buffer <see cref="TermDictionary.TryTripleParts"/> reads into holds
    /// the longest triple term entry storage format §4.4 can write: three
    /// component ids at the ten bytes §1 allows a <c>uleb</c>, which an inline
    /// id takes, and an id at its maximum. A canonical id is below 2⁶², nine
    /// bytes, so the entry a section holds is 40 bytes and the bound, 41,
    /// is reached only by an id no section holds; both read back.
    /// </summary>
    [Fact]
    public void the_longest_triple_term_entry_fits_the_buffer_it_is_read_into()
    {
        Assert.Equal(41, LogFormat.MaxTripleEntryLength);

        ArrayBufferWriter<byte> writer = new();
        LogFormat.WriteAllocation(writer, new Allocation(ulong.MaxValue, null, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue));
        Assert.Equal(LogFormat.MaxTripleEntryLength, writer.WrittenCount);
        Assert.True(LogFormat.TryReadTripleEntry(writer.WrittenSpan, out ulong s, out ulong p, out ulong o));
        Assert.Equal((ulong.MaxValue, ulong.MaxValue, ulong.MaxValue), (s, p, o));

        long from = (1L << 62) - 2;
        ulong id = TermIds.Canonical(from + 1);
        Allocation[] allocations = [new Allocation(id, null, ulong.MaxValue, ulong.MaxValue - 1, ulong.MaxValue - 2)];
        Run run = Run.FromDelta([], [], TermSection.Of(allocations, from), 0, 1);
        Span<byte> entry = stackalloc byte[64];
        Assert.Equal(LogFormat.MaxTripleEntryLength - 1, run.Terms.ReadEntry(from + 1, entry));

        Assert.True(TermDictionary.TryTripleParts([run], id, long.MaxValue, out s, out p, out o));
        Assert.Equal((ulong.MaxValue, ulong.MaxValue - 1, ulong.MaxValue - 2), (s, p, o));
        Assert.False(TermDictionary.TryTripleParts([run], id, from, out _, out _, out _));
    }

    /// <summary>
    /// A validator reads a triple term the commit creates by its components,
    /// handles of the proposed state, which are the store's after the commit.
    /// </summary>
    [Fact]
    public async Task a_validator_takes_a_fresh_triple_term_apart_by_handle()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("old"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("a"), T.Iri("tp"), T.Literal("old"))), T.Ct);
        Destructuring validator = new();

        await dataset.CommitAsync(
            new CommitRequest { Validators = [validator] }.Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside: true)),
            T.Ct);

        using DatasetView view = dataset.Pin();
        Assert.True(view.TryGetTripleTermComponents(ObjectOf(view, "r"), out TermHandle s, out TermHandle p, out TermHandle o));
        Assert.Equal((true, s, p, o), validator.Fresh);
        Assert.True(view.TryInternalise(T.Iri("a"), out TermHandle a));
        Assert.Equal(a, validator.Existing.S);
        Assert.True(validator.Existing.Found);
    }

    /// <summary>A staging view takes apart its own staged triple terms and forwards the view's.</summary>
    [Fact]
    public async Task a_staging_view_takes_apart_staged_and_existing_triple_terms()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside: true)), T.Ct);
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();

        TermHandle existing = ObjectOf(view, "r");
        Assert.True(view.TryGetTripleTermComponents(existing, out TermHandle vs, out TermHandle vp, out TermHandle vo));
        Assert.True(staging.TryGetTripleTermComponents(existing, out TermHandle s, out TermHandle p, out TermHandle o));
        Assert.Equal((vs, vp, vo), (s, p, o));

        TermHandle blank = staging.StageBlank();
        TermHandle fresh = staging.Stage(T.Literal("fresh"));
        TermHandle staged = staging.StageTriple(blank, vp, fresh);
        Assert.True(staging.TryGetTripleTermComponents(staged, out s, out p, out o));
        Assert.Equal((blank, vp, fresh), (s, p, o));

        Assert.False(staging.TryGetTripleTermComponents(blank, out _, out _, out _));
        Assert.False(staging.TryGetTripleTermComponents(fresh, out _, out _, out _));
        Assert.False(view.TryGetTripleTermComponents(staged, out _, out _, out _));
    }

    /// <summary>
    /// Taking a triple term apart allocates nothing, a hit or a miss, over the
    /// memtable and over a run read through its blob (constraint 5).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task taking_a_triple_term_apart_allocates_nothing(bool flushed)
    {
        await using Dataset dataset = await TwoPositions(flushed);
        using DatasetView head = dataset.Pin();
        using DatasetView asOf = await dataset.AsOfAsync(new Position(1), T.Ct);
        TermHandle triple = ObjectOf(head, "r");
        TermHandle later = ObjectOf(head, "later");
        Assert.True(head.TryInternalise(T.Literal("to"), out TermHandle literal));
        StagingView staging = head.Stage();
        TermHandle staged = staging.StageTriple(staging.StageBlank(), triple, triple);

        Assert.Equal(0, AllocationMeter.Measure(Repeat(head, triple)));
        Assert.Equal(0, AllocationMeter.Measure(Repeat(head, literal)));
        Assert.Equal(0, AllocationMeter.Measure(Repeat(asOf, triple)));
        Assert.Equal(0, AllocationMeter.Measure(Repeat(asOf, later)));
        Assert.Equal(0, AllocationMeter.Measure(Repeat(staging, staged)));
        Assert.Equal(0, AllocationMeter.Measure(Repeat(staging, triple)));
    }

    private static ulong sink;

    private static Func<object?> Repeat(IQuadSource source, TermHandle handle) => () =>
    {
        for (int i = 0; i < 1_000; i++)
        {
            if (source.TryGetTripleTermComponents(handle, out TermHandle s, out TermHandle p, out TermHandle o))
            {
                sink += s.Value ^ p.Value ^ o.Value;
            }
        }

        return null;
    };

    /// <summary>
    /// The pending source's override allocates nothing, for a triple term the
    /// commit creates and for one the head holds; the table of the commit's
    /// triple terms is built once, with the source.
    /// </summary>
    [Fact]
    public async Task the_pending_source_takes_a_triple_term_apart_without_allocating()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("old"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("a"), T.Iri("tp"), T.Literal("old"))), T.Ct);
        Measuring validator = new();

        await dataset.CommitAsync(
            new CommitRequest { Validators = [validator] }.Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside: true)),
            T.Ct);

        Assert.Equal((0L, 0L), validator.Bytes);
    }

    private sealed class Destructuring : ICommitValidator
    {
        public (bool Found, TermHandle S, TermHandle P, TermHandle O) Fresh { get; private set; }

        public (bool Found, TermHandle S, TermHandle P, TermHandle O) Existing { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            bool found = proposed.TryGetTripleTermComponents(delta.Asserted[0].Object, out TermHandle s, out TermHandle p, out TermHandle o);
            Fresh = (found, s, p, o);
            found = proposed.TryGetTripleTermComponents(ObjectOf(proposed, "old"), out s, out p, out o);
            Existing = (found, s, p, o);
            return ValidationVerdict.Accept();
        }
    }

    private sealed class Measuring : ICommitValidator
    {
        public (long Fresh, long Existing) Bytes { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            TermHandle existing = ObjectOf(proposed, "old");
            Assert.True(proposed.TryGetTripleTermComponents(delta.Asserted[0].Object, out _, out _, out _));
            Assert.True(proposed.TryGetTripleTermComponents(existing, out _, out _, out _));
            Bytes = (AllocationMeter.Measure(Repeat(proposed, delta.Asserted[0].Object)), AllocationMeter.Measure(Repeat(proposed, existing)));
            return ValidationVerdict.Accept();
        }
    }

    private sealed class Externalising : ICommitValidator
    {
        public RdfTerm? Seen { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            Seen = proposed.TryExternalise(delta.Asserted[0].Object, out RdfTerm? term) ? term : null;
            return ValidationVerdict.Accept();
        }
    }
}
