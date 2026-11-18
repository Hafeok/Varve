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
/// 0195 point 6, as amended 2026-11-18), before it is allocated. The fact
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

    private static ClosureCosts Rdfs { get; } = new(new(128), new(96), new(256), new(160), new(80));

    private static ClosureCosts Owl2Rl { get; } = new(new(128), new(96), new(256), new(160), new(80));

    /// <summary>The same costs, each fact charged <paramref name="bytes"/> more: what its owner keeps per fact beside the closure.</summary>
    public ClosureCosts WithPerFactBeside(MemoryAmount bytes) => this with { PerFact = new(PerFact.Value + bytes.Value) };
}
