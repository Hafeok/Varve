// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Inference.Store.Model;

/// <summary>
/// What a projection published at the end of its last pass (ADR 0195 point
/// 6, amended 2026-11-25): read without a lock, in constant time, by
/// readiness and by a query's <c>Varve-Inconsistent</c>; never computed by
/// the reader.
/// </summary>
/// <param name="Position">The position the pass brought the projection to.</param>
/// <param name="Clashes">The clashes there (ADR 0197 point 5), kept per pass.</param>
/// <param name="PremiseSetGraphs">The premise-set graphs with open rows there (ADR 0190 point 6).</param>
/// <param name="Held">What the closures held when the pass ended.</param>
/// <param name="Peak">The most they have held at once since they were built.</param>
/// <param name="UnreadLists">The cells of asserted lists the rules do not read there (ADR 0197, amended 2026-11-24).</param>
[Contract(typeof(TheInferenceProjection.ReadinessFromAPublishedSnapshot), Role = "what a pass publishes, read without its lock")]
public sealed record InferenceSnapshot(Position Position, IReadOnlyList<PlacedClash> Clashes, IReadOnlyList<RdfTerm> PremiseSetGraphs, MemoryAmount Held, MemoryAmount Peak, IReadOnlyList<NamedUnreadList> UnreadLists)
{
    /// <summary>Before the first pass: nothing held, no clash.</summary>
    public static InferenceSnapshot None { get; } = new(new Position(0), [], [], new MemoryAmount(0), new MemoryAmount(0), []);
}
