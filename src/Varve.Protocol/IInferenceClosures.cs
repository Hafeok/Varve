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
    /// The binding's implicit quads at <paramref name="position"/>: the rows
    /// held there, or, below what is held, the closure from scratch, after
    /// one bounded wait for the projection to reach the position.
    /// </summary>
    /// <exception cref="InferenceBehindException">The projection had not reached the position when the wait ended.</exception>
    /// <exception cref="InferenceUnavailableException">The binding is stopped by its memory limit at the position.</exception>
    InferredRows RowsAt(Position position);

    /// <summary>The clashes in the closure at the projection's position (ADR 0197 point 5).</summary>
    IReadOnlyList<PlacedClash> Clashes();
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

    /// <summary>The configured limit.</summary>
    public ByteCount Limit { get; }

    /// <summary>What the closure held when it stopped: a lower bound on what it needs.</summary>
    public ByteCount Reached { get; }
}
