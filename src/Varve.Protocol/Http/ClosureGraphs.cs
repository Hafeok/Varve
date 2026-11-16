// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Store;

namespace Varve.Protocol.Http;

/// <summary>Which part of a graph a read sees (ADR 0198 point 1).</summary>
[Flags]
internal enum ClosurePart
{
    /// <summary>The quads asserted.</summary>
    Explicit = 1,

    /// <summary>The quads its binding's closure entails and are not asserted.</summary>
    Implicit = 2,

    /// <summary>Both: the graph under <c>owl2-rl</c>, or both views named.</summary>
    Both = Explicit | Implicit,
}

/// <summary>
/// What a read makes of the closure (ADR 0198): the part its default graph
/// and its named graphs are read in, and, for a query that names a view, the
/// dataset its <c>FROM</c> and <c>FROM NAMED</c> give, which this source then
/// is. A plain read is <see cref="Plain"/>: every graph explicit, and the
/// reserved graphs added.
/// </summary>
/// <param name="Default">The part the default graph's graphs are read in.</param>
/// <param name="Named">The part each named graph is read in.</param>
/// <param name="DefaultGraphs">The graphs merged as the default graph; null for the dataset's own default graph.</param>
/// <param name="NamedGraphs">The named graphs; null for every one, and empty for none.</param>
internal sealed record ClosureMode(ClosurePart Default, ClosurePart Named, IReadOnlyList<RdfTerm>? DefaultGraphs, IReadOnlyList<RdfTerm>? NamedGraphs)
{
    /// <summary>A read that names no view under simple entailment: the explicit part, and the reserved graphs.</summary>
    internal static ClosureMode Plain { get; } = new(ClosurePart.Explicit, ClosurePart.Explicit, null, null);

    /// <summary>Every graph read as explicit ∪ implicit: <c>Varve-Entailment: owl2-rl</c> (ADR 0197 point 6).</summary>
    internal static ClosureMode Union { get; } = new(ClosurePart.Both, ClosurePart.Both, null, null);

    /// <summary>Whether the read sees no implicit part of any data graph.</summary>
    internal bool ExplicitOnly => Default == ClosurePart.Explicit && Named == ClosurePart.Explicit && DefaultGraphs is null && NamedGraphs is null;
}

/// <summary>
/// A read with the materialised closures the caller may read (ADRs 0195,
/// 0198): each data graph in the part <see cref="ClosureMode"/> asks for,
/// and each binding's reserved graph of TBox conclusions added as a named
/// graph. The closures' rows are read through the tier-2 seam
/// (<see cref="IInferenceClosure"/>) when a read first needs them, and then
/// once; one its projection has not reached is
/// <see cref="InferenceBehindException"/> there, one stopped by its memory
/// limit <see cref="InferenceUnavailableException"/>. Their terms are staged
/// on the read's view, never allocated.
/// </summary>
/// <remarks>
/// A term lookup never denies a term while a closure is unread (ADR 0022, as
/// amended 2026-10-30; #186): it names it, staged as a closure's row would
/// name it, so that a pattern naming it waits for the closure, or is
/// <c>inference-behind</c>, rather than matching nothing.
/// </remarks>
[Contract(typeof(TheViewsAndTheReservedGraphs.ClosureSourcesHoldTheContract), Role = "a read with the closures it may read")]
internal sealed class ClosureGraphs : IQuadSource
{
    private readonly IQuadSource _base;
    private readonly StagingView _staging;
    private readonly IReadOnlyList<IInferenceClosure> _closures;
    private readonly ClosureMode _mode;
    private readonly TermHandle[] _reserved;
    private readonly Dictionary<TermHandle, List<Quad>> _implicit;
    private readonly Dictionary<TermHandle, IReadOnlyList<RdfTerm>> _premiseSets;
    private bool _read;

    private ClosureGraphs(IQuadSource scoped, StagingView staging, IReadOnlyList<IInferenceClosure> closures, ClosureMode mode)
    {
        _base = scoped;
        _staging = staging;
        _closures = closures;
        _mode = mode;
        _implicit = new Dictionary<TermHandle, List<Quad>>(staging.TermComparer);
        _premiseSets = new Dictionary<TermHandle, IReadOnlyList<RdfTerm>>(staging.TermComparer);
        _reserved = new TermHandle[closures.Count];
        for (int i = 0; i < closures.Count; i++)
        {
            _reserved[i] = staging.Stage(closures[i].TBoxConclusions);
        }
    }

    /// <summary>
    /// <paramref name="scoped"/>, the caller's view over
    /// <paramref name="staging"/>, with the closures of
    /// <paramref name="closures"/> read as <paramref name="mode"/> asks; the
    /// scoped view itself when there is none and the mode asks for no part
    /// but the explicit one.
    /// </summary>
    internal static IQuadSource Compose(IQuadSource scoped, StagingView staging, IReadOnlyList<IInferenceClosure> closures, ClosureMode mode) =>
        closures.Count == 0 && mode.ExplicitOnly ? scoped : new ClosureGraphs(scoped, staging, closures, mode);

