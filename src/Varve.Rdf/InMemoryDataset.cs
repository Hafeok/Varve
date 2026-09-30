// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A quad source held entirely in memory, with its own interning table: an
/// immutable value, made by <see cref="InMemoryDatasetBuilder.ToDataset"/>.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0022: an in-memory dataset is a quad source like any other, and nothing
/// about the contract presumes a log. Its handles are indices into its own
/// table and mean nothing to another source.
/// </para>
/// <para>
/// ADR 0067: once made, its quads and its interning table do not change. It is
/// what a caller is handed and passes on as an <see cref="IQuadSource"/>; the
/// <see cref="InMemoryDatasetBuilder"/> is what is filled. A handle means the
/// same term in every dataset one builder produces, before and after later
/// additions, because the builder only ever appends to its table.
/// <see cref="TryInternalise"/> answers false for a term this dataset has never
/// seen, which ADR 0022 calls a read-only source's entitlement.
/// </para>
/// <para>
/// <see cref="Match"/> is a linear scan, and so is <see cref="Estimate"/>,
/// which counts and reports the count exact (ADR 0049). There are no indices
/// here, because this type exists to hold a parsed file and to give layer 3
/// something to run against before there is a store — not to be one. The
/// store at milestone 4 is where access paths are a design question.
/// </para>
/// <para>
/// There are no inline handles either: <see cref="TryGetInlineValue"/> always
/// answers false (ADR 0050). It could parse the interned term on demand and
/// deliberately does not, so that the member promises what its name says and
/// ADR 0022's benchmark measures the handle rather than parsing.
/// </para>
/// </remarks>
public sealed class InMemoryDataset : IQuadSource
{
    // Copies, never the builder's own collections: aliasing them would make
    // this value change when the builder did (ADR 0067). Nothing writes these
    // after the constructor. The quads are held twice: in the order they were
    // added, which is the order Match walks, as the mutable dataset's set did;
    // and sorted by handle bits, which is what Contains searches. Both are
    // arrays, so the per-quad paths are index loops over memory this value
    // owns (VARVE0003), and neither calls into a collection.
    private readonly Dictionary<RdfTerm, TermHandle> _handles;
    private readonly RdfTerm[] _terms;
    private readonly Quad[] _quads;
    private readonly Quad[] _sorted;

    internal InMemoryDataset(
        Dictionary<RdfTerm, TermHandle> handles, List<RdfTerm> terms, HashSet<Quad> quads)
    {
        _handles = new Dictionary<RdfTerm, TermHandle>(handles, RdfTerm.Comparer);
        _terms = terms.ToArray();
        _quads = new Quad[quads.Count];
        quads.CopyTo(_quads);
        _sorted = (Quad[])_quads.Clone();
        _sorted.AsSpan().Sort(static (left, right) => QuadDelta.Compare(in left, in right));
    }

    /// <summary>
    /// Bit equality, which is correct here because every term is interned and
    /// none is private. A store's comparer is not this one.
    /// </summary>
    public IEqualityComparer<TermHandle> TermComparer => EqualityComparer<TermHandle>.Default;

    /// <summary>How many quads the dataset holds.</summary>
    public QuadCount Count => new(_quads.Length);

    /// <summary>How many distinct terms had been interned when it was made.</summary>
    [DesignDecision(typeof(RdfModelSurfaces.InMemoryTermCountIsAnInteger), Scope = ExceptionScope.Boundary)]
    public int TermCount => _terms.Length;

    /// <inheritdoc />
    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        ArgumentNullException.ThrowIfNull(term);
        return _handles.TryGetValue(term, out handle);
    }

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        ulong value = handle.Value;

        if (value == 0 || value > (ulong)_terms.Length)
        {
            term = null;
            return false;
        }

        term = _terms[(int)(value - 1)];
        return true;
    }

    /// <summary>Whether the dataset holds <paramref name="quad"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad) => QuadDelta.IndexOf(_sorted, in quad) >= 0;

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new Cursor(_quads, subject, predicate, @object, graph);

    /// <inheritdoc />
    /// <remarks>A count by scan, exact: this source has no index to consult.</remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        long count = 0;

        foreach (Quad quad in _quads)
        {
            if (QuadPatterns.Matches(in quad, subject, predicate, @object, graph))
            {
                count++;
            }
        }

        return CardinalityEstimate.Exact(new QuadCount(count));
    }

    /// <inheritdoc />
    /// <remarks>Always false: this source's handles are table indices and encode nothing.</remarks>
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value)
    {
        value = InlineValue.None;
        return false;
    }

    private sealed class Cursor : IQuadCursor
    {
        private readonly Quad[] _quads;
        private readonly TermHandle _subject;
        private readonly TermHandle _predicate;
        private readonly TermHandle _object;
        private readonly GraphPattern _graph;
        private int _next;

        internal Cursor(
            Quad[] quads,
            TermHandle subject,
            TermHandle predicate,
            TermHandle @object,
            GraphPattern graph)
        {
            _quads = quads;
            _subject = subject;
            _predicate = predicate;
            _object = @object;
            _graph = graph;
        }

        public Quad Current { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            while (_next < _quads.Length)
            {
                Quad candidate = _quads[_next++];

                if (Matches(candidate))
                {
                    Current = candidate;
                    return true;
                }
            }

            Current = default;
            return false;
        }

        public void Dispose()
        {
            // An array holds nothing to release; the contract asks for disposal
            // because a store's cursor does.
        }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        private bool Matches(in Quad quad) => QuadPatterns.Matches(in quad, _subject, _predicate, _object, _graph);
    }
}
