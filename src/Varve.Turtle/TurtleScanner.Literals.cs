// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Unicode;
using Varve.Rdf;
using Varve.Turtle.Model;

namespace Varve.Turtle;

/// <summary>[128s] RDFLiteral and [17] its four String forms.</summary>
internal ref partial struct TurtleScanner
{
    private bool TryLiteral(out int slot)
    {
        slot = -1;

        if (!TryString(out TermSpan lexical))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd && MayGrow)
        {
            // A string may be followed by "@" or "^^", so what kind of literal
            // this is has not been settled yet.
            return Truncated();
        }

        if (!AtEnd && Peek == (byte)'@')
        {
            if (!TryLanguage(out TermSpan language, out TextDirection direction))
            {
                return false;
            }

            slot = _state.Arena.AddLiteral(lexical, TermSpan.None, language, direction);
            return true;
        }

        if (!AtEnd && Peek == (byte)'^' && Consumed + 1 >= _text.Length && MayGrow)
        {
            return Truncated();
        }

        if (!AtEnd && Peek == (byte)'^' && Consumed + 1 < _text.Length && _text[Consumed + 1] == (byte)'^')
        {
            int at = Consumed;
            Consumed += 2;
            SkipIgnorable();

            if (!TryIriTerm(out TermSpan datatype))
            {
                return false;
            }

            ReadOnlySpan<byte> iri = _state.Arena.Bytes(_text, datatype);

            if (iri.SequenceEqual(RdfVocabulary.RdfLangString)
                || iri.SequenceEqual(RdfVocabulary.RdfDirLangString))
            {
                return Fail(ParseErrorKind.DatatypeRequiresLanguage, at);
            }

            slot = _state.Arena.AddLiteral(lexical, datatype, TermSpan.None, TextDirection.None);
            return true;
        }

        slot = _state.Arena.AddLiteral(lexical, TermSpan.None, TermSpan.None, TextDirection.None);
        return true;
    }

    /// <summary>
    /// [17] one of the four String forms. The long forms are the interesting
    /// ones: a run of one or two quote characters inside them does not
    /// terminate, so <c>"""a"b"""</c> is a single literal.
    /// </summary>
    private bool TryString(out TermSpan span)
    {
        span = TermSpan.None;
        byte quote = Peek;
        bool longForm = Consumed + 2 < _text.Length && _text[Consumed + 1] == quote && _text[Consumed + 2] == quote;
        int width = longForm ? 3 : 1;
        int start = Consumed + width;
        int i = start;
        bool escaped = false;

        while (true)
        {
            if (i >= _text.Length)
            {
                return Truncated();
            }

            byte b = _text[i];

            if (b == (byte)'\\')
            {
                escaped = true;

                if (!EscapeDecoder.TryMeasure(
                    _text, i, EscapeDecoder.Allowed.UcharAndEchar, out int length,
                    out ParseErrorKind error, out int at, out bool truncated))
                {
                    return truncated ? Truncated() : Fail(error, at);
                }

                i += length;
                continue;
            }

            if (b == quote)
            {
                if (!longForm)
                {
                    break;
                }

                if (i + 2 < _text.Length && _text[i + 1] == quote && _text[i + 2] == quote)
                {
                    break;
                }

                i++;
                continue;
            }

            // A short string may not contain a raw newline; a long one may.
            if (!longForm && b is 0x0A or 0x0D)
            {
                return Fail(ParseErrorKind.UnterminatedLiteral, i);
            }

            i++;
        }

        if (escaped)
        {
            if (!EscapeDecoder.TryDecode(
                _text, start, i, EscapeDecoder.Allowed.UcharAndEchar, _state.Arena,
                out span, out ParseErrorKind error, out int at))
            {
                return Fail(error, at);
            }
        }
        else
        {
            if (!Utf8.IsValid(_text[start..i]))
            {
                return Fail(ParseErrorKind.InvalidUtf8, start);
            }

            span = TermSpan.FromText(start, i - start);
        }

        Consumed = i + width;
        return true;
    }

    /// <summary>[144s] LANGTAG, with RDF 1.2's base direction where present.</summary>
    private bool TryLanguage(out TermSpan span, out TextDirection direction)
    {
        span = TermSpan.None;
        direction = TextDirection.None;

        int start = Consumed + 1;
        int i = start;

        while (i < _text.Length && NTriplesChars.IsAsciiLetter(_text[i]))
        {
            i++;
        }

        if (i >= _text.Length && MayGrow)
        {
            // The tag ends where the buffer does, so its last subtag may still
            // grow. Judging it now would reject "@en-GB" cut after "@en-G".
            return Truncated();
        }

        if (i == start)
        {
            return Fail(ParseErrorKind.InvalidLanguageTag, i);
        }

        while (i < _text.Length && _text[i] == (byte)'-')
        {
            if (i + 1 < _text.Length && _text[i + 1] == (byte)'-')
            {
                break;
            }

            int sub = i + 1;

            while (sub < _text.Length
                && (NTriplesChars.IsAsciiLetter(_text[sub]) || NTriplesChars.IsAsciiDigit(_text[sub])))
            {
                sub++;
            }

            if (sub >= _text.Length && MayGrow)
            {
                return Truncated();
            }

            if (sub == i + 1)
            {
                return Fail(ParseErrorKind.InvalidLanguageTag, sub);
            }

            i = sub;
        }

        if (!LanguageTag.IsWellFormed(_text[start..i]))
        {
            return Fail(ParseErrorKind.InvalidLanguageTag, start);
        }

        span = TermSpan.FromText(start, i - start);
        Consumed = i;

        if (i + 1 >= _text.Length || _text[i] != (byte)'-' || _text[i + 1] != (byte)'-')
        {
            return true;
        }

        // A base direction is RDF 1.2's LANG_DIR, and this reader is RDF 1.1
        // Turtle and TriG (`turtle.md` §9). [144s] LANGTAG requires each
        // subtag after a '-' to be alphanumeric, so "en--ltr" is not a tag this
        // syntax has — the term model carries a direction, and the syntaxes
        // that can write one are N-Triples and N-Quads, where the rdf12 suites
        // gate it. Accepting it here would be a feature with no suite behind
        // it, which is the defect that gating rdf12 found at 3a.
        return Fail(ParseErrorKind.InvalidLanguageTag, i);
    }
}
