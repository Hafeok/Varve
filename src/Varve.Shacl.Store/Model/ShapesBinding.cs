// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Shacl.Store.Model;

/// <summary>What a shapes binding does to a commit (ADR 0169).</summary>
public enum ValidationMode
{
    /// <summary>Recorded only: nothing runs and nothing is attached.</summary>
    Off,

    /// <summary>The commit is accepted, with a digest of its affected report attached.</summary>
    Report,

    /// <summary>A commit that introduces a violation is refused; an accepted one is attached to as under <see cref="Report"/>.</summary>
    Reject,
}

/// <summary>
/// A shapes binding (ADR 0170): a shapes graph of the dataset, the graphs it
/// validates as one data graph, and a mode. A host maps a
/// <c>varve:ShapesBinding</c> declaration to this value.
/// </summary>
public sealed class ShapesBinding
{
    /// <summary>A binding of <paramref name="shapesGraph"/> over <paramref name="validates"/>.</summary>
    /// <param name="binding">The binding's own IRI.</param>
    /// <param name="shapesGraph">The shapes graph, a named graph of the same dataset: <c>varve:boundGraph</c>.</param>
    /// <param name="validates">The graphs it validates: <c>varve:validates</c>.</param>
    /// <param name="mode">Its mode: <c>varve:validationMode</c>.</param>
    public ShapesBinding(RdfTerm binding, RdfTerm shapesGraph, GraphScope validates, ValidationMode mode)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(shapesGraph);
        ArgumentNullException.ThrowIfNull(validates);
        if (binding.Kind != RdfTermKind.Iri || shapesGraph.Kind != RdfTermKind.Iri)
        {
            throw new ArgumentException("A binding and its shapes graph are IRIs (ADR 0170).", nameof(binding));
        }

        Binding = binding;
        ShapesGraph = shapesGraph;
        Validates = validates;
        Mode = mode;
    }

    /// <summary>The binding's IRI.</summary>
    public RdfTerm Binding { get; }

    /// <summary>The shapes graph.</summary>
    public RdfTerm ShapesGraph { get; }

    /// <summary>The graphs validated, as one data graph without the shapes graph and the <c>varve:</c> graphs.</summary>
    public GraphScope Validates { get; }

    /// <summary>The mode.</summary>
    public ValidationMode Mode { get; }

    /// <summary><c>varve:allowFullEvaluation</c>: a <c>reject</c> binding may have full shapes (ADR 0171), at their cost.</summary>
    [DesignDecision(typeof(TheShapesBindingVocabulary.BindingTerms), Scope = ExceptionScope.Boundary)]
    public bool AllowFullEvaluation { get; init; }

    /// <summary><c>varve:subClassOfInShapesGraph</c>: SHACL 1.2 Core §6.4's parameter.</summary>
    [DesignDecision(typeof(TheShapesBindingVocabulary.BindingTerms), Scope = ExceptionScope.Boundary)]
    public bool SubClassOfInShapesGraph { get; init; }
}
