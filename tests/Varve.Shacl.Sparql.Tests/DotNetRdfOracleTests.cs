// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsCheck;
using Varve.Rdf;
using Varve.Shacl.Model;
using Xunit;
using DnrGraph = VDS.RDF.Graph;
using DnrLiteral = VDS.RDF.ILiteralNode;
using DnrNode = VDS.RDF.INode;
using DnrShapes = VDS.RDF.Shacl.ShapesGraph;
using DnrTurtle = VDS.RDF.Parsing.TurtleParser;
using DnrUri = VDS.RDF.IUriNode;

namespace Varve.Shacl.Sparql.Tests;

/// <summary>
/// ADR 0156's differential oracle: generated SHACL Core shapes, without
/// recursion, and generated data are validated by Varve.Shacl and by
/// dotNetRDF's validator, and <c>sh:conforms</c> and the set of (focus node,
/// source shape, component) agree. A disagreement is judged against the
/// specification, never settled by matching dotNetRDF; the constructs left
/// out below are each a disagreement so judged.
/// </summary>
public class DotNetRdfOracleTests
{
    /// <summary>The number of generated cases each run checks, as reported in the pull request.</summary>
    internal const int Iterations = 1_000;

    private static readonly string[] Objects =
    [
        "ex:n0", "ex:n1", "ex:n2", "ex:n3",
        "1", "5", "\"abc\"", "\"abc\"@en", "\"x\"@fr", "2.5",
    ];

    private static readonly string[] Paths = ["ex:p0", "[ sh:inversePath ex:p0 ]", "( ex:p0 ex:p1 )", "[ sh:zeroOrMorePath ex:p1 ]", "[ sh:alternativePath ( ex:p0 ex:p1 ) ]", "ex:p1"];

    private static readonly string[] Targets =
    [
        "sh:targetNode ex:n0, ex:n1", "sh:targetNode ex:n2, ex:n3, ex:n0", "sh:targetClass ex:C0", "sh:targetClass ex:C1",
        "sh:targetSubjectsOf ex:p0", "sh:targetObjectsOf ex:p1",
    ];

    private static readonly string[] NodeConstraints =
    [
        "sh:class ex:C0", "sh:nodeKind sh:IRI", "sh:nodeKind sh:Literal", "sh:in ( ex:n0 ex:n1 )", "sh:hasValue ex:n2",
        "sh:pattern \"n[01]\"", "sh:maxLength 30",
        "sh:node ex:S{0}", "sh:not ex:S{0}", "sh:or ( ex:S{0} ex:S{1} )", "sh:and ( ex:S{0} ex:S{1} )", "sh:xone ( ex:S{0} ex:S{1} )",
    ];

    private static readonly string[] PropertyConstraints =
    [
        "sh:minCount 1", "sh:maxCount 1", "sh:class ex:C1", "sh:datatype xsd:integer", "sh:datatype xsd:string", "sh:nodeKind sh:IRI",
        "sh:nodeKind sh:BlankNodeOrLiteral", "sh:minInclusive 2", "sh:maxExclusive 5", "sh:minLength 2", "sh:maxLength 3",
        "sh:pattern \"^a\"", "sh:in ( ex:n0 \"abc\" )", "sh:hasValue ex:n1", "sh:languageIn ( \"en\" )", "sh:uniqueLang true",
        "sh:equals ex:p1", "sh:disjoint ex:p1", "sh:node ex:S{0}",

        // Left out, judged: sh:lessThan and sh:lessThanOrEquals. SHACL 1.2 Core §7.6.4 and
        // §7.6.5 compare with SPARQL's < and <=, and a pair that cannot be compared is a
        // result. SPARQL's operator mapping orders no language-tagged string, nor one against
        // a simple literal, so ("x"@fr, "x"@fr) and ("abc"@en, "abc") are results; Varve's
        // evaluator gives the same type error. dotNetRDF orders both pairs and reports
        // nothing. The suite's lessThan cases cover the components instead.
    ];

    private sealed record Case(string Shapes, string Data)
    {
        public override string ToString() => Shapes + "\n# data\n" + Data;
    }

    private static readonly Gen<string> Data =
        Gen.OneOf(
            Gen.Select(Gen.Int[0, 3], Gen.Int[0, 1], Gen.Int[0, Objects.Length - 1]).Select((s, p, o) => $"ex:n{s} ex:p{p} {Objects[o]} ."),
            Gen.Select(Gen.Int[0, 3], Gen.Int[0, 1]).Select((s, c) => $"ex:n{s} a ex:C{c} ."))
        .Array[0, 14].Select(lines => string.Join("\n", lines));

