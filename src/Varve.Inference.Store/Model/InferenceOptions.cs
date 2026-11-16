// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
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
    /// What one fact of a closure costs, as the projection counts it against
    /// <see cref="ClosureBytes"/>: the fact, counting's round and count, and
    /// its links in the five indices. The benchmark measures 166 bytes a
    /// fact on REC S under OWL 2 RL, after a compacting collection; this
    /// leaves a margin over it. The figure the operator guide states is the
    /// benchmark's.
    /// </summary>
    public static ByteCount BytesPerFact { get; } = new(256);

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
}
