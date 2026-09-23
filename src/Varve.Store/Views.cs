// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Varve.Rdf;

namespace Varve.Store;

/// <summary><c>D_P</c>: the dictionary seen through one position's counters.</summary>
internal readonly struct TermView
{
    internal TermView(TermDictionary dictionary, long canonicalCount, long blankCount)
    {
        Dictionary = dictionary;
        CanonicalCount = canonicalCount;
        BlankCount = blankCount;
    }

    internal TermDictionary Dictionary { get; }

    internal long CanonicalCount { get; }

    internal long BlankCount { get; }

    internal bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        ArgumentNullException.ThrowIfNull(term);
        bool found = Dictionary.TryFind(term, CanonicalCount, out ulong id);
        handle = new TermHandle(id);
        return found;
    }

    internal bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) =>
        Dictionary.TryTerm(handle.Value, CanonicalCount, BlankCount, out term);
}

/// <summary>
/// The store's term equality (spec §6): canonical, blank and inline ids compare
/// by id. With private terms, a readable one compares by value against private
/// and canonical terms alike, and a shredded one equals only itself.
/// </summary>
/// <remarks>
/// <para>
/// Nothing allocates a private id before erasure mode exists, so the store
/// uses <see cref="ById"/>. The value-comparing form is declared now and tested
/// against a stub (<see cref="IPrivateTermValues"/>), so that milestone 9 finds
/// the equality it has to meet already written down.
/// </para>
/// <para>
/// Specification 1.2's note on §6: once a readable private term can equal a
/// canonical one, every handle must hash by value — a dictionary lookup per
/// hash. With no private terms, a hash is the id.
/// </para>
/// </remarks>
internal sealed class StoreTermComparer : IEqualityComparer<TermHandle>
{
    private readonly IPrivateTermValues? _privates;
    private readonly Func<ulong, RdfTerm?>? _values;

    private StoreTermComparer(IPrivateTermValues? privates, Func<ulong, RdfTerm?>? values)
    {
        _privates = privates;
        _values = values;
    }

    internal static StoreTermComparer ById { get; } = new(null, null);

    /// <summary>
    /// Value equality across classes. <paramref name="values"/> resolves a
    /// non-private id to its term, or null for a blank node.
    /// </summary>
    internal static StoreTermComparer ByValue(IPrivateTermValues privates, Func<ulong, RdfTerm?> values) => new(privates, values);

    [HotPath]
    public bool Equals(TermHandle x, TermHandle y)
    {
        if (x.Value == y.Value)
        {
            return true;
        }

        if (_privates is null)
        {
            return false;
        }

        bool xPrivate = TermIds.ClassOf(x.Value) == IdClass.Private;
        bool yPrivate = TermIds.ClassOf(y.Value) == IdClass.Private;

        if (!xPrivate && !yPrivate)
        {
            return false;
        }

        RdfTerm? left = Value(x.Value);
        RdfTerm? right = Value(y.Value);
        return left is not null && right is not null && left.Equals(right);
    }

    [HotPath]
    public int GetHashCode(TermHandle obj)
    {
        if (_privates is null)
        {
            return obj.Value.GetHashCode();
        }

        RdfTerm? value = Value(obj.Value);
        return value is null ? obj.Value.GetHashCode() : value.GetHashCode();
    }

    // Null for a blank node and for a shredded private term: both equal only themselves.
    private RdfTerm? Value(ulong id) =>
        TermIds.ClassOf(id) == IdClass.Private
            ? _privates!.TryValue(id, out RdfTerm? term) ? term : null
            : _values!(id);
}

/// <summary>
/// Decrypts private terms for <see cref="StoreTermComparer"/>. Milestone 9's;
/// before it, only a test stub implements this.
/// </summary>
internal interface IPrivateTermValues
{
    /// <summary>The plaintext of a readable private term; false for a shredded one.</summary>
    bool TryValue(ulong id, [MaybeNullWhen(false)] out RdfTerm term);
}

/// <summary>A quad source over one index version and one position's dictionary.</summary>
internal sealed class IndexSource : IQuadSource
{
    private readonly IndexVersion _index;
    private readonly TermView _terms;

