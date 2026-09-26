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
/// A quad source held entirely in memory, with its own interning table.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0022: an in-memory dataset is a quad source like any other, and nothing
/// about the contract presumes a log. Its handles are indices into its own
/// table and mean nothing to another source.
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
    private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);
    private readonly List<RdfTerm> _terms = [];
    private readonly HashSet<Quad> _quads = [];

    /// <summary>
    /// Bit equality, which is correct here because every term is interned and
    /// none is private. A store's comparer is not this one.
    /// </summary>
    public IEqualityComparer<TermHandle> TermComparer => EqualityComparer<TermHandle>.Default;

    /// <summary>How many quads the dataset holds.</summary>
    public int Count => _quads.Count;

    /// <summary>How many distinct terms have been interned.</summary>
    public int TermCount => _terms.Count;

    /// <summary>
    /// The handle for a term, interning it if it is new. Handles start at one,
    /// so no term is ever <see cref="TermHandle.None"/>.
    /// </summary>
    public TermHandle Internalise(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        if (_handles.TryGetValue(term, out TermHandle existing))
        {
            return existing;
        }

        _terms.Add(term);
        TermHandle handle = new((ulong)_terms.Count);
        _handles.Add(term, handle);
        return handle;
    }

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

        if (value == 0 || value > (ulong)_terms.Count)
        {
            term = null;
            return false;
        }

        term = _terms[(int)(value - 1)];
        return true;
    }

    /// <summary>Adds a quad. Returns false when it was already present.</summary>
    public bool Add(in Quad quad) => _quads.Add(quad);

    /// <summary>
    /// Adds a quad from owned terms, interning each. A graph of null is the
    /// default graph.
    /// </summary>
    public bool Add(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph = null)
    {
        Quad quad = new(
            Internalise(subject),
            Internalise(predicate),
            Internalise(@object),
            graph is null ? TermHandle.None : Internalise(graph));

        return Add(in quad);
    }

    /// <summary>Removes a quad. Returns false when it was not present.</summary>
    public bool Remove(in Quad quad) => _quads.Remove(quad);

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad) => _quads.Contains(quad);

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

        return CardinalityEstimate.Exact(count);
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
        private readonly TermHandle _subject;
        private readonly TermHandle _predicate;
        private readonly TermHandle _object;
        private readonly GraphPattern _graph;
        private HashSet<Quad>.Enumerator _enumerator;

        internal Cursor(
            HashSet<Quad> quads,
            TermHandle subject,
            TermHandle predicate,
            TermHandle @object,
            GraphPattern graph)
        {
            _enumerator = quads.GetEnumerator();
            _subject = subject;
            _predicate = predicate;
            _object = @object;
            _graph = graph;
        }

        public Quad Current { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            while (_enumerator.MoveNext())
            {
                Quad candidate = _enumerator.Current;

                if (Matches(candidate))
                {
                    Current = candidate;
                    return true;
                }
            }

            Current = default;
            return false;
        }

        public void Dispose() => _enumerator.Dispose();

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        private bool Matches(in Quad quad) => QuadPatterns.Matches(in quad, _subject, _predicate, _object, _graph);
    }
}
