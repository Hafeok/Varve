// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Expressions;

internal enum ValueKind : byte
{
    /// <summary>An expression error (§17.2), unbound included.</summary>
    Error,

    /// <summary>A term a solution holds, by reference: not yet looked at.</summary>
    Ref,

    /// <summary>A numeric value, computed; a term only when it has to be.</summary>
    Numeric,

    /// <summary>A boolean value, computed.</summary>
    Boolean,

    /// <summary>A computed term.</summary>
    Term,
}

/// <summary>
/// What an expression evaluates to (<c>sparql-evaluation.md</c> §7.2). A
/// numeric or boolean result stays a value, and a term held by reference is
/// externalised only when an operator needs more than its handle.
/// </summary>
internal readonly struct Value
{
    private Value(ValueKind kind, TermRef reference, XsdNumeric number, bool flag, RdfTerm? term)
    {
        Kind = kind;
        Ref = reference;
        Number = number;
        Flag = flag;
        Term = term;
    }

    internal static Value Error => default;

    internal ValueKind Kind { get; }

    internal TermRef Ref { get; }

    internal XsdNumeric Number { get; }

    internal bool Flag { get; }

    internal RdfTerm? Term { get; }

    internal bool IsError => Kind == ValueKind.Error;

    internal static Value Of(TermRef reference) => reference.IsBound ? new(ValueKind.Ref, reference, default, false, null) : Error;

    /// <summary>A term by reference whose numeric value is already known: a constant, parsed once per execution.</summary>
    internal static Value Of(TermRef reference, XsdNumeric number) => new(ValueKind.Ref, reference, number, true, null);

    /// <summary>For <see cref="ValueKind.Ref"/>: whether <see cref="Number"/> holds the term's numeric value.</summary>
    internal bool HasNumber => Kind == ValueKind.Ref && Flag;

    internal static Value Of(XsdNumeric number) => new(ValueKind.Numeric, default, number, false, null);

    internal static Value Of(bool flag) => new(ValueKind.Boolean, default, default, flag, null);

    internal static Value Of(RdfTerm term) => new(ValueKind.Term, default, default, false, term);
}
