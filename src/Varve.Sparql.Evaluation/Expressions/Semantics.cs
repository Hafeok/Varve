// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Expressions;

/// <summary>
/// The value semantics of SPARQL 1.1 §17.2 and §17.3: how a value becomes a
/// term or a number, effective boolean value, value equality and
/// <c>RDFterm-equal</c>, the operator order, and the order of <c>ORDER BY</c>
/// (<c>sparql-evaluation.md</c> §7.2–§7.5).
/// </summary>
internal static class Semantics
{
    // ------------------------------------------------------------ conversions

    /// <summary>The term a value is; null for an error.</summary>
    internal static RdfTerm? AsTerm(Exec exec, in Value value) => value.Kind switch
    {
        ValueKind.Ref => exec.Materialise(value.Ref),
        ValueKind.Numeric => Terms.Literal(value.Number),
        ValueKind.Boolean => Terms.Boolean(value.Flag),
        ValueKind.Term => value.Term,
        _ => null,
    };

    /// <summary>A value as a slot: the source's handle when it has the term, else a local term.</summary>
    internal static TermRef ToRef(Exec exec, in Value value) => value.Kind switch
    {
        ValueKind.Ref => value.Ref,
        ValueKind.Error => TermRef.Unbound,
        _ => exec.Intern(AsTerm(exec, value)!),
    };

    /// <summary>
    /// The numeric value of a value, by the cheapest route ADR 0050's arm
    /// allows: a computed number, a local term's cached parse, the inline
    /// accessor, and only then an externalisation and a parse.
    /// </summary>
    [HotPath]
    internal static bool TryNumeric(Exec exec, in Value value, out XsdNumeric number)
    {
        switch (value.Kind)
        {
            case ValueKind.Numeric:
                number = value.Number;
                return true;
            case ValueKind.Ref:
                if (value.HasNumber)
                {
                    number = value.Number;
                    return true;
                }

                if (value.Ref.IsLocal)
                {
                    return exec.Locals.TryGetNumeric(value.Ref.Raw, out number);
                }

                if (exec.Options.ValueAccess == ValueAccess.InlineAccessor
                    && exec.Source.TryGetInlineValue(new TermHandle(value.Ref.Raw), out InlineValue inline))
                {
                    number = XsdNumeric.FromInteger(new XsdInteger(inline.Integer));
                    return inline.Kind == InlineValueKind.Integer;
                }

                return Terms.TryNumeric(exec.Materialise(value.Ref), out number);
            case ValueKind.Term:
                return Terms.TryNumeric(value.Term!, out number);
            default:
                number = default;
                return false;
        }
    }

    /// <summary>Effective boolean value (§17.2.2); null is a type error.</summary>
    internal static bool? Ebv(Exec exec, in Value value)
    {
        switch (value.Kind)
        {
            case ValueKind.Boolean:
                return value.Flag;
            case ValueKind.Numeric:
                return NumericTruth(value.Number);
            case ValueKind.Error:
                return null;
            case ValueKind.Ref when !value.Ref.IsLocal
                && exec.Options.ValueAccess == ValueAccess.InlineAccessor
                && exec.Source.TryGetInlineValue(new TermHandle(value.Ref.Raw), out InlineValue inline):
                return inline.Kind == InlineValueKind.Boolean ? inline.Boolean : inline.Integer != 0;
        }

        RdfTerm term = AsTerm(exec, value)!;
        if (term.Kind != RdfTermKind.Literal)
        {
            return null;
        }

        if (term.Datatype is null)
        {
            return term.Language.IsEmpty ? !term.Lexical.IsEmpty : null;
        }

        XsdDatatype datatype = XsdDatatypes.FromIri(term.DatatypeIri);
        if (datatype == XsdDatatype.Boolean)
        {
            return XsdBoolean.TryParse(term.Lexical, out XsdBoolean flag) ? flag.Value : null;
        }

        if (XsdDatatypes.IsNumeric(datatype))
        {
            return XsdNumeric.TryParse(term.Lexical, datatype, out XsdNumeric number) ? NumericTruth(number) : null;
        }

        return null;
    }

    private static bool NumericTruth(XsdNumeric number) =>
        number.Kind switch
        {
            XsdNumericKind.Integer => !number.AsInteger.IsZero,
            XsdNumericKind.Decimal => !number.AsDecimal.IsZero,
            _ => !double.IsNaN(number.AsDouble.Value) && number.AsDouble.Value != 0,
        };

    // ---------------------------------------------------------------- equality

