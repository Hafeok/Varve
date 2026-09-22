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
    private readonly HashSet<int> _assigned = [];
    private readonly Dictionary<int, int> _claimed = [];
    private readonly List<int> _assignedPending = [];
    private readonly List<KeyValuePair<int, int>> _claimedPending = [];
    private int _next;
    private int _nextAtStatementStart;

    /// <summary>A number for a blank node the document did not name.</summary>
    internal int Mint()
    {
        while (IsAssigned(_next))
        {
            _next++;
        }

        int n = _next++;
        _assignedPending.Add(n);
        return n;
    }

    /// <summary>
    /// The number to use for a document's <c>g</c>-form label, which is the
    /// document's own unless it has already been handed out.
    /// </summary>
    internal int Claim(int n)
    {
        if (_claimed.TryGetValue(n, out int assigned))
        {
            return assigned;
        }

        foreach (KeyValuePair<int, int> pending in _claimedPending)
        {
            if (pending.Key == n)
            {
                return pending.Value;
            }
        }

        if (!IsAssigned(n))
        {
            _assignedPending.Add(n);
            _claimedPending.Add(new KeyValuePair<int, int>(n, n));
            return n;
        }

        int fresh = Mint();
        _claimedPending.Add(new KeyValuePair<int, int>(n, fresh));
        return fresh;
    }

    /// <summary>Drops everything the abandoned statement decided.</summary>
    internal void BeginStatement()
    {
        _assignedPending.Clear();
        _claimedPending.Clear();
        _next = _nextAtStatementStart;
    }

    /// <summary>Keeps everything the completed statement decided.</summary>
    internal void CompleteStatement()
    {
        foreach (int n in _assignedPending)
        {
            _assigned.Add(n);
        }

        foreach (KeyValuePair<int, int> claim in _claimedPending)
        {
            _claimed[claim.Key] = claim.Value;
        }

        _assignedPending.Clear();
        _claimedPending.Clear();
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

    private bool IsAssigned(int n) => _assigned.Contains(n) || _assignedPending.Contains(n);
}
