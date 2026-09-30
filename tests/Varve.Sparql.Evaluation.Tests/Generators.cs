// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CsCheck;
using Varve.Rdf;

namespace Varve.Sparql.Evaluation.Tests;

/// <summary>A generated quad over a small vocabulary; <see cref="Graph"/> -1 for the default graph.</summary>
internal readonly record struct GenQuad(int Subject, int Predicate, int Object, int Graph)
{
    internal static RdfTerm Node(int index) => index switch
    {
        < 5 => Support.Named("s" + index.ToString(CultureInfo.InvariantCulture)),
        < 9 => RdfTerm.Literal(Encoding.UTF8.GetBytes((index - 5).ToString(CultureInfo.InvariantCulture)), RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8.ToArray())),
        9 => RdfTerm.Literal("x"u8.ToArray()),
        _ => RdfTerm.BlankNode(Encoding.UTF8.GetBytes("b" + index.ToString(CultureInfo.InvariantCulture))),
    };

    internal RdfTerm S => Node(Subject % 5 == Subject ? Subject : 10 + (Subject % 2));

    internal RdfTerm P => Support.Named("p" + Predicate.ToString(CultureInfo.InvariantCulture));

    internal RdfTerm O => Node(Object);

    internal RdfTerm? G => Graph < 0 ? null : Support.Named("g" + Graph.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// Generated data and queries for the evaluator's properties
/// (<c>sparql-evaluation.md</c> §12.2): a vocabulary of five IRIs, four
/// integers, a string and two blank nodes, three predicates and two named
/// graphs, so that joins meet; and SPARQL text over it with every operator the
/// optimiser rewrites or must not cross.
/// </summary>
internal static class Generators
{
    internal static readonly Gen<GenQuad> Quad =
        Gen.Select(Gen.Int[0, 6], Gen.Int[0, 2], Gen.Frequency((3, Gen.Int[0, 4]), (2, Gen.Int[5, 9]), (1, Gen.Int[10, 11])), Gen.Frequency((3, Gen.Const(-1)), (1, Gen.Int[0, 1])), (s, p, o, g) => new GenQuad(s, p, o, g));

    internal static readonly Gen<GenQuad[]> Data = Quad.Array[10, 60];

    private static readonly string[] Variables = ["?a", "?b", "?c", "?d"];

    private static readonly Gen<string> Variable = Gen.OneOfConst(Variables);

    private static readonly Gen<string> Constant = Gen.Frequency((6, Gen.OneOfConst(":s0", ":s1", ":s2", "1", "2", "\"x\"")), (1, Gen.Const(":nowhere")));

    private static readonly Gen<string> Term = Gen.Frequency((6, Variable), (1, Constant));

    private static readonly Gen<string> Triple = Gen.Select(
        Term,
        Gen.Frequency((6, Gen.OneOfConst(":p0", ":p1", ":p2")), (1, Variable)),
        Gen.Frequency((4, Term), (1, Gen.Const("[]"))),
        (s, p, o) => s + " " + p + " " + o + " .");

    private static readonly Gen<string> Path = Gen.Select(
        Variable,
        Gen.OneOfConst(":p0+", ":p1*", ":p0/:p1", ":p0|:p2", "^:p1", "!:p0", ":p2?"),
        Term,
        (s, p, o) => s + " " + p + " " + o + " .");

    /// <summary>Expressions, pure and not: constant-foldable parts, errors, BOUND and COALESCE.</summary>
    private static readonly Gen<string> Expression = Gen.OneOf(
        Gen.Select(Variable, Gen.OneOfConst("<", ">", "=", "!=", "<="), Gen.OneOfConst("1", "2", "(1 + 1)", "\"x\"", ":s1", "(2 * 3 - 5)"), (v, op, c) => v + " " + op + " " + c),
        Gen.Select(Variable, Variable, (a, b) => a + " != " + b),
        Gen.Select(Variable, v => "isIRI(" + v + ")"),
        Gen.Select(Variable, v => "BOUND(" + v + ")"),
        Gen.Select(Variable, v => "COALESCE(" + v + ", 0) > 0"),
        Gen.Select(Variable, v => "STR(" + v + ") = STR(:s1)"),
        Gen.Select(Variable, v => "(" + v + " + 1) > 2"),
        Gen.OneOfConst("true", "(1 / 0) = 1", "STRLEN(CONCAT(\"a\", \"b\")) = 2", "xsd:integer(\"3\") > 2"),
        Gen.Select(Variable, Variable, (a, b) => "(" + a + " = 1 && " + b + " != 2)"),
        Gen.Select(Variable, Variable, (a, b) => "(" + a + " = 1 || " + b + " = 2)"));

    private static Gen<string> Group(int depth) => Gen.Int[1, 3].SelectMany(count => Element(depth).Array[count]).Select(parts => "{ " + string.Join(" ", parts) + " }");

    private static Gen<string> Element(int depth)
    {
        List<(int, Gen<string>)> choices =
        [
            (6, Triple),
            (1, Path),
            (3, Expression.Select(e => "FILTER(" + e + ")")),
            (1, Gen.Select(Expression, Gen.Int[0, 1_000_000], (e, n) => "BIND(" + (e.Contains('!') ? "(" + e + ")" : e) + " AS ?x" + n.ToString(CultureInfo.InvariantCulture) + ")")),
            (1, Gen.Select(Variable, Gen.OneOfConst(":s0", "1", "UNDEF"), Gen.OneOfConst(":s1", "2", "UNDEF"), (v, a, b) => "VALUES " + v + " { " + a + " " + b + " }")),
            (1, Gen.Const("VALUES () { () }")),
            (1, Gen.Const("FILTER EXISTS { ?a :p0 ?z }")),
        ];

        if (depth > 0)
        {
            Gen<string> inner = Group(depth - 1);
            choices.Add((2, inner.Select(g => "OPTIONAL " + g)));
            choices.Add((2, Gen.Select(inner, inner, (l, r) => l + " UNION " + r)));
            choices.Add((1, inner.Select(g => "MINUS " + g)));
            choices.Add((1, Gen.Select(Gen.OneOfConst("?g", ":g0", ":g1"), inner, (n, g) => "GRAPH " + n + " " + g)));
            choices.Add((1, inner));
            choices.Add((1, inner.Select(g => "{ SELECT ?a ?b " + g + " }")));
        }

        return Gen.Frequency([.. choices]);
    }

    /// <summary>A query: plain, DISTINCT, or grouped with an aggregate and HAVING.</summary>
    internal static readonly Gen<string> Query = Gen.Select(
        Group(2),
        Gen.Int[0, 5],
        (where, form) => "PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>\n" + form switch
        {
            0 => "SELECT DISTINCT * " + where,
            1 => "SELECT ?a (COUNT(?b) AS ?n) (SUM(?c) AS ?sum) " + where + " GROUP BY ?a HAVING (COUNT(*) > 0)",
            2 => "ASK " + where,
            _ => "SELECT * " + where,
        });

    /// <summary>A commit history: each commit a list of assertions and retractions (true: retract).</summary>
    internal static readonly Gen<(bool Retract, GenQuad Quad)[][]> History =
        Gen.Select(Gen.Frequency((3, Gen.Const(false)), (1, Gen.Const(true))), Quad).Array[2, 12].Array[1, 8];

    internal static string Show(GenQuad[] data) =>
        string.Join("\n", data.Select(q => Support.Text(q.S) + " " + Support.Text(q.P) + " " + Support.Text(q.O) + (q.G is { } g ? " " + Support.Text(g) : "") + " ."));

    internal static InMemoryDataset Load(GenQuad[] data)
    {
        InMemoryDatasetBuilder builder = new();
        foreach (GenQuad quad in data)
        {
            builder.Add(quad.S, quad.P, quad.O, quad.G);
        }

        return builder.ToDataset();
    }
}
