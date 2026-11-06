// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// An interned set of data subjects within one <see cref="IRowClassification"/>
/// (ADR 0231): equal ids are equal sets, and <see cref="None"/> is the empty
/// set of an unclassified node.
/// </summary>
/// <param name="Value">The id; 0 is the empty set.</param>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly record struct SubjectSetId(int Value)
{
    /// <summary>The empty set: an unclassified node.</summary>
    public static SubjectSetId None => default;

    /// <summary>Whether this is the empty set.</summary>
    public bool IsNone => Value == 0;
}

/// <summary>
/// The index of a classifier binding within one <see cref="IRowClassification"/>.
/// </summary>
/// <param name="Value">The index; <see cref="None"/> is no binding.</param>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly record struct BindingIndex(int Value)
{
    /// <summary>No binding: a graph that no classifier binding classifies.</summary>
    public static BindingIndex None => new(-1);

    /// <summary>Whether this names no binding.</summary>
    public bool IsNone => Value < 0;
}

/// <summary>
/// A classification at one state, in the handles of one quad source (ADRs
/// 0230 and 0231): which binding classifies a graph, which data subjects a
/// node belongs to, and which predicates are public links. Built by an
/// <c>IClassifier</c> or read from the classification projection; read per
/// quad by <see cref="RowScopedQuadSource"/>.
/// </summary>
/// <remarks>
/// The subjects of a triple-term handle are the union of its components'
/// (ADR 0230 point 1); an implementation answers them for every handle it
/// can meet as a subject or an object of a classified quad. A literal is
/// never classified.
/// </remarks>
[Contract(typeof(TheClassifierAndSelectorContracts.RowClassificationAtLayerOne), Role = "a classification at one state, read per quad")]
public interface IRowClassification
{
    /// <summary>The bindings the classification holds, by IRI, in the order their <see cref="BindingIndex"/> counts.</summary>
    IReadOnlyList<RdfTerm> Bindings { get; }

    /// <summary>The binding whose graph set holds <paramref name="graph"/>; <see cref="TermHandle.None"/> is the default graph.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    BindingIndex BindingOf(TermHandle graph);

    /// <summary>The data subjects <paramref name="node"/> belongs to in <paramref name="binding"/>, interned.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    SubjectSetId SubjectsOf(BindingIndex binding, TermHandle node);

    /// <summary>The subjects of <paramref name="set"/>, as handles of the source.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    ReadOnlySpan<TermHandle> Members(BindingIndex binding, SubjectSetId set);

    /// <summary>Whether <paramref name="predicate"/> is a public link of <paramref name="binding"/>; <c>rdf:type</c> always is.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    bool IsPublicLink(BindingIndex binding, TermHandle predicate);
}
