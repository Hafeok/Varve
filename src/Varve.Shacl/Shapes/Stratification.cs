// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Derivation;
using Varve.Derivation.Model;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Validation;

namespace Varve.Shacl.Shapes;

/// <summary>
/// ADR 0150's static check, computed by <c>Varve.Derivation</c>'s dependency
/// graph (ADR 0172): the strongly connected components, each shape told
/// whether conformance to it is a fixpoint, and a problem naming the cycle
/// for every component a negative or target edge runs inside.
/// </summary>
internal static class Stratification
{
    /// <summary>The dependency graph of the shapes: an edge per reference, labelled by its parameter, and per <c>sh:targetWhere</c>.</summary>
    internal static DependencyGraph<RdfTerm> Graph(IReadOnlyList<Shape> shapes)
    {
        DependencyGraph<RdfTerm> graph = new(shapes.Count);
        foreach (Shape shape in shapes)
        {
            foreach (Constraint constraint in shape.Constraints)
            {
                foreach (ShapeReference reference in constraint.References)
                {
                    graph.Add(shape.Index, reference.Target, reference.Polarity, reference.Parameter);
                }
            }

            foreach (int where in shape.TargetWhere)
            {
                graph.Add(shape.Index, where, Polarity.Target, Vocabulary.TargetWhere);
            }
        }

        return graph;
    }

    /// <summary>Assigns components and returns the problems; an empty list means the shapes graph is stratifiable.</summary>
    internal static List<ShaclProblem> Apply(IReadOnlyList<Shape> shapes)
    {
        Stratification<RdfTerm> strata = Graph(shapes).Stratify();
        for (int s = 0; s < shapes.Count; s++)
        {
            shapes[s].Component = strata.ComponentOf(s);
            shapes[s].Cyclic = strata.IsCyclic(s);
        }

        List<ShaclProblem> problems = [];
        foreach (RefusedCycle<RdfTerm> refused in strata.Refused)
        {
            List<RdfTerm> cycle = [shapes[refused.Edge.Source].Node];
            foreach (DependencyEdge<RdfTerm> edge in refused.Edges)
            {
                cycle.Add(edge.Label);
                cycle.Add(shapes[edge.Target].Node);
            }

            problems.Add(new ShaclProblem(
                ShaclProblemKind.UnstratifiableRecursion,
                shapes[refused.Edge.Source].Node,
                null,
                "The shapes graph is not stratifiable (ADR 0150): the cycle " + Describe(cycle) + " runs through "
                    + (refused.Edge.Polarity == Polarity.Target ? "a target" : "a negative") + " reference, "
                    + Strings.NTriples(refused.Edge.Label) + ", so it has no defined meaning (SHACL 1.2 Core §6.6.3).",
                cycle));
        }

        return problems;
    }

    private static string Describe(List<RdfTerm> cycle)
    {
        List<string> parts = cycle.ConvertAll(Strings.NTriples);
        return string.Join(" → ", parts);
    }
}
