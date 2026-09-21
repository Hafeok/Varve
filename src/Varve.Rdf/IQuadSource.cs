using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Varve.Rdf;

/// <summary>
/// A forward-only walk over quads, disposed when the caller is done with it.
/// </summary>
/// <remarks>
/// Not <see cref="IEnumerator{T}"/>: there is no reset, and a source is free to
/// hold a read lock, a pinned segment or a snapshot for the cursor's lifetime,
/// which is what makes disposal part of the contract rather than a courtesy.
/// </remarks>
public interface IQuadCursor : IDisposable
{
    /// <summary>Advances to the next quad, or returns false at the end.</summary>
    bool MoveNext();

    /// <summary>
    /// The quad at the current position. Undefined before the first
    /// <see cref="MoveNext"/> and after it returns false.
    /// </summary>
    Quad Current { get; }
}

/// <summary>
/// A source of quads: the contract the evaluator, the overlay and every read
/// path are written against (ADR 0022).
/// </summary>
/// <remarks>
/// <para>
/// Layer 1, so nothing here names a log, a position, a commit or a key. A
/// store is a quad source, an in-memory dataset is a quad source, and an
/// overlay composes two of them.
/// </para>
/// <para>
/// **Compare terms with <see cref="TermComparer"/>, never with
/// <c>==</c> on the handle.** A readable private term compares by decrypted
/// value against private and canonical terms alike, and a shredded one is
/// equal only to itself. No consumer can derive that from the bits.
/// </para>
/// </remarks>
public interface IQuadSource
{
    /// <summary>
    /// The equality this source's handles obey. Supplied by the source
    /// because only the source knows what its handles stand for.
    /// </summary>
    IEqualityComparer<TermHandle> TermComparer { get; }

    /// <summary>
    /// Looks up the handle this source uses for a term. Returns false when the
    /// source does not have one, which is not an error: a read-only source is
    /// entitled to report that a term it has never seen cannot appear in any
    /// quad it holds.
    /// </summary>
    bool TryInternalise(RdfTerm term, out TermHandle handle);

    /// <summary>
    /// Materialises the term a handle names. Returns false when the handle is
    /// unknown, and when it names a private term whose key has been destroyed.
    /// </summary>
    bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term);

    /// <summary>Whether this source holds a quad.</summary>
    bool Contains(in Quad quad);

    /// <summary>
    /// Walks the quads matching a pattern. <see cref="TermHandle.None"/> in any
    /// position is a wildcard; in the graph position it therefore matches every
    /// graph, and the default graph is asked for by a pattern no wildcard can
    /// express — <see cref="MatchDefaultGraph"/> exists for that.
    /// </summary>
    IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, TermHandle graph);

    /// <summary>
    /// Walks the quads in the default graph matching a triple pattern.
    /// </summary>
    IQuadCursor MatchDefaultGraph(TermHandle subject, TermHandle predicate, TermHandle @object);
}
