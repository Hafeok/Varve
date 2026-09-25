// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Execution;

/// <summary>Literal construction and classification, over <c>Varve.Xsd</c>.</summary>
internal static class Terms
{
    private static readonly RdfTerm?[] Datatypes = BuildDatatypes();

    internal static RdfTerm True { get; } = RdfTerm.Literal("true"u8, Datatype(XsdDatatype.Boolean));

    internal static RdfTerm False { get; } = RdfTerm.Literal("false"u8, Datatype(XsdDatatype.Boolean));

    internal static RdfTerm EmptyString { get; } = RdfTerm.Literal(default);

    internal static RdfTerm RdfLangString { get; } = RdfTerm.Iri(RdfVocabulary.RdfLangString);

    internal static RdfTerm RdfDirLangString { get; } = RdfTerm.Iri(RdfVocabulary.RdfDirLangString);

    /// <summary>The datatype IRI term of an XSD datatype.</summary>
    internal static RdfTerm Datatype(XsdDatatype datatype) =>
        Datatypes[(int)datatype] ?? throw new ArgumentOutOfRangeException(nameof(datatype));

    internal static RdfTerm Boolean(bool value) => value ? True : False;

    /// <summary>The XSD datatype a literal has, or <see cref="XsdDatatype.None"/> for any other datatype.</summary>
    internal static XsdDatatype DatatypeOf(RdfTerm literal) =>
        literal.Kind != RdfTermKind.Literal ? XsdDatatype.None
        : literal.Datatype is null ? (literal.Language.IsEmpty ? XsdDatatype.String : XsdDatatype.None)
        : XsdDatatypes.FromIri(literal.DatatypeIri);

    /// <summary>A literal whose datatype is <c>xsd:string</c>: a simple literal, in RDF 1.1.</summary>
    internal static bool IsSimple(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && term.Datatype is null && term.Language.IsEmpty;

    internal static bool IsLanguageTagged(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && !term.Language.IsEmpty;

    /// <summary>A string literal in §17.4.3.1.1's sense: simple, <c>xsd:string</c>, or language-tagged.</summary>
    internal static bool IsStringLiteral(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && term.Datatype is null;

    internal static bool TryNumeric(RdfTerm term, out XsdNumeric value)
    {
        value = default;
        if (term.Kind != RdfTermKind.Literal || term.Datatype is null)
        {
            return false;
        }

        XsdDatatype datatype = XsdDatatypes.FromIri(term.DatatypeIri);
        return XsdDatatypes.IsNumeric(datatype) && XsdNumeric.TryParse(term.Lexical, datatype, out value);
    }

    /// <summary>Whether a literal's datatype is numeric, whatever its lexical form.</summary>
    internal static bool HasNumericDatatype(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && term.Datatype is not null && XsdDatatypes.IsNumeric(XsdDatatypes.FromIri(term.DatatypeIri));

    /// <summary>A numeric value as a literal in its canonical form.</summary>
    internal static RdfTerm Literal(XsdNumeric value)
    {
        Span<byte> buffer = stackalloc byte[64];
        if (!value.TryFormat(buffer, out int written))
        {
            throw new InvalidOperationException("A numeric value did not fit its canonical form's buffer.");
        }

        return RdfTerm.Literal(buffer[..written], Datatype(value.Datatype));
    }

    internal static RdfTerm Typed(ReadOnlySpan<byte> lexical, XsdDatatype datatype) =>
        datatype == XsdDatatype.String ? RdfTerm.Literal(lexical) : RdfTerm.Literal(lexical, Datatype(datatype));

    /// <summary>A string with the language and direction of another, or simple.</summary>
    internal static RdfTerm StringLike(ReadOnlySpan<byte> lexical, RdfTerm model) =>
        model.Language.IsEmpty ? RdfTerm.Literal(lexical) : RdfTerm.Literal(lexical, model.Language, model.Direction);

    private static RdfTerm?[] BuildDatatypes()
    {
        XsdDatatype[] all = Enum.GetValues<XsdDatatype>();
        int max = 0;
        foreach (XsdDatatype d in all)
        {
            max = Math.Max(max, (int)d);
        }

        RdfTerm?[] terms = new RdfTerm?[max + 1];
        foreach (XsdDatatype d in all)
        {
            if (d != XsdDatatype.None)
            {
                terms[(int)d] = RdfTerm.Iri(XsdDatatypes.Iri(d));
            }
        }

        return terms;
    }
}
