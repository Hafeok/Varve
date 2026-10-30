// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A quad source that shows only the rows a <see cref="RowScope"/> allows
/// (ADRs 0230 and 0235): a quad of a classified graph is shown when every
/// subject it is about, its subject node's and, unless its predicate is a
/// public link, its object's, is in the caller's subject set for the
/// binding. A quad outside every binding passes untouched. Terms pass
/// through unfiltered: a term's existence is not a row's, so the source
/// never denies a term.
/// </summary>
/// <remarks>
/// Built once per request over a graph-scoped view (ADR 0235 point 4), with
/// the classification of that view's position. Safe to read from several
/// threads: every memo is a concurrent map filled through <c>GetOrAdd</c>,
/// and every decision is a pure function of the view. A scope restricting no
/// binding is not wrapped (<see cref="Wrap"/>).
/// </remarks>
[Contract(typeof(TheRowScopedQuadSource.RowScopedSourceFiltersQuadsPassesTerms), Role = "a read view filtered by row")]
public sealed class RowScopedQuadSource : IQuadSource
{
    private readonly IQuadSource _inner;
    private readonly IRowClassification _rows;
    private readonly SubjectDecider?[] _deciders;
    [Memoized]
    private readonly ConcurrentDictionary<TermHandle, BindingIndex> _graphs;

    /// <summary><paramref name="inner"/> seen through <paramref name="scope"/>, classified by <paramref name="rows"/> in <paramref name="inner"/>'s handles.</summary>
    public RowScopedQuadSource(IQuadSource inner, IRowClassification rows, RowScope scope)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(scope);
        _inner = inner;
        _rows = rows;
        Scope = scope;
        _deciders = Deciders(inner, rows, scope);
        _graphs = new ConcurrentDictionary<TermHandle, BindingIndex>(inner.TermComparer);
    }

    /// <summary>The scope this source shows.</summary>
    public RowScope Scope { get; }

    /// <summary><paramref name="inner"/> through <paramref name="scope"/>, or <paramref name="inner"/> itself when the scope restricts no binding.</summary>
    public static IQuadSource Wrap(IQuadSource inner, IRowClassification rows, RowScope scope)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(scope);
        return scope.IsAll ? inner : new RowScopedQuadSource(inner, rows, scope);
    }

    /// <inheritdoc />
    public IEqualityComparer<TermHandle> TermComparer => _inner.TermComparer;

    /// <inheritdoc />
    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _inner.TryInternalise(term, out handle);

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _inner.TryExternalise(handle, out term);

    /// <inheritdoc/>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _inner.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    /// <inheritdoc />
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _inner.TryGetInlineValue(handle, out value);

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad) => _inner.Contains(in quad) && Allows(in quad);

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        IQuadCursor inner = _inner.Match(subject, predicate, @object, graph);

        // A named graph outside every restricted binding is the inner's.
        return graph.Match == GraphMatch.Named && Restriction(graph.Graph) is null ? inner : new Cursor(inner, this);
    }

    /// <inheritdoc />
    /// <remarks>The inner's bound, as an estimate: the filter only removes quads (ADR 0049).</remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        CardinalityEstimate inner = _inner.Estimate(subject, predicate, @object, graph);

        if (graph.Match == GraphMatch.Named && Restriction(graph.Graph) is null)
        {
            return inner;
        }

        return inner.IsUnknown || !inner.IsExact ? inner : CardinalityEstimate.Estimated(inner.Count);
    }

    /// <summary>Whether <paramref name="quad"/>, a quad of the inner source, is shown.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Allows(in Quad quad)
    {
        SubjectDecider? decider = Restriction(quad.Graph);
        return decider is null || AllowsIn(decider, in quad);
    }

    // Two probes per quad at most: the subject's set, and the object's
    // unless the predicate is a public link; each set's decision memoised.
    [DesignDecision(typeof(TheRowScopedQuadSource.RowDecisionIsTwoProbesPerQuad), Scope = ExceptionScope.HotPath)]
    private bool AllowsIn(SubjectDecider decider, in Quad quad)
    {
        BindingIndex binding = BindingOf(quad.Graph);

        if (!decider.Allows(_rows.SubjectsOf(binding, quad.Subject)))
        {
            return false;
        }

        return _rows.IsPublicLink(binding, quad.Predicate) || decider.Allows(_rows.SubjectsOf(binding, quad.Object));
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private SubjectDecider? Restriction(TermHandle graph)
    {
        BindingIndex binding = BindingOf(graph);
        return binding.IsNone ? null : _deciders[binding.Value];
    }

    // Decided once per graph handle, as a prefix grant is (ADR 0107).
    [DesignDecision(typeof(TheRowScopedQuadSource.RowDecisionIsTwoProbesPerQuad), Scope = ExceptionScope.HotPath)]
    private BindingIndex BindingOf(TermHandle graph) =>
        _graphs.GetOrAdd(graph, static (handle, rows) => rows.BindingOf(handle), _rows);

    private static SubjectDecider?[] Deciders(IQuadSource inner, IRowClassification rows, RowScope scope)
    {
        SubjectDecider?[] deciders = new SubjectDecider?[rows.Bindings.Count];

        for (int i = 0; i < deciders.Length; i++)
        {
            BindingIndex index = new(i);
            SubjectScope subjects = scope.For(rows.Bindings[i]);
            deciders[i] = subjects.IsAll ? null : new SubjectDecider(inner, rows, index, subjects);
        }

        return deciders;
    }

    private sealed class Cursor(IQuadCursor inner, RowScopedQuadSource owner) : IQuadCursor
    {
        public Quad Current => inner.Current;

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            while (inner.MoveNext())
            {
                Quad quad = inner.Current;

                if (owner.Allows(in quad))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose() => inner.Dispose();
    }
}
