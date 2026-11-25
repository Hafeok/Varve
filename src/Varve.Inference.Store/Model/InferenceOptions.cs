// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Store.Log;

namespace Varve.Inference.Store.Model;

/// <summary>What a binding's projection may hold in memory (ADR 0195 points 5a and 6).</summary>
[Contract(typeof(TheInferenceProjection.ClosedRowsUnderACeiling), Role = "a projection's memory settings")]
public sealed class InferenceOptions
{
    /// <summary>
    /// What one closed row costs in the history, as the projection counts it:
    /// its graph, its fact and its interval, and the queue's slot.
    /// </summary>
    public static ByteCount BytesPerClosedRow { get; } = new(64);

    /// <summary>
    /// What the projection keeps per fact beside the closure, charged to the
    /// binding's budget with each fact (ADR 0195 point 6): an open row's entry in its graph's map, with room for
    /// the map to double. The closures' own costs are the engine's, per
    /// profile (<see cref="ClosureCosts.For"/>).
    /// </summary>
    public static MemoryAmount RowBytes { get; } = new(96);

    /// <summary>A dataset's term the projection keeps resolved, charged to the binding's budget when it is first kept: the term and its entry, with room for the map to double.</summary>
    public static MemoryAmount TermBytes { get; } = new(160);

    /// <summary>What a binding's closures are charged under <paramref name="profile"/>: the engine's costs, a row's bytes beside each fact.</summary>
    public static ClosureCosts CostsFor(InferenceProfile profile) => ClosureCosts.For(profile).WithPerFactBeside(RowBytes);

    /// <summary>
    /// The budget every binding of the process also takes from (ADR 0195
    /// point 6, as amended 2026-11-18): the heap limit less what the rest
    /// of the server is left; none for a host that sets none.
    /// </summary>
    public FactBudget? Pool { get; init; }

    /// <summary>
    /// The closed rows kept in memory, oldest positions out first
    /// (<c>Varve:Inference:HistoryBytes</c>): 32 MiB by default, as the seek
    /// directory cache (ADR 0240). Zero keeps none.
    /// </summary>
    public ByteCount HistoryBytes { get; init; } = new(32L << 20);

    /// <summary>
    /// What the binding's closures may hold together
    /// (<c>Varve:Inference:ClosureBytes</c>); past it the binding stops and
    /// answers <c>inference-unavailable</c>. The host's default is a quarter
    /// of the heap limit; this type's, for a host that sets none, 1 GiB.
    /// </summary>
    public ByteCount ClosureBytes { get; init; } = new(1L << 30);

    /// <summary>
    /// How long explain waits for a pass to let go of the live closures before
    /// it answers <c>inference-behind</c> naming the pass
    /// (<c>Varve:Inference:ExplainWait</c>): 2 s by default, a read's bounded
    /// wait (ADR 0195 point 6, amended 2026-11-25).
    /// </summary>
    public ExplainWait ExplainWait { get; init; } = new(TimeSpan.FromSeconds(2));

    /// <summary>The clock a pass's start is read from, which readiness reports: the system's by default.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
