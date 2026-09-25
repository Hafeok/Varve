// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;

namespace Varve.Sparql.Results;

/// <summary>
/// A forward cursor over a <see cref="ReadOnlySequence{T}"/> of UTF-8 that
/// counts lines and byte columns as it goes, and does not care where one
/// segment ends and the next begins. The XML, CSV and TSV readers walk it one
/// byte at a time and copy what they keep into the reader's arena, so a token
/// that straddles two segments costs nothing special.
/// </summary>
internal sealed class ByteCursor
{
    private readonly ReadOnlySequence<byte> _sequence;
    private SequencePosition _next;
    private ReadOnlyMemory<byte> _segment;
    private int _index;

    internal ByteCursor(ReadOnlySequence<byte> sequence)
    {
        _sequence = sequence;
        _next = sequence.Start;
        Line = 1;
        Column = 1;
        Advance();
    }

    internal long Offset { get; private set; }

    internal int Line { get; private set; }

    internal int Column { get; private set; }

    internal ResultsPosition Position => new(Offset, Line, Column);

    internal bool AtEnd => _index >= _segment.Length;

    /// <summary>The current byte, or -1 at the end.</summary>
    internal int Peek() => _index < _segment.Length ? _segment.Span[_index] : -1;

    /// <summary>The byte <paramref name="ahead"/> places after the current one, or -1.</summary>
    internal int PeekAt(int ahead)
    {
        int index = _index + ahead;
        if (index < _segment.Length)
        {
            return _segment.Span[index];
        }

        // Crossing into later segments is rare; walk them.
        index -= _segment.Length;
        SequencePosition position = _next;
        while (_sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            if (index < memory.Length)
            {
                return memory.Span[index];
            }

            index -= memory.Length;
        }

        return -1;
    }

    /// <summary>Consumes the current byte and returns it, or -1 at the end.</summary>
    internal int Next()
    {
        if (_index >= _segment.Length)
        {
            return -1;
        }

        byte value = _segment.Span[_index++];
        Offset++;
        if (value == (byte)'\n')
        {
            Line++;
            Column = 1;
        }
        else
        {
            Column++;
        }

        if (_index >= _segment.Length)
        {
            Advance();
        }

        return value;
    }

    /// <summary>Consumes the next bytes if they are exactly <paramref name="expected"/>.</summary>
    internal bool TryConsume(ReadOnlySpan<byte> expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (PeekAt(i) != expected[i])
            {
                return false;
            }
        }

        for (int i = 0; i < expected.Length; i++)
        {
            Next();
        }

        return true;
    }

    private void Advance()
    {
        _index = 0;
        _segment = default;
        while (_sequence.TryGet(ref _next, out ReadOnlyMemory<byte> memory))
        {
            if (!memory.IsEmpty)
            {
                _segment = memory;
                return;
            }
        }
    }
}
