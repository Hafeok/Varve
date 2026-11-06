// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Classification.Model;
using Varve.Rdf;
using Varve.Store;

namespace Varve.Classification;

/// <summary>
/// The shape-derived classifier (ADR 0232): each binding's annotated shapes
/// graph, as of the state classified, says which nodes are data subjects and
/// which predicates link and which are public; the classification of the
/// state follows ADR 0230 point 1, from scratch.
/// </summary>
[Contract(typeof(TheShapeDerivedClassifier.AnnotationsReadAsRdf), Role = "the shape-derived classifier")]
public sealed class ShapeClassifier : IClassifier
{
    private readonly ClassifierBinding[] _bindings;

    /// <summary>The classifier of <paramref name="bindings"/>, whose graph sets do not overlap.</summary>
    public ShapeClassifier(IEnumerable<ClassifierBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = [.. bindings];
    }

    /// <summary>The bindings classified, in the order their <see cref="BindingIndex"/> counts.</summary>
    public IReadOnlyList<ClassifierBinding> Bindings => _bindings;

    /// <inheritdoc />
    public IRowClassification Classify(IQuadSource pinned, QuadDelta pending)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        IQuadSource state = pending.IsEmpty ? pinned : new QuadOverlay(pinned, pending);
        return Compose([.. _bindings.Select(binding => OnDemand(state, binding))]);
    }

    /// <summary>
    /// One binding classified node by node as asked (ADR 0231 point 2): equal
    /// to <see cref="Scratch"/> for every node, at the cost of the nodes asked
    /// about and their links.
    /// </summary>
    internal static RowClassification OnDemand(IQuadSource state, ClassifierBinding binding)
    {
        Annotations annotations = Annotations.Read(state, binding);
        Engine engine = new(state, binding, annotations);
        SetTable sets = new();
        INodeSets nodes = annotations.Problems.Count > 0 ? new FailedSets(sets) : new OnDemandSets(engine, sets, state.TermComparer);
        return new RowClassification(state, binding, nodes, sets, engine.Public);
    }

    /// <summary>Why each binding's shapes graph in <paramref name="state"/> does not classify; empty when they all do.</summary>
    public IReadOnlyList<ClassifierProblem> Problems(IQuadSource state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return [.. _bindings.SelectMany(binding => Annotations.Read(state, binding).Problems)];
    }

    /// <summary>Several single-binding classifications of one source, whose graph sets do not overlap, as one.</summary>
    public static IRowClassification Compose(IReadOnlyList<IRowClassification> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return parts.Count == 1 ? parts[0] : new ComposedClassification([.. parts]);
    }

    /// <summary>One binding classified from scratch over <paramref name="state"/>.</summary>
    internal static RowClassification Scratch(IQuadSource state, ClassifierBinding binding)
    {
        Annotations annotations = Annotations.Read(state, binding);
        Engine engine = new(state, binding, annotations);
        SetTable sets = new();

        if (annotations.Problems.Count > 0)
        {
            return new RowClassification(state, binding, new FailedSets(sets), sets, engine.Public);
        }

        Dictionary<TermHandle, SubjectSetId> nodes = new(state.TermComparer);

        foreach ((TermHandle node, TermHandle[] subjects) in engine.Scratch())
        {
            nodes[node] = sets.Intern(subjects);
        }

        return new RowClassification(state, binding, new ScratchSets(nodes), sets, engine.Public);
    }
}
