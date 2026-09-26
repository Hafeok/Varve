// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A quad of term handles, all issued by the same quad source.
/// </summary>
/// <remarks>
/// A graph position of <see cref="TermHandle.None"/> is the default graph,
/// which is the form the specification's dictionary-encoded quads take. The
/// structural equality here compares handles bit for bit and is therefore
/// valid only inside one source; across sources, and for private terms within
/// one, use <see cref="IQuadSource.TermComparer"/>.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly struct Quad : IEquatable<Quad>
{
    /// <summary>A quad in a named graph.</summary>
    public Quad(TermHandle subject, TermHandle predicate, TermHandle @object, TermHandle graph)
    {
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        Graph = graph;
    }

    /// <summary>A quad in the default graph.</summary>
    public Quad(TermHandle subject, TermHandle predicate, TermHandle @object)
        : this(subject, predicate, @object, TermHandle.None)
    {
    }

    /// <summary>The subject.</summary>
    public TermHandle Subject { get; }

    /// <summary>The predicate.</summary>
    public TermHandle Predicate { get; }

    /// <summary>The object.</summary>
    public TermHandle Object { get; }

    /// <summary>The graph name, or <see cref="TermHandle.None"/>.</summary>
    public TermHandle Graph { get; }

    /// <summary>True when the quad is in the default graph.</summary>
    public bool IsDefaultGraph => Graph.IsNone;

    /// <inheritdoc />
    public bool Equals(Quad other) =>
        Subject.Equals(other.Subject)
        && Predicate.Equals(other.Predicate)
        && Object.Equals(other.Object)
        && Graph.Equals(other.Graph);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Quad other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Subject, Predicate, Object, Graph);

    /// <summary>Compares the handles. See the type's remarks before using it.</summary>
    public static bool operator ==(Quad left, Quad right) => left.Equals(right);

    /// <summary>Compares the handles. See the type's remarks before using it.</summary>
    public static bool operator !=(Quad left, Quad right) => !left.Equals(right);
}
