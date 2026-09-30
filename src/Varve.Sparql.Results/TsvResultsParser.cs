// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Results.Model;

namespace Varve.Sparql.Results;

/// <summary>
/// SPARQL 1.1 Query Results TSV Format (<c>sparql-results.md</c> §3.3): a line
/// of <c>?variables</c>, then one line per solution, each field an RDF term in
/// Turtle syntax or empty for unbound. The term reader is this package's own,
/// because <c>Varve.Turtle</c> is at the same layer and may not be referenced.
/// </summary>
internal sealed class TsvResultsParser : FormatParser
{
    private static ReadOnlySpan<byte> XsdInteger => "http://www.w3.org/2001/XMLSchema#integer"u8;

    private static ReadOnlySpan<byte> XsdDecimal => "http://www.w3.org/2001/XMLSchema#decimal"u8;

    private static ReadOnlySpan<byte> XsdDouble => "http://www.w3.org/2001/XMLSchema#double"u8;

    private static ReadOnlySpan<byte> XsdBoolean => "http://www.w3.org/2001/XMLSchema#boolean"u8;

    private readonly ByteCursor _cursor;
    private readonly ByteBuilder _field = new();
    private readonly ByteBuilder _decoded = new();
    private ResultsPosition _fieldStart;
    private int _lead;

    internal TsvResultsParser(ReadOnlySequence<byte> input) => _cursor = new ByteCursor(input);

