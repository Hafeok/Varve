// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Writes N-Triples and N-Quads, from a view or from a quad and its source.
/// </summary>
/// <remarks>
/// <para>
/// Canonical form is RDF 1.2 N-Triples §3's (ADR 0061), and N-Quads 1.2's with
/// the graph label: a lowercase language tag, triple terms as
/// <c>&lt;&lt;( s p o )&gt;&gt;</c>, and the string escapes of
/// <see cref="Escapes.WriteLineString"/>. It is RDFC-1.0 Appendix A's form,
/// which <c>Varve.Rdf</c> writes for itself one layer down; a property holds
/// the two byte-identical.
/// </para>
/// <para>
/// A canonical round trip is byte-stable: parsing a canonical document and
/// writing it again reproduces it exactly, which is what the round-trip
/// property tests assert.
/// </para>
/// </remarks>
public static class NQuadsWriter
{
    /// <summary>
    /// Writes one quad, terminated by a line feed. Returns false when
    /// <paramref name="destination"/> is too small, in which case nothing has
    /// been written and <paramref name="written"/> is zero.
    /// </summary>
    [Varve.HotPath]
    public static bool TryWrite(in QuadView quad, Span<byte> destination, out int written, in WriteOptions options)
    {
        SpanWriter writer = new(destination);

        WriteTerm(ref writer, quad.Subject, in options);
        writer.Byte((byte)' ');
        WriteTerm(ref writer, quad.Predicate, in options);
        writer.Byte((byte)' ');
        WriteTerm(ref writer, quad.Object, in options);

        if (quad.HasGraph && options.Syntax == RdfSyntax.NQuads)
        {
            writer.Byte((byte)' ');
            WriteTerm(ref writer, quad.Graph, in options);
        }

        writer.Bytes(" .\n"u8);

        written = writer.Overflowed ? 0 : writer.Written;
        return !writer.Overflowed;
    }

    /// <summary>Writes one quad to a buffer writer.</summary>
    [Varve.HotPath]
    public static void Write(IBufferWriter<byte> output, in QuadView quad, in WriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);

        int size = 256;