    /// <summary>
    /// <c>=</c> of §17.3: value equality where an operator applies, and
    /// <c>RDFterm-equal</c> (§17.4.1.7) otherwise. Null is a type error.
    /// </summary>
    internal static bool? Equal(Exec exec, in Value left, in Value right)
    {
        if (left.IsError || right.IsError)
        {
            return null;
        }

        if (left.Kind == ValueKind.Ref && right.Kind == ValueKind.Ref && exec.TermEquals(left.Ref, right.Ref))
        {
            return true;
        }

        if (TryNumeric(exec, left, out XsdNumeric a) && TryNumeric(exec, right, out XsdNumeric b))
        {
            return XsdNumeric.Compare(a, b) == PartialOrdering.Equal;
        }

        return TermsEqual(exec, AsTerm(exec, left)!, AsTerm(exec, right)!);
    }

    internal static bool? TermsEqual(Exec exec, RdfTerm left, RdfTerm right)
    {
        if (left.Equals(right))
        {
            return true;
        }

        if (left.Kind == RdfTermKind.TripleTerm && right.Kind == RdfTermKind.TripleTerm)
        {
            bool? s = TermsEqual(exec, left.Subject!, right.Subject!);
            bool? p = TermsEqual(exec, left.Predicate!, right.Predicate!);
            bool? o = TermsEqual(exec, left.Object!, right.Object!);
            return s == false || p == false || o == false ? false
                : s is null || p is null || o is null ? null
                : true;
        }

        if (left.Kind != RdfTermKind.Literal || right.Kind != RdfTermKind.Literal)
        {
            return false;
        }

        Typed a = Typed.Of(left);
        Typed b = Typed.Of(right);

        // A language-tagged string that is not the identical term is a
        // different value from every other literal: its value is the pair of
        // string and tag. The open-world suite's open-eq-08 and its relatives
        // hold this even against an unknown or ill-typed datatype.
        if (a.Family == Family.LangString || b.Family == Family.LangString)
        {
            return false;
        }

        if (a.IsKnown && b.IsKnown)
        {
            if (a.Family != b.Family || a.Family == Family.LangString)
            {
                return false;
            }

            PartialOrdering order = Order(exec, a, b);
            return order == PartialOrdering.Indeterminate && a.Family != Family.Numeric ? null : order == PartialOrdering.Equal;
        }

        // An unknown datatype, or a lexical form outside a known one's value
        // space: the terms differ, and whether the values do cannot be said.
        return null;
    }

    /// <summary><c>sameTerm</c> (§17.4.1.8): term identity, under the source's equality for handles.</summary>
    internal static bool SameTerm(Exec exec, in Value left, in Value right)
    {
        if (left.Kind == ValueKind.Ref && right.Kind == ValueKind.Ref)
        {
            return exec.TermEquals(left.Ref, right.Ref);
        }

        return AsTerm(exec, left)!.Equals(AsTerm(exec, right));
    }

    // ------------------------------------------------------------------- order

    /// <summary>
    /// The order of <c>&lt;</c> and its relatives (§17.3): false when no
    /// operator applies, which is a type error; <c>NaN</c> is
    /// <see cref="PartialOrdering.Indeterminate"/>.
    /// </summary>
    internal static bool TryCompare(Exec exec, in Value left, in Value right, out PartialOrdering order)
    {
        order = PartialOrdering.Indeterminate;
        if (left.IsError || right.IsError)
        {
            return false;
        }

        if (TryNumeric(exec, left, out XsdNumeric a) && TryNumeric(exec, right, out XsdNumeric b))
        {
            order = XsdNumeric.Compare(a, b);
            return true;
        }

        RdfTerm l = AsTerm(exec, left)!;
        RdfTerm r = AsTerm(exec, right)!;
        if (l.Kind != RdfTermKind.Literal || r.Kind != RdfTermKind.Literal)
        {
            return false;
        }

        Typed x = Typed.Of(l);
        Typed y = Typed.Of(r);
        if (!x.IsKnown || !y.IsKnown || x.Family != y.Family || x.Family == Family.LangString)
        {
            return false;
        }

        order = Order(exec, x, y);
        return order != PartialOrdering.Indeterminate || x.Family is Family.Numeric;
    }

    private static PartialOrdering Order(Exec exec, in Typed a, in Typed b)
    {
        int tz = exec.Options.ImplicitTimezoneOffsetMinutes;
        return a.Family switch
        {
            Family.Numeric => XsdNumeric.Compare(a.Numeric, b.Numeric),
            Family.String => Sign(XsdString.CompareCodePoints(a.Lexical.Span, b.Lexical.Span)),
            Family.Boolean => Sign(a.Boolean.CompareTo(b.Boolean)),
            Family.DateTime => Sign(XsdDateTime.Compare(a.DateTime, b.DateTime, tz)),
            // ADR 0051 fixes the implicit-timezone total order for xsd:dateTime
            // and leaves the other seven-property types to the suite, which
            // decides for XSD's partial order (sparql-evaluation.md §13.1:
            // open-world date-1 and date-2).
            Family.Date => XsdDate.CompareXsd(a.Date, b.Date),
            Family.Time => XsdTime.CompareXsd(a.Time, b.Time),
            Family.GYearMonth => XsdGYearMonth.CompareXsd(a.GYearMonth, b.GYearMonth),
            Family.GYear => XsdGYear.CompareXsd(a.GYear, b.GYear),
            Family.GMonthDay => XsdGMonthDay.CompareXsd(a.GMonthDay, b.GMonthDay),
            Family.GDay => XsdGDay.CompareXsd(a.GDay, b.GDay),
            Family.GMonth => XsdGMonth.CompareXsd(a.GMonth, b.GMonth),
            Family.Duration => XsdDuration.CompareXsd(a.Duration, b.Duration),
            _ => PartialOrdering.Indeterminate,
        };
    }

