// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CsCheck;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Xunit;

namespace Varve.Shacl.Sparql.Tests;

/// <summary>
/// ADR 0152 as a property: Varve.Shacl's own path evaluator and
/// Varve.Sparql.Evaluation's property paths agree. For a generated path and
/// generated data, the value nodes a property shape sees at each node of the
/// graph — read off the results of <c>sh:in ()</c>, which every value node
/// fails — are the solutions of <c>SELECT DISTINCT ?o { focus path ?o }</c>,
/// at every node, but in one case excluded by name:
/// <see cref="ZeroLengthStepThroughASequenceFromAnAbsentNode"/>.
/// </summary>
public class PathDifferentialTests
{
    /// <summary>The number of generated cases each run checks, as reported in the pull request.</summary>
    internal const int Iterations = 2_000;

    private const int Nodes = 5;

    private static readonly Gen<(int S, int P, int O)[]> Edges =
        Gen.Select(Gen.Int[0, Nodes - 1], Gen.Int[0, 1], Gen.Int[0, Nodes]).Array[0, 12];

    [Fact]
    public void The_shacl_path_evaluator_agrees_with_sparql_property_paths()
    {
        Gen.Select(Gen.Int[0, 99].Array[1, 12], Edges).Sample((codes, edges) =>
        {
            int at = 0;
            (string turtle, string sparql, _, bool zeroLengthThroughSequence) = Path(codes, ref at, 0);
            InMemoryDataset data = Data(edges);

            InMemoryDataset shapes = Support.Dataset(
                "ex:S sh:targetNode ex:n0, ex:n1, ex:n2, ex:n3, ex:n4 ; sh:property [ sh:path " + turtle + " ; sh:in () ] .");
            ShapesLoad load = ShapesGraph.Load(shapes, GraphPattern.DefaultGraph);
            Assert.True(load.Succeeded, string.Join("; ", load.Problems));
            ValidationReport report = ShaclValidator.Validate(load.Shapes!, data, GraphPattern.DefaultGraph).Report!;

            for (int n = 0; n < Nodes; n++)
            {
                RdfTerm focus = Node(n);
                SortedSet<string> shacl = new(StringComparer.Ordinal);
                foreach (ShaclResult result in report.Results)
                {
                    if (result.FocusNode.Equals(focus))
                    {
                        Assert.True(shacl.Add(Show(result.Value!)), "a value node reported twice: " + result.Value);
                    }
                }

                SortedSet<string> expected = Select(data, "SELECT DISTINCT ?o WHERE { <http://example.com/ns#n" + n + "> " + sparql + " ?o }");
                string shown = $"at n{n} over {turtle} ({sparql}): SPARQL [{string.Join(", ", expected)}], SHACL [{string.Join(", ", shacl)}]";
                bool absent = !Array.Exists(edges, e => e.S == n || e.O == n);
                if (ZeroLengthStepThroughASequenceFromAnAbsentNode(absent, zeroLengthThroughSequence))
                {
                    continue;
                }

                Assert.True(expected.SetEquals(shacl), shown);
            }
        }, iter: Iterations, print: c => Data(c.Item2).Count + " quads, path codes " + string.Join(",", c.Item1));
    }

    /// <summary>
    /// The one case excluded, by the maintainer's decision of 2026-10-17 (ADR 0152): a
    /// focus node the data graph does not hold, and a path in which a sequence has a
    /// step after its first that can match the empty path. SPARQL 1.1's algebra
    /// (§18.4) evaluates that step with both ends variables, over the graph's nodes
    /// only, so it loses the focus node; SHACL's value nodes keep it. The evaluator
    /// keeps the algebra the W3C suites define, and Varve.Shacl keeps SHACL's reading.
    /// </summary>
    private static bool ZeroLengthStepThroughASequenceFromAnAbsentNode(bool focusAbsent, bool zeroLengthThroughSequence) =>
        focusAbsent && zeroLengthThroughSequence;

    /// <summary>
    /// A path read off <paramref name="codes"/>: the same path in SHACL's Turtle form and in
    /// SPARQL; whether it can match the empty path; and whether a sequence in it has a step
    /// after its first that can.
    /// </summary>
    private static (string Turtle, string Sparql, bool Nullable, bool ZeroLengthThroughSequence) Path(int[] codes, ref int at, int depth)
    {
        int code = at < codes.Length ? codes[at++] : 0;
        int kind = depth >= 3 ? code % 4 : code % 10;
        if (kind < 4)
        {
            int p = kind % 2;
            return ("ex:p" + p, "<http://example.com/ns#p" + p + ">", false, false);
        }

        (string t1, string s1, bool n1, bool z1) = Path(codes, ref at, depth + 1);
        if (kind is 5 or 6)
        {
            (string t2, string s2, bool n2, bool z2) = Path(codes, ref at, depth + 1);
            return kind == 5
                ? ("( " + t1 + " " + t2 + " )", "(" + s1 + ")/(" + s2 + ")", n1 && n2, z1 || z2 || n2)
                : ("[ sh:alternativePath ( " + t1 + " " + t2 + " ) ]", "(" + s1 + ")|(" + s2 + ")", n1 || n2, z1 || z2);
        }

        return kind switch
        {
            4 => ("[ sh:inversePath " + t1 + " ]", "^(" + s1 + ")", n1, z1),
            7 => ("[ sh:zeroOrMorePath " + t1 + " ]", "(" + s1 + ")*", true, z1),
            8 => ("[ sh:oneOrMorePath " + t1 + " ]", "(" + s1 + ")+", n1, z1),
            _ => ("[ sh:zeroOrOnePath " + t1 + " ]", "(" + s1 + ")?", true, z1),
        };
    }

    private static string Show(RdfTerm term) => term.Kind + " " + Support.Text(term);

    private static RdfTerm Node(int n) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.com/ns#n" + n.ToString(CultureInfo.InvariantCulture)));

    private static InMemoryDataset Data((int S, int P, int O)[] edges)
    {
        InMemoryDatasetBuilder builder = new();
        foreach ((int s, int p, int o) in edges)
        {
            // The sixth object is a literal: a path may reach it, and nothing goes on from it.
            builder.Add(
                Node(s),
                RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.com/ns#p" + p.ToString(CultureInfo.InvariantCulture))),
                o == Nodes ? RdfTerm.Literal("a literal"u8) : Node(o));
        }

        return builder.ToDataset();
    }

    private static SortedSet<string> Select(InMemoryDataset data, string text)
    {
        Assert.True(SparqlParser.TryParseQuery(text.AsSpan(), new SparqlParseOptions(), out Query? query, out SparqlParseError error), error.ToString());
        SortedSet<string> found = new(StringComparer.Ordinal);
        using SolutionResults solutions = (SolutionResults)new SparqlEvaluator().Evaluate(query!, data, TestContext.Current.CancellationToken);
        while (solutions.MoveNext())
        {
            Assert.True(solutions.TryGetTerm(new ColumnIndex(0), out RdfTerm? term));
            found.Add(Show(term!));
        }

        return found;
    }
}