    internal override void ReadHead()
    {
        _cursor.TryConsume([0xEF, 0xBB, 0xBF]);
        if (_cursor.AtEnd)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, _cursor.Position, "A TSV result has at least a header line.");
        }

        bool more = true;
        while (more)
        {
            more = ReadField();
            ReadOnlySpan<byte> name = Trim(_field.Span);
            if (name.IsEmpty && !more && Variables.Count == 0)
            {
                // A header with no variables: a solution sequence over none.
                break;
            }

            if (name.Length < 2 || (name[0] != (byte)'?' && name[0] != (byte)'$'))
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedStructure, _fieldStart, "A TSV header field is a variable written with '?'.");
            }

            AddVariable(name[1..], _fieldStart);
        }
    }

    internal override bool ReadSolution(TermArena arena, int[] bindings)
    {
        if (_cursor.AtEnd)
        {
            return false;
        }

        int column = 0;
        bool more = true;
        while (more)
        {
            more = ReadField();
            if (column >= Variables.Count)
            {
                // One empty field on a line of a zero-variable result is its one empty solution.
                if (Variables.Count == 0 && _field.Length == 0 && !more)
                {
                    return true;
                }

                throw new ResultsSyntaxException(SparqlResultsErrorKind.FieldCount, _fieldStart, "A row has more fields than the header has variables.");
            }

            ReadOnlySpan<byte> text = Trim(_field.Span);
            _lead = _field.Span.IndexOfAnyExcept((byte)' ') is int lead and >= 0 ? lead : 0;
            if (!text.IsEmpty)
            {
                int end = ParseTerm(text, 0, arena, out int term);
                if (end != text.Length)
                {
                    throw Invalid(end, "Content after the term in a field.");
                }

                bindings[column] = term;
            }

            column++;
        }

        if (column != Variables.Count)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.FieldCount, _cursor.Position, "A row has fewer fields than the header has variables.");
        }

        return true;
    }

    /// <summary>Reads one field into <see cref="_field"/>; false when it ended the line.</summary>
    private bool ReadField()
    {
        _field.Clear();
        _fieldStart = _cursor.Position;
        while (true)
        {
            int c = _cursor.Peek();
            if (c < 0)
            {
                return false;
            }

            _cursor.Next();
            if (c == '\t')
            {
                return true;
            }

            if (c == '\n')
            {
                return false;
            }

            if (c == '\r' && _cursor.Peek() == '\n')
            {
                _cursor.Next();
                return false;
            }

            _field.Append((byte)c);
        }
    }

    // ------------------------------------------------------------ Turtle terms

    /// <summary>Parses one term starting at <paramref name="at"/>; returns the index after it.</summary>
    private int ParseTerm(ReadOnlySpan<byte> text, int at, TermArena arena, out int term)
    {
        byte first = text[at];
        if (first == (byte)'<')
        {
            if (at + 2 < text.Length && text[at + 1] == (byte)'<' && text[at + 2] == (byte)'(')
            {
                return ParseTripleTerm(text, at + 3, arena, out term);
            }

            int end = ParseIri(text, at, out TermSpan iri, arena);
            term = arena.AddIri(iri);
            return end;
        }

        if (first == (byte)'_' && at + 1 < text.Length && text[at + 1] == (byte)':')
        {
            int end = at + 2;
            while (end < text.Length && !IsDelimiter(text[end]))
            {
                end++;
            }

            if (end == at + 2)
            {
                throw Invalid(at, "An empty blank node label.");
            }

            TermSpan label = arena.AppendScratch(text[(at + 2)..end]);
            RequireUtf8(arena, label, _fieldStart);
            term = arena.AddBlankNode(label);
            return end;
        }

        if (first is (byte)'"' or (byte)'\'')
        {
            return ParseLiteral(text, at, arena, out term);
        }

        if (text[at..].StartsWith("true"u8) && (at + 4 == text.Length || IsDelimiter(text[at + 4])))
        {
            term = arena.AddLiteral(arena.AppendScratch("true"u8), arena.AppendScratch(XsdBoolean), TermSpan.None, TextDirection.None);
            return at + 4;
        }

        if (text[at..].StartsWith("false"u8) && (at + 5 == text.Length || IsDelimiter(text[at + 5])))
        {
            term = arena.AddLiteral(arena.AppendScratch("false"u8), arena.AppendScratch(XsdBoolean), TermSpan.None, TextDirection.None);
            return at + 5;
        }

        return ParseNumber(text, at, arena, out term);
    }

    private int ParseTripleTerm(ReadOnlySpan<byte> text, int at, TermArena arena, out int term)
    {
        at = SkipSpace(text, at);
        at = ParseTerm(text, at, arena, out int subject);
        at = SkipSpace(text, at);
        at = ParseTerm(text, at, arena, out int predicate);
        at = SkipSpace(text, at);
        at = ParseTerm(text, at, arena, out int @object);
        at = SkipSpace(text, at);
        if (!text[at..].StartsWith(")>>"u8))
        {
            throw Invalid(at, "A triple term ends with ')>>'.");
        }

        term = arena.AddTripleTerm(subject, predicate, @object);
        return at + 3;
    }

    private int ParseIri(ReadOnlySpan<byte> text, int at, out TermSpan iri, TermArena arena)
    {
        _decoded.Clear();
        int i = at + 1;
        while (true)
        {
            if (i >= text.Length)
            {
                throw Invalid(at, "An unterminated IRI.");
            }

            byte b = text[i];
            if (b == (byte)'>')
            {
                i++;
                break;
            }

            if (b == (byte)'\\')
            {
                i = ReadUnicodeEscape(text, i, allowCharacterEscapes: false);
                continue;
            }

            if (b <= 0x20 || b is (byte)'<' or (byte)'"' or (byte)'{' or (byte)'}' or (byte)'|' or (byte)'^' or (byte)'`')
            {
                throw Invalid(i, "A character not allowed in an IRI.");
            }

            _decoded.Append(b);
            i++;
        }

        iri = arena.AppendScratch(_decoded.Span);
        RequireUtf8(arena, iri, _fieldStart);
        return i;
    }

    private int ParseLiteral(ReadOnlySpan<byte> text, int at, TermArena arena, out int term)
    {
        byte quote = text[at];
        bool isLong = at + 2 < text.Length && text[at + 1] == quote && text[at + 2] == quote;
        int i = at + (isLong ? 3 : 1);
        _decoded.Clear();
        while (true)
        {
            if (i >= text.Length)
            {
                throw Invalid(at, "An unterminated string.");
            }

            byte b = text[i];
            if (b == quote)
            {
                if (!isLong)
                {
                    i++;
                    break;
                }

                if (i + 2 < text.Length && text[i + 1] == quote && text[i + 2] == quote)
                {
                    i += 3;
                    break;
                }
            }

            if (b == (byte)'\\')
            {
                i = ReadUnicodeEscape(text, i, allowCharacterEscapes: true);
                continue;
            }

            if (!isLong && b is (byte)'\n' or (byte)'\r')
            {
                throw Invalid(i, "A line break in a short string.");
            }

            _decoded.Append(b);
            i++;
        }

        TermSpan lexical = arena.AppendScratch(_decoded.Span);
        TermSpan datatype = TermSpan.None;
        TermSpan language = TermSpan.None;
        ReadOnlySpan<byte> direction = default;
        if (i < text.Length && text[i] == (byte)'@')
        {
            int start = ++i;
            while (i < text.Length && (IsAsciiLetterOrDigit(text[i]) || (text[i] == (byte)'-' && !(i + 1 < text.Length && text[i + 1] == (byte)'-'))))
            {
                i++;
            }

            language = arena.AppendScratch(text[start..i]);
            if (i + 1 < text.Length && text[i] == (byte)'-' && text[i + 1] == (byte)'-')
            {
                int dirStart = i + 2;
                i = dirStart;
                while (i < text.Length && IsAsciiLetterOrDigit(text[i]))
                {
                    i++;
                }

                direction = text[dirStart..i];
            }
        }
        else if (i + 1 < text.Length && text[i] == (byte)'^' && text[i + 1] == (byte)'^')
        {
            if (i + 2 >= text.Length || text[i + 2] != (byte)'<')
            {
                throw Invalid(i, "A datatype is an IRI in angle brackets; TSV has no prefixes.");
            }

            i = ParseIri(text, i + 2, out datatype, arena);
        }

        term = AddLiteral(arena, lexical, datatype, language, direction, _fieldStart);
        return i;
    }

    private int ParseNumber(ReadOnlySpan<byte> text, int at, TermArena arena, out int term)
    {
        int i = at;
        if (i < text.Length && text[i] is (byte)'+' or (byte)'-')
        {
            i++;
        }

        int digits = 0;
        while (i < text.Length && IsDigit(text[i]))
        {
            i++;
            digits++;
        }

        bool dot = false;
        int fraction = 0;
        if (i < text.Length && text[i] == (byte)'.' && i + 1 < text.Length && IsDigit(text[i + 1]))
        {
            dot = true;
            i++;
            while (i < text.Length && IsDigit(text[i]))
            {
                i++;
                fraction++;
            }
        }

        bool exponent = false;
        if (i < text.Length && text[i] is (byte)'e' or (byte)'E' && digits + fraction > 0)
        {
            exponent = true;
            i++;
            if (i < text.Length && text[i] is (byte)'+' or (byte)'-')
            {
                i++;
            }

            int exponentDigits = 0;
            while (i < text.Length && IsDigit(text[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                throw Invalid(at, "An exponent without digits.");
            }
        }

        if (digits + fraction == 0 || (i < text.Length && !IsDelimiter(text[i])))
        {
            throw Invalid(at, "Not an RDF term: expected an IRI, a blank node, a literal, a number or a boolean.");
        }

        ReadOnlySpan<byte> type = exponent ? XsdDouble : dot ? XsdDecimal : XsdInteger;
        term = arena.AddLiteral(arena.AppendScratch(text[at..i]), arena.AppendScratch(type), TermSpan.None, TextDirection.None);
        return i;
    }

    /// <summary>Decodes a backslash escape at <paramref name="at"/> into <see cref="_decoded"/>.</summary>
    private int ReadUnicodeEscape(ReadOnlySpan<byte> text, int at, bool allowCharacterEscapes)
    {
        if (at + 1 >= text.Length)
        {
            throw Invalid(at, "A backslash at the end of a field.");
        }

        byte kind = text[at + 1];
        if (kind is (byte)'u' or (byte)'U')
        {
            int length = kind == (byte)'u' ? 4 : 8;
            if (at + 2 + length > text.Length)
            {
                throw Invalid(at, "A truncated \\u escape.");
            }

            int value = 0;
            for (int k = 0; k < length; k++)
            {
                byte h = text[at + 2 + k];
                int digit = h is >= (byte)'0' and <= (byte)'9' ? h - '0'
                    : h is >= (byte)'a' and <= (byte)'f' ? h - 'a' + 10
                    : h is >= (byte)'A' and <= (byte)'F' ? h - 'A' + 10
                    : -1;
                if (digit < 0)
                {
                    throw Invalid(at, "A \\u escape with a non-hexadecimal digit.");
                }

                value = (value << 4) | digit;
            }

            if (!Rune.IsValid(value))
            {
                throw Invalid(at, "A \\u escape that is not a Unicode scalar value.");
            }

            _decoded.AppendCodePoint(value);
            return at + 2 + length;
        }

        if (allowCharacterEscapes)
        {
            byte decoded = kind switch
            {
                (byte)'t' => (byte)'\t',
                (byte)'b' => (byte)'\b',
                (byte)'n' => (byte)'\n',
                (byte)'r' => (byte)'\r',
                (byte)'f' => (byte)'\f',
                (byte)'"' => (byte)'"',
                (byte)'\'' => (byte)'\'',
                (byte)'\\' => (byte)'\\',
                _ => 0,
            };
            if (decoded != 0)
            {
                _decoded.Append(decoded);
                return at + 2;
            }
        }

        throw Invalid(at, "An unknown escape.");
    }

    private static int SkipSpace(ReadOnlySpan<byte> text, int at)
    {
        while (at < text.Length && text[at] == (byte)' ')
        {
            at++;
        }

        return at;
    }

    private static bool IsDelimiter(byte b) => b is (byte)' ' or (byte)')';

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool IsAsciiLetterOrDigit(byte b) =>
        b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9';

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        int start = 0, end = value.Length;
        while (start < end && value[start] == (byte)' ')
        {
            start++;
        }

        while (end > start && value[end - 1] == (byte)' ')
        {
            end--;
        }

        return value[start..end];
    }

    private ResultsSyntaxException Invalid(int at, string message) =>
        new(SparqlResultsErrorKind.InvalidTerm, new ResultsPosition(_fieldStart.ByteOffset + _lead + at, _fieldStart.Line, _fieldStart.Column + _lead + at), message);
}
