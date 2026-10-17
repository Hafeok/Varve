// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.Shacl.Validation;

/// <summary>
/// What SHACL asks of a literal's value: whether it is well-typed for its
/// datatype (§7.1.2), and SPARQL's <c>&lt;</c> between two of them (§7.3,
/// §7.6.4), over <c>Varve.Xsd</c>'s value spaces and orders (ADR 0051).
/// </summary>
internal static class Literals
{
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";
    private const string RdfLangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";
    private const string RdfDirLangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString";

    /// <summary>
    /// Whether <paramref name="literal"/> matches <paramref name="datatype"/>
    /// (§7.1.2): the same datatype IRI and, for a datatype SPARQL 1.2 supports,
    /// a lexical form in its lexical space.
    /// </summary>
    internal static bool Matches(RdfTerm literal, RdfTerm datatype)
    {
        if (literal.Kind != RdfTermKind.Literal || !literal.DatatypeIri.SequenceEqual(datatype.Lexical))
        {
            return false;
        }

        string iri = System.Text.Encoding.UTF8.GetString(datatype.Lexical);
        if (iri == RdfLangString)
        {
            return !literal.Language.IsEmpty;
        }

        if (iri == RdfDirLangString)
        {
            return !literal.Language.IsEmpty && literal.Direction != TextDirection.None;
        }

        XsdDatatype known = XsdDatatypes.FromIri(datatype.Lexical);
        return known == XsdDatatype.None || IsWellTyped(literal.Lexical, known);
    }

    /// <summary>Whether a lexical form is in the lexical space of an XSD datatype <c>Varve.Xsd</c> knows.</summary>
    internal static bool IsWellTyped(ReadOnlySpan<byte> lexical, XsdDatatype datatype)
    {
        switch (datatype)
        {
            case XsdDatatype.String:
                return true;
            case XsdDatatype.Boolean:
                return XsdBoolean.TryParse(lexical, out _);
            case XsdDatatype.Decimal:
                return XsdDecimal.TryParse(lexical, out _) || IsDecimalLexical(lexical);
            case XsdDatatype.Double:
                return XsdDouble.TryParse(lexical, out _);
            case XsdDatatype.Float:
                return XsdFloat.TryParse(lexical, out _);
            case XsdDatatype.DateTime or XsdDatatype.DateTimeStamp:
                return XsdDateTime.TryParse(lexical, out XsdDateTime dateTime)
                    && (datatype != XsdDatatype.DateTimeStamp || dateTime.HasTimezone);
            case XsdDatatype.Date:
                return XsdDate.TryParse(lexical, out _);
            case XsdDatatype.Time:
                return XsdTime.TryParse(lexical, out _);
            case XsdDatatype.GYearMonth:
                return XsdGYearMonth.TryParse(lexical, out _);
            case XsdDatatype.GYear:
                return XsdGYear.TryParse(lexical, out _);
            case XsdDatatype.GMonthDay:
                return XsdGMonthDay.TryParse(lexical, out _);
            case XsdDatatype.GDay:
                return XsdGDay.TryParse(lexical, out _);
            case XsdDatatype.GMonth:
                return XsdGMonth.TryParse(lexical, out _);
            case XsdDatatype.Duration:
                return XsdDuration.TryParse(lexical, out _);
            case XsdDatatype.YearMonthDuration:
                return XsdYearMonthDuration.TryParse(lexical, out _);
            case XsdDatatype.DayTimeDuration:
                return XsdDayTimeDuration.TryParse(lexical, out _);
            default:
                if (XsdDatatypes.IsIntegerType(datatype))
                {
                    return XsdInteger.TryParse(lexical, datatype, out _) || IsUnboundedIntegerLexical(lexical, datatype);
                }

                return true;
        }
    }

