// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The one seam every tier-2 surface resolves a binding's closure through
/// (ADR 0195 point 7): the views <c>varve:explicit</c> and
/// <c>varve:implicit</c>, the reserved graphs <c>varve:inferred/…</c>,
/// <c>Varve-Entailment: owl2-rl</c> where the closure serves it, and
/// explain. The host implements it over its inference projections;
/// <c>Varve.Protocol</c> knows nothing of them. A refusal of tier 2 sits
/// here and nowhere else, in the seam's order: the row-level refusal (ADR
/// 0238, which #14 adds where a closure is resolved for a caller), a binding
/// stopped by its memory limit, the refusal after erasure (milestone 9), and
/// a projection behind the read.
/// </summary>
[Contract(typeof(TheInferenceProjection.OneSeamForTierTwo), Role = "the seam every tier-2 surface reads a closure through")]
public interface IInferenceClosures
{
    /// <summary>The closures of <paramref name="dataset"/>'s materialising bindings; empty when it has none.</summary>
    IReadOnlyList<IInferenceClosure> Of(DatasetName dataset);
}

/// <summary>One materialising binding's closure, as the tier-2 surfaces read it.</summary>
[Contract(typeof(TheInferenceProjection.OneSeamForTierTwo), Role = "one binding's closure, as a surface reads it")]
public interface IInferenceClosure
{
    /// <summary>The binding's IRI.</summary>
    RdfTerm Binding { get; }

    /// <summary>The reserved graph its TBox-only conclusions are placed in: <c>varve:inferred/{enc(binding)}/tbox</c> (ADR 0198).</summary>
    RdfTerm TBoxConclusions { get; }

    /// <summary>Its TBox graphs, which every reader of the dataset reads (ADR 0190 point 3).</summary>
    IReadOnlyList<RdfTerm> TBoxGraphs { get; }

    /// <summary>Its data graphs, before its TBox graphs and the <c>varve:</c> graphs are taken out.</summary>
    GraphScope DataGraphs { get; }

    /// <summary>Its profile.</summary>
    InferenceProfile Profile { get; }

    /// <summary>
    /// The data graphs of one of its premise-set graphs (ADR 0190 point 6),
    /// the default graph as <c>varve:defaultGraph</c>; null for a graph that
    /// is not one, and always for an intra-graph binding. A caller reads a
    /// premise-set graph when it reads every one of them.
    /// </summary>
    IReadOnlyList<RdfTerm>? PremiseSetOf(RdfTerm graph);

    /// <summary>
    /// A cross-graph binding's premise-set graphs that hold rows at its
    /// projection's position (ADR 0190 point 6), so that a merge of its data
    /// graphs can read them (ADR 0197 point 6); null for an
    /// intra-graph binding, which has none.
    /// </summary>
    IReadOnlyList<RdfTerm>? PremiseSetGraphs();

    /// <summary>
    /// The binding's implicit quads at <paramref name="position"/>: the rows
    /// held there, or, below what is held, the closure from scratch, after
    /// one bounded wait for the projection to reach the position.
    /// </summary>
    /// <exception cref="InferenceBehindException">The projection had not reached the position when the wait ended.</exception>
    /// <exception cref="InferenceUnavailableException">The binding is stopped by its memory limit at the position.</exception>
    InferredRows RowsAt(Position position);

    /// <summary>The clashes in the closure at the projection's position (ADR 0197 point 5).</summary>
    IReadOnlyList<PlacedClash> Clashes();

    /// <summary>
    /// Explains <paramref name="subject"/> <paramref name="predicate"/>
    /// <paramref name="object"/> in <paramref name="graph"/> at
    /// <paramref name="position"/> (ADR 0199): one of the binding's data
    /// graphs, null for the default graph, or its TBox conclusions. After one
    /// bounded wait, as a read.
    /// </summary>
    /// <exception cref="InferenceBehindException">The projection had not reached the position when the wait ended.</exception>
    /// <exception cref="InferenceUnavailableException">The binding is stopped by its memory limit at the position.</exception>
    Explanation Explain(Position position, RdfTerm? graph, RdfTerm subject, RdfTerm predicate, RdfTerm @object);
}

