// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>Which value an <see cref="InlineValue"/> carries.</summary>
public enum InlineValueKind : byte
{
    /// <summary>No value: the handle does not encode one.</summary>
    None,

    /// <summary>An <c>xsd:integer</c>, in <see cref="InlineValue.Integer"/>.</summary>
    Integer,

    /// <summary>An <c>xsd:boolean</c>, in <see cref="InlineValue.Boolean"/>.</summary>
    Boolean,
}

/// <summary>
/// The value a term handle encodes in its own bits, when it encodes one.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0050. A source that answers <see cref="IQuadSource.TryGetInlineValue"/>
/// with true has handed over the value of a literal whose lexical form is
/// canonical, without materialising the term: the store's inline ids carry
/// canonical <c>xsd:integer</c> and <c>xsd:boolean</c> (ADR 0012, 0045), and
/// only a canonical form takes one. False means "not inline", never "not a
/// number": the consumer externalises and parses as it would have anyway.
/// </para>
/// <para>
/// BCL primitives only, so that the contract at layer 1 takes no dependency on
/// <c>Varve.Xsd</c>. The evaluator converts a <see cref="long"/> to the XSD
/// type at no cost. The <see cref="Kind"/> grows additively when the inline
/// set does.
/// </para>
/// </remarks>
public readonly struct InlineValue : IEquatable<InlineValue>
{
    private readonly long _bits;

    private InlineValue(InlineValueKind kind, long bits)
    {
        Kind = kind;
        _bits = bits;
    }

    /// <summary>Which value this carries.</summary>
    public InlineValueKind Kind { get; }

    /// <summary>The integer, when <see cref="Kind"/> is <see cref="InlineValueKind.Integer"/>. Zero otherwise.</summary>
    [DesignDecision(typeof(RdfModelSurfaces.InlineValueIsAUnionOfTypedPrimitives), Scope = ExceptionScope.Boundary)]
    public long Integer => Kind == InlineValueKind.Integer ? _bits : 0;

    /// <summary>The boolean, when <see cref="Kind"/> is <see cref="InlineValueKind.Boolean"/>. False otherwise.</summary>
    public bool Boolean => Kind == InlineValueKind.Boolean && _bits != 0;

    /// <summary>No value.</summary>
    public static InlineValue None => default;

    /// <summary>An <c>xsd:integer</c> value.</summary>
    public static InlineValue FromInteger(long value) => new(InlineValueKind.Integer, value);

    /// <summary>An <c>xsd:boolean</c> value.</summary>
    [DesignDecision(typeof(BoolValues.BoolParameterIsTheValue), Scope = ExceptionScope.Boundary)]
    public static InlineValue FromBoolean(bool value) => new(InlineValueKind.Boolean, value ? 1 : 0);

    /// <inheritdoc />
    public bool Equals(InlineValue other) => Kind == other.Kind && _bits == other._bits;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InlineValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, _bits);

    /// <summary>Same kind and value.</summary>
    public static bool operator ==(InlineValue left, InlineValue right) => left.Equals(right);

    /// <summary>Different kind or value.</summary>
    public static bool operator !=(InlineValue left, InlineValue right) => !left.Equals(right);
}
