// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.Sparql.Entailment;

/// <summary>
/// The canonical literal of a value (Entailment Regimes §5.1, ADR 0193
/// point 7): Varve.Xsd's canonical lexical form, and a canonical datatype —
/// <c>xsd:integer</c> for every integer, the integer types' and an integral
/// <c>xsd:decimal</c>'s alike; <c>xsd:dateTime</c> for a
/// <c>xsd:dateTimeStamp</c>; <c>xsd:duration</c> for the two duration
/// subtypes; otherwise the literal's own. A literal of a datatype outside the
/// map, or ill-typed, is its own canonical form.
/// </summary>
/// <remarks>
/// A query rewritten under D calls <see cref="FunctionIri"/>; its evaluator
/// registers <see cref="Canonical"/> under that IRI, as the evaluator's
/// extension functions are registered (<c>sparql-evaluation.md</c> §7.10).
/// </remarks>
[Contract(typeof(RdfsRdfAndDByRewriting.DByValue), Role = "the canonical literal of a value under D")]
public static class LiteralValues
{
    /// <summary>The extension function a rewritten D query calls: <c>https://w3id.org/varve/ns#canonical</c>.</summary>
    public static RdfTerm FunctionIri { get; } = RdfTerm.Iri("https://w3id.org/varve/ns#canonical"u8);

    /// <summary>
    /// The extension function a rewritten D query calls to test a literal's
    /// value against a datatype: <c>https://w3id.org/varve/ns#hasValueType</c>.
    /// </summary>
    public static RdfTerm HasValueTypeIri { get; } = RdfTerm.Iri("https://w3id.org/varve/ns#hasValueType"u8);