/// <summary>
/// A read of a binding's closure at a position its projection has not
/// reached, after one bounded wait: answered <c>503 inference-behind</c>
/// (ADR 0195 point 6).
/// </summary>
[Contract(typeof(TheInferenceProjection.InferenceBehind), Role = "a closure read above its projection")]
public sealed class InferenceBehindException : Exception
{
    /// <summary>The projection of <paramref name="binding"/> is at <paramref name="projected"/>, below <paramref name="position"/>.</summary>
    public InferenceBehindException(RdfTerm binding, Position position, Position projected)
        : base("The closure of this binding is at position " + projected.Value.ToString(CultureInfo.InvariantCulture)
            + ", below the position read, " + position.Value.ToString(CultureInfo.InvariantCulture) + ". Retry, or read a position it has reached.")
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        Position = position;
        Projected = projected;
    }

    /// <summary>
    /// The projection of <paramref name="binding"/> has reached
    /// <paramref name="position"/> and is in a pass that holds its closures:
    /// <paramref name="message"/> names the pass (ADR 0195 point 6, amended
    /// 2026-11-25).
    /// </summary>
    public InferenceBehindException(RdfTerm binding, Position position, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        Position = position;
        Projected = position;
    }

    /// <summary>The read was refused; nothing more is known.</summary>
    public InferenceBehindException() => Binding = RdfTerm.Iri("urn:x-varve:binding"u8);

    /// <summary>The read was refused, and why.</summary>
    public InferenceBehindException(string message)
        : base(message) => Binding = RdfTerm.Iri("urn:x-varve:binding"u8);

    /// <summary>The read was refused, why, and the cause.</summary>
    public InferenceBehindException(string message, Exception innerException)
        : base(message, innerException) => Binding = RdfTerm.Iri("urn:x-varve:binding"u8);

    /// <summary>The binding.</summary>
    public RdfTerm Binding { get; }

    /// <summary>The position read.</summary>
    public Position Position { get; }

    /// <summary>The position the projection had reached.</summary>
    public Position Projected { get; }
}

/// <summary>
/// A tier-2 read by a caller whose grant restricts rows over a binding's
/// data graphs: refused, never filtered, as <c>403
/// entailment-not-available</c> with no member (ADR 0238 point 2, amended
/// 2026-11-16). Tier-1 regimes are still served.
/// </summary>
[Contract(typeof(InferenceUnderRowScopedGrants.ClosureRefusedToRowRestricted), Role = "a tier-2 read refused to a row-restricted caller")]
public sealed class EntailmentNotAvailableException : Exception
{
    /// <summary>The read was refused.</summary>
    public EntailmentNotAvailableException()
        : base("The materialised closure is not available under a row-restricted grant; the regimes rewriting serves (rdf, rdfs, d) are.")
    {
    }

    /// <summary>The read was refused, and why.</summary>
    public EntailmentNotAvailableException(string message)
        : base(message)
    {
    }

    /// <summary>The read was refused, why, and the cause.</summary>
    public EntailmentNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A read of a binding whose closure was stopped by its memory limit:
/// answered <c>503 inference-unavailable</c> at once, with no
/// <c>Retry-After</c>, its failure <c>configuration</c> (ADR 0195 point 6,
/// ADR 0242 ruling 2).
/// </summary>
[Contract(typeof(TheInferenceProjection.ClosureMemoryLimit), Role = "a closure read of a binding stopped by its memory limit")]
public sealed class InferenceUnavailableException : Exception
{
    /// <summary>The binding's closure needs more than <paramref name="limit"/>; it stopped at <paramref name="reached"/>.</summary>
    public InferenceUnavailableException(RdfTerm binding, ByteCount limit, ByteCount reached)
        : base("The closure of this binding needs more than the " + limit.Value.ToString(CultureInfo.InvariantCulture)
            + " bytes Varve:Inference:ClosureBytes allows (it stopped at " + reached.Value.ToString(CultureInfo.InvariantCulture)
            + "). Raise the setting or the instance's memory, then restart it.")
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        Limit = limit;
        Reached = reached;
    }

