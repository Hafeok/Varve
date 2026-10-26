// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Shacl.Model;

/// <summary>
/// One (shape, focus node) pair of a validation (ADR 0171) and its results:
/// the unit a validation projection keeps a row for (ADR 0174). The shape is
/// the one that targets the focus node, not a nested shape a result names as
/// its source.
/// </summary>
[Contract(typeof(TheValidationProjection.OneProjectionPerBinding), Role = "a pair and its results, a projection row's content")]
public sealed class FocusPair
{
    internal FocusPair(RdfTerm shape, RdfTerm focusNode, ValidationReport? results)
    {
        Shape = shape;
        FocusNode = focusNode;
        Results = results;
    }

    /// <summary>The shape that targets the focus node, as the shapes graph names it.</summary>
    public RdfTerm Shape { get; }

    /// <summary>The focus node.</summary>
    public RdfTerm FocusNode { get; }

    /// <summary>The pair's results, as one report; null when the focus node is not a target of the shape.</summary>
    public ValidationReport? Results { get; }
}