    private static PartialOrdering Sign(int comparison) =>
        comparison < 0 ? PartialOrdering.Less : comparison > 0 ? PartialOrdering.Greater : PartialOrdering.Equal;

    /// <summary>
    /// The total order <c>ORDER BY</c> sorts by (§15.1, <c>sparql-evaluation.md</c>
    /// §7.5): unbound, blank nodes, IRIs, literals, triple terms; literals by
    /// <c>&lt;</c> where it orders them, and otherwise by lexical form, datatype
    /// and language, which is where <c>NaN</c> falls.
    /// </summary>
    internal static int OrderCompare(Exec exec, in Value left, in Value right)
    {
        int lr = Rank(exec, left, out RdfTerm? l);
        int rr = Rank(exec, right, out RdfTerm? r);
        if (lr != rr)
        {
            return lr.CompareTo(rr);
        }

        if (l is null || r is null)
        {
            // Both unbound, or both numbers compared by value below.
            if (lr == 0)
            {
                return 0;
            }
        }

        if (lr == 3 && TryNumeric(exec, left, out XsdNumeric a) && TryNumeric(exec, right, out XsdNumeric b))
        {
            PartialOrdering numeric = XsdNumeric.Compare(a, b);
            if (numeric is PartialOrdering.Less or PartialOrdering.Greater)
            {
                return numeric == PartialOrdering.Less ? -1 : 1;
            }
        }

        l ??= AsTerm(exec, left)!;
        r ??= AsTerm(exec, right)!;
        return CompareTerms(exec, l, r);
    }

    private static int CompareTerms(Exec exec, RdfTerm l, RdfTerm r)
    {
        switch (l.Kind)
        {
            case RdfTermKind.BlankNode:
            case RdfTermKind.Iri:
                return XsdString.CompareCodePoints(l.Lexical, r.Lexical);
            case RdfTermKind.TripleTerm:
                int s = OrderCompare(exec, Value.Of(l.Subject!), Value.Of(r.Subject!));
                if (s != 0)
                {
                    return s;
                }

                int p = OrderCompare(exec, Value.Of(l.Predicate!), Value.Of(r.Predicate!));
                return p != 0 ? p : OrderCompare(exec, Value.Of(l.Object!), Value.Of(r.Object!));
        }

        Typed x = Typed.Of(l);
        Typed y = Typed.Of(r);
        if (x.IsKnown && y.IsKnown && x.Family == y.Family && x.Family != Family.LangString)
        {
            PartialOrdering order = Order(exec, x, y);
            if (order is PartialOrdering.Less or PartialOrdering.Greater)
            {
                return order == PartialOrdering.Less ? -1 : 1;
            }
        }

        int lexical = XsdString.CompareCodePoints(l.Lexical, r.Lexical);
        if (lexical != 0)
        {
            return lexical;
        }

        int datatype = XsdString.CompareCodePoints(l.DatatypeIri, r.DatatypeIri);
        if (datatype != 0)
        {
            return datatype;
        }

        int language = XsdString.CompareCodePoints(l.Language, r.Language);
        return language != 0 ? language : ((int)l.Direction).CompareTo((int)r.Direction);
    }

    /// <summary>0 unbound or error, 1 blank node, 2 IRI, 3 literal, 4 triple term.</summary>
    private static int Rank(Exec exec, in Value value, out RdfTerm? term)
    {
        term = null;
        switch (value.Kind)
        {
            case ValueKind.Error:
                return 0;
            case ValueKind.Numeric:
            case ValueKind.Boolean:
                return 3;
        }

        term = AsTerm(exec, value)!;
        return term.Kind switch
        {
            RdfTermKind.BlankNode => 1,
            RdfTermKind.Iri => 2,
            RdfTermKind.Literal => 3,
            _ => 4,
        };
    }
}

/// <summary>The value families §17.3 and its extension order within.</summary>
internal enum Family : byte
{
    Other,
    Numeric,
    String,
    LangString,
    Boolean,
    DateTime,
    Date,
    Time,
    GYearMonth,
    GYear,
    GMonthDay,
    GDay,
    GMonth,
    Duration,
}

