// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using System.Text.Unicode;
using Varve.Iri;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>The term productions: IRIREF, BLANK_NODE_LABEL, literal and triple term.</summary>
internal ref partial struct LineParser
{
    private bool TryIriSlot(out int slot)
    {
        slot = -1;

        if (!TryIri(out TermSpan span))
        {
            return false;
        }

        slot = _arena.AddIri(span);
        return true;
    }

    private bool TryBlankNodeSlot(out int slot)
    {
        slot = -1;

        if (!TryBlankNodeLabel(out TermSpan span))
        {
            return false;
        }

        slot = _arena.AddBlankNode(span);
        return true;
    }

    /// <summary>
    /// A triple term, <c>'&lt;&lt;(' ttSubject predicate ttObject ')&gt;&gt;'</c>.
    /// RDF 1.2 N-Triples; see <c>docs/spec/rdf-model.md</c>.
    /// </summary>
    private bool TryTripleTermSlot(out int slot)
    {
        slot = -1;
        _at += 3;
        SkipWhitespace();

        if (_at >= _line.Length)
        {
            return Fail(ParseErrorKind.UnexpectedEnd, _at);
        }

        if (!TrySubject(out int subject))
        {
            return false;
        }

        SkipWhitespace();

        if (!TryPredicate(out int predicate))
        {
            return false;
        }

        SkipWhitespace();

        if (!TryObject(out int obj))
        {
            return false;
        }

        SkipWhitespace();

        if (_at + 2 >= _line.Length
            || _line[_at] != (byte)')'
            || _line[_at + 1] != (byte)'>'
            || _line[_at + 2] != (byte)'>')
        {
            return Fail(ParseErrorKind.UnterminatedTripleTerm, _at);
        }

        _at += 3;
        slot = _arena.AddTripleTerm(subject, predicate, obj);
        return true;
    }

    private bool TryIri(out TermSpan span)
    {
        span = TermSpan.None;
        int start = _at + 1;
        int i = start;
        bool escaped = false;

        while (true)
        {
            if (i >= _line.Length)
            {
                return Fail(ParseErrorKind.UnterminatedIri, i);
            }

            byte b = _line[i];

            if (b == (byte)'>')
            {
                break;
            }

            if (b == (byte)'\\')
            {
                escaped = true;

                if (!TryMeasureEscape(i, allowEchar: false, out int length))
                {
                    return false;
                }

                i += length;
                continue;
            }

            if (NTriplesChars.IsForbiddenInIri(b))
            {
                return Fail(ParseErrorKind.InvalidIriCharacter, i);
            }

            i++;
        }

        if (escaped)
        {
            if (!TryDecodeEscaped(start, i, allowEchar: false, out span))
            {
                return false;
            }
        }
        else
        {
            if (!TryValidateIri(_line[start..i], start))
            {
                return false;
            }

            span = TermSpan.FromText(start, i - start);
        }

        _at = i + 1;
        return true;
    }

    private bool TryBlankNodeLabel(out TermSpan span)
    {
        span = TermSpan.None;
        int start = _at + 2;

        if (!TryReadRune(start, out int first, out int firstLength))
        {
            return Fail(ParseErrorKind.InvalidBlankNodeLabel, start);
        }

        if (!NTriplesChars.IsPnCharsU(first) && !(first is >= '0' and <= '9'))
        {
            return Fail(ParseErrorKind.InvalidBlankNodeLabel, start);
        }

        int i = start + firstLength;
        int end = i;

        while (TryReadRune(i, out int c, out int length))
        {
            if (c == '.')
            {
                i += length;
                continue;
            }

            if (!NTriplesChars.IsPnChars(c))
            {
                break;
            }

            i += length;
            end = i;
        }

        span = TermSpan.FromText(start, end - start);
        _at = end;
        return true;
    }

    private bool TryLiteralSlot(out int slot)
    {
        slot = -1;
        int start = _at + 1;
        int i = start;
        bool escaped = false;

        while (true)
        {
            if (i >= _line.Length)
            {
                return Fail(ParseErrorKind.UnterminatedLiteral, i);
            }

            byte b = _line[i];

            if (b == (byte)'"')
            {
                break;
            }

            if (b == (byte)'\\')
            {
                escaped = true;

                if (!TryMeasureEscape(i, allowEchar: true, out int length))
                {
                    return false;
                }

                i += length;
                continue;
            }

            if (NTriplesChars.IsEol(b))
            {
                return Fail(ParseErrorKind.UnterminatedLiteral, i);
            }

            i++;
        }

        TermSpan lexical;

        if (escaped)
        {
            if (!TryDecodeEscaped(start, i, allowEchar: true, out lexical))
            {
                return false;
            }
        }
        else
        {
            if (!Utf8.IsValid(_line[start..i]))
            {
                return Fail(ParseErrorKind.InvalidUtf8, start);
            }

            lexical = TermSpan.FromText(start, i - start);
        }

        _at = i + 1;

        if (_at + 1 < _line.Length && _line[_at] == (byte)'^' && _line[_at + 1] == (byte)'^')
        {
            _at += 2;

            if (_at >= _line.Length || _line[_at] != (byte)'<')
            {
                return Fail(ParseErrorKind.ExpectedObject, _at);
            }

            int datatypeAt = _at;

            if (!TryIri(out TermSpan datatype))
            {
                return false;
            }

            // rdf:langString and rdf:dirLangString are the datatypes a
            // language-tagged literal already has. Written out with no language
            // tag they describe a literal that cannot exist (RDF 1.1 Concepts
            // §3.3), which the rdf12 suite tests directly and the rdf11 suite
            // happens not to — it is a 1.1 defect as much as a 1.2 one.
            ReadOnlySpan<byte> iri = _arena.Bytes(_line, datatype);

            if (iri.SequenceEqual(RdfVocabulary.RdfLangString)
                || iri.SequenceEqual(RdfVocabulary.RdfDirLangString))
            {
                return Fail(ParseErrorKind.DatatypeRequiresLanguage, datatypeAt);
            }

            slot = _arena.AddLiteral(lexical, datatype, TermSpan.None, TextDirection.None);
            return true;
        }

        if (_at < _line.Length && _line[_at] == (byte)'@')
        {
            if (!TryLanguageTag(out TermSpan language, out TextDirection direction))
            {
                return false;
            }

            slot = _arena.AddLiteral(lexical, TermSpan.None, language, direction);
            return true;
        }

        slot = _arena.AddLiteral(lexical, TermSpan.None, TermSpan.None, TextDirection.None);
        return true;
    }

    /// <summary>
    /// LANGTAG [144s], with the RDF 1.2 base direction suffix
    /// <c>'--' ('ltr' | 'rtl')</c> when one is present.
    /// </summary>
    private bool TryLanguageTag(out TermSpan span, out TextDirection direction)
    {
        span = TermSpan.None;
        direction = TextDirection.None;

        int start = _at + 1;
        int i = start;

        while (i < _line.Length && NTriplesChars.IsAsciiLetter(_line[i]))
        {
            i++;
        }

        if (i == start)
        {
            return Fail(ParseErrorKind.InvalidLanguageTag, i);
        }

        while (i < _line.Length && _line[i] == (byte)'-')
        {
            if (i + 1 < _line.Length && _line[i + 1] == (byte)'-')
            {
                break;
            }

            int sub = i + 1;

            while (sub < _line.Length
                && (NTriplesChars.IsAsciiLetter(_line[sub]) || NTriplesChars.IsAsciiDigit(_line[sub])))
            {
                sub++;
            }

            if (sub == i + 1)
            {
                return Fail(ParseErrorKind.InvalidLanguageTag, sub);
            }

            i = sub;
        }

        // RDF 1.2 N-Triples [15] gives the shape; the prose beside it requires
        // the tag to be well-formed per BCP 47 §2.2.9, which the shape alone
        // does not enforce.
        if (!LanguageTag.IsWellFormed(_line[start..i]))
        {
            return Fail(ParseErrorKind.InvalidLanguageTag, start);
        }

        span = TermSpan.FromText(start, i - start);
        _at = i;

        if (i + 1 >= _line.Length || _line[i] != (byte)'-' || _line[i + 1] != (byte)'-')
        {
            return true;
        }

        ReadOnlySpan<byte> rest = _line[(i + 2)..];

        if (rest.StartsWith("ltr"u8))
        {
            direction = TextDirection.LeftToRight;
        }
        else if (rest.StartsWith("rtl"u8))
        {
            direction = TextDirection.RightToLeft;
        }
        else
        {
            return Fail(ParseErrorKind.InvalidBaseDirection, i + 2);
        }

        _at = i + 5;
        return true;
    }

    private readonly bool TryReadRune(int index, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;

        if (index >= _line.Length)
        {
            return false;
        }

        if (Rune.DecodeFromUtf8(_line[index..], out Rune rune, out int consumed) != OperationStatus.Done)
        {
            return false;
        }

        codePoint = rune.Value;
        length = consumed;
        return true;
    }

    private bool TryValidateIri(ReadOnlySpan<byte> utf8, int at)
    {
        if (!_validateIris)
        {
            return true;
        }

        if (!IriRef.TryValidate(utf8, out IriComponents components, out IriError error))
        {
            return FailIri(error.Kind, at);
        }

        return components.HasScheme || Fail(ParseErrorKind.RelativeIri, at);
    }
}