    /// <summary>
    /// The binding was stopped at its memory limit before it built anything:
    /// the instance has no memory for closures, <paramref name="memory"/>
    /// saying why (ADR 0195 point 6).
    /// </summary>
    public InferenceUnavailableException(RdfTerm binding, NoClosureMemory memory)
        : base(Explain(memory))
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(memory);
        Binding = binding;
        NoMemory = memory;
    }

    /// <summary>
    /// The binding's projection stopped because a pass failed with
    /// <paramref name="failure"/> (ADR 0195 point 6):
    /// not retried, so not retryable; a fix or an upgrade, then a restart.
    /// </summary>
    public InferenceUnavailableException(RdfTerm binding, Exception failure)
        : base("The closure of this binding stopped: its projection failed (" + failure?.GetType().Name + "), a defect that the same commit repeats. It is not tried again. Report it with the instance's log, and upgrade to a version that fixes it; a restart builds the binding again.", failure)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(failure);
        Binding = binding;
        Failure = failure;
    }

    /// <summary>The read was refused; nothing more is known.</summary>
    public InferenceUnavailableException() => Binding = RdfTerm.Iri("urn:x-varve:binding"u8);

    /// <summary>The read was refused, and why.</summary>
    public InferenceUnavailableException(string message)
        : base(message) => Binding = RdfTerm.Iri("urn:x-varve:binding"u8);

    /// <summary>The read was refused, why, and the cause.</summary>
    public InferenceUnavailableException(string message, Exception innerException)
        : base(message, innerException) => Binding = RdfTerm.Iri("urn:x-varve:binding"u8);

    /// <summary>The binding.</summary>
    public RdfTerm Binding { get; }

    /// <summary>Why the instance had no memory for the binding's closure, when that is why it stopped; null otherwise.</summary>
    public NoClosureMemory? NoMemory { get; }

    private static string Explain(NoClosureMemory memory) =>
        memory is null
            ? "The closure of this binding is not built: the instance has no memory for closures."
            : "The closure of this binding is not built: the instance has no memory for closures. Its heap limit, "
                + memory.HeapLimit.Value.ToString(CultureInfo.InvariantCulture) + " bytes, less the memtable's worst case, "
                + memory.MemtableWorst.Value.ToString(CultureInfo.InvariantCulture) + " bytes, and Varve:Inference:ReserveBytes, "
                + memory.Reserve.Value.ToString(CultureInfo.InvariantCulture)
                + " bytes, leaves the bindings' pool nothing (ADR 0195). Lower Varve:Inference:ReserveBytes or Varve:Store:MemtableLimit, or raise the heap limit, then restart the instance.";

    /// <summary>What a projection that failed threw; null when it stopped at its memory limit.</summary>
    public Exception? Failure { get; }

    /// <summary>The configured limit.</summary>
    public ByteCount Limit { get; }

    /// <summary>What the closure held when it stopped: a lower bound on what it needs.</summary>
    public ByteCount Reached { get; }
}

/// <summary>
/// Why an instance has no memory for closures (ADR 0195 point 6): its heap limit, less the memtable's worst case and the reserve
/// kept for queries, caches and requests, leaves the bindings' pool nothing.
/// </summary>
/// <param name="HeapLimit">The collector's heap limit.</param>
/// <param name="MemtableWorst">The memtable's worst case: four times its limit at its bytes an entry.</param>
/// <param name="Reserve"><c>Varve:Inference:ReserveBytes</c>.</param>
[Contract(typeof(TheInferenceProjection.ClosureMemoryLimit), Role = "why an instance has no memory for a binding's closure")]
public sealed record NoClosureMemory(ByteCount HeapLimit, ByteCount MemtableWorst, ByteCount Reserve);
