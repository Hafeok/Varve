// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// The XML Schema datatypes this package gives a value space to.
/// </summary>
/// <remarks>
/// The list is ADR 0051's scope: what SPARQL 1.1 §17.3 dispatches on, and
/// nothing else. A datatype IRI that maps to none of these names a literal
/// this package never sees, compared by lexical form wherever it appears.
/// </remarks>
public enum XsdDatatype
{
    /// <summary>Not one of this package's datatypes.</summary>
    None = 0,

    /// <summary><c>xsd:string</c>, §3.3.1.</summary>
    String,

    /// <summary><c>xsd:boolean</c>, §3.3.2.</summary>
    Boolean,

    /// <summary><c>xsd:decimal</c>, §3.3.3.</summary>
    Decimal,

    /// <summary><c>xsd:float</c>, §3.3.4.</summary>
    Float,

    /// <summary><c>xsd:double</c>, §3.3.5.</summary>
    Double,

    /// <summary><c>xsd:duration</c>, §3.3.6.</summary>
    Duration,

    /// <summary><c>xsd:dateTime</c>, §3.3.7.</summary>
    DateTime,

    /// <summary><c>xsd:time</c>, §3.3.8.</summary>
    Time,

    /// <summary><c>xsd:date</c>, §3.3.9.</summary>
    Date,

    /// <summary><c>xsd:gYearMonth</c>, §3.3.10.</summary>
    GYearMonth,

    /// <summary><c>xsd:gYear</c>, §3.3.11.</summary>
    GYear,

    /// <summary><c>xsd:gMonthDay</c>, §3.3.12.</summary>
    GMonthDay,

    /// <summary><c>xsd:gDay</c>, §3.3.13.</summary>
    GDay,

    /// <summary><c>xsd:gMonth</c>, §3.3.14.</summary>
    GMonth,

    /// <summary><c>xsd:integer</c>, §3.4.13.</summary>
    Integer,

    /// <summary><c>xsd:nonPositiveInteger</c>, §3.4.14.</summary>
    NonPositiveInteger,

    /// <summary><c>xsd:negativeInteger</c>, §3.4.15.</summary>
    NegativeInteger,

    /// <summary><c>xsd:long</c>, §3.4.16.</summary>
    Long,

    /// <summary><c>xsd:int</c>, §3.4.17.</summary>
    Int,

    /// <summary><c>xsd:short</c>, §3.4.18.</summary>
    Short,

    /// <summary><c>xsd:byte</c>, §3.4.19.</summary>
    Byte,

    /// <summary><c>xsd:nonNegativeInteger</c>, §3.4.20.</summary>
    NonNegativeInteger,

    /// <summary><c>xsd:unsignedLong</c>, §3.4.21.</summary>
    UnsignedLong,

    /// <summary><c>xsd:unsignedInt</c>, §3.4.22.</summary>
    UnsignedInt,

    /// <summary><c>xsd:unsignedShort</c>, §3.4.23.</summary>
    UnsignedShort,

    /// <summary><c>xsd:unsignedByte</c>, §3.4.24.</summary>
    UnsignedByte,

    /// <summary><c>xsd:positiveInteger</c>, §3.4.25.</summary>
    PositiveInteger,

    /// <summary><c>xsd:yearMonthDuration</c>, §3.4.26.</summary>
    YearMonthDuration,

    /// <summary><c>xsd:dayTimeDuration</c>, §3.4.27.</summary>
    DayTimeDuration,

    /// <summary><c>xsd:dateTimeStamp</c>, §3.4.28.</summary>
    DateTimeStamp,
}

/// <summary>
/// The datatype IRIs, and the mapping from an IRI to an <see cref="XsdDatatype"/>.
/// </summary>
public static class XsdDatatypes
{
    /// <summary>The XML Schema namespace, <c>http://www.w3.org/2001/XMLSchema#</c>.</summary>
    public static ReadOnlySpan<byte> Namespace => "http://www.w3.org/2001/XMLSchema#"u8;

