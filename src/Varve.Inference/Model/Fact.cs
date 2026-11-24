// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;

namespace Varve.Inference.Model;

/// <summary>
/// A generalised triple over a source's handles (RDF 1.1 Semantics,
/// Appendix A): any handle in any position, as the rules derive it. Only a
/// well-formed one is served (ADR 0195 point 2).
/// </summary>
/// <remarks>
/// Equality is by handle, which is exact while erasure mode is off
/// (ADR 0195 point 8).
/// </remarks>
public readonly struct Fact : IEquatable<Fact>
{
    /// <summary>A fact of three handles.</summary>
    public Fact(TermHandle subject, TermHandle predicate, TermHandle @object)
    {
        Subject = subject;
        Predicate = predicate;
        Object = @object;
    }

    /// <summary>The subject.</summary>
    public TermHandle Subject { get; }

    /// <summary>The predicate.</summary>
    public TermHandle Predicate { get; }

    /// <summary>The object.</summary>
    public TermHandle Object { get; }

    /// <inheritdoc/>
    public bool Equals(Fact other) =>
        Subject.Value == other.Subject.Value && Predicate.Value == other.Predicate.Value && Object.Value == other.Object.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Fact other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Subject.Value, Predicate.Value, Object.Value);

    /// <summary>Equal facts.</summary>
    public static bool operator ==(Fact left, Fact right) => left.Equals(right);

    /// <summary>Unequal facts.</summary>
    public static bool operator !=(Fact left, Fact right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"({Subject.Value} {Predicate.Value} {Object.Value})";
}
