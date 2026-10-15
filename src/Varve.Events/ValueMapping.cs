// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Varve.Json.Canonical;
using Varve.Rdf;
using Varve.Xsd;
using Varve.Events.Model;

namespace Varve.Events;

/// <summary>The JSON a field's value becomes (docs/spec/events.md §5.2).</summary>
internal enum JsonKind
{
    Integer,
    Number,
    Boolean,
    DateTime,
    Date,
    Time,
    Iri,
    LanguageObject,
    String,
}

/// <summary>
/// The fixed XSD-to-JSON mapping of docs/spec/events.md §5.2–§5.4, and the
/// number rule: a JSON number only when the shortest ECMAScript form of the
/// nearest double equals the literal's exact decimal value (ADR 0143).
/// </summary>
internal static partial class ValueMapping
{
    /// <summary>The JSON a field of <paramref name="field"/>'s declaration becomes.</summary>
    internal static JsonKind KindOf(EventFieldDeclaration field)
    {
        if (field.AsString)
        {
            return JsonKind.String;
        }

        string datatype = Encoding.UTF8.GetString(field.Datatype.Lexical);

        if (datatype is Vocabulary.LangStringIri or Vocabulary.DirLangStringIri)
        {
            return field.KeepLanguage ? JsonKind.LanguageObject : JsonKind.String;
        }

        if (datatype == Vocabulary.AnyUriIri)
        {
            return JsonKind.Iri;
        }

        XsdDatatype xsd = XsdDatatypes.FromIri(field.Datatype.Lexical);
        return xsd switch
        {
            _ when XsdDatatypes.IsIntegerType(xsd) => JsonKind.Integer,
            XsdDatatype.Decimal or XsdDatatype.Double or XsdDatatype.Float => JsonKind.Number,
            XsdDatatype.Boolean => JsonKind.Boolean,
            XsdDatatype.DateTime or XsdDatatype.DateTimeStamp => JsonKind.DateTime,
            XsdDatatype.Date => JsonKind.Date,
            XsdDatatype.Time => JsonKind.Time,
            _ => JsonKind.String,
        };
    }

    /// <summary>
    /// Writes <paramref name="value"/> as the member's JSON, or answers the
    /// problem's reason (<c>datatype</c> or <c>number</c>) and writes nothing.
    /// </summary>
    internal static string? TryWrite(CanonicalJsonWriter writer, EventFieldDeclaration field, RdfTerm value)
    {
        if (value.Kind is RdfTermKind.BlankNode or RdfTermKind.TripleTerm)
        {
            return Problems.Datatype;
        }

        string declared = Encoding.UTF8.GetString(field.Datatype.Lexical);

        if (!IsOfDatatype(value, declared))
        {
            return Problems.Datatype;
        }

        JsonKind kind = KindOf(field);

        if (kind == JsonKind.String)
        {
            writer.WriteString(value.Lexical);
            return null;
        }

        string lexical = Encoding.UTF8.GetString(value.Lexical);

        switch (kind)
        {
            case JsonKind.Iri:
                writer.WriteString(value.Lexical);
                return null;
            case JsonKind.Boolean:
                writer.WriteBoolean(lexical is "true" or "1");
                return null;
            case JsonKind.Integer:
            case JsonKind.Number:
                XsdDatatype xsd = XsdDatatypes.FromIri(value.DatatypeIri);

                if (!TryNumber(lexical, xsd, out double number))
                {
                    return Problems.Number;
                }

                writer.WriteNumber(number);
                return null;
            case JsonKind.DateTime:
                return Rfc3339DateTime().IsMatch(lexical) && HourBelow24(lexical, lexical.IndexOf('T') + 1)
                    ? WriteString(writer, value) : Problems.Datatype;
            case JsonKind.Date:
                return Rfc3339Date().IsMatch(lexical) ? WriteString(writer, value) : Problems.Datatype;
            case JsonKind.Time:
                return Rfc3339Time().IsMatch(lexical) && HourBelow24(lexical, 0)
                    ? WriteString(writer, value) : Problems.Datatype;
            case JsonKind.LanguageObject:
                writer.StartObject();

                if (value.Direction != TextDirection.None)
                {
                    writer.WriteName("direction"u8);
                    writer.WriteString(value.Direction == TextDirection.RightToLeft ? "rtl"u8 : "ltr"u8);
                }

                writer.WriteName("language"u8);
                writer.WriteString(value.Language);
                writer.WriteName("value"u8);
                writer.WriteString(value.Lexical);
                writer.EndObject();
                return null;
            default:
                return Problems.Datatype;
        }
    }

