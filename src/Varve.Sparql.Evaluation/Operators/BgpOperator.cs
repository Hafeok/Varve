// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>
/// A basic graph pattern (§18.3, <c>sparql-evaluation.md</c> §6.9): its triple
/// patterns matched depth-first, in the order the optimiser left them, each
/// scan bound by what the patterns before it bound.
/// </summary>
internal sealed class BgpOperator(TriplePatternSpec[] patterns) : Operator
{
    internal TriplePatternSpec[] Patterns { get; } = patterns;

    internal override bool Substitutable => true;

    internal override bool ScansBindGraph => Patterns.Length > 0;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        foreach (TriplePatternSpec pattern in Patterns)
        {
            if (!pattern.CanMatch)
            {
                return Solutions.Empty;
            }
        }

        return Patterns.Length == 0 ? Solutions.Once(Rows.Copy(input)) : new BgpCursor(exec, Patterns, input, graph);
    }
}

/// <summary>
/// The depth-first match of a basic graph pattern. One working solution is
/// bound and unbound in place as the search moves; a solution array is
/// allocated only when the last pattern matches, which is what makes the cost
/// per quad scanned and not matched zero (§11).
/// </summary>
internal sealed class BgpCursor : IEnumerator<ulong[]>
{
    private readonly Exec _exec;
    private readonly TriplePatternSpec[] _patterns;
    private readonly ActiveGraph _graph;
    private readonly ulong[] _work;
    private readonly ScanCursor[] _scans;
    private readonly int[] _bound;
    private readonly int[] _boundCount;
    private readonly bool[] _open;
    private int _level = -1;
    private bool _done;

    internal BgpCursor(Exec exec, TriplePatternSpec[] patterns, ulong[] input, ActiveGraph graph)
    {
        _exec = exec;
        _patterns = patterns;
        _graph = graph;
        _work = Rows.Copy(input);
        _scans = new ScanCursor[patterns.Length];
        _open = new bool[patterns.Length];
        _bound = new int[patterns.Length * 8];
        _boundCount = new int[patterns.Length];
        for (int i = 0; i < patterns.Length; i++)
        {
            _scans[i] = new ScanCursor();
        }
    }

    public ulong[] Current { get; private set; } = [];

    object IEnumerator.Current => Current;

    [HotPath]
    public bool MoveNext()
    {
        if (_done)
        {
            return false;
        }

        if (_level < 0)
        {
            _level = 0;
            OpenLevel(0);
        }

        while (true)
        {
            if (Advance(_level))
            {
                if (_level == _patterns.Length - 1)
                {
                    _exec.Check();
                    Current = Rows.Copy(_work);
                    return true;
                }

                _level++;
                OpenLevel(_level);
                continue;
            }

            _scans[_level].Close();
            _open[_level] = false;
            if (_level == 0)
            {
                _done = true;
                return false;
            }

            _level--;
        }
    }

    public void Reset() => throw new NotSupportedException();

    public void Dispose()
    {
        foreach (ScanCursor scan in _scans)
        {
            scan.Close();
        }

        _done = true;
    }

    /// <summary>Opens level <paramref name="level"/>'s scan against the working solution.</summary>
    [HotPath]
    private void OpenLevel(int level)
    {
        _boundCount[level] = 0;
        TriplePatternSpec pattern = _patterns[level];
        _open[level] = Resolve(pattern.Subject, out TermHandle s)
            && Resolve(pattern.Predicate, out TermHandle p)
            && Resolve(pattern.Object, out TermHandle o)
            && _scans[level].Open(_exec, s, p, o, _graph, _work);
    }

    /// <summary>A position as a scan argument: a handle, or the wildcard; false when nothing can match.</summary>
    [HotPath]
    private bool Resolve(in PatternPosition position, out TermHandle handle)
    {
        handle = default;
        switch (position.Kind)
        {
            case PositionKind.Constant:
                handle = position.Handle;
                return true;
            case PositionKind.Slot:
                return _work[position.Slot] == 0
                    || _exec.TryGetSourceHandle(Rows.Get(_work, _exec.Width, position.Slot), out handle);
            case PositionKind.Nested:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves level <paramref name="level"/> to its next quad that unifies with the working solution.</summary>
    [HotPath]
    private bool Advance(int level)
    {
        Unbind(level);
        if (!_open[level])
        {
            return false;
        }

        ScanCursor scan = _scans[level];
        TriplePatternSpec pattern = _patterns[level];
        while (scan.MoveNext())
        {
            Quad quad = scan.Current;
            if (Unify(level, pattern.Subject, quad.Subject)
                && Unify(level, pattern.Predicate, quad.Predicate)
                && Unify(level, pattern.Object, quad.Object)
                && UnifyGraph(level, quad.Graph))
            {
                return true;
            }

            Unbind(level);
        }

        return false;
    }

    [HotPath]
    private bool Unify(int level, in PatternPosition position, TermHandle found)
    {
        switch (position.Kind)
        {
            case PositionKind.Slot:
                return UnifySlot(level, position.Slot, _exec.FromSource(found));
            case PositionKind.Nested:
                return _exec.Source.TryExternalise(found, out RdfTerm? term) && UnifyNested(level, position.Nested!, term);
            default:
                return true;
        }
    }

    [HotPath]
    private bool UnifySlot(int level, int slot, TermRef value)
    {
        if (_work[slot] != 0)
        {
            return _exec.TermEquals(Rows.Get(_work, _exec.Width, slot), value);
        }

        Rows.Set(_work, _exec.Width, slot, value);
        _bound[(level * 8) + _boundCount[level]++] = slot;
        return true;
    }

    private bool UnifyGraph(int level, TermHandle graph) =>
        _graph.Mode != GraphMode.Slot || UnifySlot(level, _graph.Slot, _exec.FromSource(graph));

    /// <summary>A triple term found, against a triple term pattern with variables inside (1.2).</summary>
    private bool UnifyNested(int level, NestedPattern pattern, RdfTerm term) =>
        term.Kind == RdfTermKind.TripleTerm
        && UnifyTerm(level, pattern.Subject, term.Subject!)
        && UnifyTerm(level, pattern.Predicate, term.Predicate!)
        && UnifyTerm(level, pattern.Object, term.Object!);

    private bool UnifyTerm(int level, in PatternPosition position, RdfTerm term)
    {
        switch (position.Kind)
        {
            case PositionKind.Constant:
            case PositionKind.Never:
                return position.Constant!.Equals(term);
            case PositionKind.Slot:
                if (_boundCount[level] == 8)
                {
                    throw new NotSupportedException("A triple term pattern binds more than eight variables in one position.");
                }

                return UnifySlot(level, position.Slot, _exec.Intern(term));
            default:
                return UnifyNested(level, position.Nested!, term);
        }
    }

    [HotPath]
    private void Unbind(int level)
    {
        int count = _boundCount[level];
        for (int i = 0; i < count; i++)
        {
            Rows.Clear(_work, _exec.Width, _bound[(level * 8) + i]);
        }

        _boundCount[level] = 0;
    }
}

/// <summary>Small enumerators with no state worth a class of their own.</summary>
internal static class Solutions
{
    internal static IEnumerator<ulong[]> Empty => ((IEnumerable<ulong[]>)Array.Empty<ulong[]>()).GetEnumerator();

    internal static IEnumerator<ulong[]> Once(ulong[] row) => ((IEnumerable<ulong[]>)new[] { row }).GetEnumerator();
}