/// <summary>A literal's family and, when its lexical form is in the value space, its value.</summary>
internal readonly struct Typed
{
    internal Family Family { get; init; }

    /// <summary>The family is not <see cref="Family.Other"/> and the lexical form is valid for it.</summary>
    internal bool IsKnown { get; init; }

    internal ReadOnlyMemory<byte> Lexical { get; init; }

    internal XsdNumeric Numeric { get; init; }

    internal bool Boolean { get; init; }

    internal XsdDateTime DateTime { get; init; }

    internal XsdDate Date { get; init; }

    internal XsdTime Time { get; init; }

    internal XsdGYearMonth GYearMonth { get; init; }

    internal XsdGYear GYear { get; init; }

    internal XsdGMonthDay GMonthDay { get; init; }

    internal XsdGDay GDay { get; init; }

    internal XsdGMonth GMonth { get; init; }

    internal XsdDuration Duration { get; init; }

    internal static Typed Of(RdfTerm literal)
    {
        if (literal.Datatype is null)
        {
            return literal.Language.IsEmpty
                ? new Typed { Family = Family.String, IsKnown = true, Lexical = literal.Lexical.ToArray() }
                : new Typed { Family = Family.LangString, IsKnown = true };
        }

        ReadOnlySpan<byte> lexical = literal.Lexical;
        XsdDatatype datatype = XsdDatatypes.FromIri(literal.DatatypeIri);
        if (XsdDatatypes.IsNumeric(datatype))
        {
            bool ok = XsdNumeric.TryParse(lexical, datatype, out XsdNumeric n);
            return new Typed { Family = Family.Numeric, IsKnown = ok, Numeric = n };
        }

        switch (datatype)
        {
            case XsdDatatype.Boolean:
                {
                    bool ok = XsdBoolean.TryParse(lexical, out XsdBoolean b);
                    return new Typed { Family = Family.Boolean, IsKnown = ok, Boolean = b.Value };
                }

            case XsdDatatype.DateTime:
            case XsdDatatype.DateTimeStamp:
                {
                    bool ok = XsdDateTime.TryParse(lexical, out XsdDateTime v) && (datatype == XsdDatatype.DateTime || v.HasTimezone);
                    return new Typed { Family = Family.DateTime, IsKnown = ok, DateTime = v };
                }

            case XsdDatatype.Date:
                {
                    bool ok = XsdDate.TryParse(lexical, out XsdDate v);
                    return new Typed { Family = Family.Date, IsKnown = ok, Date = v };
                }

            case XsdDatatype.Time:
                {
                    bool ok = XsdTime.TryParse(lexical, out XsdTime v);
                    return new Typed { Family = Family.Time, IsKnown = ok, Time = v };
                }

            case XsdDatatype.GYearMonth:
                {
                    bool ok = XsdGYearMonth.TryParse(lexical, out XsdGYearMonth v);
                    return new Typed { Family = Family.GYearMonth, IsKnown = ok, GYearMonth = v };
                }

            case XsdDatatype.GYear:
                {
                    bool ok = XsdGYear.TryParse(lexical, out XsdGYear v);
                    return new Typed { Family = Family.GYear, IsKnown = ok, GYear = v };
                }

            case XsdDatatype.GMonthDay:
                {
                    bool ok = XsdGMonthDay.TryParse(lexical, out XsdGMonthDay v);
                    return new Typed { Family = Family.GMonthDay, IsKnown = ok, GMonthDay = v };
                }

            case XsdDatatype.GDay:
                {
                    bool ok = XsdGDay.TryParse(lexical, out XsdGDay v);
                    return new Typed { Family = Family.GDay, IsKnown = ok, GDay = v };
                }

            case XsdDatatype.GMonth:
                {
                    bool ok = XsdGMonth.TryParse(lexical, out XsdGMonth v);
                    return new Typed { Family = Family.GMonth, IsKnown = ok, GMonth = v };
                }

            case XsdDatatype.Duration:
            case XsdDatatype.DayTimeDuration:
            case XsdDatatype.YearMonthDuration:
                {
                    bool ok = datatype switch
                    {
                        XsdDatatype.DayTimeDuration => XsdDayTimeDuration.TryParse(lexical, out _),
                        XsdDatatype.YearMonthDuration => XsdYearMonthDuration.TryParse(lexical, out _),
                        _ => true,
                    };
                    ok &= XsdDuration.TryParse(lexical, out XsdDuration v);
                    return new Typed { Family = Family.Duration, IsKnown = ok, Duration = v };
                }

            case XsdDatatype.String:
                return new Typed { Family = Family.String, IsKnown = true, Lexical = literal.Lexical.ToArray() };

            default:
                return new Typed { Family = Family.Other, IsKnown = false };
        }
    }
}
