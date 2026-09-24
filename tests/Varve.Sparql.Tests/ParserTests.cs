// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Sparql.Tests;

/// <summary>
/// The mapped graph pattern examples of SPARQL 1.2 Query §18.3.3, each built
/// by hand and compared with the parse; the doubly-nested filter pair of
/// <c>docs/spec/sparql-algebra.md</c> §4.5; and the errors' positions.
/// </summary>
public class ParserTests
{
    private const string Prefix = "PREFIX : <http://example/>\n";

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example/" + local));

    private static VariablePattern V(string name) => new(new Variable(name));

    private static TermPattern T(string local) => new(Iri(local));

    private static TriplePattern Triple(PatternTerm s, PatternTerm p, PatternTerm o) => new(s, p, o);

    private static Bgp Bgp(params TriplePattern[] triples) => new(AlgebraList.From(triples));

    private static QueryPattern Parse(string body)
    {
        Query query = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(Prefix + "SELECT * WHERE " + body));
        Project project = Assert.IsType<Project>(query.Pattern);
        return project.Inner;
    }

    private static SparqlParseError Error(string text, SparqlVersion version = SparqlVersion.Sparql12)
    {
        Assert.False(SparqlParser.TryParseQuery(Encoding.UTF8.GetBytes(text), new SparqlParseOptions(default, version), out _, out SparqlParseError error));
        return error;
    }

    [Fact]
    public void a_single_triple_pattern_is_a_bgp()
    {
        Assert.Equal(Bgp(Triple(V("s"), V("p"), V("o"))), Parse("{ ?s ?p ?o }"));
    }

    [Fact]
    public void two_triple_patterns_join_one_bgp()
    {
        Assert.Equal(
            Bgp(Triple(V("s"), T("p1"), V("v1")), Triple(V("s"), T("p2"), V("v2"))),
            Parse("{ ?s :p1 ?v1 ; :p2 ?v2 }"));
    }

    [Fact]
    public void a_union_of_two_and_of_three_is_left_nested()
    {
        Bgp a = Bgp(Triple(V("s"), T("p1"), V("v1")));
        Bgp b = Bgp(Triple(V("s"), T("p2"), V("v2")));
        Bgp c = Bgp(Triple(V("s"), T("p3"), V("v3")));

        Assert.Equal(new Union(a, b), Parse("{ { ?s :p1 ?v1 } UNION { ?s :p2 ?v2 } }"));
        Assert.Equal(new Union(new Union(a, b), c), Parse("{ { ?s :p1 ?v1 } UNION { ?s :p2 ?v2 } UNION { ?s :p3 ?v3 } }"));
    }

    [Fact]
    public void optionals_left_join_in_order_with_true_as_the_condition()
    {
        Bgp a = Bgp(Triple(V("s"), T("p1"), V("v1")));
        Bgp b = Bgp(Triple(V("s"), T("p2"), V("v2")));
        Bgp c = Bgp(Triple(V("s"), T("p3"), V("v3")));

        Assert.Equal(new LeftJoin(a, b, null), Parse("{ ?s :p1 ?v1 OPTIONAL { ?s :p2 ?v2 } }"));
        Assert.Equal(new LeftJoin(new LeftJoin(a, b, null), c, null), Parse("{ ?s :p1 ?v1 OPTIONAL { ?s :p2 ?v2 } OPTIONAL { ?s :p3 ?v3 } }"));
        Assert.Equal(new LeftJoin(new Union(a, b), c, null), Parse("{ { ?s :p1 ?v1 } UNION { ?s :p2 ?v2 } OPTIONAL { ?s :p3 ?v3 } }"));
    }

    [Fact]
    public void a_filter_inside_an_optional_is_the_left_join_condition()
    {
        Bgp a = Bgp(Triple(V("s"), T("p1"), V("v1")));
        Bgp b = Bgp(Triple(V("s"), T("p2"), V("v2")));
        Expression less = new BinaryExpression(BinaryOperator.Less, new VariableExpression(new Variable("v1")), new ConstantExpression(RdfTerm.Literal("3"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8))));

        Assert.Equal(new LeftJoin(a, b, less), Parse("{ ?s :p1 ?v1 OPTIONAL { ?s :p2 ?v2 FILTER(?v1<3) } }"));
    }

    [Fact]
    public void a_filter_in_the_group_wraps_the_whole_group()
    {
        Bgp a = Bgp(Triple(V("s"), T("p1"), V("v1")));
        Bgp b = Bgp(Triple(V("s"), T("p2"), V("v2")));
        Expression less = new BinaryExpression(BinaryOperator.Less, new VariableExpression(new Variable("v1")), new ConstantExpression(RdfTerm.Literal("3"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8))));

        Assert.Equal(new Filter(less, new LeftJoin(a, b, null)), Parse("{ ?s :p1 ?v1 FILTER (?v1 < 3 ) OPTIONAL { ?s :p2 ?v2 } }"));
    }

    [Fact]
    public void the_doubly_nested_filter_forms_differ_as_the_draft_prefers()
    {
        Bgp a = Bgp(Triple(V("s"), T("p1"), V("v1")));
        Bgp b = Bgp(Triple(V("s"), T("p2"), V("v2")));
        Expression bound = new FunctionCall(BuiltInFunction.Bound, AlgebraList.Of<Expression>(new VariableExpression(new Variable("v1"))));

        Assert.Equal(new LeftJoin(a, b, bound), Parse("{ ?s :p1 ?v1 OPTIONAL { ?s :p2 ?v2 FILTER(BOUND(?v1)) } }"));
        Assert.Equal(new LeftJoin(a, new Filter(bound, b), null), Parse("{ ?s :p1 ?v1 OPTIONAL { { ?s :p2 ?v2 FILTER(BOUND(?v1)) } } }"));
    }

    [Fact]
    public void bind_extends_and_the_following_triples_join()
    {
        Bgp a = Bgp(Triple(V("s"), T("p"), V("v")));
        Bgp b = Bgp(Triple(V("s"), T("p1"), V("v2")));
        Expression twice = new BinaryExpression(BinaryOperator.Multiply, new ConstantExpression(RdfTerm.Literal("2"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8))), new VariableExpression(new Variable("v")));

        Assert.Equal(new Join(new Extend(a, new Variable("v2"), twice), b), Parse("{ ?s :p ?v . BIND (2*?v AS ?v2) ?s :p1 ?v2 }"));
        Assert.Equal(new Extend(a, new Variable("v2"), twice), Parse("{ ?s :p ?v . {} BIND (2*?v AS ?v2) }"));
    }

    [Fact]
    public void minus_and_a_subquery_translate_as_written()
    {
        Bgp a = Bgp(Triple(V("s"), T("p"), V("v")));
        Bgp b = Bgp(Triple(V("s"), T("p1"), V("v2")));
        Assert.Equal(new Minus(a, b), Parse("{ ?s :p ?v . MINUS {?s :p1 ?v2 } }"));

        Bgp outer = Bgp(Triple(V("s"), T("p"), V("o")));
        Bgp inner = Bgp(Triple(V("o"), V("p"), V("z")));
        Assert.Equal(
            new Join(outer, new Distinct(new Project(inner, AlgebraList.Of(new Variable("o"))))),
            Parse("{ ?s :p ?o . {SELECT DISTINCT ?o {?o ?p ?z} } }"));
    }

    [Fact]
    public void paths_and_the_simple_forms_that_become_triples()
    {
        Assert.Equal(Bgp(Triple(V("o"), T("p"), V("s"))), Parse("{ ?s ^:p ?o }"));
        Assert.Equal(
            new PathPattern(V("s"), new SequencePath(new PredicatePath(Iri("p")), new PredicatePath(Iri("q"))), V("o")),
            Parse("{ ?s :p/:q ?o }"));
        Assert.Equal(
            new Join(new Join(Bgp(Triple(V("s"), T("p"), V("a"))), new PathPattern(V("s"), new ZeroOrMorePath(new PredicatePath(Iri("q"))), V("b"))), Bgp(Triple(V("s"), T("t"), V("c")))),
            Parse("{ ?s :p ?a ; :q* ?b ; :t ?c }"));
        Assert.Equal(
            new PathPattern(V("s"), new NegatedPropertySet(AlgebraList.Of(Iri("p")), AlgebraList.Of(Iri("q"))), V("o")),
            Parse("{ ?s !(:p|^:q) ?o }"));
    }

    [Fact]
    public void select_star_projects_the_in_scope_variables_in_order_and_modifiers_nest_in_the_drafts_order()
    {
        Query query = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(Prefix + "SELECT DISTINCT * WHERE { ?b :p ?a } ORDER BY ?a LIMIT 10 OFFSET 5"));
        Slice slice = Assert.IsType<Slice>(query.Pattern);
        Assert.Equal(5, slice.Offset);
        Assert.Equal(10, slice.Limit);
        Distinct distinct = Assert.IsType<Distinct>(slice.Inner);
        Project project = Assert.IsType<Project>(distinct.Inner);
        Assert.Equal(AlgebraList.Of(new Variable("b"), new Variable("a")), project.Variables);
        Assert.IsType<OrderBy>(project.Inner);
    }

    [Fact]
    public void aggregates_stay_where_they_were_written_above_a_group()
    {
        Query query = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(Prefix + "SELECT ?s (COUNT(*) AS ?c) WHERE { ?s :p ?o } GROUP BY ?s HAVING (COUNT(*) > 1)"));
        Project project = Assert.IsType<Project>(query.Pattern);
        Extend extend = Assert.IsType<Extend>(project.Inner);
        Assert.Equal(new Variable("c"), extend.Variable);
        Assert.Equal(new AggregateExpression(AggregateFunction.Count, null, false, null, null), extend.Expression);
        Filter having = Assert.IsType<Filter>(extend.Inner);
        Group group = Assert.IsType<Group>(having.Inner);
        Assert.Equal(AlgebraList.Of(new GroupKey(new VariableExpression(new Variable("s")), new Variable("s"))), group.Keys);
        Assert.Equal(AlgebraList.Of(new Variable("s"), new Variable("c")), project.Variables);
    }

    [Fact]
    public void reified_triples_and_annotations_expand_to_reifies_triples()
    {
        Query query = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(Prefix + "SELECT * { ?person :name \"Alice\" ~ :t {| :statedBy ?authority |} }"));
        Bgp bgp = Assert.IsType<Bgp>(Assert.IsType<Project>(query.Pattern).Inner);
        RdfTerm reifies = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8);

        Assert.Equal(3, bgp.Triples.Count);
        Assert.Equal(Triple(V("person"), T("name"), new TermPattern(RdfTerm.Literal("Alice"u8))), bgp.Triples[0]);
        Assert.Equal(new TripleTermPattern(V("person"), T("name"), new TermPattern(RdfTerm.Literal("Alice"u8))), Assert.IsType<TriplePattern>(bgp.Triples[1]).Object);
        Assert.Equal(new TermPattern(reifies), bgp.Triples[1].Predicate);
        Assert.Equal(T("t"), bgp.Triples[1].Subject);
        Assert.Equal(Triple(T("t"), T("statedBy"), V("authority")), bgp.Triples[2]);

        Query fresh = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(Prefix + "SELECT * { << ?s :p ?o >> :q ?z }"));
        Bgp expanded = Assert.IsType<Bgp>(Assert.IsType<Project>(fresh.Pattern).Inner);
        BlankNodePattern reifier = Assert.IsType<BlankNodePattern>(expanded.Triples[0].Subject);
        Assert.Equal(reifier, expanded.Triples[1].Subject);
        Assert.Equal(AlgebraList.Of(new Variable("s"), new Variable("o"), new Variable("z")), Assert.IsType<Project>(fresh.Pattern).Variables);
    }

    [Fact]
    public void a_generated_label_never_collides_with_a_written_one()
    {
        Query query = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(Prefix + "SELECT * { _:b0 :p [ :q ?x ] }"));
        Bgp bgp = Assert.IsType<Bgp>(Assert.IsType<Project>(query.Pattern).Inner);
        string written = Assert.IsType<BlankNodePattern>(bgp.Triples[1].Subject).Label;
        string generated = Assert.IsType<BlankNodePattern>(bgp.Triples[0].Subject).Label;
        Assert.Equal("b0", written);
        Assert.NotEqual(written, generated);
    }

    [Fact]
    public void errors_carry_the_byte_offset_line_and_column_of_the_offending_token()
    {
        SparqlParseError error = Error("SELECT *\nWHERE { ?s ?p }");
        Assert.Equal(SparqlErrorKind.Syntax, error.Kind);
        Assert.Equal(2, error.Line);
        Assert.Equal(15, error.Column);
        Assert.Equal(23, error.Offset);
        Assert.Contains("Expected", error.Message, StringComparison.Ordinal);

        SparqlParseError end = Error("SELECT * WHERE { ?s ?p ?o ");
        Assert.Equal(SparqlErrorKind.UnexpectedEnd, end.Kind);

        SparqlParseError prefix = Error("SELECT * WHERE { ?s ex:p ?o }");
        Assert.Equal(SparqlErrorKind.UndeclaredPrefix, prefix.Kind);
        Assert.Equal(20, prefix.Offset);

        SparqlParseError scope = Error("SELECT (1 AS ?s) WHERE { ?s ?p ?o }");
        Assert.Equal(SparqlErrorKind.VariableScope, scope.Kind);

        SparqlParseError column = Error("SELECT * WHERE { ?s ?p 'é' . ?x }");
        Assert.Equal(SparqlErrorKind.Syntax, column.Kind);
        Assert.Equal(34, column.Column); // counted in bytes: é is two
    }

    [Fact]
    public void a_1_2_construct_is_refused_under_1_1_and_a_version_declaration_narrows()
    {
        string query = "SELECT * { << ?s ?p ?o >> }";
        Assert.True(SparqlParser.TryParseQuery(Encoding.UTF8.GetBytes(query), new SparqlParseOptions(default, SparqlVersion.Sparql12), out _, out _));
        Assert.Equal(SparqlErrorKind.Version, Error(query, SparqlVersion.Sparql11).Kind);
        Assert.Equal(SparqlErrorKind.Version, Error(query, SparqlVersion.Sparql12Basic).Kind);
        Assert.Equal(SparqlErrorKind.Version, Error("VERSION \"1.1\" " + query).Kind);
        Assert.Equal(SparqlErrorKind.Version, Error("VERSION \"1.2\" SELECT * { ?s ?p ?o }", SparqlVersion.Sparql12Basic).Kind);
        Assert.Equal(SparqlErrorKind.Version, Error("VERSION \"2.0\" SELECT * { ?s ?p ?o }").Kind);

        Query declared = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes("VERSION '1.2-basic' SELECT * { ?s ?p ?o }"));
        Assert.Equal(SparqlVersion.Sparql12Basic, declared.Prologue.Version);
    }

    [Fact]
    public void utf16_input_parses_to_the_same_tree_and_positions_are_in_utf8_bytes()
    {
        string text = Prefix + "SELECT ?s WHERE { ?s :p \"naïve\" }";
        Query fromChars = SparqlParser.ParseQuery(text.AsSpan());
        Query fromBytes = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(text));
        Assert.Equal(fromBytes, fromChars);
        Assert.Equal(fromBytes.Span, fromChars.Span);

        Assert.False(SparqlParser.TryParseQuery("SELECT * { ?s ?p \"\uD800\" }".AsSpan(), default, out _, out SparqlParseError error));
        Assert.Equal(SparqlErrorKind.InvalidEncoding, error.Kind);
    }
}
