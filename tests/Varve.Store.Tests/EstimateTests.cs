// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// ADR 0049's claim for the store: the sum over runs of a prefix range per
/// run is the count, because each run is an exact delta (I2). Held to the
/// counted <c>Match</c> for every subset of bound positions and every graph
/// mode, at the head over generated histories and at every as-of position,
/// which is the overlay path. And ADR 0050's accessor on the ids the store
/// inlines.
/// </summary>
public class EstimateTests
{
    private const int Iterations = 60;

    private static readonly GraphPattern[] Modes = [GraphPattern.DefaultGraph, GraphPattern.AnyNamed, GraphPattern.Any];

    private static IEnumerable<(TermHandle S, TermHandle P, TermHandle O, GraphPattern G)> Patterns(DatasetView source)
    {
        List<Quad> all = T.All(source);
        TermHandle[] subjects = [TermHandle.None, .. all.Select(q => q.Subject).Distinct().Take(3)];
        TermHandle[] predicates = [TermHandle.None, .. all.Select(q => q.Predicate).Distinct().Take(3)];
        TermHandle[] objects = [TermHandle.None, .. all.Select(q => q.Object).Distinct().Take(3)];
        GraphPattern[] graphs = [.. Modes, .. all.Select(q => q.Graph).Where(g => !g.IsNone).Distinct().Take(2).Select(GraphPattern.Named)];

        // A pattern the source has never seen, so that the empty range is covered too.
        TermHandle stranger = new(TermIds.Canonical(long.MaxValue >> 8));

        foreach (TermHandle s in subjects.Append(stranger))
        {
            foreach (TermHandle p in predicates)
            {
                foreach (TermHandle o in objects)
                {
                    foreach (GraphPattern g in graphs)
                    {
                        yield return (s, p, o, g);
                    }
                }
            }
        }
    }

    private static void Agree(DatasetView source, string when)
    {
        foreach ((TermHandle s, TermHandle p, TermHandle o, GraphPattern g) in Patterns(source))
        {
            CardinalityEstimate estimate = source.Estimate(s, p, o, g);
            long counted = T.Drain(source.Match(s, p, o, g)).Count;

            if (!estimate.IsExact || estimate.Count.Value != counted)
            {
                throw new InvalidOperationException(
                    when + ": estimate " + estimate + " for (" + s.Value + ", " + p.Value + ", " + o.Value + ", " + g.Match + "/" + g.Graph.Value
                    + ") but Match yields " + counted + ".");
            }
        }
    }

