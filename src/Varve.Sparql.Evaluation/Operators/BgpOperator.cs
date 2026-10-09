// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>
/// A basic graph pattern (§18.3, <c>sparql-evaluation.md</c> §6.9): its triple
/// patterns matched depth-first, in the order the optimiser left them, each
/// scan bound by what the patterns before it bound.
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class BgpOperator(TriplePatternSpec[] patterns) : Operator
{
    /// <summary>The cursor the last <see cref="BgpCursor.Dispose"/> handed back, for the next <see cref="Open"/>.</summary>
    private BgpCursor? _spare;

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

        // The right side of a bind join is opened once per left solution, and
        // each cursor is disposed before the next is opened: one spare is
        // enough to make the cursor once per execution.
        BgpCursor? spare = _spare;
        if (spare is not null && spare.Exec == exec)
        {
            _spare = null;
            spare.Restart(input, graph);
            return spare;
        }

        return NewCursor(exec, input, graph);
    }

    /// <summary>Takes a disposed cursor back, for the next <see cref="Open"/>.</summary>
    internal void Return(BgpCursor cursor) => _spare = cursor;

    [DesignDecision(typeof(EvaluationHotPathScope.OperatorStateIsMadeOncePerExecution), Scope = ExceptionScope.HotPath)]
    private BgpCursor NewCursor(Exec exec, ulong[] input, ActiveGraph graph) => new(this, exec, Patterns, input, graph);
}

/// <summary>
/// The depth-first match of a basic graph pattern. One working solution is
/// bound and unbound in place as the search moves; a solution array is
/// allocated only when the last pattern matches, which is what makes the cost
/// per quad scanned and not matched zero (§11). Disposing it hands it back to
/// its operator, which restarts it for the next incoming solution: a bind
/// join's right side makes one cursor per execution, not one per left solution.
/// With no patterns, it yields the incoming solution once.
/// </summary>
internal sealed class BgpCursor : IEnumerator<ulong[]>
{
    private readonly BgpOperator _owner;
    private readonly TriplePatternSpec[] _patterns;
    private ActiveGraph _graph;
    private readonly ulong[] _work;
    private readonly ScanCursor[] _scans;
    private readonly int[] _bound;
    private readonly int[] _boundCount;
    private readonly bool[] _open;
    private int _level = -1;
    private bool _done;
    private bool _returned;

