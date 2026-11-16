// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;

namespace Varve.Classification.Model;

/// <summary>
/// A classifier binding (ADR 0234): the annotated shapes graph of the
/// dataset that classifies, and the graphs it classifies as one data graph
/// (ADR 0230 point 1). A value the host maps a <c>varve:ClassifierBinding</c>
/// declaration to.
/// </summary>
public sealed class ClassifierBinding
{
    /// <summary>The binding <paramref name="binding"/>, reading <paramref name="shapesGraph"/> and classifying <paramref name="classifies"/>.</summary>
    /// <exception cref="ArgumentException">The binding or the shapes graph is not an IRI, or the shapes graph is among the classified graphs.</exception>
    public ClassifierBinding(RdfTerm binding, RdfTerm shapesGraph, GraphScope classifies)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(shapesGraph);
        ArgumentNullException.ThrowIfNull(classifies);

        if (binding.Kind != RdfTermKind.Iri || shapesGraph.Kind != RdfTermKind.Iri)
        {
            throw new ArgumentException("A classifier binding and its shapes graph are IRIs.", nameof(binding));
        }

        if (!classifies.IsAll && classifies.Allows(shapesGraph))
        {
            throw new ArgumentException("A classifier binding's shapes graph is not among the graphs it classifies (ADR 0232 point 6).", nameof(shapesGraph));
        }

        Binding = binding;
        ShapesGraph = shapesGraph;
        Classifies = classifies;
    }

    /// <summary>The binding's IRI.</summary>
    public RdfTerm Binding { get; }

    /// <summary>The shapes graph whose annotations classify.</summary>
    public RdfTerm ShapesGraph { get; }

    /// <summary>The graphs classified, Γ_B; the shapes graph is never among them.</summary>
    public GraphScope Classifies { get; }

    /// <summary>Whether <paramref name="graph"/> is classified: never the shapes graph, even under every graph.</summary>
    public bool ClassifiesGraph(RdfTerm? graph) => (graph is null || !graph.Equals(ShapesGraph)) && Classifies.Allows(graph);
}
