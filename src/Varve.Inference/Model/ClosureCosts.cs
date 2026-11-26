// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Inference.Model;

/// <summary>An amount of memory, in bytes.</summary>
/// <param name="Value">The bytes, not negative.</param>
public readonly record struct MemoryAmount(long Value);

/// <summary>
/// What a <see cref="FactBudget"/> charges each structure of a closure (ADR
/// 0195 point 6), before it is allocated. The fact
/// store's record array and table are charged at their own size; the rest
/// per entry, each figure an entry's bytes with room for the collection
/// holding it to double.
/// </summary>
/// <param name="PerFact">A fact held: its entries in the five indices, beside its record.</param>
/// <param name="PerWorkingEntry">An entry of a build's working sets while it settles: the frontier, the queue and the facts it was handed.</param>
/// <param name="PerTouched">A fact a pass changes, while the pass records it and its owner reads the record.</param>
/// <param name="PerLabel">A fact's premise sets in a cross-graph binding, and its place in their propagation.</param>
/// <param name="PerExplicit">A fact asserted, in the closure's set of what is explicit.</param>
public sealed record ClosureCosts(MemoryAmount PerFact, MemoryAmount PerWorkingEntry, MemoryAmount PerTouched, MemoryAmount PerLabel, MemoryAmount PerExplicit)
{
    /// <summary>
    /// The engine's costs under <paramref name="profile"/>, measured (#335):
    /// what a from-scratch build needs per fact beyond its records, so that a
    /// build whose budget's peak fits the heap fits it.
    /// </summary>
    public static ClosureCosts For(InferenceProfile profile) => profile switch
    {
        InferenceProfile.Rdfs => Rdfs,
        InferenceProfile.Owl2Rl => Owl2Rl,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "No such profile."),
    };

    // Measured on REC S (#335): a from-scratch build's least GC heap, bisected under
    // DOTNET_GCHeapHardLimit, less the heap before it, over its budget's peak. Measured
    // again for DRed's build when it was chosen (ADR 0196, 2026-11-19): at 100 a fact,
    // rdfs gave 0.653 per graph and 0.718 cross-graph, counting more than a build needs;
    // at 40, 0.835 and 0.809.
    private static ClosureCosts Rdfs { get; } = new(new(40), new(96), new(256), new(280), new(80));

    // DRed's build gives 0.771 per graph under owl2rl and 0.847 cross-graph at 70 a fact, kept:
    // the cross-graph build is already near the margin (ADR 0195 point 6).
    private static ClosureCosts Owl2Rl { get; } = new(new(70), new(96), new(256), new(280), new(80));

    /// <summary>
    /// A clash a closure keeps between passes (ADR 0195 point 6, amended
    /// 2026-11-25): the clash, its premises' array, its key and its entry
    /// among its check's. Measured with <see cref="PerClashPremise"/> by the
    /// benchmarks' <c>--clash-costs</c> on 2026-11-26: 873 bytes retained by a
    /// clash of three premises, one shared by every clash, and 793 by one of
    /// two premises, each its own; solved, 632 a clash and 80 a premise entry.
    /// Charged 700 and 160, which counts 1.35 and 1.29 times what those keep:
    /// a premise of its own costs a set of its own, which the solve averages.
    /// </summary>
    public MemoryAmount PerClash { get; init; } = new(700);

    /// <summary>A kept clash's entry under one of its premises, in the index that finds the clashes a fact is a premise of.</summary>
    public MemoryAmount PerClashPremise { get; init; } = new(160);

    /// <summary>The same costs, each fact charged <paramref name="bytes"/> more: what its owner keeps per fact beside the closure.</summary>
    public ClosureCosts WithPerFactBeside(MemoryAmount bytes) => this with { PerFact = new(PerFact.Value + bytes.Value) };
}
