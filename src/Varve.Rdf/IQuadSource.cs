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
/// A forward-only walk over quads, disposed when the caller is done with it.
/// </summary>
/// <remarks>
/// Not <see cref="IEnumerator{T}"/>: there is no reset, and a source is free to
/// hold a read lock, a pinned segment or a snapshot for the cursor's lifetime,
/// which is what makes disposal part of the contract rather than a courtesy.
/// </remarks>
[Contract(typeof(RdfModelSurfaces.QuadCursorIsForwardOnlyAndDisposable), Role = "the walk a quad source answers a match with")]
public interface IQuadCursor : IDisposable
{
    /// <summary>Advances to the next quad, or returns false at the end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    bool MoveNext();

    /// <summary>
    /// The quad at the current position. Undefined before the first
    /// <see cref="MoveNext"/> and after it returns false.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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
[Contract(typeof(QuadSourceTermHandle.OpaqueTermHandle), Role = "the source of quads every read path is written against")]
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
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    bool Contains(in Quad quad);

    /// <summary>
    /// Walks the quads matching a pattern. <see cref="TermHandle.None"/> in the
    /// subject, predicate or object position is a wildcard; the graph position
    /// takes a <see cref="GraphPattern"/>, because "any graph" is two different
    /// questions there and a sentinel cannot tell them apart.
    /// </summary>
    IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph);

    /// <summary>
    /// How many quads <see cref="Match"/> would yield for the same pattern:
    /// exact, estimated, or unknown (ADR 0049). The cost is bounded by the
    /// source's documentation, and a consumer may call it freely only where
    /// that documentation says it is cheap. A source that claims to be an
    /// index does not answer by scanning.
    /// </summary>
    CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph);

    /// <summary>
    /// The value a handle encodes in its own bits, when it encodes one (ADR
    /// 0050). True hands over the value of a literal whose lexical form is
    /// canonical, without materialising the term. False means the handle is
    /// not inline — nothing more — and the consumer externalises as it would
    /// have anyway.
    /// </summary>
    bool TryGetInlineValue(TermHandle handle, out InlineValue value);
}