    /// <summary>
    /// SPARQL's <c>&lt;</c> family between two terms: the order, or
    /// <see cref="PartialOrdering.Indeterminate"/> when no operator applies or
    /// the values are incomparable.
    /// </summary>
    internal static PartialOrdering Compare(RdfTerm left, RdfTerm right)
    {
        if (left.Kind != RdfTermKind.Literal || right.Kind != RdfTermKind.Literal)
        {
            return PartialOrdering.Indeterminate;
        }

        XsdDatatype a = DatatypeOf(left), b = DatatypeOf(right);
        if (a == XsdDatatype.None || b == XsdDatatype.None)
        {
            return PartialOrdering.Indeterminate;
        }

        if (XsdDatatypes.IsNumeric(a) && XsdDatatypes.IsNumeric(b))
        {
            return TryNumeric(left.Lexical, a, out XsdNumeric x) && TryNumeric(right.Lexical, b, out XsdNumeric y)
                ? XsdNumeric.Compare(x, y)
                : PartialOrdering.Indeterminate;
        }

        Family family = FamilyOf(a);
        if (family == Family.None || family != FamilyOf(b))
        {
            return PartialOrdering.Indeterminate;
        }

        ReadOnlySpan<byte> l = left.Lexical, r = right.Lexical;
        return family switch
        {
            Family.String => Sign(XsdString.CompareCodePoints(l, r)),
            Family.Boolean => XsdBoolean.TryParse(l, out XsdBoolean p) && XsdBoolean.TryParse(r, out XsdBoolean q) ? Sign(p.CompareTo(q)) : PartialOrdering.Indeterminate,
            Family.DateTime => XsdDateTime.TryParse(l, out XsdDateTime p) && XsdDateTime.TryParse(r, out XsdDateTime q) ? XsdDateTime.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.Date => XsdDate.TryParse(l, out XsdDate p) && XsdDate.TryParse(r, out XsdDate q) ? XsdDate.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.Time => XsdTime.TryParse(l, out XsdTime p) && XsdTime.TryParse(r, out XsdTime q) ? XsdTime.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.GYearMonth => XsdGYearMonth.TryParse(l, out XsdGYearMonth p) && XsdGYearMonth.TryParse(r, out XsdGYearMonth q) ? XsdGYearMonth.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.GYear => XsdGYear.TryParse(l, out XsdGYear p) && XsdGYear.TryParse(r, out XsdGYear q) ? XsdGYear.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.GMonthDay => XsdGMonthDay.TryParse(l, out XsdGMonthDay p) && XsdGMonthDay.TryParse(r, out XsdGMonthDay q) ? XsdGMonthDay.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.GDay => XsdGDay.TryParse(l, out XsdGDay p) && XsdGDay.TryParse(r, out XsdGDay q) ? XsdGDay.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.GMonth => XsdGMonth.TryParse(l, out XsdGMonth p) && XsdGMonth.TryParse(r, out XsdGMonth q) ? XsdGMonth.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            Family.Duration => TryDuration(l, a, out XsdDuration p) && TryDuration(r, b, out XsdDuration q) ? XsdDuration.CompareXsd(p, q) : PartialOrdering.Indeterminate,
            _ => PartialOrdering.Indeterminate,
        };
    }

    /// <summary>The datatype of a literal as <c>Varve.Xsd</c> knows it: a simple literal is <c>xsd:string</c>, a language-tagged one none.</summary>
    private static XsdDatatype DatatypeOf(RdfTerm literal)
    {
        if (!literal.Language.IsEmpty)
        {
            return XsdDatatype.None;
        }

        ReadOnlySpan<byte> iri = literal.DatatypeIri;
        return iri.IsEmpty ? XsdDatatype.String : XsdDatatypes.FromIri(iri);
    }

    private static bool TryNumeric(ReadOnlySpan<byte> lexical, XsdDatatype datatype, out XsdNumeric value)
    {
        value = default;
        switch (datatype)
        {
            case XsdDatatype.Double:
                if (XsdDouble.TryParse(lexical, out XsdDouble d))
                {
                    value = XsdNumeric.FromDouble(d);
                    return true;
                }

                return false;
            case XsdDatatype.Float:
                if (XsdFloat.TryParse(lexical, out XsdFloat f))
                {
                    value = XsdNumeric.FromFloat(f);
                    return true;
                }

                return false;
            case XsdDatatype.Decimal:
                if (XsdDecimal.TryParse(lexical, out XsdDecimal m))
                {
                    value = XsdNumeric.FromDecimal(m);
                    return true;
                }

                return false;
            default:
                if (XsdInteger.TryParse(lexical, datatype, out XsdInteger i))
                {
                    value = XsdNumeric.FromInteger(i);
                    return true;
                }

                // An integer beyond Int64 has no value here (ADR 0051); as a decimal it still orders.
                if (XsdDecimal.TryParse(lexical, out XsdDecimal big))
                {
                    value = XsdNumeric.FromDecimal(big);
                    return true;
                }

                return false;
        }
    }

