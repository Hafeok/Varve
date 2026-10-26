// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Shacl.Model;

/// <summary>
/// A delta's effect on a validation (ADR 0171): the (shape, focus node) pairs
/// it affects, their results before and after, and the results it
/// introduces. Every pair it does not affect keeps its results, so the whole
/// report after is the whole report before without <see cref="Before"/> and
/// with <see cref="After"/>.
/// </summary>
public sealed class IncrementalComparison
{
    internal IncrementalComparison(
        IReadOnlyList<RdfTerm> affectedFocusNodes,
        ValidationReport before,
        ValidationReport after,
        IReadOnlyList<ShaclResult> introduced,
        int affectedPairs)
    {
        AffectedFocusNodes = affectedFocusNodes;
        Before = before;
        After = after;
        Introduced = introduced;
        AffectedPairs = affectedPairs;
    }

    /// <summary>The focus nodes of the affected pairs, distinct, in canonical N-Triples order.</summary>
    public IReadOnlyList<RdfTerm> AffectedFocusNodes { get; }

    /// <summary>How many (shape, focus node) pairs were validated, before and after together.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int AffectedPairs { get; }

    /// <summary>The results of the affected pairs that were targets before the delta, as one report restricted to them (ADR 0154, amended).</summary>
    public ValidationReport Before { get; }

    /// <summary>The results of the affected pairs that are targets after the delta, as one report restricted to them: what ADR 0173's digest is over.</summary>
    public ValidationReport After { get; }

    /// <summary>
    /// The results of <see cref="After"/> whose identity — focus node, source
    /// shape, component, path and value — is in no result of the whole report
    /// before the delta (ADR 0169's R1).
    /// </summary>
    public IReadOnlyList<ShaclResult> Introduced { get; }
}
