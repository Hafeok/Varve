// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

namespace Varve.Derivation.Tests;

/// <summary>
/// ADR 0172's paths against a naive oracle: a path's relation computed by
/// relational algebra over small integer graphs with cycles — union,
/// composition, converse, and the closures by iteration to their least
/// fixpoint — compared with the automaton's searches.
/// </summary>
public class PathPropertyTests
{
    /// <summary>Generated cases per property, as reported in the pull request.</summary>
    internal const int Iterations = 3_000;

    /// <summary>A node no edge mentions: a path from it reaches it by a zero-length walk only.</summary>
    private const int Absent = 99;

    internal static readonly Gen<Expr> Exprs = Gen.Recursive<Expr>((depth, self) =>
        depth >= 3
            ? Gen.Int[0, 2].Select(l => (Expr)new Label(l))
            : Gen.Frequency(
                (4, Gen.Int[0, 2].Select(l => (Expr)new Label(l))),
                (1, self.Select(e => (Expr)new Inv(e))),
                (1, self.Array[2, 3].Select(es => (Expr)new Seq(es))),
                (1, self.Array[2, 3].Select(es => (Expr)new Alt(es))),
                (1, self.Select(e => (Expr)new Star(e))),
                (1, self.Select(e => (Expr)new Plus(e))),
                (1, self.Select(e => (Expr)new Opt(e)))));

    internal static readonly Gen<(int Nodes, (int S, int L, int O)[] Edges)> Graphs =
        from nodes in Gen.Int[1, 6]
        from edges in Gen.Select(Gen.Int[0, nodes - 1], Gen.Int[0, 2], Gen.Int[0, nodes - 1]).Array[0, 12]
        select (nodes, edges);

    [Fact]
    public void Evaluate_is_the_relational_oracle()
    {
        Gen.Select(Exprs, Graphs).Sample((expr, g) =>
        {
            IntGraph graph = new(g.Edges);
            PathAutomaton<int> automaton = expr.ToPath().Compile();
            HashSet<(int, int)> relation = expr.Relation(g.Nodes, g.Edges);
            foreach (int start in Starts(g.Nodes))
            {
                SortedSet<int> expected = Image(relation, start, expr.AcceptsEmpty);
                List<int> actual = [];
                automaton.Evaluate(graph, start, actual, []);
                Assert.Equal(expected, new SortedSet<int>(actual));
                Assert.Equal(expected.Count, actual.Count);
            }
        }, iter: Iterations);
    }

    [Fact]
    public void Reverse_is_the_preimage()
    {
        Gen.Select(Exprs, Graphs).Sample((expr, g) =>
        {
            IntGraph graph = new(g.Edges);
            PathAutomaton<int> automaton = expr.ToPath().Compile();
            HashSet<(int, int)> relation = expr.Relation(g.Nodes, g.Edges);
            foreach (int end in Starts(g.Nodes))
            {
                SortedSet<int> expected = [];
                foreach (int start in Starts(g.Nodes))
                {
                    if (Image(relation, start, expr.AcceptsEmpty).Contains(end))
                    {
                        expected.Add(start);
                    }
                }

                List<int> actual = [];
                automaton.Reverse(graph, end, actual, []);
                Assert.Equal(expected, new SortedSet<int>(actual));
            }
        }, iter: Iterations);
    }

    [Fact]
    public void Anchors_find_every_start_whose_image_an_edge_changes()
    {
        Gen.Select(Exprs, Graphs, Gen.Int[0, 11]).Sample((expr, g, pick) =>
        {
            if (g.Edges.Length == 0)
            {
                return;
            }

            (int S, int L, int O) edge = g.Edges[pick % g.Edges.Length];
            (int, int, int)[] without = [.. g.Edges.Where(e => e != edge)];
            PathAutomaton<int> automaton = expr.ToPath().Compile();
            List<int> anchors = [];

            // The search runs in the graph that holds the edge, as for a retraction's pre-state.
            automaton.Anchors(new IntGraph(g.Edges), edge.S, edge.L, edge.O, anchors, []);
            HashSet<(int, int)> with = expr.Relation(g.Nodes, g.Edges);
            HashSet<(int, int)> withoutRelation = expr.Relation(g.Nodes, without);
            foreach (int start in Starts(g.Nodes))
            {
                bool changed = !Image(with, start, expr.AcceptsEmpty).SetEquals(Image(withoutRelation, start, expr.AcceptsEmpty));
                Assert.True(!changed || anchors.Contains(start), $"{start}'s image changes with ({edge.S} {edge.L} {edge.O}) but it is not an anchor of {expr}");
            }
        }, iter: Iterations);
    }

    [Fact]
    public void A_path_is_bounded_exactly_when_it_has_no_closure()
    {
        Exprs.Sample(expr => Assert.Equal(!expr.HasClosure, expr.ToPath().Compile().IsBounded), iter: Iterations);
    }

    [Fact]
    public void A_zero_length_path_reaches_an_absent_start()
    {
        PathAutomaton<int> star = RegularPath.ZeroOrMore(RegularPath.Edge(0)).Compile();
        List<int> found = [];
        star.Evaluate(new IntGraph([(0, 0, 1)]), Absent, found, []);
        Assert.Equal([Absent], found);

        PathAutomaton<int> plus = RegularPath.OneOrMore(RegularPath.Edge(0)).Compile();
        found.Clear();
        plus.Evaluate(new IntGraph([(0, 0, 1)]), Absent, found, []);
        Assert.Empty(found);
    }