    public IEqualityComparer<TermHandle> TermComparer => _base.TermComparer;

    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        for (int i = 0; i < _closures.Count; i++)
        {
            if (_closures[i].TBoxConclusions.Equals(term))
            {
                handle = _reserved[i];
                return true;
            }
        }

        // A cross-graph binding's premise-set graph (ADR 0190 point 6), named as its rows name it.
        foreach (IInferenceClosure closure in _closures)
        {
            if (term.Kind == RdfTermKind.Iri && closure.PremiseSetOf(term) is { } set)
            {
                handle = Stage(term);
                _premiseSets[handle] = set;
                return true;
            }
        }

        if (_base.TryInternalise(term, out handle) || _staging.TryInternalise(term, out handle))
        {
            return true;
        }

        // A term only a closure holds, a vocabulary constant the dataset lacks
        // (#186): named, staged as the rows stage it, while any closure is unread.
        if (!_read && _closures.Count > 0 && term.Kind != RdfTermKind.BlankNode)
        {
            handle = Stage(term);
            return true;
        }

        if (_read && _staging.TryInternalise(term, out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) =>
        _base.TryExternalise(handle, out term) || _staging.TryExternalise(handle, out term);

    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _base.TryGetTripleTermComponents(handle, out subject, out predicate, out @object)
        || _staging.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    [DesignDecision(typeof(TheViewsAndTheReservedGraphs.ClosureReadOnFirstNeed), Scope = ExceptionScope.HotPath)]
    public bool Contains(in Quad quad)
    {
        using IQuadCursor cursor = Match(quad.Subject, quad.Predicate, quad.Object, quad.Graph.IsNone ? GraphPattern.DefaultGraph : GraphPattern.Named(quad.Graph));
        return cursor.MoveNext();
    }

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        List<Quad> found = [];
        switch (graph.Match)
        {
            case GraphMatch.DefaultGraph:
                Default(subject, predicate, @object, found);
                break;
            case GraphMatch.Named:
                Named(graph.Graph, subject, predicate, @object, found);
                break;
            case GraphMatch.AnyNamed:
                AnyNamed(subject, predicate, @object, found);
                break;
            default:
                Default(subject, predicate, @object, found);
                AnyNamed(subject, predicate, @object, found);
                break;
        }

        return new ListCursor(found);
    }

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _mode.ExplicitOnly && !IsReserved(graph) ? _base.Estimate(subject, predicate, @object, graph) : CardinalityEstimate.Unknown;

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _base.TryGetInlineValue(handle, out value);

    private bool IsReserved(GraphPattern graph) => graph.Match == GraphMatch.Named && (Array.IndexOf(_reserved, graph.Graph) >= 0 || _premiseSets.ContainsKey(graph.Graph));

    /// <summary>The default graph: the merge of its graphs' parts, each quad once.</summary>
    private void Default(TermHandle s, TermHandle p, TermHandle o, List<Quad> into)
    {
        if (_mode.DefaultGraphs is null)
        {
            Part(TermHandle.None, _mode.Default, s, p, o, into, asDefault: true);
            return;
        }

        HashSet<(ulong, ulong, ulong)> seen = [];
        List<Quad> part = [];
        foreach (RdfTerm graph in _mode.DefaultGraphs)
        {
            if (!TryInternalise(graph, out TermHandle handle))
            {
                continue;
            }

            part.Clear();
            Part(handle, _mode.Default, s, p, o, part, asDefault: true);
            foreach (Quad quad in part)
            {
                if (seen.Add((quad.Subject.Value, quad.Predicate.Value, quad.Object.Value)))
                {
                    into.Add(quad);
                }
            }
        }
    }

    private void Named(TermHandle graph, TermHandle s, TermHandle p, TermHandle o, List<Quad> into)
    {
        int reserved = Array.IndexOf(_reserved, graph);
        if (reserved >= 0)
        {
            Rows(graph, s, p, o, into, asDefault: false);
            return;
        }

        if (_premiseSets.TryGetValue(graph, out IReadOnlyList<RdfTerm>? set))
        {
            // Read when the caller reads every graph of its set, and only when named.
            if (EveryReadable(set))
            {
                Rows(graph, s, p, o, into, asDefault: false);
            }

            return;
        }

        if (_mode.NamedGraphs is { } named && !Listed(named, graph))
        {
            return;
        }

        Part(graph, _mode.Named, s, p, o, into, asDefault: false);
    }

    private void AnyNamed(TermHandle s, TermHandle p, TermHandle o, List<Quad> into)
    {
        if (_mode.NamedGraphs is { } named)
        {
            foreach (RdfTerm graph in named)
            {
                if (TryInternalise(graph, out TermHandle handle))
                {
                    Named(handle, s, p, o, into);
                }
            }

            return;
        }

        if ((_mode.Named & ClosurePart.Explicit) != 0)
        {
            using IQuadCursor cursor = _base.Match(s, p, o, GraphPattern.AnyNamed);
            while (cursor.MoveNext())
            {
                into.Add(cursor.Current);
            }
        }

        // The implicit part of every named graph the caller reads. A reserved
        // graph is read only when named (ADR 0198): a read that never names
        // one never waits for its closure.
        if ((_mode.Named & ClosurePart.Implicit) == 0)
        {
            return;
        }

        Load();
        foreach ((TermHandle graph, List<Quad> rows) in _implicit)
        {
            if (!graph.IsNone && Array.IndexOf(_reserved, graph) < 0 && Readable(graph))
            {
                Filter(rows, s, p, o, into, asDefault: false);
            }
        }
    }

