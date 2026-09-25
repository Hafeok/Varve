// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using Varve.Rdf;

namespace Varve.Sparql.Results;

/// <summary>
/// SPARQL 1.1 Query Results CSV Format (<c>sparql-results.md</c> §3.4), as
/// RFC 4180 records. CSV is lossy by design: the reader returns a field that
/// begins <c>_:</c> as a blank node, an empty unquoted field as unbound, and
/// every other field as a simple literal holding its text. It does not guess
/// which fields were IRIs.
/// </summary>
internal sealed class CsvResultsParser : FormatParser
{
    private readonly ByteCursor _cursor;
    private readonly ByteBuilder _field = new();
    private ResultsPosition _fieldStart;
    private bool _quoted;

    internal CsvResultsParser(ReadOnlySequence<byte> input) => _cursor = new ByteCursor(input);

    internal override void ReadHead()
    {
        _cursor.TryConsume([0xEF, 0xBB, 0xBF]);
        if (_cursor.AtEnd)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, _cursor.Position, "A CSV result has at least a header record.");
        }

        bool more = true;
        while (more)
        {
            more = ReadField();
            if (_field.Length == 0 && !more && Variables.Count == 0)
            {
                break;
            }

            AddVariable(_field.Span, _fieldStart);
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
                if (Variables.Count == 0 && _field.Length == 0 && !more)
                {
                    return true;
                }

                throw new ResultsSyntaxException(SparqlResultsErrorKind.FieldCount, _fieldStart, "A record has more fields than the header has variables.");
            }

            ReadOnlySpan<byte> text = _field.Span;
            if (!text.IsEmpty || _quoted)
            {
                if (!_quoted && text.Length > 2 && text[0] == (byte)'_' && text[1] == (byte)':')
                {
                    TermSpan label = arena.AppendScratch(text[2..]);
                    RequireUtf8(arena, label, _fieldStart);
                    bindings[column] = arena.AddBlankNode(label);
                }
                else
                {
                    TermSpan lexical = arena.AppendScratch(text);
                    bindings[column] = AddLiteral(arena, lexical, TermSpan.None, TermSpan.None, default, _fieldStart);
                }
            }

            column++;
        }

        if (column != Variables.Count)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.FieldCount, _cursor.Position, "A record has fewer fields than the header has variables.");
        }

        return true;
    }

    /// <summary>Reads one field into <see cref="_field"/>; false when it ended the record.</summary>
    private bool ReadField()
    {
        _field.Clear();
        _fieldStart = _cursor.Position;
        _quoted = false;
        if (_cursor.Peek() == '"')
        {
            _quoted = true;
            _cursor.Next();
            while (true)
            {
                int c = _cursor.Next();
                if (c < 0)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, _fieldStart, "An unterminated quoted field.");
                }

                if (c == '"')
                {
                    if (_cursor.Peek() == '"')
                    {
                        _cursor.Next();
                        _field.Append((byte)'"');
                        continue;
                    }

                    break;
                }

                _field.Append((byte)c);
            }

            int after = _cursor.Peek();
            if (after is not (',' or '\r' or '\n' or -1))
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, _cursor.Position, "Content after a quoted field.");
            }
        }

        while (true)
        {
            int c = _cursor.Peek();
            if (c < 0)
            {
                return false;
            }

            _cursor.Next();
            if (c == ',')
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

            if (c == '"')
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, _cursor.Position, "A quote inside an unquoted field.");
            }

            _field.Append((byte)c);
        }
    }
}