    private static string? WriteString(CanonicalJsonWriter writer, RdfTerm value)
    {
        writer.WriteString(value.Lexical);
        return null;
    }

    /// <summary>Whether a value is of a field's datatype: an IRI for <c>xsd:anyURI</c>; otherwise a well-typed literal of the datatype or, for the integer types, of one derived from it.</summary>
    internal static bool IsOfDatatype(RdfTerm value, string declared)
    {
        if (value.Kind == RdfTermKind.Iri)
        {
            return declared == Vocabulary.AnyUriIri;
        }

        if (value.Kind != RdfTermKind.Literal)
        {
            return false;
        }

        string actual = value.DatatypeIri.IsEmpty
            ? (value.Language.IsEmpty ? Vocabulary.StringIri : value.Direction == TextDirection.None ? Vocabulary.LangStringIri : Vocabulary.DirLangStringIri)
            : Encoding.UTF8.GetString(value.DatatypeIri);

        if (declared is Vocabulary.LangStringIri)
        {
            return actual is Vocabulary.LangStringIri or Vocabulary.DirLangStringIri;
        }

        XsdDatatype declaredXsd = XsdDatatypes.FromIri(Encoding.UTF8.GetBytes(declared));
        XsdDatatype actualXsd = XsdDatatypes.FromIri(Encoding.UTF8.GetBytes(actual));

        bool derived = actual == declared
            || (declaredXsd == XsdDatatype.Decimal && XsdDatatypes.IsIntegerType(actualXsd))
            || (XsdDatatypes.IsIntegerType(declaredXsd) && DerivesFrom(actualXsd, declaredXsd))
            || (declaredXsd == XsdDatatype.DateTime && actualXsd == XsdDatatype.DateTimeStamp);

        return derived && IsWellTyped(Encoding.UTF8.GetString(value.Lexical), actualXsd);
    }

    /// <summary>Whether a lexical form is in its XSD datatype's lexical space, for the datatypes the mapping reads.</summary>
    private static bool IsWellTyped(string lexical, XsdDatatype datatype)
    {
        if (XsdDatatypes.IsIntegerType(datatype))
        {
            return IntegerLexical().IsMatch(lexical) && InRange(BigInteger.Parse(lexical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), datatype);
        }

        return datatype switch
        {
            XsdDatatype.Decimal => DecimalLexical().IsMatch(lexical),
            XsdDatatype.Double or XsdDatatype.Float => DoubleLexical().IsMatch(lexical),
            XsdDatatype.Boolean => lexical is "true" or "false" or "1" or "0",
            XsdDatatype.DateTime or XsdDatatype.DateTimeStamp => XsdDateTime.TryParse(lexical.AsSpan(), out XsdDateTime dt)
                && (datatype != XsdDatatype.DateTimeStamp || dt.HasTimezone),
            XsdDatatype.Date => XsdDate.TryParse(lexical.AsSpan(), out _),
            XsdDatatype.Time => XsdTime.TryParse(lexical.AsSpan(), out _),
            _ => true,
        };
    }

    private static bool InRange(BigInteger value, XsdDatatype datatype) => datatype switch
    {
        XsdDatatype.NonPositiveInteger => value <= 0,
        XsdDatatype.NegativeInteger => value < 0,
        XsdDatatype.Long => value >= long.MinValue && value <= long.MaxValue,
        XsdDatatype.Int => value >= int.MinValue && value <= int.MaxValue,
        XsdDatatype.Short => value >= short.MinValue && value <= short.MaxValue,
        XsdDatatype.Byte => value >= sbyte.MinValue && value <= sbyte.MaxValue,
        XsdDatatype.NonNegativeInteger => value >= 0,
        XsdDatatype.UnsignedLong => value >= 0 && value <= ulong.MaxValue,
        XsdDatatype.UnsignedInt => value >= 0 && value <= uint.MaxValue,
        XsdDatatype.UnsignedShort => value >= 0 && value <= ushort.MaxValue,
        XsdDatatype.UnsignedByte => value >= 0 && value <= byte.MaxValue,
        XsdDatatype.PositiveInteger => value > 0,
        _ => true,
    };