    internal BgpCursor(BgpOperator owner, Exec exec, TriplePatternSpec[] patterns, ulong[] input, ActiveGraph graph)
    {
        _owner = owner;
        Exec = exec;
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

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal Exec Exec { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public ulong[] Current { get; private set; } = [];

    object IEnumerator.Current => Current;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool MoveNext()
    {
        if (_done)
        {
            return false;
        }

        if (_level < 0)
        {
            if (_patterns.Length == 0)
            {
                _done = true;
                Current = Rows.Copy(_work);
                return true;
            }

            _level = 0;
            OpenLevel(0);
        }

        while (true)
        {
            if (Advance(_level))
            {
                if (_level == _patterns.Length - 1)
                {
                    Exec.Check();
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

    [DesignDecision(typeof(EvaluationSurfaces.OperatorsAreEnumerators), Scope = ExceptionScope.Compatibility)]
    public void Reset() => throw new NotSupportedException();

    /// <summary>Starts again from a new incoming solution, as a new cursor would.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Restart(ulong[] input, ActiveGraph graph)
    {
        input.AsSpan().CopyTo(_work);
        _graph = graph;
        _level = -1;
        _done = false;
        _returned = false;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public void Dispose()
    {
        foreach (ScanCursor scan in _scans)
        {
            scan.Close();
        }

        _done = true;
        if (!_returned)
        {
            _returned = true;
            _owner.Return(this);
        }
    }

    /// <summary>Opens level <paramref name="level"/>'s scan against the working solution.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void OpenLevel(int level)
    {
        _boundCount[level] = 0;
        TriplePatternSpec pattern = _patterns[level];
        _open[level] = Resolve(pattern.Subject, out TermHandle s)
            && Resolve(pattern.Predicate, out TermHandle p)
            && Resolve(pattern.Object, out TermHandle o)
            && _scans[level].Open(Exec, s, p, o, _graph, _work);
    }

    /// <summary>A position as a scan argument: a handle, or the wildcard; false when nothing can match.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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
                    || Exec.TryGetSourceHandle(Rows.Get(_work, Exec.Width, position.Slot), out handle);
            case PositionKind.Nested:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves level <paramref name="level"/> to its next quad that unifies with the working solution.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Unify(int level, in PatternPosition position, TermHandle found)
    {
        switch (position.Kind)
        {
            case PositionKind.Slot:
                return UnifySlot(level, position.Slot, Exec.FromSource(found));
            case PositionKind.Nested:
                return UnifyExternalised(level, position.Nested!, found);
            default:
                return true;
        }
    }

    /// <summary>
    /// A triple term found, against a nested pattern. By handles when the
    /// source can take the term apart (<see cref="IQuadSource.TryGetTripleTermComponents"/>,
    /// ADR 0110) — the only way a blank node inside a triple term keeps its
    /// identity over a source that never internalises a label (ADR 0044) — and
    /// as an externalised term otherwise.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool UnifyExternalised(int level, NestedPattern nested, TermHandle found)
    {
        if (Exec.Source.TryGetTripleTermComponents(found, out TermHandle s, out TermHandle p, out TermHandle o))
        {
            return UnifyComponent(level, nested.Subject, s)
                && UnifyComponent(level, nested.Predicate, p)
                && UnifyComponent(level, nested.Object, o);
        }

        return UnifyMaterialised(level, nested, found);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool UnifyComponent(int level, in PatternPosition position, TermHandle handle)
    {
        switch (position.Kind)
        {
            case PositionKind.Constant:
                return Exec.HandleEquals(handle, position.Handle);
            case PositionKind.Never:
                return false;
            case PositionKind.Slot:
                if (_boundCount[level] == 8)
                {
                    throw new NotSupportedException("A triple term pattern binds more than eight variables in one position.");
                }

                return UnifySlot(level, position.Slot, Exec.FromSource(handle));
            default:
                return UnifyExternalised(level, position.Nested!, handle);
        }
    }

    // A triple term the source cannot take apart is matched as a term: the
    // source is asked for it.
    [DesignDecision(typeof(EvaluationHotPathScope.NestedPatternsExternalise), Scope = ExceptionScope.HotPath)]
    private bool UnifyMaterialised(int level, NestedPattern nested, TermHandle found) =>
        Exec.Source.TryExternalise(found, out RdfTerm? term) && UnifyNested(level, nested, term);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool UnifySlot(int level, int slot, TermRef value)
    {
        if (_work[slot] != 0)
        {
            return Exec.TermEquals(Rows.Get(_work, Exec.Width, slot), value);
        }

        Rows.Set(_work, Exec.Width, slot, value);
        _bound[(level * 8) + _boundCount[level]++] = slot;
        return true;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool UnifyGraph(int level, TermHandle graph) =>
        _graph.Mode != GraphMode.Slot || UnifySlot(level, _graph.Slot, Exec.FromSource(graph));

    /// <summary>A triple term found, against a triple term pattern with variables inside (1.2).</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool UnifyNested(int level, NestedPattern pattern, RdfTerm term) =>
        term.Kind == RdfTermKind.TripleTerm
        && UnifyTerm(level, pattern.Subject, term.Subject!)
        && UnifyTerm(level, pattern.Predicate, term.Predicate!)
        && UnifyTerm(level, pattern.Object, term.Object!);

    [DesignDecision(typeof(EvaluationHotPathScope.NestedPatternsExternalise), Scope = ExceptionScope.HotPath)]
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

                return UnifySlot(level, position.Slot, Exec.Intern(term));
            default:
                return UnifyNested(level, position.Nested!, term);
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Unbind(int level)
    {
        int count = _boundCount[level];
        for (int i = 0; i < count; i++)
        {
            Rows.Clear(_work, Exec.Width, _bound[(level * 8) + i]);
        }

        _boundCount[level] = 0;
    }
}

/// <summary>Small enumerators with no state worth a class of their own.</summary>
internal static class Solutions
{
    /// <summary>No solutions: one enumerator for every caller, since it has no state.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static IEnumerator<ulong[]> Empty => NoSolutions.Instance;

    internal static IEnumerator<ulong[]> Once(ulong[] row) => ((IEnumerable<ulong[]>)new[] { row }).GetEnumerator();
}

/// <summary>The enumerator of no solutions. It has no state, so one serves every caller.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class NoSolutions : IEnumerator<ulong[]>
{
    internal static readonly NoSolutions Instance = new();

    private NoSolutions()
    {
    }

    public ulong[] Current => throw new InvalidOperationException("There are no solutions.");

    object IEnumerator.Current => Current;

    public bool MoveNext() => false;

    [DesignDecision(typeof(EvaluationSurfaces.OperatorsAreEnumerators), Scope = ExceptionScope.Compatibility)]
    public void Reset() => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
