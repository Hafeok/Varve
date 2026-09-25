// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Sparql.Tests;

/// <summary>
/// The tree's equality is what the round-trip property compares with, so its
/// three promises are pinned here: spans are outside it, lists compare
/// element-wise, and everything else is by value.
/// </summary>
public class AlgebraTests
{
    private static readonly RdfTerm P = RdfTerm.Iri("http://example/p"u8);

    private static VariablePattern Var(string name, SourceSpan span = default) => new(new Variable(name)) { Span = span };

    private static Bgp OneTriple(SourceSpan span = default) =>
        new(AlgebraList.Of(new TriplePattern(Var("s", span), new TermPattern(P) { Span = span }, Var("o", span)) { Span = span })) { Span = span };

    [Fact]
    public void spans_take_no_part_in_equality()
    {
        Bgp here = OneTriple(new SourceSpan(0, 10, 1, 1));
        Bgp there = OneTriple(new SourceSpan(50, 60, 3, 4));

        Assert.Equal(here, there);
        Assert.Equal(here.GetHashCode(), there.GetHashCode());
        Assert.True(here == there);
        Assert.NotEqual(here.Span, there.Span);
        Assert.Equal(new SourceSpan(0, 10, 1, 1), here.Span);
    }

    [Fact]
    public void lists_compare_element_wise_and_terms_by_value()
    {
        AlgebraList<TriplePattern> a = AlgebraList.Of(new TriplePattern(Var("s"), new TermPattern(RdfTerm.Iri("http://example/p"u8)), Var("o")));
        AlgebraList<TriplePattern> b = AlgebraList.Of(new TriplePattern(Var("s"), new TermPattern(RdfTerm.Iri("http://example/p"u8)), Var("o")));
        AlgebraList<TriplePattern> c = AlgebraList.Of(new TriplePattern(Var("s"), new TermPattern(RdfTerm.Iri("http://example/q"u8)), Var("o")));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.Equal(new Bgp(a), new Bgp(b));
        Assert.NotEqual(new Bgp(a), new Bgp(c));
        Assert.Equal(AlgebraList.Empty<TriplePattern>(), default);
        Assert.Equal(0, default(AlgebraList<TriplePattern>).Count);
        Assert.True(AlgebraList.From(Array.Empty<TriplePattern>()).IsEmpty);
        Assert.NotEqual(AlgebraList.Of(1, 2), AlgebraList.Of(2, 1));
    }

    [Fact]
    public void different_node_types_with_the_same_shape_are_different()
    {
        Bgp inner = OneTriple();
        Assert.NotEqual<QueryPattern>(new Distinct(inner), new Reduced(inner));
        Assert.NotEqual<QueryPattern>(new Join(inner, inner), new Union(inner, inner));
        Assert.NotEqual<PropertyPath>(new ZeroOrMorePath(new PredicatePath(P)), new OneOrMorePath(new PredicatePath(P)));
        Assert.Equal<QueryPattern>(new Join(inner, inner), new Join(OneTriple(), OneTriple()));
    }

    [Fact]
    public void a_with_expression_keeps_the_span_and_a_query_exposes_its_pattern()
    {
        Bgp bgp = OneTriple(new SourceSpan(0, 10, 1, 1));
        Bgp renamed = bgp with { Triples = AlgebraList.Of(new TriplePattern(Var("x"), new TermPattern(P), Var("y"))) };
        Assert.Equal(bgp.Span, renamed.Span);
        Assert.NotEqual(bgp, renamed);

        Query query = new SelectQuery(Prologue.Empty, null, new Project(bgp, AlgebraList.Of(new Variable("s"))));
        Assert.IsType<Project>(query.Pattern);
        Assert.Null(query.Dataset);
        Assert.Equal(Prologue.Empty, query.Prologue);
    }

    [Fact]
    public void a_rewriter_that_changes_nothing_returns_the_same_instance()
    {
        Bgp bgp = OneTriple();
        Filter filter = new(new BinaryExpression(BinaryOperator.Less, new VariableExpression(new Variable("o")), new ConstantExpression(RdfTerm.Literal("5"u8))), bgp);
        SelectQuery query = new(Prologue.Empty, null, new Slice(new Project(new OrderBy(filter, AlgebraList.Of(new OrderCondition(new VariableExpression(new Variable("o")), false))), AlgebraList.Of(new Variable("s"))), 0, 10));

        Identity identity = new();
        Assert.Same(query, identity.Rewrite(query));
        Assert.Same(filter, identity.Rewrite(filter));

        Update update = new(Prologue.Empty, AlgebraList.Of<UpdateOperation>(
            new InsertData(AlgebraList.Of(new QuadPattern(new TermPattern(P), new TermPattern(P), new TermPattern(P), null))),
            new Modify(null, default, default, null, bgp),
            new Clear(GraphTarget.All, true)));
        Assert.Same(update, identity.Rewrite(update));
    }

    [Fact]
    public void a_rewriter_rebuilds_only_the_path_to_what_it_changed()
    {
        Bgp left = OneTriple();
        Bgp right = new(AlgebraList.Of(new TriplePattern(Var("o"), new TermPattern(P), Var("z"))));
        Join join = new(left, right);
        Filter filter = new(new VariableExpression(new Variable("z")), join);

        RenameVariable rename = new("z", "w");
        Filter rewritten = Assert.IsType<Filter>(rename.Rewrite(filter));

        Assert.NotSame(filter, rewritten);
        Join rewrittenJoin = Assert.IsType<Join>(rewritten.Inner);
        Assert.Same(left, rewrittenJoin.Left);
        Assert.NotSame(right, rewrittenJoin.Right);
        Assert.Equal(new Variable("w"), Assert.IsType<VariableExpression>(rewritten.Condition).Variable);
        Assert.Equal(new Variable("w"), Assert.IsType<VariablePattern>(Assert.IsType<Bgp>(rewrittenJoin.Right).Triples[0].Object).Variable);
    }

    [Fact]
    public void update_targets_carry_their_kind()
    {
        Assert.Equal(GraphTargetKind.Graph, GraphTarget.Of(P).Kind);
        Assert.Equal(P, GraphTarget.Of(P).Graph);
        Assert.Equal(GraphTargetKind.Named, GraphTarget.Named.Kind);
        Assert.True(GraphOrDefault.Default.IsDefault);
        Assert.False(GraphOrDefault.Of(P).IsDefault);
        Assert.Throws<ArgumentNullException>(() => GraphTarget.Of(null!));
        Assert.Throws<ArgumentException>(() => new Variable(string.Empty));
    }

    private sealed class Identity : AlgebraRewriter
    {
    }

    private sealed class RenameVariable(string from, string to) : AlgebraRewriter
    {
        protected override PatternTerm RewriteVariablePattern(VariablePattern term) =>
            term.Variable.Name == from ? term with { Variable = new Variable(to) } : term;

        protected override Expression RewriteVariableExpression(VariableExpression expression) =>
            expression.Variable.Name == from ? expression with { Variable = new Variable(to) } : expression;
    }
}