    /// <summary>XSD's derivation of the integer types, by restriction.</summary>
    private static bool DerivesFrom(XsdDatatype actual, XsdDatatype ancestor)
    {
        for (XsdDatatype? at = actual; at is { } current; at = Parent(current))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static XsdDatatype? Parent(XsdDatatype datatype) => datatype switch
    {
        XsdDatatype.NonPositiveInteger or XsdDatatype.Long or XsdDatatype.NonNegativeInteger => XsdDatatype.Integer,
        XsdDatatype.NegativeInteger => XsdDatatype.NonPositiveInteger,
        XsdDatatype.Int => XsdDatatype.Long,
        XsdDatatype.Short => XsdDatatype.Int,
        XsdDatatype.Byte => XsdDatatype.Short,
        XsdDatatype.UnsignedLong or XsdDatatype.PositiveInteger => XsdDatatype.NonNegativeInteger,
        XsdDatatype.UnsignedInt => XsdDatatype.UnsignedLong,
        XsdDatatype.UnsignedShort => XsdDatatype.UnsignedInt,
        XsdDatatype.UnsignedByte => XsdDatatype.UnsignedShort,
        _ => null,
    };

    /// <summary>
    /// The number rule (§5.4). For a double or float, the value's nearest
    /// double, when finite. For a decimal or an integer, the nearest double
    /// when its shortest ECMAScript form equals the literal's exact decimal
    /// value, so that the JSON text is exact; otherwise none.
    /// </summary>
    internal static bool TryNumber(string lexical, XsdDatatype datatype, out double value)
    {
        if (datatype is XsdDatatype.Double or XsdDatatype.Float)
        {
            if (lexical is "INF" or "+INF" or "-INF" or "NaN")
            {
                value = 0;
                return false;
            }

            value = datatype == XsdDatatype.Float
                ? float.Parse(lexical, NumberStyles.Float, CultureInfo.InvariantCulture)
                : double.Parse(lexical, NumberStyles.Float, CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }

        value = double.Parse(lexical, NumberStyles.Float, CultureInfo.InvariantCulture);

        if (!double.IsFinite(value))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[CanonicalJson.MaxNumberLength];
        int written = CanonicalJson.FormatNumber(value, buffer);
        return ExactDecimal.Parse(Encoding.ASCII.GetString(buffer[..written])).Equals(ExactDecimal.Parse(lexical));
    }

    private static bool HourBelow24(string lexical, int at) =>
        at + 2 <= lexical.Length && string.CompareOrdinal(lexical, at, "24", 0, 2) < 0;

    [GeneratedRegex(@"^[+-]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerLexical();

    [GeneratedRegex(@"^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalLexical();

    [GeneratedRegex(@"^([+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)([Ee][+-]?[0-9]+)?|[+-]?INF|NaN)$", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleLexical();

    // RFC 3339 §5.6: date-time requires an offset, a four-digit year, a T.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Rfc3339DateTime();

    // RFC 3339 §5.6: full-date, with no offset.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Rfc3339Date();

    // RFC 3339 §5.6: full-time, with an offset.
    [GeneratedRegex(@"^[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Rfc3339Time();
}

/// <summary>An exact decimal value: a sign, its significant digits and the power of ten of the last one.</summary>
internal readonly record struct ExactDecimal(bool Negative, string Digits, int Exponent)
{
    /// <summary>Parses a decimal, integer or exponent-form numeral exactly; zero is one value, unsigned.</summary>
    internal static ExactDecimal Parse(string text)
    {
        int at = 0;
        bool negative = false;

        if (text[at] is '+' or '-')
        {
            negative = text[at] == '-';
            at++;
        }

        StringBuilder digits = new();
        int exponent = 0;
        bool afterPoint = false;

        for (; at < text.Length; at++)
        {
            char c = text[at];

            if (c == '.')
            {
                afterPoint = true;
            }
            else if (c is 'e' or 'E')
            {
                exponent += int.Parse(text.AsSpan(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }
            else
            {
                digits.Append(c);

                if (afterPoint)
                {
                    exponent--;
                }
            }
        }

        string all = digits.ToString().TrimStart('0');

        if (all.Length == 0)
        {
            return new ExactDecimal(false, "0", 0);
        }

        int trailing = all.Length - all.TrimEnd('0').Length;
        return new ExactDecimal(negative, all[..^trailing], exponent + trailing);
    }
}

/// <summary>The reasons an event is not rendered (docs/spec/events.md §4.3).</summary>
internal static class Problems
{
    internal const string BlankEvent = "blank-event";
    internal const string Types = "types";
    internal const string NoRow = "no-row";
    internal const string Rows = "rows";
    internal const string UnboundRequired = "unbound-required";
    internal const string Datatype = "datatype";
    internal const string Number = "number";
}
