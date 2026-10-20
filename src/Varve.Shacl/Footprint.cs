// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Model;

namespace Varve.Shacl;

/// <summary>
/// What an extension's constraint reads of the data, for incremental
/// validation (ADR 0171): an <see cref="IShaclConstraint"/> may also
/// implement this. A constraint that does not is classed full: every focus
/// node of its shape is affected by every change to the data graph.
/// </summary>
[Contract(typeof(WhichShapesAreIncrementallyMaintainable.SparqlByJoinGraph), Role = "what an extension's constraint reads of the data")]
public interface IShaclFootprint
{
    /// <summary>How the constraint's affected focus nodes are found: scoped or full, never incremental for an extension.</summary>
    Maintainability Maintainability { get; }

    /// <summary>The construct that decided the class, such as a SPARQL query's unanalysable pattern.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    string Construct { get; }

    /// <summary>
    /// Adds every focus node whose validation against this constraint may
    /// change when the context's triple is asserted or retracted, searching
    /// the context's data, which holds the triple. Erring wide is sound.
    /// </summary>
    void Anchors(FootprintContext context);
}

/// <summary>A changed triple of the data graph, and the data to search for its anchors in.</summary>
[Contract(typeof(WhichShapesAreIncrementallyMaintainable.SparqlByJoinGraph), Role = "a changed triple and the data to search")]
public sealed class FootprintContext
{
    private readonly List<TermHandle> _anchors;

    internal FootprintContext(IQuadSource data, GraphPattern dataGraph, TermHandle subject, TermHandle predicate, TermHandle @object, List<TermHandle> anchors)
    {
        Data = data;
        DataGraph = dataGraph;
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        _anchors = anchors;
    }

    /// <summary>The data: the state that holds the triple — after the delta for an assertion, before it for a retraction.</summary>
    public IQuadSource Data { get; }

    /// <summary>The data graph in it.</summary>
    public GraphPattern DataGraph { get; }

    /// <summary>The triple's subject, a handle of <see cref="Data"/>.</summary>
    public TermHandle Subject { get; }

    /// <summary>The triple's predicate, a handle of <see cref="Data"/>.</summary>
    public TermHandle Predicate { get; }

    /// <summary>The triple's object, a handle of <see cref="Data"/>.</summary>
    public TermHandle Object { get; }

    /// <summary>Adds a focus node, by its handle in <see cref="Data"/>.</summary>
    public void Add(TermHandle focus)
    {
        if (focus.IsNone)
        {
            throw new ArgumentException("A focus node is a held term.", nameof(focus));
        }

        _anchors.Add(focus);
    }
}
