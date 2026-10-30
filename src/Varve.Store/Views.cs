// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary><c>D_P</c>: the dictionary seen through one position's counters.</summary>
internal readonly struct TermView
{
    internal TermView(TermDictionary dictionary, Run[] runs, long canonicalCount, long blankCount)
    {
        Dictionary = dictionary;
        Runs = runs;
        CanonicalCount = canonicalCount;
        BlankCount = blankCount;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal TermDictionary Dictionary { get; }

    /// <summary>The runs whose term sections hold every entry up to <see cref="CanonicalCount"/>, held open by the view.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal Run[] Runs { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long CanonicalCount { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long BlankCount { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        ArgumentNullException.ThrowIfNull(term);
        bool found = Dictionary.TryFind(Runs, term, CanonicalCount, out ulong id);
        handle = new TermHandle(id);
        return found;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryFindTriple(ulong subject, ulong predicate, ulong @object, out ulong id) =>
        TermDictionary.TryFindTriple(Runs, subject, predicate, @object, CanonicalCount, out id);

    /// <summary>
    /// A triple term's components as handles, from its entry, when this
    /// position knows it (ADR 0121). The dictionary is append-only, so every
    /// position that knows the term answers the same.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryTripleParts(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object)
    {
        bool found = TermDictionary.TryTripleParts(Runs, handle.Value, CanonicalCount, out ulong s, out ulong p, out ulong o);
        subject = new TermHandle(s);
        predicate = new TermHandle(p);
        @object = new TermHandle(o);
        return found;
    }

    internal bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) =>
        Dictionary.TryTerm(Runs, handle.Value, CanonicalCount, BlankCount, out term);

    /// <summary>The term of an id the view knows.</summary>
    internal RdfTerm Term(ulong id) => Dictionary.Term(Runs, id);
}

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

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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

        return SameValue(x.Value, y.Value);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int GetHashCode(TermHandle obj)
    {
        if (_privates is null)
        {
            return obj.Value.GetHashCode();
        }

        return ValueHash(obj.Value);
    }

    // Resolving a private term is the per-hash dictionary lookup the remarks
    // describe: the value path, taken only once a dataset has private terms.
    [DesignDecision(typeof(StoreHotPathScope.PrivateTermComparisonResolvesValues), Scope = ExceptionScope.HotPath)]
    private bool SameValue(ulong x, ulong y)
    {
        RdfTerm? left = Value(x);
        RdfTerm? right = Value(y);
        return left is not null && right is not null && left.Equals(right);
    }

    [DesignDecision(typeof(StoreHotPathScope.PrivateTermComparisonResolvesValues), Scope = ExceptionScope.HotPath)]
    private int ValueHash(ulong id)
    {
        RdfTerm? value = Value(id);
        return value is null ? id.GetHashCode() : value.GetHashCode();
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

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _terms.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _terms.TryExternalise(handle, out term);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad) => _index.Contains(in quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _index.Match(subject, predicate, @object, graph);

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _index.Estimate(subject, predicate, @object, graph);

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => TermIds.TryInlineValue(handle.Value, out value);

    /// <inheritdoc/>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _terms.TryTripleParts(handle, out subject, out predicate, out @object);
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

    // The components of the triple terms the commit creates, and the reverse,
    // built once per commit, and only when it creates one.
    private readonly Dictionary<ulong, (ulong S, ulong P, ulong O)>? _freshTriples;
    private readonly Dictionary<(ulong S, ulong P, ulong O), ulong>? _freshTripleByParts;

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

            if (allocation.IsTriple)
            {
                _freshTriples ??= [];
                _freshTripleByParts ??= [];
                _freshTriples[allocation.Id] = (allocation.Subject, allocation.Predicate, allocation.Object);
                _freshTripleByParts[(allocation.Subject, allocation.Predicate, allocation.Object)] = allocation.Id;
            }
            else if (TermIds.ClassOf(allocation.Id) == IdClass.Canonical)
            {
                _freshByTerm[term] = allocation.Id;
            }
        }
    }

    public IEqualityComparer<TermHandle> TermComparer => StoreTermComparer.ById;

    /// <inheritdoc/>
    /// <remarks>
    /// A term the commit creates is the commit's: a plain one by value, and a
    /// triple term by its parts, each looked up here, so that a part the
    /// commit creates is found too. The head's dictionary knows neither, and
    /// a validator reads the delta's quads through this source, so "no such
    /// term" for one of them is a denial of a term it yields (ADR 0022,
    /// amended 2026-10-30; #191).
    /// </remarks>
    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        if (_freshByTerm.TryGetValue(term, out ulong id))
        {
            handle = new TermHandle(id);
            return true;
        }

        if (_terms.TryInternalise(term, out handle))
        {
            return true;
        }

        if (_freshTripleByParts is not null
            && term.Kind == RdfTermKind.TripleTerm
            && TryInternalise(term.Subject!, out TermHandle s)
            && TryInternalise(term.Predicate!, out TermHandle p)
            && TryInternalise(term.Object!, out TermHandle o)
            && _freshTripleByParts.TryGetValue((s.Value, p.Value, o.Value), out id))
        {
            handle = new TermHandle(id);
            return true;
        }

        handle = TermHandle.None;
        return false;
    }

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) =>
        _fresh.TryGetValue(handle.Value, out term) || _terms.TryExternalise(handle, out term);

    public bool Contains(in Quad quad) => _head.Contains(in quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _head.Match(subject, predicate, @object, graph);

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _head.Estimate(subject, predicate, @object, graph);

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _head.TryGetInlineValue(handle, out value);

    /// <inheritdoc/>
    /// <remarks>
    /// A triple term the commit creates answers from the commit's own
    /// allocation, whose components are already handles; every other one
    /// from the head's dictionary.
    /// </remarks>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object)
    {
        if (_freshTriples is not null && TryFindFreshTriple(handle.Value, out (ulong S, ulong P, ulong O) parts))
        {
            subject = new TermHandle(parts.S);
            predicate = new TermHandle(parts.P);
            @object = new TermHandle(parts.O);
            return true;
        }

        return _head.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);
    }

    [DesignDecision(typeof(StoreHotPathScope.TermLookupsAreHashLookups), Scope = ExceptionScope.HotPath)]
    private bool TryFindFreshTriple(ulong id, out (ulong S, ulong P, ulong O) parts) => _freshTriples!.TryGetValue(id, out parts);
}

