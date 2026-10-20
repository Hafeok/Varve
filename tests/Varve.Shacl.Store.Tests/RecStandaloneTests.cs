// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Shacl.Model;
using Varve.Shacl.Sparql;
using Varve.Turtle;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>
/// REC 4.1.0's shapes (ADR 0141), standalone: part 2a's early signal for the
/// REC gate of part 2b. The three files are read as an RDF merge, each file's
/// blank nodes its own, with <c>owl:Class rdfs:subClassOf rdfs:Class</c>
/// added (the maintainer's ruling on #13).
/// </summary>
public class RecStandaloneTests
{
    private static readonly RdfTerm Sparql = RdfTerm.Iri("http://www.w3.org/ns/shacl#sparql"u8);

    private static readonly ShaclOptions Options = new() { Extensions = [SparqlConstraints.Extension()], SubClassOfInShapesGraph = true, RefuseNonDeterministicFunctions = true };

    /// <summary>
    /// Brick's three SPARQL constraints name prefixes by <c>sh:prefixes</c>
    /// that no file declares with <c>sh:declare</c>, so the shapes graph is
    /// refused as it stands; the third also uses <c>owl:</c> without naming it.
    /// Reported upstream, with no exemption.
    /// </summary>
    [Fact]
    public void BricksSparqlConstraintsDeclareNoPrefixes()
    {
        ShapesLoad load = ShapesGraph.Load(Shapes(withSparql: true), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);

        Assert.Null(load.Shapes);
        Assert.Equal(3, load.Problems.Count);
        Assert.All(load.Problems, p =>
        {
            Assert.Equal(ShaclProblemKind.ExtensionFailure, p.Kind);
            Assert.Contains("is not declared", p.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Without those three, the shapes load, none is classed full, and the
    /// head of the S portfolio has one kind of violation: its leases lack the
    /// <c>rec:leasor</c> REC requires. A full validation, so a long run.
    /// </summary>
    [Fact]
    public void TheSPortfolioUnderRecsOtherShapes()
    {
        if (Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null)
        {
            Assert.Skip("A full validation of REC S: set VARVE_LONG_PROPERTIES.");
        }

        ShapesLoad load = ShapesGraph.Load(Shapes(withSparql: false), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);
        ShapesGraph shapes = Assert.IsType<ShapesGraph>(load.Shapes);
        ShapesClassification classification = shapes.Classify();
        Assert.NotEqual(Maintainability.Full, classification.Worst);

        ValidationOutcome outcome = ShaclValidator.Validate(shapes, Head(), GraphPattern.Any, Options, TestContext.Current.CancellationToken);
        ValidationReport report = Assert.IsType<ValidationReport>(outcome.Report);
        Assert.NotEmpty(report.Results);
        Assert.All(report.Results, r =>
        {
            Assert.Equal("http://www.w3.org/ns/shacl#MinCountConstraintComponent", Encoding.UTF8.GetString(r.SourceConstraintComponent.Lexical));
            Assert.Equal("https://w3id.org/rec#leasor", Encoding.UTF8.GetString(r.Path!.Lexical));
        });
    }

    private static InMemoryDataset Shapes(bool withSparql)
    {
        InMemoryDatasetBuilder shapes = new();
        string directory = Path.Combine(Root(), "testdata", "rec", "v4.1.0");
        string[] files = ["rec.ttl", "Brick+patches.ttl", "recimports.ttl"];
        for (int f = 0; f < files.Length; f++)
        {
            string prefix = "f" + f + "_";
            RdfTerm Own(RdfTerm term) => term.Kind == RdfTermKind.BlankNode ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes(prefix + Encoding.UTF8.GetString(term.Lexical))) : term;
            ParseResult parsed = TurtleParser.Parse(File.ReadAllBytes(Path.Combine(directory, files[f])), (in QuadView q) =>
            {
                RdfTerm predicate = q.Predicate.Materialise();
                if (withSparql || !predicate.Equals(Sparql))
                {
                    shapes.Add(Own(q.Subject.Materialise()), predicate, Own(q.Object.Materialise()));
                }
            }, new TurtleOptions { Syntax = RdfSyntax.Turtle });
            Assert.True(parsed.Succeeded, files[f] + ": " + parsed.FirstError);
        }

        shapes.Add(RdfTerm.Iri("http://www.w3.org/2002/07/owl#Class"u8), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf"u8), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#Class"u8));
        return shapes.ToDataset();
    }

    /// <summary>The head of the S portfolio, every site's graph.</summary>
    private static InMemoryDataset Head()
    {
        HashSet<RecQuad> head = [];
        foreach (RecCommit commit in RecPortfolio.Generate(RecSize.S, 20261015))
        {
            foreach (RecQuad q in commit.Retracted)
            {
                head.Remove(q);
            }

            foreach (RecQuad q in commit.Asserted)
            {
                head.Add(q);
            }
        }

        StringBuilder text = new();
        foreach (RecQuad q in head)
        {
            text.Append(q.Subject).Append(' ').Append(q.Predicate).Append(' ').Append(q.Object).Append(' ').Append(q.Graph).Append(" .\n");
        }

        InMemoryDatasetBuilder data = new();
        ParseResult parsed = NQuadsParser.Parse(Encoding.UTF8.GetBytes(text.ToString()), (in QuadView q) => data.Add(q.Subject.Materialise(), q.Predicate.Materialise(), q.Object.Materialise(), q.HasGraph ? q.Graph.Materialise() : null), new ParseOptions { Syntax = RdfSyntax.NQuads });
        Assert.True(parsed.Succeeded, parsed.FirstError.ToString());
        return data.ToDataset();
    }

    private static string Root()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Varve.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("The repository root is not above " + AppContext.BaseDirectory + ".");
    }
}