    private static Gen<string> Shape(int index, int count) =>
        Gen.Select(
            Gen.Int[0, Targets.Length - 1],
            Gen.Select(Gen.Int[0, NodeConstraints.Length - 1], Gen.Int[0, 9], Gen.Int[0, 9]).Array[0, 2],
            Gen.Select(Gen.Int[0, Paths.Length - 1], Gen.Select(Gen.Int[0, PropertyConstraints.Length - 1], Gen.Int[0, 9]).Array[1, 2]).Array[0, 2])
        .Select((target, nodes, properties) =>
        {
            StringBuilder text = new($"ex:S{index} a sh:NodeShape ; {Targets[target]}");
            HashSet<string> parameters = new(StringComparer.Ordinal);
            foreach ((int c, int a, int b) in nodes)
            {
                // Most parameters take one value per shape; a second is ill-formed, and refused.
                if (parameters.Add(Parameter(NodeConstraints[c])))
                {
                    text.Append(" ;\n    ").Append(Reference(NodeConstraints[c], index, count, a, b));
                }
            }

            for (int k = 0; k < properties.Length; k++)
            {
                text.Append(CultureInfo.InvariantCulture, $" ;\n    sh:property ex:S{index}P{k}");
            }

            text.Append(" .\n");
            for (int k = 0; k < properties.Length; k++)
            {
                (int path, (int, int)[] constraints) = properties[k];
                text.Append(CultureInfo.InvariantCulture, $"ex:S{index}P{k} a sh:PropertyShape ; sh:path {Paths[path]}");
                parameters.Clear();
                foreach ((int c, int a) in constraints)
                {
                    if (parameters.Add(Parameter(PropertyConstraints[c])))
                    {
                        text.Append(" ; ").Append(Reference(PropertyConstraints[c], index, count, a, a + 1));
                    }
                }

                text.Append(" .\n");
            }

            return text.ToString();
        });

    private static string Parameter(string constraint) => constraint[..constraint.IndexOf(' ', StringComparison.Ordinal)];

    /// <summary>A constraint naming other shapes names later ones only, so no shape is recursive; the last shape gets a class constraint instead.</summary>
    private static string Reference(string constraint, int index, int count, int a, int b)
    {
        if (!constraint.Contains('{', StringComparison.Ordinal))
        {
            return constraint;
        }

        int later = count - index - 1;
        return later == 0
            ? "sh:class ex:C1"
            : constraint.Replace("{0}", (index + 1 + (a % later)).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{1}", (index + 1 + (b % later)).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static readonly Gen<Case> Cases =
        from count in Gen.Int[1, 3]
        from shapes in Gen.Select(Shape(0, count), Shape(1, count), Shape(2, count))
        from data in Data
        select new Case(shapes.Item1 + (count > 1 ? shapes.Item2 : string.Empty) + (count > 2 ? shapes.Item3 : string.Empty), data);

    [Fact]
    public void Varve_and_dotnetrdf_agree_on_shacl_core()
    {
        Cases.Sample(c =>
        {
            ShapesLoad load = ShapesGraph.Load(Support.Dataset(c.Shapes), GraphPattern.DefaultGraph);
            Assert.True(load.Succeeded, string.Join("; ", load.Problems));
            ValidationReport varve = ShaclValidator.Validate(load.Shapes!, Support.Dataset(c.Data), GraphPattern.DefaultGraph).Report!;

            SortedSet<string> ours = new(StringComparer.Ordinal);
            foreach (ShaclResult result in varve.Results)
            {
                ours.Add(Key(Show(result.FocusNode), Show(result.SourceShape), Show(result.SourceConstraintComponent)));
            }

            DnrGraph shapes = Graph(c.Shapes), data = Graph(c.Data);
            VDS.RDF.Shacl.Validation.Report report = new DnrShapes(shapes).Validate(data);
            SortedSet<string> theirs = new(StringComparer.Ordinal);
            foreach (VDS.RDF.Shacl.Validation.Result result in report.Results)
            {
                theirs.Add(Key(Show(result.FocusNode), Show(result.SourceShape), Show(result.SourceConstraintComponent)));
            }

            Assert.True(
                ours.SetEquals(theirs) && varve.Conforms == report.Conforms,
                $"Varve conforms {varve.Conforms}, dotNetRDF {report.Conforms}\nonly Varve: {string.Join(" | ", Except(ours, theirs))}\nonly dotNetRDF: {string.Join(" | ", Except(theirs, ours))}");
        }, iter: Iterations);
    }

    private static string Key(string focus, string shape, string component) =>
        focus.Replace("http://example.com/ns#", string.Empty, StringComparison.Ordinal) + " "
        + shape.Replace("http://example.com/ns#", string.Empty, StringComparison.Ordinal) + " "
        + component.Replace("http://www.w3.org/ns/shacl#", string.Empty, StringComparison.Ordinal);

    /// <summary>One rendering for both sides: an IRI as itself, a literal as its lexical form and then its language or datatype IRI.</summary>
    private static string Show(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Literal when !term.Language.IsEmpty => Support.Text(term) + "@" + Encoding.UTF8.GetString(term.Language),
        RdfTermKind.Literal => Support.Text(term) + "^^" + Encoding.UTF8.GetString(term.DatatypeIri),
        _ => Support.Text(term),
    };

    private static string Show(DnrNode node) => node switch
    {
        DnrUri uri => uri.Uri.AbsoluteUri,
        DnrLiteral literal when !string.IsNullOrEmpty(literal.Language) => literal.Value + "@" + literal.Language,
        DnrLiteral literal => literal.Value + "^^" + (literal.DataType?.AbsoluteUri ?? "http://www.w3.org/2001/XMLSchema#string"),
        _ => node.ToString() ?? string.Empty,
    };

    private static IEnumerable<string> Except(SortedSet<string> left, SortedSet<string> right)
    {
        foreach (string item in left)
        {
            if (!right.Contains(item))
            {
                yield return item;
            }
        }
    }

    private static DnrGraph Graph(string turtle)
    {
        DnrGraph graph = new();
        new DnrTurtle().Load(graph, new StringReader(Support.Prefixes + turtle));
        return graph;
    }
}