/// <summary>
/// A read of the dataset at one position: a pinned read (<see cref="Dataset.Pin"/>)
/// or an as-of read (<see cref="Dataset.AsOfAsync(Position, System.Threading.CancellationToken)"/>).
/// </summary>
/// <remarks>
/// <para>
/// It sees <c>G_P</c> and <c>D_P</c> exactly: a term allocated after its
/// position is unknown to it, so nothing a later commit does changes any
/// answer it gives (R1).
/// </para>
/// <para>
/// A pinned read is an engine snapshot for the lifetime of one operation, not
/// time travel (ADR 0015). Dispose it when the operation is done; for a query,
/// that is when its result stream has been consumed or abandoned (ADR 0052,
/// documented on <see cref="Dataset.Pin"/>).
/// </para>
/// </remarks>
[Contract(typeof(CheckpointsAndReads.PinnedReadIsASnapshot), Role = "a stable read of the dataset at one position")]
public sealed class DatasetView : IQuadSource, IDisposable
{
    private readonly IQuadSource _source;
    private readonly TermView _terms;
    private Action? _release;
    private bool _disposed;

    internal DatasetView(long position, IQuadSource source, TermView terms, Action? release = null)
    {
        Position = new Position(position);
        _source = source;
        _terms = terms;
        _release = release;
    }

    /// <summary>The position this view reads.</summary>
    public Position Position { get; }

    /// <summary>Test seam: the dictionary the view reads.</summary>
    internal TermView TermsForTests() => _terms;

    /// <summary>
    /// Id equality: canonical, blank and inline handles compare by id. Private
    /// terms, which compare by value, do not exist before erasure mode.
    /// </summary>
    public IEqualityComparer<TermHandle> TermComparer => StoreTermComparer.ById;

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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

    /// <inheritdoc />
    /// <remarks>
    /// Exact, and cheap: the sum over the projection's runs of a prefix range
    /// per run, at <c>O(runs × log n)</c> (ADR 0049). An as-of read adds one
    /// lookup per quad of the log tail it overlays. An as-of view strictly
    /// inside an extent set's window reads that set sought (ADR 0177): its
    /// rows in the pattern's range are classified one by one while they are
    /// at most 4,096, and the estimate stays exact; past that the set adds its
    /// count at its end — asserted keys in the range minus retracted ones —
    /// and the estimate says it is estimated.
    /// </remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        ThrowIfDisposed();
        return _source.Estimate(subject, predicate, @object, graph);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decodes the inline classes the store allocates (ADR 0045): canonical
    /// <c>xsd:integer</c> within 56 bits and <c>xsd:boolean</c>. Every other
    /// handle answers false, a term with a non-canonical lexical form included,
    /// because it took an ordinary id (ADR 0012).
    /// </remarks>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value)
    {
        ThrowIfDisposed();
        return _source.TryGetInlineValue(handle, out value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One entry read from the dictionary at this view's position (ADR 0121).
    /// The dictionary is append-only, so a pinned view and an as-of view
    /// answer as the head does for every triple term they know, and false
    /// for one allocated after their position. A blank, inline or private
    /// handle never names a triple term and answers false without a read.
    /// </remarks>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object)
    {
        ThrowIfDisposed();
        return _source.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);
    }

    /// <summary>
    /// A staging view over this view: the same quads, and handles for terms
    /// this view does not hold (ADR 0058) — the provisional ids of T1 step 2,
    /// exposed for a caller that composes several changes before one commit.
    /// The staging view reads through this one, so it lives no longer.
    /// </summary>
    public StagingView Stage()
    {
        ThrowIfDisposed();
        return new StagingView(this, _terms);
    }

    /// <summary>Releases the view. In memory this holds nothing a later commit could need back.</summary>
    public void Dispose()
    {
        _disposed = true;

        // The derived blobs the view reads are closed once nothing reads them (ADR 0071).
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
