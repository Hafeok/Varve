// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CsCheck;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// ADR 0049's first promise — an exact estimate is the count <c>Match</c>
/// yields — held to for the in-memory dataset and for the overlay over every
/// pattern shape and graph mode, and ADR 0050's accessor on the sources that
/// have no inline handles.
/// </summary>
public class EstimateTests
{
    private static readonly Gen<TermHandle> Position = Gen.ULong[0, 4].Select(v => new TermHandle(v));

    private static readonly Gen<GraphPattern> Graphs = Gen.Int[0, 5].SelectMany(mode => mode switch
    {
        0 => Gen.Const(GraphPattern.DefaultGraph),
        1 => Gen.Const(GraphPattern.AnyNamed),
        2 => Gen.Const(GraphPattern.Any),
        _ => Gen.ULong[1, 2].Select(g => GraphPattern.Named(new TermHandle(g))),
    });

    private static readonly Gen<(TermHandle S, TermHandle P, TermHandle O, GraphPattern G)> Patterns =
        Gen.Select(Position, Position, Position, Graphs);

    private static InMemoryDataset Dataset(IEnumerable<Quad> quads)
    {
        InMemoryDataset dataset = new();

        for (int i = 1; i <= 4; i++)
        {
            dataset.Internalise(RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://a/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        foreach (Quad quad in quads)
        {
            dataset.Add(in quad);
        }

        return dataset;
    }

    private static long Counted(IQuadSource source, (TermHandle S, TermHandle P, TermHandle O, GraphPattern G) pattern)
    {
        using IQuadCursor cursor = source.Match(pattern.S, pattern.P, pattern.O, pattern.G);
        long count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void the_three_states_are_distinct_and_the_default_is_unknown()
    {
        CardinalityEstimate unknown = default;
        Assert.True(unknown.IsUnknown);
        Assert.False(unknown.IsExact);
        Assert.False(unknown.IsEstimated);
        Assert.Equal(CardinalityEstimate.Unknown, unknown);
        Assert.Equal("?", unknown.ToString());

        CardinalityEstimate exact = CardinalityEstimate.Exact(3);
        Assert.True(exact.IsExact);
        Assert.False(exact.IsUnknown);
        Assert.Equal(3, exact.Count);
        Assert.Equal("3", exact.ToString());

        CardinalityEstimate estimated = CardinalityEstimate.Estimated(3);
        Assert.True(estimated.IsEstimated);
        Assert.Equal("~3", estimated.ToString());

        Assert.NotEqual(exact, estimated);
        Assert.NotEqual(exact, CardinalityEstimate.Exact(4));
        Assert.True(exact == CardinalityEstimate.Exact(3));
        Assert.Equal(exact.GetHashCode(), CardinalityEstimate.Exact(3).GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => CardinalityEstimate.Exact(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardinalityEstimate.Estimated(-1));
    }

    [Fact]
    public void an_inline_value_reads_only_as_its_own_kind()
    {
        InlineValue integer = InlineValue.FromInteger(-42);
        Assert.Equal(InlineValueKind.Integer, integer.Kind);
        Assert.Equal(-42, integer.Integer);
        Assert.False(integer.Boolean);

        InlineValue boolean = InlineValue.FromBoolean(true);
        Assert.Equal(InlineValueKind.Boolean, boolean.Kind);
        Assert.True(boolean.Boolean);
        Assert.Equal(0, boolean.Integer);

        Assert.Equal(InlineValueKind.None, InlineValue.None.Kind);
        Assert.Equal(default, InlineValue.None);
        Assert.NotEqual(InlineValue.FromInteger(1), InlineValue.FromBoolean(true));
        Assert.True(InlineValue.FromInteger(7) == InlineValue.FromInteger(7));
        Assert.Equal(InlineValue.FromBoolean(false).GetHashCode(), InlineValue.FromBoolean(false).GetHashCode());
    }

    [Fact]
    public void the_in_memory_dataset_counts_exactly()
    {
        Gen.Select(DeltaPropertyTests.SmallQuad.Array[0, 16], Patterns)
            .Sample(
                (quads, pattern) =>
                {
                    InMemoryDataset dataset = Dataset(quads);
                    CardinalityEstimate estimate = dataset.Estimate(pattern.S, pattern.P, pattern.O, pattern.G);
                    return estimate.IsExact && estimate.Count == Counted(dataset, pattern);
                },
                iter: DeltaPropertyTests.Iterations);
    }

    [Fact]
    public void the_overlay_adjusts_its_base_and_stays_exact_for_any_delta()
    {
        Gen.Select(DeltaPropertyTests.SmallQuad.Array[0, 12], DeltaPropertyTests.Delta, Gen.Bool, Patterns)
            .Sample(
                (start, raw, exact, pattern) =>
                {
                    HashSet<Quad> baseSet = [.. start];
                    QuadDelta delta = exact ? DeltaPropertyTests.Exact(baseSet, raw) : raw;
                    QuadOverlay overlay = new(Dataset(start), delta);
                    CardinalityEstimate estimate = overlay.Estimate(pattern.S, pattern.P, pattern.O, pattern.G);
                    return estimate.IsExact && estimate.Count == Counted(overlay, pattern);
                },
                iter: DeltaPropertyTests.Iterations);
    }

    [Fact]
    public void the_overlay_keeps_the_base_state_it_adjusts()
    {
        Quad inBase = new(new TermHandle(1), new TermHandle(2), new TermHandle(3));
        Quad added = new(new TermHandle(1), new TermHandle(2), new TermHandle(4));
        QuadDelta delta = QuadDelta.Create([added], [inBase]);

        QuadOverlay unknown = new(new Stub(CardinalityEstimate.Unknown, [inBase]), delta);
        Assert.True(unknown.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).IsUnknown);

        QuadOverlay estimated = new(new Stub(CardinalityEstimate.Estimated(5), [inBase]), delta);
        CardinalityEstimate adjusted = estimated.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        Assert.True(adjusted.IsEstimated);
        Assert.Equal(5, adjusted.Count); // +1 for the assertion the base lacks, -1 for the retraction it has
    }

    [Fact]
    public void the_in_memory_dataset_has_no_inline_values_and_the_overlay_asks_its_base()
    {
        InMemoryDataset dataset = new();
        TermHandle one = dataset.Internalise(RdfTerm.Literal("1"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)));
        Assert.False(dataset.TryGetInlineValue(one, out InlineValue none));
        Assert.Equal(InlineValue.None, none);
        Assert.False(new QuadOverlay(dataset, QuadDelta.Empty).TryGetInlineValue(one, out _));

        QuadOverlay overlay = new(new Stub(CardinalityEstimate.Unknown, []), QuadDelta.Empty);
        Assert.True(overlay.TryGetInlineValue(new TermHandle(Stub.Inline), out InlineValue value));
        Assert.Equal(InlineValue.FromInteger(99), value);
        Assert.False(overlay.TryGetInlineValue(new TermHandle(1), out _));
    }

    /// <summary>A source with a fixed answer, standing in for a backend that samples.</summary>
    private sealed class Stub : IQuadSource
    {
        internal const ulong Inline = 0xC000_0000_0000_0063;

        private readonly CardinalityEstimate _estimate;
        private readonly HashSet<Quad> _quads;

        internal Stub(CardinalityEstimate estimate, IEnumerable<Quad> quads)
        {
            _estimate = estimate;
            _quads = [.. quads];
        }

        public IEqualityComparer<TermHandle> TermComparer => EqualityComparer<TermHandle>.Default;

        public bool TryInternalise(RdfTerm term, out TermHandle handle)
        {
            handle = TermHandle.None;
            return false;
        }

        public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
        {
            term = null;
            return false;
        }

        public bool Contains(in Quad quad) => _quads.Contains(quad);

        public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
            throw new NotSupportedException();

        public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) => _estimate;

        public bool TryGetInlineValue(TermHandle handle, out InlineValue value)
        {
            value = handle.Value == Inline ? InlineValue.FromInteger(99) : InlineValue.None;
            return handle.Value == Inline;
        }
    }
}