    [Fact]
    public async Task the_estimate_is_the_count_at_the_head_and_at_every_position()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);

                if (harness.Dataset.IsFailed)
                {
                    return;
                }

                using (DatasetView head = harness.Dataset.Pin())
                {
                    Agree(head, "head " + head.Position);
                }

                for (long position = 0; position <= harness.Dataset.Head; position++)
                {
                    using DatasetView view = await harness.Dataset.AsOfAsync(position, T.Ct);
                    Agree(view, "as of " + position);
                }
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>
    /// Found by the property above (CsCheck seed <c>1zI0tBTNKoy3</c>, no
    /// shrink). A merge of two runs kept the newer verdict for a key the older
    /// run asserted and the newer retracted, so the merged run retracted a key
    /// nothing older held: the same through a lookup, one short through the
    /// count. Built here without the generator: a run of eight, which a run of
    /// one does not merge into, then the pair that merges with each other. Two
    /// more commits would cascade the merge into the oldest run, which drops
    /// retractions and hides the case.
    /// </summary>
    [Fact]
    public async Task a_merged_run_cancels_an_assertion_the_newer_run_retracted()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(Eight(), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("x"), T.Iri("p"), T.Iri("o")), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Retract(T.Iri("x"), T.Iri("p"), T.Iri("o")), T.Ct);

        using DatasetView view = dataset.Pin();
        Assert.True(view.TryInternalise(T.Iri("x"), out TermHandle x));

        Assert.Equal(CardinalityEstimate.Exact(new QuadCount(0)), view.Estimate(x, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph));
        Assert.Equal(CardinalityEstimate.Exact(new QuadCount(8)), view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph));
        Agree(view, "head " + view.Position);
    }

    /// <summary>
    /// The reverse pair of the case above: a key the older run retracted and
    /// the newer asserted was kept as an assertion, and counted once over on
    /// top of the run that held it all along.
    /// </summary>
    [Fact]
    public async Task a_merged_run_cancels_a_retraction_the_newer_run_reasserted()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(Eight(), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Retract(T.Iri("s0"), T.Iri("p"), T.Iri("o")), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s0"), T.Iri("p"), T.Iri("o")), T.Ct);

        using DatasetView view = dataset.Pin();
        Assert.True(view.TryInternalise(T.Iri("s0"), out TermHandle s0));

        Assert.Equal(CardinalityEstimate.Exact(new QuadCount(1)), view.Estimate(s0, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph));
        Assert.Equal(CardinalityEstimate.Exact(new QuadCount(8)), view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph));
        Agree(view, "head " + view.Position);
    }

    private static CommitRequest Eight()
    {
        CommitRequest request = new();

        for (int i = 0; i < 8; i++)
        {
            request.Assert(T.Iri("s" + i), T.Iri("p"), T.Iri("o"));
        }

        return request;
    }

    [Fact]
    public async Task a_validator_sees_the_proposed_state_estimated()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Integer("1")), T.Ct);
        Counting validator = new();

        CommitRequest request = new CommitRequest()
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("2"))
            .Retract(T.Iri("s"), T.Iri("p"), T.Integer("1"));
        request.Validators.Add(validator);
        await dataset.CommitAsync(request, T.Ct);

        Assert.Equal(CardinalityEstimate.Exact(new QuadCount(1)), validator.Seen);
    }

    [Fact]
    public async Task inline_ids_hand_over_their_value_and_nothing_else_does()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(new CommitRequest()
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("1"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("-36028797018963968"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("36028797018963967"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("36028797018963968"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Integer("01"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Boolean("true"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Boolean("false"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Boolean("1"))
            .Assert(T.Iri("s"), T.Iri("p"), T.Literal("1")), T.Ct);

        using DatasetView view = dataset.Pin();

        Assert.Equal(InlineValue.FromInteger(1), Inline(view, T.Integer("1")));
        Assert.Equal(InlineValue.FromInteger(-(1L << 55)), Inline(view, T.Integer("-36028797018963968")));
        Assert.Equal(InlineValue.FromInteger((1L << 55) - 1), Inline(view, T.Integer("36028797018963967")));
        Assert.Equal(InlineValue.FromBoolean(true), Inline(view, T.Boolean("true")));
        Assert.Equal(InlineValue.FromBoolean(false), Inline(view, T.Boolean("false")));

        Assert.Null(Inline(view, T.Integer("36028797018963968"))); // out of the inline range
        Assert.Null(Inline(view, T.Integer("01"))); // not canonical
        Assert.Null(Inline(view, T.Boolean("1"))); // not canonical
        Assert.Null(Inline(view, T.Literal("1"))); // xsd:string
        Assert.Null(Inline(view, T.Iri("s")));
        Assert.False(view.TryGetInlineValue(TermHandle.None, out _));

        view.Dispose();
        Assert.Throws<ObjectDisposedException>(() => view.TryGetInlineValue(TermHandle.None, out _));
        Assert.Throws<ObjectDisposedException>(() => view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any));
    }

    private static InlineValue? Inline(DatasetView view, RdfTerm term)
    {
        Assert.True(view.TryInternalise(term, out TermHandle handle));

        if (view.TryGetInlineValue(handle, out InlineValue value))
        {
            Assert.NotEqual(InlineValueKind.None, value.Kind);
            return value;
        }

        Assert.Equal(InlineValue.None, value);
        return null;
    }

    private sealed class Counting : ICommitValidator
    {
        public CardinalityEstimate Seen { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            Seen = proposed.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
            return ValidationVerdict.Accept();
        }
    }
}
