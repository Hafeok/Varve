// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Events.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Model;
using Varve.Turtle;

namespace Varve.Events;

/// <summary>
/// ADR 0157's shapes as the runtime validator of event declarations (ADR
/// 0175): one catalogue graph's triples validated against
/// <see cref="EventDeclarationShapes.Turtle"/>, each result a
/// <see cref="DeclarationProblem"/> naming the result's focus node, so that
/// <c>event-declaration-invalid</c> keeps its wire shape. The shapes target
/// event nodes by class, so the graph may be a catalogue graph or a
/// management graph.
/// </summary>
[Contract(typeof(ShapesValidateVarvesDeclarations.ShapesAreTheRuntimeValidator), Role = "the event shapes, as the runtime validator")]
internal static class EventShapes
{
    private const string Sh = "http://www.w3.org/ns/shacl#";

    private static readonly ShapesGraph Shapes = Load();

    /// <summary>The problems of one graph's <paramref name="triples"/> against the shapes, in the report's order.</summary>
    internal static List<DeclarationProblem> Validate(IReadOnlyList<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> triples)
    {
        InMemoryDatasetBuilder data = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            data.Add(s, p, o);
        }

        List<DeclarationProblem> problems = [];
        ValidationOutcome outcome = ShaclValidator.Validate(Shapes, data.ToDataset(), GraphPattern.DefaultGraph);
        if (outcome.Report is not { } report)
        {
            foreach (ShaclProblem problem in outcome.Problems)
            {
                problems.Add(new(problem.FocusNode ?? Vocabulary.EventField, Reasons.Term, "the event shapes could not validate this graph: " + problem.Message));
            }

            return problems;
        }

        foreach (ShaclResult result in report.Results)
        {
            problems.Add(new(result.FocusNode, Reason(result), Detail(result)));
        }

        return problems;
    }

    /// <summary>The code's reason for the rule a result reports: a class taken, a field's link to its type, or a term.</summary>
    private static string Reason(ShaclResult result)
    {
        string component = Encoding.UTF8.GetString(result.SourceConstraintComponent.Lexical);
        if (component == Sh + "UniqueValuesForConstraintComponent")
        {
            return Reasons.ClassTaken;
        }

        bool linkToType = result.HasPath && result.Path is null;
        bool eventField = result.Path is { } path && path.Lexical.SequenceEqual(Encoding.UTF8.GetBytes(Vocabulary.EventFieldPropertyIri));
        bool namedEvent = result.Path is { } named && named.Lexical.SequenceEqual(Encoding.UTF8.GetBytes(Vocabulary.FieldNameIri)) && component == Sh + "NotConstraintComponent";
        return linkToType || eventField || namedEvent ? Reasons.Field : Reasons.Term;
    }

    private static string Detail(ShaclResult result)
    {
        if (result.Messages.Count > 0)
        {
            return Encoding.UTF8.GetString(result.Messages[0].Lexical);
        }

        string component = Encoding.UTF8.GetString(result.SourceConstraintComponent.Lexical);
        string constraint = component.StartsWith(Sh, StringComparison.Ordinal) ? "sh:" + component[Sh.Length..] : component;
        return result.Path is { } path
            ? Local(path) + " does not conform to the declaration's shape (" + constraint + ")"
            : "the node does not conform to the declaration's shape (" + constraint + ")";
    }

    private static string Local(RdfTerm iri)
    {
        string text = Encoding.UTF8.GetString(iri.Lexical);
        return text.StartsWith(Vocabulary.Varve, StringComparison.Ordinal) ? "varve:" + text[Vocabulary.Varve.Length..] : "<" + text + ">";
    }

    private static ShapesGraph Load()
    {
        InMemoryDatasetBuilder builder = new();
        ParseResult parsed = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(EventDeclarationShapes.Turtle),
            (in QuadView quad) => builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        if (!parsed.Succeeded)
        {
            throw new InvalidDataException("The event declaration shapes do not parse: " + parsed.FirstError);
        }

        ShapesLoad load = ShapesGraph.Load(builder.ToDataset(), GraphPattern.DefaultGraph);
        return load.Shapes ?? throw new InvalidDataException("The event declaration shapes do not load: " + string.Join("; ", load.Problems));
    }
}
