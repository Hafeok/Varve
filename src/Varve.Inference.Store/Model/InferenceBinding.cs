// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference.Store.Model;

/// <summary>
/// A materialising TBox binding as the projection takes it (ADRs 0194 and
/// 0197): the host maps the declaration to this value.
/// </summary>
[Contract(typeof(TheInferenceProjection.OneProjectionPerMaterialisingBinding), Role = "a materialising binding, as the projection takes it")]
public sealed class InferenceBinding
{
    /// <summary>A binding of <paramref name="tboxGraphs"/> over <paramref name="dataGraphs"/> under <paramref name="profile"/>.</summary>
    /// <param name="binding">The binding's own IRI.</param>
    /// <param name="tboxGraphs">Its TBox graphs: <c>varve:boundGraph</c>, one or more.</param>
    /// <param name="dataGraphs">Its data graphs. The TBox graphs and every graph under <c>varve:</c> are never among them, whatever the scope allows.</param>
    /// <param name="profile">Its profile: <c>varve:RDFS</c> or <c>varve:OWL2RL</c>.</param>
    /// <exception cref="ArgumentException">The binding or a TBox graph is not an IRI, or there is no TBox graph.</exception>
    public InferenceBinding(RdfTerm binding, IReadOnlyList<RdfTerm> tboxGraphs, GraphScope dataGraphs, InferenceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(tboxGraphs);
        ArgumentNullException.ThrowIfNull(dataGraphs);
        if (binding.Kind != RdfTermKind.Iri || tboxGraphs.Count == 0)
        {
            throw new ArgumentException("A binding is an IRI with one or more TBox graphs (ADR 0194).", nameof(binding));
        }

        foreach (RdfTerm graph in tboxGraphs)
        {
            if (graph.Kind != RdfTermKind.Iri)
            {
                throw new ArgumentException("A TBox graph is an IRI (ADR 0194).", nameof(tboxGraphs));
            }
        }

        Binding = binding;
        TBoxGraphs = tboxGraphs;
        DataGraphs = dataGraphs;
        Profile = profile;
    }

    /// <summary>The binding's IRI.</summary>
    public RdfTerm Binding { get; }

    /// <summary>Its TBox graphs.</summary>
    public IReadOnlyList<RdfTerm> TBoxGraphs { get; }

    /// <summary>Its data graphs, before the TBox graphs and the <c>varve:</c> graphs are taken out.</summary>
    public GraphScope DataGraphs { get; }

    /// <summary>Its profile.</summary>
    public InferenceProfile Profile { get; }

    /// <summary>
    /// <c>varve:crossGraph</c>: the closure of the data graphs' merge, placed
    /// by premise set (ADR 0190 point 6). <strong>Not built yet:</strong> the
    /// projection refuses a cross-graph binding until it is (#171).
    /// </summary>
    [DesignDecision(typeof(TheInferenceProjection.PlacedByPremiseSets), Scope = ExceptionScope.Boundary)]
    public bool CrossGraph { get; init; }
}