        while (true)
        {
            Span<byte> span = output.GetSpan(size);

            if (TryWrite(in quad, span, out int written, in options))
            {
                output.Advance(written);
                return;
            }

            size *= 2;
        }
    }

    /// <summary>
    /// Writes one quad whose terms live in a quad source, materialising each
    /// term as it goes. The allocating path, for a caller that has handles
    /// rather than a view.
    /// </summary>
    public static void Write(IBufferWriter<byte> output, in Quad quad, IQuadSource source, in WriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);

        RdfTerm subject = Externalise(source, quad.Subject);
        RdfTerm predicate = Externalise(source, quad.Predicate);
        RdfTerm obj = Externalise(source, quad.Object);
        RdfTerm? graph = quad.IsDefaultGraph ? null : Externalise(source, quad.Graph);

        int size = 256;

        while (true)
        {
            Span<byte> span = output.GetSpan(size);
            SpanWriter writer = new(span);

            WriteTerm(ref writer, subject, in options);
            writer.Byte((byte)' ');
            WriteTerm(ref writer, predicate, in options);
            writer.Byte((byte)' ');
            WriteTerm(ref writer, obj, in options);

            if (graph is not null && options.Syntax == RdfSyntax.NQuads)
            {
                writer.Byte((byte)' ');
                WriteTerm(ref writer, graph, in options);
            }

            writer.Bytes(" .\n"u8);

            if (!writer.Overflowed)
            {
                output.Advance(writer.Written);
                return;
            }

            size *= 2;
        }
    }

    /// <summary>
    /// Writes one term, with no terminator. Returns false when
    /// <paramref name="destination"/> is too small.
    /// </summary>
    public static bool TryWriteTerm(RdfTerm term, Span<byte> destination, out int written, in WriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(term);

        SpanWriter writer = new(destination);
        WriteTerm(ref writer, term, in options);
        written = writer.Overflowed ? 0 : writer.Written;
        return !writer.Overflowed;
    }

    private static RdfTerm Externalise(IQuadSource source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term)
            ? term
            : throw new InvalidOperationException(
                "The quad source cannot externalise a term of this quad. A term whose key has been "
                + "destroyed has no serialisation, and writing a placeholder would claim it does.");

    private static void WriteTerm(ref SpanWriter writer, in RdfTermView term, in WriteOptions options)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                WriteIri(ref writer, term.Lexical, in options);
                return;

            case RdfTermKind.BlankNode:
                writer.Bytes("_:"u8);
                writer.Bytes(term.Lexical);
                return;

            case RdfTermKind.TripleTerm:
                writer.Bytes("<<( "u8);
                WriteTerm(ref writer, term.Subject, in options);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Predicate, in options);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Object, in options);
                writer.Bytes(" )>>"u8);
                return;

            default:
                WriteLiteral(
                    ref writer,
                    term.Lexical,
                    term.HasDatatype ? term.Datatype : default,
                    term.HasLanguage ? term.Language : default,
                    term.HasLanguage,
                    term.Direction,
                    in options);
                return;
        }
    }

    private static void WriteTerm(ref SpanWriter writer, RdfTerm term, in WriteOptions options)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                WriteIri(ref writer, term.Lexical, in options);
                return;

            case RdfTermKind.BlankNode:
                writer.Bytes("_:"u8);
                writer.Bytes(term.Lexical);
                return;

            case RdfTermKind.TripleTerm:
                writer.Bytes("<<( "u8);
                WriteTerm(ref writer, term.Subject!, in options);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Predicate!, in options);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Object!, in options);
                writer.Bytes(" )>>"u8);
                return;

            default:
                WriteLiteral(
                    ref writer,
                    term.Lexical,
                    term.Datatype is null ? default : term.Datatype.Lexical,
                    term.Language,
                    term.Language.Length > 0,
                    term.Direction,
                    in options);
                return;
        }
    }

    private static void WriteLiteral(
        ref SpanWriter writer,
        ReadOnlySpan<byte> lexical,
        ReadOnlySpan<byte> datatype,
        ReadOnlySpan<byte> language,
        bool hasLanguage,
        TextDirection direction,
        in WriteOptions options)
    {
        writer.Byte((byte)'"');
        Escapes.WriteLineString(ref writer, lexical, options.Canonical);
        writer.Byte((byte)'"');

        if (hasLanguage)
        {
            writer.Byte((byte)'@');

            if (options.Canonical)
            {
                // Tags compare case-insensitively, so one term has one form:
                // RDF 1.2 N-Triples §3 requires the lowercase one.
                foreach (byte b in language)
                {
                    writer.Byte(b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b);
                }
            }
            else
            {
                writer.Bytes(language);
            }

            switch (direction)
            {
                case TextDirection.LeftToRight:
                    writer.Bytes("--ltr"u8);
                    break;

                case TextDirection.RightToLeft:
                    writer.Bytes("--rtl"u8);
                    break;

                default:
                    break;
            }

            return;
        }

        // A simple literal is xsd:string, and canonical form writes it without
        // the datatype (RDF 1.2 N-Triples §3). A term never holds it; a view
        // of what a parser read can.
        if (!datatype.IsEmpty && !(options.Canonical && datatype.SequenceEqual(RdfVocabulary.XsdString)))
        {
            writer.Bytes("^^"u8);
            WriteIri(ref writer, datatype, in options);
        }
    }

    private static void WriteIri(ref SpanWriter writer, ReadOnlySpan<byte> text, in WriteOptions options)
    {
        writer.Byte((byte)'<');
        Escapes.WriteIri(ref writer, text, options.Canonical);
        writer.Byte((byte)'>');
    }
}