    private static IEnumerable<int> Starts(int nodes) => Enumerable.Range(0, nodes).Append(Absent);

    private static SortedSet<int> Image(HashSet<(int, int)> relation, int start, bool acceptsEmpty)
    {
        SortedSet<int> image = [.. relation.Where(p => p.Item1 == start).Select(p => p.Item2)];

        // The oracle's relation is over the graph's nodes; a zero-length walk also reaches a node outside it.
        if (acceptsEmpty)
        {
            image.Add(start);
        }

        return image;
    }

    internal abstract record Expr
    {
        internal abstract RegularPath<int> ToPath();

        /// <summary>The relation over the nodes 0 … n−1, the identity included where the path accepts the empty walk.</summary>
        internal abstract HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges);

        internal abstract bool AcceptsEmpty { get; }

        internal abstract bool HasClosure { get; }

        protected static HashSet<(int, int)> Identity(int nodes) => [.. Enumerable.Range(0, nodes).Select(n => (n, n))];

        protected static HashSet<(int, int)> Compose(HashSet<(int, int)> a, HashSet<(int, int)> b) =>
            [.. from x in a from y in b where x.Item2 == y.Item1 select (x.Item1, y.Item2)];

        protected static HashSet<(int, int)> Closure(HashSet<(int, int)> step)
        {
            HashSet<(int, int)> result = [.. step];
            while (true)
            {
                int before = result.Count;
                result.UnionWith(Compose(result, step));
                if (result.Count == before)
                {
                    return result;
                }
            }
        }
    }

    internal sealed record Label(int L) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.Edge(L);

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges) => [.. edges.Where(e => e.L == L).Select(e => (e.S, e.O))];

        internal override bool AcceptsEmpty => false;

        internal override bool HasClosure => false;

        public override string ToString() => "p" + L;
    }

    internal sealed record Inv(Expr Inner) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.Inverse(Inner.ToPath());

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges) => [.. Inner.Relation(nodes, edges).Select(p => (p.Item2, p.Item1))];

        internal override bool AcceptsEmpty => Inner.AcceptsEmpty;

        internal override bool HasClosure => Inner.HasClosure;

        public override string ToString() => "^(" + Inner + ")";
    }

    internal sealed record Seq(Expr[] Steps) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.Sequence(Steps.Select(s => s.ToPath()));

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges)
        {
            HashSet<(int, int)> result = Steps[0].Relation(nodes, edges);
            foreach (Expr step in Steps.Skip(1))
            {
                result = Compose(result, step.Relation(nodes, edges));
            }

            return result;
        }

        internal override bool AcceptsEmpty => Steps.All(s => s.AcceptsEmpty);

        internal override bool HasClosure => Steps.Any(s => s.HasClosure);

        public override string ToString() => "(" + string.Join("/", Steps.Select(s => s.ToString())) + ")";
    }

    internal sealed record Alt(Expr[] Alternatives) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.Alternative(Alternatives.Select(s => s.ToPath()));

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges) =>
            [.. Alternatives.SelectMany(a => a.Relation(nodes, edges))];

        internal override bool AcceptsEmpty => Alternatives.Any(s => s.AcceptsEmpty);

        internal override bool HasClosure => Alternatives.Any(s => s.HasClosure);

        public override string ToString() => "(" + string.Join("|", Alternatives.Select(s => s.ToString())) + ")";
    }

    internal sealed record Star(Expr Inner) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.ZeroOrMore(Inner.ToPath());

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges) =>
            [.. Closure(Inner.Relation(nodes, edges)).Union(Identity(nodes))];

        internal override bool AcceptsEmpty => true;

        internal override bool HasClosure => true;

        public override string ToString() => "(" + Inner + ")*";
    }

    internal sealed record Plus(Expr Inner) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.OneOrMore(Inner.ToPath());

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges) => Closure(Inner.Relation(nodes, edges));

        internal override bool AcceptsEmpty => Inner.AcceptsEmpty;

        internal override bool HasClosure => true;

        public override string ToString() => "(" + Inner + ")+";
    }

    internal sealed record Opt(Expr Inner) : Expr
    {
        internal override RegularPath<int> ToPath() => RegularPath.ZeroOrOne(Inner.ToPath());

        internal override HashSet<(int, int)> Relation(int nodes, (int S, int L, int O)[] edges) =>
            [.. Inner.Relation(nodes, edges).Union(Identity(nodes))];

        internal override bool AcceptsEmpty => true;

        internal override bool HasClosure => Inner.HasClosure;

        public override string ToString() => "(" + Inner + ")?";
    }
}

/// <summary>A graph of integers with integer labels, as the paths walk it.</summary>
internal sealed class IntGraph((int S, int L, int O)[] edges) : IPathGraph<int, int>
{
    public IEqualityComparer<int> Nodes => EqualityComparer<int>.Default;

    public void Objects(int subject, int label, List<int> into)
    {
        foreach ((int s, int l, int o) in edges)
        {
            if (s == subject && l == label && !into.Contains(o))
            {
                into.Add(o);
            }
        }
    }

    public void Subjects(int label, int obj, List<int> into)
    {
        foreach ((int s, int l, int o) in edges)
        {
            if (o == obj && l == label && !into.Contains(s))
            {
                into.Add(s);
            }
        }
    }
}
