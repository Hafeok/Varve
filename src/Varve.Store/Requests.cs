// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A pre-commit validator (ADR 0017): it sees what the dataset would look like
/// and what is changing, and nothing else, and decides.
/// </summary>
/// <remarks>
/// SPARQL-free and SHACL-free by ADR 0005. A validator driven by either is a
/// layer 5 integration that implements this.
/// </remarks>
[Contract(typeof(ValidatorContractAndOverlay.ValidatorSeesOverlayAndDelta), Role = "a pre-commit validator over the proposed state and the delta")]
public interface ICommitValidator
{
    /// <summary>
    /// Decides whether a commit may land. <paramref name="proposed"/> is
    /// <c>Overlay(G_head, δ)</c>; <paramref name="delta"/> is <c>δ</c>, in
    /// <paramref name="proposed"/>'s handles.
    /// </summary>
    ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta);
}
