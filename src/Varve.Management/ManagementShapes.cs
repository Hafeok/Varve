// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Model;
using Varve.Turtle;

namespace Varve.Management;

/// <summary>
/// The management vocabulary's shapes (ADR 0175), <c>shapes/management.ttl</c>
/// embedded in the assembly: SHACL Core only, validating one management
/// graph as the data graph. Each result is a finding by its graph, focus node,
/// path and message, which is <c>declaration-invalid</c>'s wire shape.
/// </summary>
[Contract(typeof(ShapesValidateVarvesDeclarations.ShapesAreTheRuntimeValidator), Role = "the management shapes, as the runtime validator")]
public static class ManagementShapes
{
    private const string ResourceName = "Varve.Management.shapes.management.ttl";

    /// <summary>The shapes graph, as Turtle.</summary>
    [DesignDecision(typeof(ShapesValidateVarvesDeclarations.ShapesAreTheRuntimeValidator), Scope = ExceptionScope.Boundary)]
    public static string Turtle { get; } = Read();

    private static readonly ShapesGraph Shapes = Load();

    /// <summary>The findings of <paramref name="graph"/>'s triples against the shapes, in the report's order.</summary>
    internal static List<DeclarationFinding> Validate(RdfTerm graph, List<DeclarationReader.Triple> triples)
    {
        InMemoryDatasetBuilder data = new();
        foreach (DeclarationReader.Triple triple in triples)
        {
            data.Add(triple.Subject, triple.Predicate, triple.Object);
        }

        ValidationOutcome outcome = ShaclValidator.Validate(Shapes, data.ToDataset(), GraphPattern.DefaultGraph);
        List<DeclarationFinding> findings = [];
        if (outcome.Report is not { } report)
        {
            foreach (ShaclProblem problem in outcome.Problems)
            {
                findings.Add(new DeclarationFinding(graph, problem.FocusNode, null, "the management shapes could not validate this graph: " + problem.Message));
            }

            return findings;
        }

        foreach (ShaclResult result in report.Results)
        {
            string message = result.Messages.Count > 0
                ? Encoding.UTF8.GetString(result.Messages[0].Lexical)
                : "this node does not conform to " + Encoding.UTF8.GetString(result.SourceShape.Lexical);
            findings.Add(new DeclarationFinding(graph, result.FocusNode, result.Path, message));
        }

        return findings;
    }

    private static string Read()
    {
        using Stream stream = typeof(ManagementShapes).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The assembly does not carry " + ResourceName + ".");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static ShapesGraph Load()
    {
        InMemoryDatasetBuilder builder = new();
        ParseResult parsed = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(Turtle),
            (in QuadView quad) => builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        if (!parsed.Succeeded)
        {
            throw new InvalidDataException("The management shapes do not parse: " + parsed.FirstError);
        }

        ShapesLoad load = ShapesGraph.Load(builder.ToDataset(), GraphPattern.DefaultGraph);
        return load.Shapes ?? throw new InvalidDataException("The management shapes do not load: " + string.Join("; ", load.Problems));
    }
}
