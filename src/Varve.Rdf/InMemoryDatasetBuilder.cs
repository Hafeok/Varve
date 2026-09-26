// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Rdf;

/// <summary>
/// Assembles an <see cref="InMemoryDataset"/>: interns terms and adds quads,
/// and hands out immutable snapshots with <see cref="ToDataset"/>.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0067. The dataset is the value that gets passed, as an
/// <see cref="IQuadSource"/>; this is the thing that gets filled. A sealed
/// <c>*Builder</c> in the model namespace is the package's escape hatch from
/// DD0019 (<c>ImmutableModel.BuildersAreTheEscapeHatch</c>), and it may appear
/// on no contract.
/// </para>
/// <para>
/// The interning table is only ever appended to. A handle issued here means the
/// same term in every dataset this builder produces, so a caller that builds,
/// snapshots, adds and snapshots again can compare handles across the two
/// snapshots with either's <see cref="InMemoryDataset.TermComparer"/>.
/// </para>
/// </remarks>
public sealed class InMemoryDatasetBuilder
{
    private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);
    private readonly List<RdfTerm> _terms = [];
    private readonly HashSet<Quad> _quads = [];

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

    /// <summary>The handle for a term already interned; false for one never seen.</summary>
    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        ArgumentNullException.ThrowIfNull(term);
        return _handles.TryGetValue(term, out handle);
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

    /// <summary>
    /// Removes a quad. Returns false when it was not present. The term table is
    /// not shrunk: handles stay stable.
    /// </summary>
    public bool Remove(in Quad quad) => _quads.Remove(quad);

    /// <summary>
    /// An immutable snapshot of what has been added so far. It copies, so later
    /// additions here do not reach it.
    /// </summary>
    public InMemoryDataset ToDataset() => new(_handles, _terms, _quads);
}