    /// <summary>One graph's part: its explicit quads from the caller's view, its implicit ones from its binding's rows.</summary>
    private void Part(TermHandle graph, ClosurePart part, TermHandle s, TermHandle p, TermHandle o, List<Quad> into, bool asDefault)
    {
        if ((part & ClosurePart.Explicit) != 0)
        {
            using IQuadCursor cursor = _base.Match(s, p, o, graph.IsNone ? GraphPattern.DefaultGraph : GraphPattern.Named(graph));
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                into.Add(asDefault ? new Quad(quad.Subject, quad.Predicate, quad.Object) : quad);
            }
        }

        if ((part & ClosurePart.Implicit) != 0 && Readable(graph))
        {
            Rows(graph, s, p, o, into, asDefault);
        }
    }

    /// <summary>Whether the caller reads a graph's explicit part, and so its implicit one (ADR 0190 point 2).</summary>
    private bool Readable(TermHandle graph)
    {
        using IQuadCursor cursor = _base.Match(TermHandle.None, TermHandle.None, TermHandle.None, graph.IsNone ? GraphPattern.DefaultGraph : GraphPattern.Named(graph));
        return cursor.MoveNext();
    }

    /// <summary>Whether the caller reads every data graph of a premise set (ADR 0190 point 6), each as <see cref="Readable(TermHandle)"/> decides.</summary>
    private bool EveryReadable(IReadOnlyList<RdfTerm> set)
    {
        foreach (RdfTerm member in set)
        {
            TermHandle graph = TermHandle.None;
            if (!member.Equals(DefaultGraphTerm) && !_base.TryInternalise(member, out graph))
            {
                return false;
            }

            if (!Readable(graph))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly RdfTerm DefaultGraphTerm = RdfTerm.Iri("https://w3id.org/varve/ns#defaultGraph"u8);

    private void Rows(TermHandle graph, TermHandle s, TermHandle p, TermHandle o, List<Quad> into, bool asDefault)
    {
        Load();
        if (_implicit.TryGetValue(graph, out List<Quad>? rows))
        {
            Filter(rows, s, p, o, into, asDefault);
        }
    }

    private void Filter(List<Quad> rows, TermHandle s, TermHandle p, TermHandle o, List<Quad> into, bool asDefault)
    {
        IEqualityComparer<TermHandle> same = TermComparer;
        foreach (Quad quad in rows)
        {
            if ((s.IsNone || same.Equals(s, quad.Subject)) && (p.IsNone || same.Equals(p, quad.Predicate)) && (o.IsNone || same.Equals(o, quad.Object)))
            {
                into.Add(asDefault ? new Quad(quad.Subject, quad.Predicate, quad.Object) : quad);
            }
        }
    }

    /// <summary>Whether a graph is one of the listed named graphs.</summary>
    private bool Listed(IReadOnlyList<RdfTerm> graphs, TermHandle graph)
    {
        if (graph.IsNone || !TryExternalise(graph, out RdfTerm? term))
        {
            return false;
        }

        foreach (RdfTerm listed in graphs)
        {
            if (listed.Equals(term))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads every closure's rows at the read's position, once, through the seam: behind or stopped throws.</summary>
    private void Load()
    {
        if (_read)
        {
            return;
        }

        for (int i = 0; i < _closures.Count; i++)
        {
            InferredRows rows = _closures[i].RowsAt(_staging.Position);
            foreach (InferredRow row in rows.Rows)
            {
                TermHandle graph = Name(row.Graph, rows);
                if (!_implicit.TryGetValue(graph, out List<Quad>? quads))
                {
                    _implicit[graph] = quads = [];
                }

                quads.Add(new Quad(Name(row.Fact.Subject, rows), Name(row.Fact.Predicate, rows), Name(row.Fact.Object, rows), graph));
            }
        }

        _read = true;
    }

    /// <summary>A row's handle in the read: the dataset's own, or the projection's term staged.</summary>
    private TermHandle Name(TermHandle handle, InferredRows rows) =>
        handle.IsNone || !rows.TryExternalise(handle, out RdfTerm? term) ? handle : Stage(term);

    private TermHandle Stage(RdfTerm term) => term.Kind == RdfTermKind.TripleTerm
        ? _staging.StageTriple(Stage(term.Subject!), Stage(term.Predicate!), Stage(term.Object!))
        : _staging.Stage(term);

    private sealed class ListCursor(List<Quad> quads) : IQuadCursor
    {
        private int _at = -1;

        public Quad Current => quads[_at];

        public bool MoveNext() => ++_at < quads.Count;

        public void Dispose()
        {
        }
    }
}
