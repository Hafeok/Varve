// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>
/// One <see cref="IQuadSource.Match"/> over the active graph (§6.9, §6.11):
/// the source's default graph, or the merge of the <c>FROM</c> graphs with a
/// triple in two of them returned once, or one named graph, or every named
/// graph (restricted to <c>FROM NAMED</c> when there is one). Reused by its
/// owner from scan to scan, so opening one allocates only the source's own
/// cursor.
/// </summary>
internal sealed class ScanCursor
{
    private Exec _exec = null!;
    private IQuadCursor? _cursor;
    private TermHandle _subject;
    private TermHandle _predicate;
    private TermHandle _object;
    private TermHandle[]? _graphs;
    private int _nextGraph;
    private HashSet<(ulong, ulong, ulong)>? _seen;
    private bool _filterNamed;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal Quad Current { get; private set; }

    /// <summary>
    /// Opens a scan. False when nothing can match — a graph the source does not
    /// hold, or an empty default graph — in which case no cursor is opened.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Open(Exec exec, TermHandle subject, TermHandle predicate, TermHandle @object, ActiveGraph graph, ulong[] row)
    {
        Close();
        _exec = exec;
        _subject = subject;
        _predicate = predicate;
        _object = @object;
        _filterNamed = false;
        _graphs = null;

        switch (graph.Mode)
        {
            case GraphMode.Default:
                if (exec.DefaultGraphs is not { } defaults)
                {
                    _cursor = Match(GraphPattern.DefaultGraph);
                    return true;
                }

                if (defaults.Length == 0)
                {
                    return false;
                }

                _cursor = Match(GraphPattern.Named(defaults[0]));
                if (defaults.Length > 1)
                {
                    _graphs = defaults;
                    _nextGraph = 1;
                    ForgetSeen();
                }

                return true;

            case GraphMode.Named:
                _cursor = Match(GraphPattern.Named(graph.Graph));
                return true;

            default:
                if (row[graph.Slot] != 0)
                {
                    if (!exec.TryGetSourceHandle(Rows.Get(row, exec.Width, graph.Slot), out TermHandle named)
                        || (exec.NamedGraphSet is { } set && !set.Contains(named)))
                    {
                        return false;
                    }

                    _cursor = Match(GraphPattern.Named(named));
                    return true;
                }

                _filterNamed = exec.NamedGraphSet is not null;
                _cursor = Match(GraphPattern.AnyNamed);
                return true;
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool MoveNext()
    {
        while (_cursor is not null)
        {
            while (_cursor.MoveNext())
            {
                _exec.Step();
                Quad quad = _cursor.Current;
                if (_filterNamed && !_exec.NamedGraphSet!.Contains(quad.Graph))
                {
                    continue;
                }

                if (_seen is not null && _graphs is not null && !FirstTime(in quad))
                {
                    continue;
                }

                Current = quad;
                return true;
            }

            CloseSource();
            if (_graphs is not null && _nextGraph < _graphs.Length)
            {
                _cursor = Match(GraphPattern.Named(_graphs[_nextGraph++]));
            }
        }

        return false;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Close()
    {
        if (_cursor is not null)
        {
            CloseSource();
        }
    }

    // What a scan asks of the source: a cursor per graph it reads, disposed
    // when that graph is done. The source may allocate it; this type never
    // does, since its owner reuses it from scan to scan.
    [DesignDecision(typeof(EvaluationHotPathScope.ScanOpensTheSourcesCursor), Scope = ExceptionScope.HotPath)]
    private IQuadCursor Match(GraphPattern graph) => _exec.Source.Match(_subject, _predicate, _object, graph);

    [DesignDecision(typeof(EvaluationHotPathScope.ScanOpensTheSourcesCursor), Scope = ExceptionScope.HotPath)]
    private void CloseSource()
    {
        _cursor!.Dispose();
        _cursor = null;
    }

    // The merge of several FROM graphs returns a triple in two of them once,
    // so the scan remembers what it returned.
    [DesignDecision(typeof(EvaluationHotPathScope.FromMergeRemembersTriples), Scope = ExceptionScope.HotPath)]
    private void ForgetSeen()
    {
        _seen ??= [];
        _seen.Clear();
    }

    [DesignDecision(typeof(EvaluationHotPathScope.FromMergeRemembersTriples), Scope = ExceptionScope.HotPath)]
    private bool FirstTime(in Quad quad) => _seen!.Add((quad.Subject.Value, quad.Predicate.Value, quad.Object.Value));
}
