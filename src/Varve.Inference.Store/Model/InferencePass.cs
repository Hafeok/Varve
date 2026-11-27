// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Inference.Store.Model;

/// <summary>What a projection's pass does (ADR 0195 point 6, amended 2026-11-25).</summary>
public enum InferencePassKind
{
    /// <summary>A build at the head with nothing loaded: the first start, or one whose saved state did not load.</summary>
    ColdStart,

    /// <summary>A build over a changed TBox, or over a term a rule names that a commit allocated.</summary>
    Generation,

    /// <summary>One commit's delta applied to the closures.</summary>
    Commit,

    /// <summary>A saved state read from the local cache.</summary>
    Load,
}

/// <summary>
/// The pass a projection is running, if any: readiness names it, and explain
/// answers <c>inference-behind</c> naming it rather than wait for it (ADR
/// 0195 point 6, amended 2026-11-25).
/// </summary>
/// <param name="Kind">What the pass does.</param>
/// <param name="Position">The position the pass brings the projection to.</param>
/// <param name="Since">When it began, by the projection's clock.</param>
[Contract(typeof(TheInferenceProjection.ReadinessFromAPublishedSnapshot), Role = "the pass readiness names while it runs")]
public sealed record InferencePass(InferencePassKind Kind, Position Position, PassStart Since);
