// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Turtle;

/// <summary>
/// Decides what a blank node is called, so that no two nodes share a label and
/// a label the parser emits is one it would read back unchanged.
/// </summary>
/// <remarks>
/// <para>
/// A Turtle document names some of its blank nodes (<c>_:x</c>) and leaves the
/// rest to the parser (<c>[]</c>, <c>()</c>). Both kinds have to come out with
/// distinct labels, and a streaming parser cannot see the document's names
/// before it has to invent its own.
/// </para>
/// <para>
/// <strong>A document's own label passes through unchanged.</strong> That is
/// what makes a round trip a fixed point: the writer emits every blank node as
/// an explicit label and never as <c>[]</c> or <c>()</c>, so re-reading the
/// writer's output invents nothing, every label passes through, and writing it
/// again is byte-identical. An earlier scheme put a <c>b</c> in front of each
/// document label, which was collision-free and renamed every node on every
/// pass — one byte per trip, which a pipeline of stages pays repeatedly.
/// </para>
/// <para>
/// <strong>Invented labels are <c>g0</c>, <c>g1</c>, …, and the document may
/// claim one.</strong> A document is allowed to write <c>_:g0</c>, so the
/// generated form is not reserved and cannot be: every label this parser can
/// emit is one a document is allowed to contain, which is why "pick a form no
/// input can take" has no answer here. Instead a claim is honoured — the
/// generator skips a number the document has used — and the one case that
/// cannot be honoured, a document claiming a number already handed out, is
/// resolved by giving <em>that</em> occurrence a fresh number instead.
/// </para>
/// <para>
/// <strong>The honest limit.</strong> A document that uses <c>g</c>-form labels
/// <em>and</em> anonymous nodes may have some of its labels renamed once. The
/// result is still a fixed point — writing it and reading it back renames
/// nothing further — but that first pass is not label-preserving, and no
/// single-pass parser can make it so without reserving names the grammar
/// allows a document to use.
/// </para>
/// <para>
/// <strong>Pending versus committed.</strong> A statement that runs past the
/// end of a chunk is scanned again when the next one arrives, so every decision
/// this type makes during a statement is held aside until the statement
/// completes. Without that, the second attempt would see the first attempt's
/// labels as already taken and answer differently — the same defect the fresh
/// counter's rewind exists to prevent, one level up.
/// </para>
/// </remarks>
internal sealed class BlankNodeNaming
{
    // Null until a document writes a g-form label, which almost none do. Held
    // that way rather than allocated eagerly because a parse pays for what it
    // uses, and these three are the naming's whole cost.
    private HashSet<int>? _claimedNumbers;
    private Dictionary<int, int>? _claimed;
    private List<KeyValuePair<int, int>>? _claimedPending;
    private int _next;
    private int _nextAtStatementStart;

    /// <summary>A number for a blank node the document did not name.</summary>
    internal int Mint()
    {
        while (IsClaimed(_next))
        {
            _next++;
        }

        return _next++;
    }

    /// <summary>
    /// Whether a number has been given to a document label, in this statement
    /// or an earlier one.
    /// </summary>
    /// <remarks>
    /// The pending half is not an optimisation. A single statement can claim a
    /// number and then mint one — <c>_:g0 :p [ :q :r ]</c> does — and if the
    /// claim is only visible after the statement completes, the mint hands out
    /// the number the claim just took and two nodes share a label.
    /// </remarks>
    private bool IsClaimed(int n)
    {
        if (_claimedNumbers is not null && _claimedNumbers.Contains(n))
        {
            return true;
        }

        if (_claimedPending is not null)
        {
            foreach (KeyValuePair<int, int> pending in _claimedPending)
            {
                if (pending.Value == n)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The number to use for a document's <c>g</c>-form label, which is the
    /// document's own unless it has already been handed out.
    /// </summary>
    internal int Claim(int n)
    {
        if (_claimed is not null && _claimed.TryGetValue(n, out int assigned))
        {
            return assigned;
        }

        if (_claimedPending is not null)
        {
            foreach (KeyValuePair<int, int> pending in _claimedPending)
            {
                if (pending.Key == n)
                {
                    return pending.Value;
                }
            }
        }

        // A number the counter has not reached is the document's for the
        // asking, and recording it is what makes the generator skip it.
        // Below the counter it was handed out — to an anonymous node, or to an
        // earlier claim that was itself moved — so this occurrence moves.
        if (n >= _next)
        {
            (_claimedPending ??= []).Add(new KeyValuePair<int, int>(n, n));
            return n;
        }

        int fresh = Mint();
        (_claimedPending ??= []).Add(new KeyValuePair<int, int>(n, fresh));
        return fresh;
    }

    /// <summary>Drops everything the abandoned statement decided.</summary>
    internal void BeginStatement()
    {
        _claimedPending?.Clear();
        _next = _nextAtStatementStart;
    }

    /// <summary>Keeps everything the completed statement decided.</summary>
    /// <remarks>
    /// Both collections stay empty for a document that writes no
    /// <c>g</c>-form label, which is almost all of them — so the naming costs
    /// nothing per blank node in the ordinary case, and the loop below does not
    /// run. An earlier version recorded every number it handed out, which was
    /// simpler to reason about and allocated in proportion to the blank nodes
    /// in the document; the allocation assertion caught it.
    /// </remarks>
    internal void CompleteStatement()
    {
        if (_claimedPending is not null && _claimedPending.Count > 0)
        {
            _claimed ??= [];
            _claimedNumbers ??= [];

            foreach (KeyValuePair<int, int> claim in _claimedPending)
            {
                _claimed[claim.Key] = claim.Value;
                _claimedNumbers.Add(claim.Value);
            }

            _claimedPending.Clear();
        }

        _nextAtStatementStart = _next;
    }

    /// <summary>
    /// The number in a label of the generated form <c>g</c> followed by digits,
    /// or -1 for any other label.
    /// </summary>
    /// <remarks>
    /// Deliberately strict: <c>g007</c> is not the generated form, because the
    /// generator never writes a leading zero and treating it as <c>g7</c> would
    /// merge two labels a document kept apart.
    /// </remarks>
    internal static int GeneratedIndex(System.ReadOnlySpan<byte> label)
    {
        if (label.Length < 2 || label[0] != (byte)'g')
        {
            return -1;
        }

        if (label.Length > 2 && label[1] == (byte)'0')
        {
            return -1;
        }

        int value = 0;

        for (int i = 1; i < label.Length; i++)
        {
            if (label[i] is not (>= (byte)'0' and <= (byte)'9'))
            {
                return -1;
            }

            // A label longer than any counter could reach is not the generated
            // form, and stopping here also keeps the arithmetic from wrapping.
            if (value > (int.MaxValue - 9) / 10)
            {
                return -1;
            }

            value = (value * 10) + (label[i] - (byte)'0');
        }

        return value;
    }
}
