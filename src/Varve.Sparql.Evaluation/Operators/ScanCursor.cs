// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
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

    internal Quad Current { get; private set; }

    /// <summary>
    /// Opens a scan. False when nothing can match — a graph the source does not
    /// hold, or an empty default graph — in which case no cursor is opened.
    /// </summary>
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
                    _cursor = exec.Source.Match(subject, predicate, @object, GraphPattern.DefaultGraph);
                    return true;
                }

                if (defaults.Length == 0)
                {
                    return false;
                }

                _cursor = exec.Source.Match(subject, predicate, @object, GraphPattern.Named(defaults[0]));
                if (defaults.Length > 1)
                {
                    _graphs = defaults;
                    _nextGraph = 1;
                    _seen ??= [];
                    _seen.Clear();
                }

                return true;

            case GraphMode.Named:
                _cursor = exec.Source.Match(subject, predicate, @object, GraphPattern.Named(graph.Graph));
                return true;

            default:
                if (row[graph.Slot] != 0)
                {
                    if (!exec.TryGetSourceHandle(Rows.Get(row, exec.Width, graph.Slot), out TermHandle named)
                        || (exec.NamedGraphSet is { } set && !set.Contains(named)))
                    {
                        return false;
                    }

                    _cursor = exec.Source.Match(subject, predicate, @object, GraphPattern.Named(named));
                    return true;
                }

                _filterNamed = exec.NamedGraphSet is not null;
                _cursor = exec.Source.Match(subject, predicate, @object, GraphPattern.AnyNamed);
                return true;
        }
    }

    [HotPath]
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

                if (_seen is not null && _graphs is not null
                    && !_seen.Add((quad.Subject.Value, quad.Predicate.Value, quad.Object.Value)))
                {
                    continue;
                }

                Current = quad;
                return true;
            }

            _cursor.Dispose();
            _cursor = null;
            if (_graphs is not null && _nextGraph < _graphs.Length)
            {
                _cursor = _exec.Source.Match(_subject, _predicate, _object, GraphPattern.Named(_graphs[_nextGraph++]));
            }
        }

        return false;
    }

    internal void Close()
    {
        _cursor?.Dispose();
        _cursor = null;
    }
}
