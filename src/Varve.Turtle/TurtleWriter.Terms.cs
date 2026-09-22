using System;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>Terms, and the one abbreviation the writer makes.</summary>
public sealed partial class TurtleWriter
{
    private void WriteTerm(ref SpanWriter writer, in RdfTermView term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                WriteIriOrPrefixed(ref writer, term.Lexical);
                return;

            case RdfTermKind.BlankNode:
                writer.Bytes("_:"u8);
                writer.Bytes(term.Lexical);
                return;

            case RdfTermKind.TripleTerm:
                writer.Bytes("<<("u8);
                WriteTerm(ref writer, term.Subject);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Predicate);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Object);
                writer.Bytes(")>>"u8);
                return;

            default:
                WriteLiteral(
                    ref writer,
                    term.Lexical,
                    term.HasDatatype ? term.Datatype : default,
                    term.HasLanguage ? term.Language : default,
                    term.HasLanguage,
                    term.Direction);
                return;
        }
    }

    private void WriteTerm(ref SpanWriter writer, RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                WriteIriOrPrefixed(ref writer, term.Lexical);
                return;

            case RdfTermKind.BlankNode:
                writer.Bytes("_:"u8);
                writer.Bytes(term.Lexical);
                return;

            case RdfTermKind.TripleTerm:
                writer.Bytes("<<("u8);
                WriteTerm(ref writer, term.Subject!);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Predicate!);
                writer.Byte((byte)' ');
                WriteTerm(ref writer, term.Object!);
                writer.Bytes(")>>"u8);
                return;

            default:
                WriteLiteral(
                    ref writer,
                    term.Lexical,
                    term.Datatype is null ? default : term.Datatype.Lexical,
                    term.Language,
                    term.Language.Length > 0,
                    term.Direction);
                return;
        }
    }

    private void WriteLiteral(
        ref SpanWriter writer,
        ReadOnlySpan<byte> lexical,
        ReadOnlySpan<byte> datatype,
        ReadOnlySpan<byte> language,
        bool hasLanguage,
        TextDirection direction)
    {
        writer.Byte((byte)'"');
        Escapes.WriteString(ref writer, lexical, _options.Canonical);
        writer.Byte((byte)'"');

        if (hasLanguage)
        {
            if (direction != TextDirection.None)
            {
                // RDF 1.1 Turtle has no syntax for a base direction: it is RDF
                // 1.2's LANG_DIR, which this reader does not accept
                // (`turtle.md` §9). Writing it would produce a document this
                // library's own reader rejects, and dropping it would write a
                // different term — so neither, and the caller is told which
                // syntaxes can carry one.
                throw new InvalidOperationException(
                    "This literal carries a base direction, and RDF 1.1 Turtle and TriG have no "
                    + "syntax for one. Write it as N-Triples or N-Quads, where RDF 1.2's LANG_DIR "
                    + "is gated by the rdf12 suites; RDF 1.2 Turtle is on the roadmap.");
            }

            writer.Byte((byte)'@');
            writer.Bytes(language);
            return;
        }

        if (!datatype.IsEmpty)
        {
            writer.Bytes("^^"u8);
            WriteIriOrPrefixed(ref writer, datatype);
        }
    }

    /// <summary>
    /// Writes an IRI compacted against a declared prefix when it can be, and in
    /// full when it cannot.
    /// </summary>
    /// <remarks>
    /// The compaction has to be checked, not assumed: an IRI can start with a
    /// declared prefix's expansion and still have a remainder that is not a
    /// <c>PN_LOCAL</c> — a space, a <c>&lt;</c>, a character the production
    /// excludes. Emitting <c>ex:</c> plus such a remainder would produce a
    /// document this library's own reader rejects, so the check is what makes
    /// the round trip hold rather than a hope about what IRIs look like.
    /// </remarks>
    private void WriteIriOrPrefixed(ref SpanWriter writer, ReadOnlySpan<byte> iri)
    {
        int index = _prefixes.LongestMatch(iri);

        if (index >= 0)
        {
            ReadOnlySpan<byte> local = iri[_prefixes.IriAt(index).Length..];

            if (CanWriteLocalName(local))
            {
                writer.Bytes(_prefixes.NameAt(index));
                writer.Byte((byte)':');
                WriteLocalName(ref writer, local);
                return;
            }
        }

        writer.Byte((byte)'<');
        Escapes.WriteIri(ref writer, iri, _options.Canonical);
        writer.Byte((byte)'>');
    }

    /// <summary>
    /// Whether <paramref name="local"/> can be written as a <c>PN_LOCAL</c>,
    /// with <c>PN_LOCAL_ESC</c> where the grammar allows an escape.
    /// </summary>
    /// <remarks>
    /// An empty local part is a <c>PNAME_NS</c> and is allowed. A <c>%</c> is
    /// refused rather than escaped: in a local name it introduces a
    /// <c>PERCENT</c>, so <c>%2F</c> written literally reads back as a
    /// percent-escape and a bare <c>%</c> is not a valid one. Falling back to
    /// the full IRI is the only form that means the same thing.
    /// </remarks>
    private static bool CanWriteLocalName(ReadOnlySpan<byte> local)
    {
        if (local.IsEmpty)
        {
            return true;
        }

        int at = 0;
        bool first = true;

        while (at < local.Length)
        {
            if (local[at] == (byte)'%')
            {
                return false;
            }

            if (EscapeDecoder.IsLocalEscape(local[at]))
            {
                at++;
                first = false;
                continue;
            }

            if (!TurtleChars.TryRune(local, at, out int c, out int width))
            {
                return false;
            }

            bool ok = first
                ? c is ':' or (>= '0' and <= '9') || TurtleChars.IsPnCharsU(c)
                : c is ':' or '.' || TurtleChars.IsPnChars(c);

            if (!ok)
            {
                return false;
            }

            at += width;
            first = false;
        }

        return true;
    }

    private static void WriteLocalName(ref SpanWriter writer, ReadOnlySpan<byte> local)
    {
        for (int i = 0; i < local.Length; i++)
        {
            byte b = local[i];

            // A '.' is legal inside a local name and not at its end, where it
            // would be read as the statement's terminator; an escape is legal
            // everywhere, so escaping only the last one keeps the common case
            // unescaped.
            bool needsEscape = EscapeDecoder.IsLocalEscape(b)
                && (b != (byte)'.' || i == local.Length - 1);

            if (needsEscape)
            {
                writer.Byte((byte)'\\');
            }

            writer.Byte(b);
        }
    }
}
