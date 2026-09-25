// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Algebra;

/// <summary>
/// The root of every algebra node: a source span that takes no part in
/// equality.
/// </summary>
/// <remarks>
/// <para>
/// The nodes are C# records, the repository's one exception to its avoidance
/// of them (ADR 0048): an algebra tree is allocated by definition, and the
/// serialiser's round-trip claim is a structural-equality claim over some
/// forty node types that generated equality cannot get wrong by omitting a
/// member.
/// </para>
/// <para>
/// A record compares every instance field, so a span declared on a node would
/// make two parses of the same text unequal. This base declares the span and
/// declares its own <see cref="Equals(AlgebraNode)"/>, which compares only the
/// runtime type; a derived record's synthesised equality calls it and then
/// compares the fields that record declares — everything but the span.
/// </para>
/// </remarks>
public abstract record AlgebraNode
{
    /// <summary>Where the node came from. Excluded from equality.</summary>
    public SourceSpan Span { get; init; }

    /// <summary>Same runtime type. The span is not compared; derived records compare their own fields.</summary>
    public virtual bool Equals(AlgebraNode? other) =>
        other is not null && EqualityContract == other.EqualityContract;

    /// <inheritdoc />
    public override int GetHashCode() => EqualityContract.GetHashCode();
}
