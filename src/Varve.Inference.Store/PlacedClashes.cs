// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using Varve.Inference.Model;

namespace Varve.Inference.Store;

/// <summary>
/// A binding's clashes as one list, over the pieces each closure placed: what
/// a snapshot publishes without copying every clash (ADR 0195 point 6,
/// amended 2026-11-25). The pieces are not changed once published: a pass
/// that changes a closure's clashes places a new piece.
/// </summary>
internal sealed class PlacedClashes : IReadOnlyList<PlacedClash>
{
    private readonly IReadOnlyList<PlacedClash>[] _pieces;
    private readonly int[] _starts;

    internal PlacedClashes(List<IReadOnlyList<PlacedClash>> pieces)
    {
        _pieces = [.. pieces];
        _starts = new int[_pieces.Length];
        int count = 0;
        for (int i = 0; i < _pieces.Length; i++)
        {
            _starts[i] = count;
            count += _pieces[i].Count;
        }

        Count = count;
    }

    public int Count { get; }

    public PlacedClash this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            int piece = Array.BinarySearch(_starts, index);
            if (piece < 0)
            {
                piece = ~piece - 1;
            }

            // Empty pieces share a start with the next: the last piece starting at or before the index that holds it.
            while (_pieces[piece].Count == 0 || index - _starts[piece] >= _pieces[piece].Count)
            {
                piece++;
            }

            return _pieces[piece][index - _starts[piece]];
        }
    }

    public IEnumerator<PlacedClash> GetEnumerator()
    {
        foreach (IReadOnlyList<PlacedClash> piece in _pieces)
        {
            foreach (PlacedClash clash in piece)
            {
                yield return clash;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