    /// <summary>
    /// Whether <paramref name="literal"/>'s value is in <paramref name="datatype"/>'s
    /// value space, among the datatypes of Varve.Xsd's map (RDF 1.1 Semantics
    /// §7: a literal is an instance of every recognised datatype whose value
    /// space holds its value). An ill-typed literal has its own datatype only.
    /// </summary>
    public static bool HasValueType(RdfTerm literal, RdfTerm datatype)
    {
        ArgumentNullException.ThrowIfNull(literal);
        ArgumentNullException.ThrowIfNull(datatype);
        if (literal.Kind != RdfTermKind.Literal || datatype.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        if (datatype.Lexical.SequenceEqual(literal.DatatypeIri))
        {
            return true;
        }

        RdfTerm canonical = Canonical(literal);
        if (canonical.Datatype is null)
        {
            return false;
        }

        XsdDatatype target = XsdDatatypes.FromIri(datatype.Lexical);
        XsdDatatype own = XsdDatatypes.FromIri(canonical.DatatypeIri);
        if (target == XsdDatatype.None || own == XsdDatatype.None || !Valid(literal))
        {
            return false;
        }

        switch (own)
        {
            case XsdDatatype.Integer:
                if (!XsdInteger.TryParse(canonical.Lexical, out XsdInteger integer))
                {
                    return false;
                }

                if (target is XsdDatatype.Integer or XsdDatatype.Decimal)
                {
                    return true;
                }

                if (!XsdDatatypes.IsIntegerType(target))
                {
                    return false;
                }

                (long minimum, long maximum) = XsdInteger.Range(target);
                return integer.Value >= minimum && integer.Value <= maximum;
            case XsdDatatype.DateTime:
                return target == XsdDatatype.DateTimeStamp
                    && XsdDateTime.TryParse(canonical.Lexical, out XsdDateTime dateTime) && dateTime.HasTimezone;
            case XsdDatatype.Duration:
                if (!XsdDuration.TryParse(canonical.Lexical, out XsdDuration duration))
                {
                    return false;
                }

                return target switch
                {
                    XsdDatatype.YearMonthDuration => duration.Seconds.IsZero,
                    XsdDatatype.DayTimeDuration => duration.Months == 0,
                    _ => false,
                };
            default:
                return own == target;
        }
    }

    // A literal in its datatype's lexical space: its canonical form is computed.
    private static bool Valid(RdfTerm literal) => !ReferenceEquals(Canonical(literal), literal) || XsdDatatypes.FromIri(literal.DatatypeIri) == XsdDatatype.String;

    /// <summary>The canonical form of <paramref name="term"/>; any other term is returned as it is.</summary>
    public static RdfTerm Canonical(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (term.Kind != RdfTermKind.Literal || term.Datatype is null)
        {
            return term;
        }

        XsdDatatype datatype = XsdDatatypes.FromIri(term.DatatypeIri);
        ReadOnlySpan<byte> lexical = term.Lexical;
        Span<byte> buffer = stackalloc byte[128];
        int written = 0;
        bool ok;
        RdfTerm? canonicalType = null;
        switch (datatype)
        {
            case XsdDatatype.None:
            case XsdDatatype.String:
                return term;
            case XsdDatatype.Decimal:
                ok = XsdDecimal.TryParse(lexical, out XsdDecimal @decimal) && @decimal.TryFormat(buffer, out written);
                if (ok && Integral(buffer[..written], out int length))
                {
                    return RdfTerm.Literal(buffer[..length], Iri(XsdDatatype.Integer));
                }

                break;
            case XsdDatatype.Float:
                ok = XsdFloat.TryParse(lexical, out XsdFloat @float) && @float.TryFormat(buffer, out written);
                break;
            case XsdDatatype.Double:
                ok = XsdDouble.TryParse(lexical, out XsdDouble @double) && @double.TryFormat(buffer, out written);
                break;
            case XsdDatatype.Boolean:
                ok = XsdBoolean.TryParse(lexical, out XsdBoolean boolean) && boolean.TryFormat(buffer, out written);
                break;
            case XsdDatatype.DateTime:
            case XsdDatatype.DateTimeStamp:
                ok = XsdDateTime.TryParse(lexical, out XsdDateTime dateTime) && dateTime.TryFormat(buffer, out written)
                    && (datatype == XsdDatatype.DateTime || dateTime.HasTimezone);
                canonicalType = Iri(XsdDatatype.DateTime);
                break;
            case XsdDatatype.Date:
                ok = XsdDate.TryParse(lexical, out XsdDate date) && date.TryFormat(buffer, out written);
                break;
            case XsdDatatype.Time:
                ok = XsdTime.TryParse(lexical, out XsdTime time) && time.TryFormat(buffer, out written);
                break;
            case XsdDatatype.GYear:
                ok = XsdGYear.TryParse(lexical, out XsdGYear gYear) && gYear.TryFormat(buffer, out written);
                break;
            case XsdDatatype.GYearMonth:
                ok = XsdGYearMonth.TryParse(lexical, out XsdGYearMonth gYearMonth) && gYearMonth.TryFormat(buffer, out written);
                break;
            case XsdDatatype.GMonth:
                ok = XsdGMonth.TryParse(lexical, out XsdGMonth gMonth) && gMonth.TryFormat(buffer, out written);
                break;
            case XsdDatatype.GMonthDay:
                ok = XsdGMonthDay.TryParse(lexical, out XsdGMonthDay gMonthDay) && gMonthDay.TryFormat(buffer, out written);
                break;
            case XsdDatatype.GDay:
                ok = XsdGDay.TryParse(lexical, out XsdGDay gDay) && gDay.TryFormat(buffer, out written);
                break;
            case XsdDatatype.Duration:
            case XsdDatatype.YearMonthDuration:
            case XsdDatatype.DayTimeDuration:
                ok = XsdDuration.TryParse(lexical, out XsdDuration duration) && duration.TryFormat(buffer, out written)
                    && Within(datatype, lexical);
                canonicalType = Iri(XsdDatatype.Duration);
                break;
            default:
                // The integer types: their value is an integer, checked against the type's range.
                ok = XsdDatatypes.IsIntegerType(datatype) && XsdInteger.TryParse(lexical, datatype, out XsdInteger integer) && integer.TryFormat(buffer, out written);
                canonicalType = Iri(XsdDatatype.Integer);
                break;
        }

        if (!ok)
        {
            return term;
        }

        return RdfTerm.Literal(buffer[..written], canonicalType ?? term.Datatype);
    }

    // A decimal's canonical form names an integer when it has no fractional
    // digits other than a single zero.
    private static bool Integral(ReadOnlySpan<byte> canonical, out int length)
    {
        int point = canonical.IndexOf((byte)'.');
        if (point < 0)
        {
            length = canonical.Length;
            return true;
        }

        length = point;
        return canonical[(point + 1)..].SequenceEqual("0"u8);
    }

    // A duration subtype's lexical form is the duration's restricted.
    private static bool Within(XsdDatatype datatype, ReadOnlySpan<byte> lexical) => datatype switch
    {
        XsdDatatype.YearMonthDuration => XsdYearMonthDuration.TryParse(lexical, out _),
        XsdDatatype.DayTimeDuration => XsdDayTimeDuration.TryParse(lexical, out _),
        _ => true,
    };

    private static RdfTerm Iri(XsdDatatype datatype) => RdfTerm.Iri(XsdDatatypes.Iri(datatype));
}