    /// <summary>
    /// The datatype a datatype IRI names, or <see cref="XsdDatatype.None"/>
    /// when it is not one this package knows.
    /// </summary>
    /// <remarks>
    /// One namespace comparison and one switch on the local name, allocating
    /// nothing. RDF IRI equality is byte equality, so no case folding.
    /// </remarks>
    public static XsdDatatype FromIri(ReadOnlySpan<byte> iri)
    {
        if (!iri.StartsWith(Namespace))
        {
            return XsdDatatype.None;
        }

        ReadOnlySpan<byte> local = iri[Namespace.Length..];

        return local switch
        {
            _ when local.SequenceEqual("string"u8) => XsdDatatype.String,
            _ when local.SequenceEqual("boolean"u8) => XsdDatatype.Boolean,
            _ when local.SequenceEqual("decimal"u8) => XsdDatatype.Decimal,
            _ when local.SequenceEqual("float"u8) => XsdDatatype.Float,
            _ when local.SequenceEqual("double"u8) => XsdDatatype.Double,
            _ when local.SequenceEqual("duration"u8) => XsdDatatype.Duration,
            _ when local.SequenceEqual("dateTime"u8) => XsdDatatype.DateTime,
            _ when local.SequenceEqual("time"u8) => XsdDatatype.Time,
            _ when local.SequenceEqual("date"u8) => XsdDatatype.Date,
            _ when local.SequenceEqual("gYearMonth"u8) => XsdDatatype.GYearMonth,
            _ when local.SequenceEqual("gYear"u8) => XsdDatatype.GYear,
            _ when local.SequenceEqual("gMonthDay"u8) => XsdDatatype.GMonthDay,
            _ when local.SequenceEqual("gDay"u8) => XsdDatatype.GDay,
            _ when local.SequenceEqual("gMonth"u8) => XsdDatatype.GMonth,
            _ when local.SequenceEqual("integer"u8) => XsdDatatype.Integer,
            _ when local.SequenceEqual("nonPositiveInteger"u8) => XsdDatatype.NonPositiveInteger,
            _ when local.SequenceEqual("negativeInteger"u8) => XsdDatatype.NegativeInteger,
            _ when local.SequenceEqual("long"u8) => XsdDatatype.Long,
            _ when local.SequenceEqual("int"u8) => XsdDatatype.Int,
            _ when local.SequenceEqual("short"u8) => XsdDatatype.Short,
            _ when local.SequenceEqual("byte"u8) => XsdDatatype.Byte,
            _ when local.SequenceEqual("nonNegativeInteger"u8) => XsdDatatype.NonNegativeInteger,
            _ when local.SequenceEqual("unsignedLong"u8) => XsdDatatype.UnsignedLong,
            _ when local.SequenceEqual("unsignedInt"u8) => XsdDatatype.UnsignedInt,
            _ when local.SequenceEqual("unsignedShort"u8) => XsdDatatype.UnsignedShort,
            _ when local.SequenceEqual("unsignedByte"u8) => XsdDatatype.UnsignedByte,
            _ when local.SequenceEqual("positiveInteger"u8) => XsdDatatype.PositiveInteger,
            _ when local.SequenceEqual("yearMonthDuration"u8) => XsdDatatype.YearMonthDuration,
            _ when local.SequenceEqual("dayTimeDuration"u8) => XsdDatatype.DayTimeDuration,
            _ when local.SequenceEqual("dateTimeStamp"u8) => XsdDatatype.DateTimeStamp,
            _ => XsdDatatype.None,
        };
    }

