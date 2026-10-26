// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Store;

namespace Varve.Protocol.Http;

/// <summary>
/// A read with the report graphs the caller may read (ADR 0174): the scoped
/// view, with each binding's report at the view's position added as its
/// reserved graph, its terms staged on the view so the dictionary is not
/// touched. A report is read when a read first needs it — the report graph
/// named, or every graph matched — and then once; a report the projection
/// has not reached is <see cref="ValidationBehindException"/> there.
/// </summary>
internal sealed class ReportGraphs : IQuadSource
{
    /// <summary>The namespace every report graph is under, which no write may touch.</summary>
    internal const string Namespace = "https://w3id.org/varve/ns#validation/";

    private readonly IQuadSource _scoped;
    private readonly StagingView _staging;
    private readonly IReadOnlyList<IValidationReportGraph> _graphs;
    private readonly TermHandle[] _handles;
    private readonly bool[] _read;
    private readonly List<Quad> _quads = [];
    private readonly Dictionary<string, TermHandle> _blanks = new(StringComparer.Ordinal);
    private IQuadSource _current;

    private ReportGraphs(IQuadSource scoped, StagingView staging, IReadOnlyList<IValidationReportGraph> graphs)
    {
        _scoped = scoped;
        _current = scoped;
        _staging = staging;
        _graphs = graphs;
        _handles = new TermHandle[graphs.Count];
        _read = new bool[graphs.Count];
        for (int i = 0; i < graphs.Count; i++)
        {
            _handles[i] = staging.Stage(graphs[i].Graph);
        }
    }

    /// <summary>
    /// The caller's view of <paramref name="view"/> through
    /// <paramref name="readable"/>, with the report graphs of
    /// <paramref name="graphs"/> whose bound graphs it covers; the scoped view
    /// alone when there is none. The scope is over the view's staging view,
    /// so that the reports' staged terms are named through it.
    /// </summary>
    internal static IQuadSource Compose(DatasetView view, GraphScope readable, IReadOnlyList<IValidationReportGraph> graphs)
    {
        List<IValidationReportGraph> visible = [];
        foreach (IValidationReportGraph graph in graphs)
        {
            if (Covers(readable, graph.Validates))
            {
                visible.Add(graph);
            }
        }

        if (visible.Count == 0)
        {
            return GraphScopedQuadSource.Wrap(view, readable);
        }

        StagingView staging = view.Stage();
        return new ReportGraphs(GraphScopedQuadSource.Wrap(staging, readable), staging, visible);
    }

    /// <summary>
    /// Whether <paramref name="readable"/> holds every graph of
    /// <paramref name="validates"/> (ADR 0107, amended): every graph, the
    /// default graph, each named graph, and each prefix under one of its own.
    /// </summary>
    internal static bool Covers(GraphScope readable, GraphScope validates)
    {
        if (readable.IsAll)
        {
            return true;
        }

        if (validates.IsAll || (validates.IncludesDefault && !readable.IncludesDefault))
        {
            return false;
        }

        foreach (RdfTerm graph in validates.Graphs)
        {
            if (!readable.Allows(graph))
            {
                return false;
            }
        }

        foreach (string prefix in validates.Prefixes)
        {
            bool covered = false;
            foreach (string own in readable.Prefixes)
            {
                covered |= prefix.StartsWith(own, StringComparison.Ordinal);
            }

            if (!covered)
            {
                return false;
            }
        }

        return true;
    }

    public IEqualityComparer<TermHandle> TermComparer => _scoped.TermComparer;

    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        for (int i = 0; i < _graphs.Count; i++)
        {
            if (_graphs[i].Graph.Equals(term))
            {
                // Naming a report graph is reading it.
                Read(i, wait: true);
                handle = _handles[i];
                return true;
            }
        }

        if (_current.TryInternalise(term, out handle))
        {
            return true;
        }

        // A term only a report holds: the reports the projection has reached, read without waiting.
        bool added = false;
        for (int i = 0; i < _graphs.Count; i++)
        {
            added |= !_read[i] && Read(i, wait: false);
        }

        return added && _current.TryInternalise(term, out handle);
    }

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _current.TryExternalise(handle, out term);

    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _current.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    public bool Contains(in Quad quad)
    {
        Need(quad.Graph.IsNone ? GraphPattern.DefaultGraph : GraphPattern.Named(quad.Graph));
        return _current.Contains(in quad);
    }

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        Need(graph);
        return _current.Match(subject, predicate, @object, graph);
    }

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        Need(graph);
        return _current.Estimate(subject, predicate, @object, graph);
    }

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _current.TryGetInlineValue(handle, out value);

    /// <summary>The reports a match of <paramref name="graph"/> reads: the one it names, or every one for every graph.</summary>
    [DesignDecision(typeof(TheValidationProjection.ReportReadOnFirstNeed), Scope = ExceptionScope.HotPath)]
    private void Need(GraphPattern graph)
    {
        for (int i = 0; i < _graphs.Count; i++)
        {
            if (!_read[i] && (graph.Match is GraphMatch.Any or GraphMatch.AnyNamed || (graph.Match == GraphMatch.Named && TermComparer.Equals(graph.Graph, _handles[i]))))
            {
                Read(i, wait: true);
            }
        }
    }

    /// <summary>Adds report <paramref name="index"/> to the read: whether it was added. Waiting, a report not reached throws.</summary>
    private bool Read(int index, bool wait)
    {
        if (_read[index])
        {
            return true;
        }

        IValidationReportGraph graph = _graphs[index];
        ValidationOutcome outcome;
        if (wait)
        {
            outcome = graph.ReportAt(_staging.Position);
        }
        else if (!graph.TryReportAt(_staging.Position, out outcome))
        {
            return false;
        }

        _read[index] = true;

        // A position whose shapes graph is refused has no report: its graph is empty, and the binding's description says why.
        if (outcome.Report is { } report)
        {
            InMemoryDataset dataset = report.ToDataset();
            using IQuadCursor cursor = dataset.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                dataset.TryExternalise(quad.Subject, out RdfTerm? s);
                dataset.TryExternalise(quad.Predicate, out RdfTerm? p);
                dataset.TryExternalise(quad.Object, out RdfTerm? o);
                _quads.Add(new Quad(Stage(s!), Stage(p!), Stage(o!), _handles[index]));
            }
        }

        _current = new QuadOverlay(_scoped, QuadDelta.Create([.. _quads], []));
        return true;
    }

    /// <summary>A report term's handle: the view's when it holds the term, a staged one otherwise; a report's own blank node is fresh, once per label.</summary>
    private TermHandle Stage(RdfTerm term)
    {
        if (_staging.TryInternalise(term, out TermHandle handle))
        {
            return handle;
        }

        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                string label = System.Text.Encoding.UTF8.GetString(term.Lexical);
                if (!_blanks.TryGetValue(label, out handle))
                {
                    handle = _staging.StageBlank();
                    _blanks[label] = handle;
                }

                return handle;
            case RdfTermKind.TripleTerm:
                return _staging.StageTriple(Stage(term.Subject!), Stage(term.Predicate!), Stage(term.Object!));
            default:
                return _staging.Stage(term);
        }
    }
}