    private static bool TryDuration(ReadOnlySpan<byte> lexical, XsdDatatype datatype, out XsdDuration value)
    {
        value = default;
        switch (datatype)
        {
            case XsdDatatype.YearMonthDuration:
                if (XsdYearMonthDuration.TryParse(lexical, out XsdYearMonthDuration ym))
                {
                    value = new XsdDuration(ym.Months, default);
                    return true;
                }

                return false;
            case XsdDatatype.DayTimeDuration:
                if (XsdDayTimeDuration.TryParse(lexical, out XsdDayTimeDuration dt))
                {
                    value = new XsdDuration(0, dt.Seconds);
                    return true;
                }

                return false;
            default:
                return XsdDuration.TryParse(lexical, out value);
        }
    }

    private static PartialOrdering Sign(int comparison) =>
        comparison < 0 ? PartialOrdering.Less : comparison > 0 ? PartialOrdering.Greater : PartialOrdering.Equal;

    private static Family FamilyOf(XsdDatatype datatype) => datatype switch
    {
        XsdDatatype.String => Family.String,
        XsdDatatype.Boolean => Family.Boolean,
        XsdDatatype.DateTime or XsdDatatype.DateTimeStamp => Family.DateTime,
        XsdDatatype.Date => Family.Date,
        XsdDatatype.Time => Family.Time,
        XsdDatatype.GYearMonth => Family.GYearMonth,
        XsdDatatype.GYear => Family.GYear,
        XsdDatatype.GMonthDay => Family.GMonthDay,
        XsdDatatype.GDay => Family.GDay,
        XsdDatatype.GMonth => Family.GMonth,
        XsdDatatype.Duration or XsdDatatype.YearMonthDuration or XsdDatatype.DayTimeDuration => Family.Duration,
        _ => Family.None,
    };

    /// <summary>XML Schema's decimal lexical space, for values beyond <c>Varve.Xsd</c>'s precision.</summary>
    private static bool IsDecimalLexical(ReadOnlySpan<byte> text)
    {
        int i = text.Length > 0 && text[0] is (byte)'+' or (byte)'-' ? 1 : 0;
        int digits = 0;
        bool point = false;
        for (; i < text.Length; i++)
        {
            if (text[i] == (byte)'.' && !point)
            {
                point = true;
            }
            else if (text[i] is >= (byte)'0' and <= (byte)'9')
            {
                digits++;
            }
            else
            {
                return false;
            }
        }

        return digits > 0;
    }

    /// <summary>The integer types without a bound on one side, whose lexical forms may exceed Int64.</summary>
    private static bool IsUnboundedIntegerLexical(ReadOnlySpan<byte> text, XsdDatatype datatype)
    {
        if (datatype is not (XsdDatatype.Integer or XsdDatatype.NonNegativeInteger or XsdDatatype.PositiveInteger
            or XsdDatatype.NonPositiveInteger or XsdDatatype.NegativeInteger))
        {
            return false;
        }

        bool negative = text.Length > 0 && text[0] == (byte)'-';
        int start = text.Length > 0 && text[0] is (byte)'+' or (byte)'-' ? 1 : 0;
        if (start == text.Length || text.Length - start < 19)
        {
            return false;
        }

        for (int i = start; i < text.Length; i++)
        {
            if (text[i] is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return datatype switch
        {
            XsdDatatype.NonNegativeInteger or XsdDatatype.PositiveInteger => !negative,
            XsdDatatype.NonPositiveInteger or XsdDatatype.NegativeInteger => negative,
            _ => true,
        };
    }

    private enum Family
    {
        None,
        String,
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
}