    /// <summary>The IRI of a datatype, or empty for <see cref="XsdDatatype.None"/>.</summary>
    public static ReadOnlySpan<byte> Iri(XsdDatatype datatype) => datatype switch
    {
        XsdDatatype.String => "http://www.w3.org/2001/XMLSchema#string"u8,
        XsdDatatype.Boolean => "http://www.w3.org/2001/XMLSchema#boolean"u8,
        XsdDatatype.Decimal => "http://www.w3.org/2001/XMLSchema#decimal"u8,
        XsdDatatype.Float => "http://www.w3.org/2001/XMLSchema#float"u8,
        XsdDatatype.Double => "http://www.w3.org/2001/XMLSchema#double"u8,
        XsdDatatype.Duration => "http://www.w3.org/2001/XMLSchema#duration"u8,
        XsdDatatype.DateTime => "http://www.w3.org/2001/XMLSchema#dateTime"u8,
        XsdDatatype.Time => "http://www.w3.org/2001/XMLSchema#time"u8,
        XsdDatatype.Date => "http://www.w3.org/2001/XMLSchema#date"u8,
        XsdDatatype.GYearMonth => "http://www.w3.org/2001/XMLSchema#gYearMonth"u8,
        XsdDatatype.GYear => "http://www.w3.org/2001/XMLSchema#gYear"u8,
        XsdDatatype.GMonthDay => "http://www.w3.org/2001/XMLSchema#gMonthDay"u8,
        XsdDatatype.GDay => "http://www.w3.org/2001/XMLSchema#gDay"u8,
        XsdDatatype.GMonth => "http://www.w3.org/2001/XMLSchema#gMonth"u8,
        XsdDatatype.Integer => "http://www.w3.org/2001/XMLSchema#integer"u8,
        XsdDatatype.NonPositiveInteger => "http://www.w3.org/2001/XMLSchema#nonPositiveInteger"u8,
        XsdDatatype.NegativeInteger => "http://www.w3.org/2001/XMLSchema#negativeInteger"u8,
        XsdDatatype.Long => "http://www.w3.org/2001/XMLSchema#long"u8,
        XsdDatatype.Int => "http://www.w3.org/2001/XMLSchema#int"u8,
        XsdDatatype.Short => "http://www.w3.org/2001/XMLSchema#short"u8,
        XsdDatatype.Byte => "http://www.w3.org/2001/XMLSchema#byte"u8,
        XsdDatatype.NonNegativeInteger => "http://www.w3.org/2001/XMLSchema#nonNegativeInteger"u8,
        XsdDatatype.UnsignedLong => "http://www.w3.org/2001/XMLSchema#unsignedLong"u8,
        XsdDatatype.UnsignedInt => "http://www.w3.org/2001/XMLSchema#unsignedInt"u8,
        XsdDatatype.UnsignedShort => "http://www.w3.org/2001/XMLSchema#unsignedShort"u8,
        XsdDatatype.UnsignedByte => "http://www.w3.org/2001/XMLSchema#unsignedByte"u8,
        XsdDatatype.PositiveInteger => "http://www.w3.org/2001/XMLSchema#positiveInteger"u8,
        XsdDatatype.YearMonthDuration => "http://www.w3.org/2001/XMLSchema#yearMonthDuration"u8,
        XsdDatatype.DayTimeDuration => "http://www.w3.org/2001/XMLSchema#dayTimeDuration"u8,
        XsdDatatype.DateTimeStamp => "http://www.w3.org/2001/XMLSchema#dateTimeStamp"u8,
        _ => default,
    };

    /// <summary>
    /// Whether the datatype is <c>xsd:integer</c> or derived from it
    /// (§3.4.13–§3.4.25), so that its values are <see cref="XsdInteger"/>s.
    /// </summary>
    public static bool IsIntegerType(XsdDatatype datatype) =>
        datatype is >= XsdDatatype.Integer and <= XsdDatatype.PositiveInteger;

    /// <summary>
    /// Whether the datatype is numeric in SPARQL 1.1 §17.1's sense: the
    /// integer family, <c>decimal</c>, <c>float</c> or <c>double</c>.
    /// </summary>
    public static bool IsNumeric(XsdDatatype datatype) =>
        datatype is XsdDatatype.Decimal or XsdDatatype.Float or XsdDatatype.Double || IsIntegerType(datatype);
}
