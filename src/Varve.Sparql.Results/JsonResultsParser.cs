// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text.Json;
using Varve.Rdf;
using Varve.Sparql.Results.Model;

namespace Varve.Sparql.Results;

/// <summary>
/// SPARQL 1.1 Query Results JSON Format, over the BCL's <see cref="Utf8JsonReader"/>
/// (<c>sparql-results.md</c> §3.2). The reader is a <c>ref struct</c>, so it is
/// rebuilt at each call from the bytes consumed so far and its saved state;
/// that is what lets one document be read a solution at a time.
/// </summary>
internal sealed class JsonResultsParser : FormatParser
{
    private readonly ReadOnlySequence<byte> _input;
    private long _consumed;
    private JsonReaderState _state;
    private bool _inBindings;
    private bool _done;
    private byte[] _unescaped = new byte[64];

    internal JsonResultsParser(ReadOnlySequence<byte> input)
    {
        _input = input;
        _state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
    }

    private enum TermType : byte
    {
        None,
        Uri,
        BlankNode,
        Literal,
        Triple,
    }

    internal override void ReadHead()
    {
        Utf8JsonReader reader = Reader();
        long resultsAt = -1;
        JsonReaderState resultsState = default;
        bool headSeen = false;
        try
        {
            Expect(ref reader, JsonTokenType.StartObject, "A result document is a JSON object.");
            while (Next(ref reader) == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("head"u8))
                {
                    ReadHeadObject(ref reader);
                    headSeen = true;
                }
                else if (reader.ValueTextEquals("boolean"u8))
                {
                    JsonTokenType value = Next(ref reader);
                    if (value is not (JsonTokenType.True or JsonTokenType.False))
                    {
                        throw Structure(ref reader, "The boolean member is not true or false.");
                    }

                    IsBoolean = true;
                    Boolean = value == JsonTokenType.True;
                }
                else if (reader.ValueTextEquals("results"u8))
                {
                    if (headSeen)
                    {
                        if (EnterBindings(ref reader))
                        {
                            Save(ref reader);
                            _inBindings = true;
                            return;
                        }
                    }
                    else
                    {
                        // The results come before the head: remember where, skip them,
                        // and come back once the variables are known.
                        resultsAt = _consumed + reader.BytesConsumed;
                        resultsState = reader.CurrentState;
                        Skip(ref reader);
                    }
                }
                else
                {
                    Skip(ref reader);
                }
            }

            ExpectEndOfDocument(ref reader);
            if (!headSeen && !IsBoolean)
            {
                throw Structure(ref reader, "The document has no head.");
            }

            if (resultsAt >= 0)
            {
                _consumed = resultsAt;
                _state = resultsState;
                reader = Reader();
                if (EnterBindings(ref reader))
                {
                    Save(ref reader);
                    _inBindings = true;
                    return;
                }
            }

            _done = true;
        }
        catch (JsonException error)
        {
            throw Malformed(ref reader, error);
        }
    }

    internal override bool ReadSolution(TermArena arena, int[] bindings)
    {
        if (_done || !_inBindings)
        {
            return false;
        }

        Utf8JsonReader reader = Reader();
        try
        {
            JsonTokenType token = Next(ref reader);
            if (token == JsonTokenType.EndArray)
            {
                FinishDocument(ref reader);
                _done = true;
                return false;
            }

            if (token != JsonTokenType.StartObject)
            {
                throw Structure(ref reader, "A solution is a JSON object.");
            }

            while (Next(ref reader) == JsonTokenType.PropertyName)
            {
                int index = Bind(Unescape(ref reader), bindings, Position(ref reader));
                bindings[index] = ReadTerm(ref reader, arena);
            }

            Save(ref reader);
            return true;
        }
        catch (JsonException error)
        {
            throw Malformed(ref reader, error);
        }
    }

    private void ReadHeadObject(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "The head is a JSON object.");
        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("vars"u8))
            {
                Expect(ref reader, JsonTokenType.StartArray, "vars is an array.");
                while (Next(ref reader) == JsonTokenType.String)
                {
                    AddVariable(Unescape(ref reader), Position(ref reader));
                }

                if (reader.TokenType != JsonTokenType.EndArray)
                {
                    throw Structure(ref reader, "vars holds something other than strings.");
                }
            }
            else
            {
                Skip(ref reader);
            }
        }
    }

    /// <summary>Positions the reader inside the bindings array of the results object; false if it has none.</summary>
    private bool EnterBindings(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "results is a JSON object.");
        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("bindings"u8))
            {
                Expect(ref reader, JsonTokenType.StartArray, "bindings is an array.");
                return true;
            }

            Skip(ref reader);
        }

        return false;
    }

    /// <summary>After the bindings array: the rest of the results object and of the document.</summary>
    private void FinishDocument(ref Utf8JsonReader reader)
    {
        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            Skip(ref reader);
        }

        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            Skip(ref reader);
        }

        ExpectEndOfDocument(ref reader);
    }

    private int ReadTerm(ref Utf8JsonReader reader, TermArena arena)
    {
        ResultsPosition position = Position(ref reader);
        Expect(ref reader, JsonTokenType.StartObject, "A term is a JSON object.");
        TermType type = TermType.None;
        TermSpan value = TermSpan.None;
        TermSpan language = TermSpan.None;
        TermSpan datatype = TermSpan.None;
        Span<byte> direction = stackalloc byte[3];
        int directionLength = 0;
        int subject = -1, predicate = -1, @object = -1;
        bool tripleValue = false;

        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("type"u8))
            {
                ExpectString(ref reader);
                type = reader.ValueTextEquals("uri"u8) ? TermType.Uri
                    : reader.ValueTextEquals("bnode"u8) ? TermType.BlankNode
                    : reader.ValueTextEquals("literal"u8) || reader.ValueTextEquals("typed-literal"u8) ? TermType.Literal
                    : reader.ValueTextEquals("triple"u8) ? TermType.Triple
                    : throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, Position(ref reader), "An unknown term type.");
            }
            else if (reader.ValueTextEquals("value"u8))
            {
                JsonTokenType token = Next(ref reader);
                if (token == JsonTokenType.String)
                {
                    value = Copy(ref reader, arena);
                }
                else if (token == JsonTokenType.StartObject)
                {
                    tripleValue = true;
                    while (Next(ref reader) == JsonTokenType.PropertyName)
                    {
                        if (reader.ValueTextEquals("subject"u8))
                        {
                            subject = ReadTerm(ref reader, arena);
                        }
                        else if (reader.ValueTextEquals("predicate"u8))
                        {
                            predicate = ReadTerm(ref reader, arena);
                        }
                        else if (reader.ValueTextEquals("object"u8))
                        {
                            @object = ReadTerm(ref reader, arena);
                        }
                        else
                        {
                            Skip(ref reader);
                        }
                    }
                }
                else
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, Position(ref reader), "A term's value is neither a string nor a triple.");
                }
            }
            else if (reader.ValueTextEquals("xml:lang"u8))
            {
                ExpectString(ref reader);
                language = Copy(ref reader, arena);
            }
            else if (reader.ValueTextEquals("datatype"u8))
            {
                ExpectString(ref reader);
                datatype = Copy(ref reader, arena);
            }
            else if (reader.ValueTextEquals("its:dir"u8))
            {
                ExpectString(ref reader);
                ReadOnlySpan<byte> dir = Unescape(ref reader);
                if (dir.Length > direction.Length)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, Position(ref reader), "A base direction is neither ltr nor rtl.");
                }

                dir.CopyTo(direction);
                directionLength = dir.Length;
            }
            else
            {
                Skip(ref reader);
            }
        }

        switch (type)
        {
            case TermType.Uri when value.IsPresent:
                RequireUtf8(arena, value, position);
                return arena.AddIri(value);
            case TermType.BlankNode when value.IsPresent:
                RequireUtf8(arena, value, position);
                return arena.AddBlankNode(value);
            case TermType.Literal when value.IsPresent:
                return AddLiteral(arena, value, datatype, language, direction[..directionLength], position);
            case TermType.Triple when tripleValue:
                if (subject < 0 || predicate < 0 || @object < 0)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A triple lacks a subject, a predicate or an object.");
                }

                return arena.AddTripleTerm(subject, predicate, @object);
            default:
                throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A term has no type, or no value of the right kind for its type.");
        }
    }

    // ---------------------------------------------------------------- plumbing

    private Utf8JsonReader Reader() => new(_input.Slice(_consumed), isFinalBlock: true, _state);

    private void Save(ref Utf8JsonReader reader)
    {
        _consumed += reader.BytesConsumed;
        _state = reader.CurrentState;
    }

    private JsonTokenType Next(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, Position(ref reader), "The document ended inside a value.");
        }

        return reader.TokenType;
    }

    private void Expect(ref Utf8JsonReader reader, JsonTokenType type, string message)
    {
        if (Next(ref reader) != type)
        {
            throw Structure(ref reader, message);
        }
    }

    private void ExpectString(ref Utf8JsonReader reader)
    {
        if (Next(ref reader) != JsonTokenType.String)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, Position(ref reader), "Expected a string.");
        }
    }

    private void Skip(ref Utf8JsonReader reader)
    {
        Next(ref reader);
        reader.Skip();
    }

    private void ExpectEndOfDocument(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw Structure(ref reader, "Expected the end of the document object.");
        }

        if (reader.Read())
        {
            throw Structure(ref reader, "Content after the document object.");
        }
    }

    private static TermSpan Copy(ref Utf8JsonReader reader, TermArena arena)
    {
        int length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        Span<byte> destination = arena.ReserveScratch(length);
        int written = reader.CopyString(destination);
        return arena.CommitScratch(written);
    }

    private ReadOnlySpan<byte> Unescape(ref Utf8JsonReader reader)
    {
        int length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        if (_unescaped.Length < length)
        {
            _unescaped = new byte[Math.Max(length, _unescaped.Length * 2)];
        }

        int written = reader.CopyString(_unescaped);
        return _unescaped.AsSpan(0, written);
    }

    /// <summary>The position of the reader's current token, as a line and a byte column counted over the document.</summary>
    private ResultsPosition Position(ref Utf8JsonReader reader) =>
        PositionAt(_consumed + reader.TokenStartIndex);

    private ResultsPosition PositionAt(long offset)
    {
        int line = 1;
        long lineStart = 0;
        long at = 0;
        foreach (ReadOnlyMemory<byte> segment in _input)
        {
            ReadOnlySpan<byte> span = segment.Span;
            for (int i = 0; i < span.Length && at < offset; i++, at++)
            {
                if (span[i] == (byte)'\n')
                {
                    line++;
                    lineStart = at + 1;
                }
            }

            if (at >= offset)
            {
                break;
            }
        }

        return new ResultsPosition(offset, line, (int)(offset - lineStart) + 1);
    }

    private ResultsSyntaxException Structure(ref Utf8JsonReader reader, string message) =>
        new(SparqlResultsErrorKind.UnexpectedStructure, Position(ref reader), message);

    private ResultsSyntaxException Malformed(ref Utf8JsonReader reader, JsonException error)
    {
        // The reader's state carries its line count across the readers rebuilt
        // at each call, so the exception's line is the document's: walk that
        // many line ends from the start, then the bytes into the line.
        long offset = 0;
        long lines = error.LineNumber ?? 0;
        long inLine = error.BytePositionInLine ?? 0;
        foreach (ReadOnlyMemory<byte> segment in _input)
        {
            ReadOnlySpan<byte> span = segment.Span;
            int i = 0;
            while (lines > 0 && i < span.Length)
            {
                if (span[i++] == (byte)'\n')
                {
                    lines--;
                }

                offset++;
            }

            if (lines == 0)
            {
                break;
            }
        }

        offset = Math.Min(offset + inLine, _input.Length);
        SparqlResultsErrorKind kind = error.Message.Contains("UTF-8", StringComparison.OrdinalIgnoreCase)
            ? SparqlResultsErrorKind.InvalidUtf8
            : offset >= _input.Length ? SparqlResultsErrorKind.UnexpectedEnd : SparqlResultsErrorKind.Malformed;
        return new ResultsSyntaxException(kind, PositionAt(offset), error.Message);
    }
}
