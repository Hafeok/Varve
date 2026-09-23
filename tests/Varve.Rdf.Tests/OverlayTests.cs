// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CsCheck;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// The overlay against the set it stands for, <c>(B \ R) ∪ A</c>, materialised
/// by hand — for every pattern shape and every graph mode, and for deltas that
/// are not exact as well as ones that are.
/// </summary>
public class OverlayTests
{
    private static readonly Gen<TermHandle> Position = Gen.ULong[0, 4].Select(v => new TermHandle(v));

    private static readonly Gen<GraphPattern> Graphs = Gen.Int[0, 5].SelectMany(mode => mode switch
    {
        0 => Gen.Const(GraphPattern.DefaultGraph),
        1 => Gen.Const(GraphPattern.AnyNamed),
        2 => Gen.Const(GraphPattern.Any),
        _ => Gen.ULong[1, 2].Select(g => GraphPattern.Named(new TermHandle(g))),
    });

    private static InMemoryDataset Base(Quad[] quads)
    {
        InMemoryDataset dataset = new();

        // Handles 1 to 4 must mean something to the dataset; the quads use them directly.
        for (int i = 1; i <= 4; i++)
        {
            dataset.Internalise(RdfTerm.Iri(Encoding.UTF8.GetBytes("http://a/" + i.ToString(CultureInfo.InvariantCulture))));
        }

        foreach (Quad quad in quads)
        {
            dataset.Add(in quad);
        }

        return dataset;
    }

    private static List<Quad> Drain(IQuadCursor cursor)
    {
        using (cursor)
        {
            List<Quad> quads = [];

            while (cursor.MoveNext())
            {
                quads.Add(cursor.Current);
            }

            return quads;
        }
    }

    private static bool Matches(Quad quad, TermHandle s, TermHandle p, TermHandle o, GraphPattern g) =>
        (s.IsNone || s == quad.Subject)
        && (p.IsNone || p == quad.Predicate)
        && (o.IsNone || o == quad.Object)
        && g.Matches(quad.Graph);

    [Fact]
    public void an_overlay_is_the_base_less_the_retractions_plus_the_assertions()
    {
        Gen.Select(DeltaPropertyTests.SmallQuad.Array[0, 12], DeltaPropertyTests.Delta, Gen.Bool)
            .Select(Gen.Select(Position, Position, Position, Graphs), (x, pattern) => (x.Item1, x.Item2, x.Item3, pattern))
            .Sample(
                (start, raw, exact, pattern) =>
                {
                    HashSet<Quad> baseSet = [.. start];
                    QuadDelta delta = exact ? DeltaPropertyTests.Exact(baseSet, raw) : raw;
                    HashSet<Quad> expected = DeltaPropertyTests.Apply(baseSet, delta);
                    (TermHandle s, TermHandle p, TermHandle o, GraphPattern g) = pattern;

                    QuadOverlay overlay = new(Base(start), delta);
                    List<Quad> seen = Drain(overlay.Match(s, p, o, g));

                    return seen.Count == seen.Distinct().Count()
                        && seen.ToHashSet().SetEquals(expected.Where(q => Matches(q, s, p, o, g)))
                        && expected.All(q => overlay.Contains(in q))
                        && start.Concat(delta.Retracted.ToArray()).Where(q => !expected.Contains(q)).All(q => !overlay.Contains(in q));
                },
                iter: DeltaPropertyTests.Iterations);
    }

    [Fact]
    public void an_overlay_answers_term_questions_from_its_base()
    {
        InMemoryDataset dataset = new();
        TermHandle handle = dataset.Internalise(RdfTerm.Iri("http://a/x"u8));
        QuadOverlay overlay = new(dataset, QuadDelta.Empty);

        Assert.True(overlay.TryInternalise(RdfTerm.Iri("http://a/x"u8), out TermHandle found));
        Assert.Equal(handle, found);
        Assert.True(overlay.TryExternalise(handle, out RdfTerm? term));
        Assert.Equal(RdfTerm.Iri("http://a/x"u8), term);
        Assert.Same(dataset.TermComparer, overlay.TermComparer);
    }

    private const int Small = 500;
    private const int Large = 4_000;
    private static long sink;

    private static (InMemoryDataset Base, QuadDelta Delta) Scannable(int quads)
    {
        InMemoryDataset dataset = new();
        TermHandle p = dataset.Internalise(RdfTerm.Iri("http://a/p"u8));
        List<Quad> asserted = [];
        List<Quad> retracted = [];

        for (int i = 0; i < quads; i++)
        {
            TermHandle s = dataset.Internalise(RdfTerm.Iri(Encoding.UTF8.GetBytes("http://a/s" + i.ToString(CultureInfo.InvariantCulture))));
            Quad quad = new(s, p, s);

            if (i % 3 == 0)
            {
                asserted.Add(quad);
            }
            else
            {
                dataset.Add(in quad);

                if (i % 3 == 1)
                {
                    retracted.Add(quad);
                }
            }
        }

        return (dataset, QuadDelta.Create([.. asserted], [.. retracted]));
    }

    private static readonly (InMemoryDataset Base, QuadDelta Delta) SmallCase = Scannable(Small);
    private static readonly (InMemoryDataset Base, QuadDelta Delta) LargeCase = Scannable(Large);

    private static long Scan(bool large)
    {
        (InMemoryDataset dataset, QuadDelta delta) = large ? LargeCase : SmallCase;
        QuadOverlay overlay = new(dataset, delta);
        long before = GC.GetAllocatedBytesForCurrentThread();

        using (IQuadCursor cursor = overlay.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                sink += (long)cursor.Current.Subject.Value;
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void scanning_an_overlay_allocates_nothing_per_quad()
    {
        for (int i = 0; i < 3; i++)
        {
            Scan(false);
            Scan(true);
        }

        long small = Scan(false);
        long large = Scan(true);

        Assert.Equal(0, large - small);
        Assert.True(small <= 512, string.Create(CultureInfo.InvariantCulture, $"fixed cost {small}"));
    }
}