    internal IndexSource(IndexVersion index, TermView terms)
    {
        _index = index;
        _terms = terms;
    }

    public IEqualityComparer<TermHandle> TermComparer => StoreTermComparer.ById;

    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _terms.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _terms.TryExternalise(handle, out term);

    [HotPath]
    public bool Contains(in Quad quad) => _index.Contains(in quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _index.Match(subject, predicate, @object, graph);
}

/// <summary>
/// The head as a pending commit sees it: the head's quads, and a dictionary
/// that also knows the commit's fresh terms. A validator reads
/// <c>Overlay(this, δ)</c> (spec T1 step 5, ADR 0017).
/// </summary>
internal sealed class PendingSource : IQuadSource
{
    private readonly IndexSource _head;
    private readonly TermView _terms;
    private readonly Dictionary<ulong, RdfTerm> _fresh;
    private readonly Dictionary<RdfTerm, ulong> _freshByTerm;

    internal PendingSource(IndexVersion head, TermView terms, IReadOnlyList<Allocation> fresh)
    {
        _head = new IndexSource(head, terms);
        _terms = terms;
        _fresh = [];
        _freshByTerm = new Dictionary<RdfTerm, ulong>(RdfTerm.Comparer);

        foreach (Allocation allocation in fresh)
        {
            RdfTerm term = allocation.Term ?? TermDictionary.BlankTerm(TermIds.Counter(allocation.Id));
            _fresh[allocation.Id] = term;

            if (TermIds.ClassOf(allocation.Id) == IdClass.Canonical && !allocation.IsTriple)
            {
                _freshByTerm[term] = allocation.Id;
            }
        }
    }

    public IEqualityComparer<TermHandle> TermComparer => StoreTermComparer.ById;

    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        if (_freshByTerm.TryGetValue(term, out ulong id))
        {
            handle = new TermHandle(id);
            return true;
        }

        return _terms.TryInternalise(term, out handle);
    }

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) =>
        _fresh.TryGetValue(handle.Value, out term) || _terms.TryExternalise(handle, out term);

    public bool Contains(in Quad quad) => _head.Contains(in quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _head.Match(subject, predicate, @object, graph);
}

/// <summary>
/// A read of the dataset at one position: a pinned read (<see cref="Dataset.Pin"/>)
/// or an as-of read (<see cref="Dataset.AsOfAsync(long, System.Threading.CancellationToken)"/>).
/// </summary>
/// <remarks>
/// <para>
/// It sees <c>G_P</c> and <c>D_P</c> exactly: a term allocated after its
/// position is unknown to it, so nothing a later commit does changes any
/// answer it gives (R1).
/// </para>
/// <para>
/// A pinned read is an engine snapshot for the lifetime of one operation, not
/// time travel (ADR 0015). Dispose it when the operation is done.
/// </para>
/// </remarks>
public sealed class DatasetView : IQuadSource, IDisposable
{
    private readonly IQuadSource _source;
    private readonly TermView _terms;
    private bool _disposed;

    internal DatasetView(long position, IQuadSource source, TermView terms)
    {
        Position = position;
        _source = source;
        _terms = terms;
    }

    /// <summary>The position this view reads.</summary>
    public long Position { get; }

    /// <summary>
    /// Id equality: canonical, blank and inline handles compare by id. Private
    /// terms, which compare by value, do not exist before erasure mode.
    /// </summary>
    public IEqualityComparer<TermHandle> TermComparer => StoreTermComparer.ById;

    /// <inheritdoc />
    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        ThrowIfDisposed();
        return _terms.TryInternalise(term, out handle);
    }

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        ThrowIfDisposed();
        return _terms.TryExternalise(handle, out term);
    }

    /// <inheritdoc />
    [HotPath]
    public bool Contains(in Quad quad)
    {
        ThrowIfDisposed();
        return _source.Contains(in quad);
    }

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        ThrowIfDisposed();
        return _source.Match(subject, predicate, @object, graph);
    }

    /// <summary>Releases the view. In memory this holds nothing a later commit could need back.</summary>
    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
