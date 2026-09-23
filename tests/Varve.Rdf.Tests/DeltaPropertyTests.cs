// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// The delta monoid of specification §6, checked against the set formula it is
/// defined by rather than against itself.
/// </summary>
/// <remarks>
/// Handles are drawn from a deliberately small space, so that generated deltas
/// collide often: a composition property over quads that never meet proves
/// nothing.
/// </remarks>
public class DeltaPropertyTests
{
    internal const int Iterations = 2_000;

    internal static readonly Gen<Quad> SmallQuad = Gen.Select(
        Gen.ULong[1, 4],
        Gen.ULong[1, 3],
        Gen.ULong[1, 4],
        Gen.ULong[0, 2],
        (s, p, o, g) => new Quad(new TermHandle(s), new TermHandle(p), new TermHandle(o), new TermHandle(g)));

    /// <summary>A delta whose halves are disjoint: each quad is asserted, retracted, or neither.</summary>
    internal static readonly Gen<QuadDelta> Delta =
        Gen.Select(SmallQuad, Gen.Int[0, 2]).Array[0, 12].Select(entries =>
        {
            Dictionary<Quad, int> chosen = [];

            foreach ((Quad quad, int side) in entries)
            {
                chosen.TryAdd(quad, side);
            }

            Quad[] asserted = [.. chosen.Where(e => e.Value == 1).Select(e => e.Key)];
            Quad[] retracted = [.. chosen.Where(e => e.Value == 2).Select(e => e.Key)];
            return QuadDelta.Create(asserted, retracted);
        });

    private static (HashSet<Quad> A, HashSet<Quad> R) Sets(QuadDelta delta) =>
        ([.. delta.Asserted.ToArray()], [.. delta.Retracted.ToArray()]);

    private static QuadDelta Formula(QuadDelta first, QuadDelta second)
    {
        (HashSet<Quad> a1, HashSet<Quad> r1) = Sets(first);
        (HashSet<Quad> a2, HashSet<Quad> r2) = Sets(second);

        Quad[] asserted = [.. a1.Except(r2).Union(a2.Except(r1))];
        Quad[] retracted = [.. r1.Except(a2).Union(r2.Except(a1))];
        return QuadDelta.Create(asserted, retracted);
    }

    [Fact]
    public void composition_is_the_specifications_formula()
    {
        Gen.Select(Delta, Delta).Sample(
            (first, second) => first.Then(second) == Formula(first, second),
            iter: Iterations);
    }

    /// <summary>
    /// Associativity over a chain of exact deltas — consecutive changes to one
    /// evolving set, which is what a log is and all the store ever composes.
    /// </summary>
    [Fact]
    public void composition_is_associative_over_a_chain_of_states()
    {
        Gen.Select(SmallQuad.Array[0, 10], Delta, Delta, Delta).Sample(
            (start, rawA, rawB, rawC) =>
            {
                HashSet<Quad> g0 = [.. start];
                QuadDelta a = Exact(g0, rawA);
                HashSet<Quad> g1 = Apply(g0, a);
                QuadDelta b = Exact(g1, rawB);
                HashSet<Quad> g2 = Apply(g1, b);
                QuadDelta c = Exact(g2, rawC);

                return a.Then(b).Then(c) == a.Then(b.Then(c));
            },
            iter: Iterations);
    }

    /// <summary>
    /// The counterexample CsCheck found (seed <c>fIBTkyl27KJ4</c>) to the
    /// specification's claim, in §6, that deltas under <c>;</c> form a monoid.
    /// Two deltas that both retract <c>q</c> cannot follow one another in any
    /// log — the second retracts a quad the first removed, which I2 forbids —
    /// and over such inputs the formula is not associative. Recorded as a
    /// proposed specification change in milestone 4's report; this test pins
    /// the behaviour so that the proposal has a witness.
    /// </summary>
    [Fact]
    public void composition_is_not_associative_over_deltas_no_log_could_hold()
    {
        Quad q = new(new TermHandle(1), new TermHandle(1), new TermHandle(1));
        QuadDelta a = QuadDelta.Create([], [q]);
        QuadDelta b = QuadDelta.Create([], [q]);
        QuadDelta c = QuadDelta.Create([q], []);

        Assert.Equal(QuadDelta.Empty, a.Then(b).Then(c));
        Assert.Equal(a, a.Then(b.Then(c)));
    }

    [Fact]
    public void the_empty_delta_is_the_identity()
    {
        Delta.Sample(
            d => QuadDelta.Empty.Then(d) == d && d.Then(QuadDelta.Empty) == d && default(QuadDelta) == QuadDelta.Empty,
            iter: Iterations);
    }

    [Fact]
    public void a_composed_delta_never_asserts_and_retracts_one_quad()
    {
        Gen.Select(Delta, Delta).Sample(
            (first, second) =>
            {
                QuadDelta composed = first.Then(second);
                return !composed.Asserted.ToArray().Any(q => composed.Retracts(in q));
            },
            iter: Iterations);
    }

    [Fact]
    public void applying_a_composition_is_applying_each_in_turn()
    {
        // (B \ R) ∪ A, applied twice, against the composition applied once —
        // over exact deltas, which is what the composition is defined for.
        Gen.Select(SmallQuad.Array[0, 10], Delta, Delta).Sample(
            (start, rawFirst, rawSecond) =>
            {
                HashSet<Quad> baseSet = [.. start];
                QuadDelta first = Exact(baseSet, rawFirst);
                HashSet<Quad> middle = Apply(baseSet, first);
                QuadDelta second = Exact(middle, rawSecond);

                return Apply(middle, second).SetEquals(Apply(baseSet, first.Then(second)));
            },
            iter: Iterations);
    }

    [Fact]
    public void the_inverse_undoes()
    {
        Gen.Select(SmallQuad.Array[0, 10], Delta).Sample(
            (start, raw) =>
            {
                HashSet<Quad> baseSet = [.. start];
                QuadDelta delta = Exact(baseSet, raw);
                return Apply(Apply(baseSet, delta), delta.Inverse()).SetEquals(baseSet);
            },
            iter: Iterations);
    }

    [Fact]
    public void creating_a_delta_that_asserts_and_retracts_one_quad_is_refused()
    {
        Quad quad = new(new TermHandle(1), new TermHandle(2), new TermHandle(3));
        Assert.Throws<ArgumentException>(() => QuadDelta.Create([quad], [quad]));
    }

    [Fact]
    public void creating_a_delta_sorts_and_removes_duplicates()
    {
        Quad high = new(new TermHandle(9), new TermHandle(1), new TermHandle(1));
        Quad low = new(new TermHandle(2), new TermHandle(1), new TermHandle(1));

        QuadDelta delta = QuadDelta.Create([high, low, high], []);

        Assert.Equal([low, high], delta.Asserted.ToArray());
        Assert.Equal(2, delta.Count);
    }

    /// <summary>Restricts a delta to what it would actually change on a base: I2's shape.</summary>
    internal static QuadDelta Exact(HashSet<Quad> baseSet, QuadDelta delta) =>
        QuadDelta.Create(
            [.. delta.Asserted.ToArray().Where(q => !baseSet.Contains(q))],
            [.. delta.Retracted.ToArray().Where(baseSet.Contains)]);

    internal static HashSet<Quad> Apply(HashSet<Quad> baseSet, QuadDelta delta)
    {
        HashSet<Quad> result = [.. baseSet];
        result.ExceptWith(delta.Retracted.ToArray());
        result.UnionWith(delta.Asserted.ToArray());
        return result;
    }
}
