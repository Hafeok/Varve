// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>One entry of <c>alloc</c>: a fresh id and what it stands for.</summary>
/// <remarks>
/// A canonical IRI or literal carries its term. A triple term carries its
/// three component ids, because its identity depends on theirs — a triple term
/// around a blank node is a different term for each blank node — and its
/// materialised term for reading. A blank node carries nothing: it is its own
/// identity.
/// </remarks>
internal readonly struct Allocation
{
    internal Allocation(ulong id, RdfTerm? term, ulong subject = 0, ulong predicate = 0, ulong @object = 0)
    {
        Id = id;
        Term = term;
        Subject = subject;
        Predicate = predicate;
        Object = @object;
    }

    internal ulong Id { get; }

    internal RdfTerm? Term { get; }

    internal ulong Subject { get; }

    internal ulong Predicate { get; }

    internal ulong Object { get; }

    internal bool IsTriple => Subject != 0;
}


/// <summary>
/// The dictionary <c>D</c>, read from the term sections of the runs a view
/// holds (ADR 0079): canonical ids injective over terms, blank ids as their own
/// identity, inline ids with no entry at all (ADR 0012).
/// </summary>
/// <remarks>
/// <para>
/// **Nothing per term lives here.** Every entry is in some run's term
/// section — in memory for the memtable's runs, on a derived blob for the
/// others — and the runs a version holds cover every canonical counter from 1
/// to its position's, so a view finds every term it can see in the runs it
/// already holds open. Opening a dataset therefore loads no dictionary.
/// </para>
/// <para>
/// **Two caches**, fixed in size: term to id and id to term, one slot per hash.
/// A canonical id is never reused and never changes its term, so an entry is
/// never stale, whichever version filled it; a view checks the id against its
/// own counter. A lookup that misses reads the sections and fills the slot.
/// </para>
/// <para>
/// Blank nodes are externalised with a label derived from the id, so that two
/// externalisations of one node agree. The label is not an identity to send
/// back: a blank node in a request is always fresh (ADR 0044).
/// </para>
/// </remarks>
internal sealed class TermDictionary
{
    /// <summary>Slots per cache: 65,536, so the two hold at most 131,072 terms.</summary>
    internal const int CacheSlots = 1 << 16;

    private const int StackBytes = 256;

    private readonly Cached?[] _byTerm = new Cached?[CacheSlots];
    private readonly Cached?[] _byId = new Cached?[CacheSlots];

    /// <summary>
    /// The id a term already has, at or below the given canonical counter, in
    /// the runs given. A blank node has none: a label is not a store identity
    /// (ADR 0044).
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryFind(Run[] runs, RdfTerm term, long canonicalLimit, out ulong id)
    {
        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                id = 0;
                return false;

            case RdfTermKind.TripleTerm:
                if (TryFind(runs, term.Subject!, canonicalLimit, out ulong s)
                    && TryFind(runs, term.Predicate!, canonicalLimit, out ulong p)
                    && TryFind(runs, term.Object!, canonicalLimit, out ulong o)
                    && TryFindTriple(runs, s, p, o, canonicalLimit, out id))
                {
                    return true;
                }

                id = 0;
                return false;

            default:
                if (TermIds.TryInline(term, out id))
                {
                    return true;
                }

                if (TryCached(term, out id))
                {
                    return TermIds.Counter(id) <= canonicalLimit;
                }

                Span<byte> stack = stackalloc byte[StackBytes];
                int length = TermKey.Write(term, stack);

                if (length >= 0)
                {
                    return Remember(term, Search(runs, stack[..length], canonicalLimit, out id), id);
                }

                byte[] rented = ArrayPool<byte>.Shared.Rent(-length);

                try
                {
                    length = TermKey.Write(term, rented);
                    return Remember(term, Search(runs, rented.AsSpan(0, length), canonicalLimit, out id), id);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
        }
    }

    /// <summary>The id of the canonical term with this key — lowercased tag and all — at or below the counter.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool TryFindKey(Run[] runs, ReadOnlySpan<byte> key, long canonicalLimit, out ulong id) =>
        Search(runs, key, canonicalLimit, out id);

    /// <summary>The id a triple term with these components already has, at or below the counter.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool TryFindTriple(Run[] runs, ulong subject, ulong predicate, ulong @object, long canonicalLimit, out ulong id)
    {
        Span<byte> key = stackalloc byte[32];
        int length = TermKey.WriteTriple(subject, predicate, @object, key);
        return Search(runs, key[..length], canonicalLimit, out id);
    }

    // The sections newest first; an id found above the limit is not one the
    // caller can see, and since ids are unique, nowhere else holds the term.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static bool Search(Run[] runs, ReadOnlySpan<byte> key, long canonicalLimit, out ulong id)
    {
        ulong hash = TermKey.Hash(key);
        int scratchLength = key.Length + LogFormat.MaxUlebLength + 1;
        byte[]? rented = scratchLength > StackBytes ? ArrayPool<byte>.Shared.Rent(scratchLength) : null;
        Span<byte> scratch = rented is null ? stackalloc byte[StackBytes] : rented;

        try
        {
            for (int i = runs.Length - 1; i >= 0; i--)
            {
                TermSection section = runs[i].Terms;

                if (section.Count > 0 && section.From < canonicalLimit && section.TryFind(key, hash, scratch, out id))
                {
                    return TermIds.Counter(id) <= canonicalLimit;
                }
            }

            id = 0;
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    // The cache's slot for the term: a hash table keyed by term under
    // RdfTerm's comparer, one entry per slot.
    [DesignDecision(typeof(StoreHotPathScope.TermLookupsAreHashLookups), Scope = ExceptionScope.HotPath)]
    private bool TryCached(RdfTerm term, out ulong id)
    {
        Cached? cached = _byTerm[term.GetHashCode() & (CacheSlots - 1)];

        if (cached is not null && cached.Term.Equals(term))
        {
            id = cached.Id;
            return true;
        }

        id = 0;
        return false;
    }

    // A term found is remembered in its slot. The slot's entry is the one
    // allocation a lookup makes, and only on a miss that found the term.
    [DesignDecision(typeof(TheTermDictionaryOnDisk.DictionaryCachesAreBounded), Scope = ExceptionScope.HotPath)]
    private bool Remember(RdfTerm term, bool found, ulong id)
    {
        if (found)
        {
            _byTerm[term.GetHashCode() & (CacheSlots - 1)] = new Cached(term, id);
        }

        return found;
    }

    /// <summary>Whether an id names something in <c>D</c> at the given counters.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool IsKnown(ulong id, long canonicalLimit, long blankLimit) =>
        TermIds.ClassOf(id) switch
        {
            IdClass.Canonical => id != 0 && TermIds.Counter(id) <= canonicalLimit,
            IdClass.Blank => TermIds.Counter(id) >= 1 && TermIds.Counter(id) <= blankLimit,
            IdClass.Inline => TermIds.IsValidInline(id),
            _ => false,
        };

    /// <summary>The term an id stands for, when it is known at the given counters.</summary>
    internal bool TryTerm(Run[] runs, ulong id, long canonicalLimit, long blankLimit, [MaybeNullWhen(false)] out RdfTerm term)
    {
        if (!IsKnown(id, canonicalLimit, blankLimit))
        {
            term = null;
            return false;
        }

        term = Term(runs, id);
        return true;
    }

    /// <summary>The term for an id known to be in <c>D</c> and held by the runs.</summary>
    internal RdfTerm Term(Run[] runs, ulong id) => TermIds.ClassOf(id) switch
    {
        IdClass.Canonical => Canonical(runs, id),
        IdClass.Blank => BlankTerm(TermIds.Counter(id)),
        IdClass.Inline => TermIds.InlineTerm(id),
        _ => throw new InvalidOperationException("Private terms are not allocated before erasure mode exists."),
    };

    /// <summary>The entry of a canonical id: its term, or a triple term's components.</summary>
    internal static Allocation Entry(Run[] runs, ulong id)
    {
        long counter = TermIds.Counter(id);

        for (int i = runs.Length - 1; i >= 0; i--)
        {
            TermSection section = runs[i].Terms;

            if (section.Holds(counter))
            {
                Span<byte> stack = stackalloc byte[StackBytes];
                int length = section.ReadEntry(counter, stack);
                byte[] bytes = length >= 0 ? stack[..length].ToArray() : new byte[-length];

                if (length < 0)
                {
                    section.ReadEntry(counter, bytes);
                }

                LogFormat.Reader reader = new(bytes, 0);
                Allocation entry = LogFormat.ReadAllocation(ref reader);
                reader.End();

                if (entry.Id != id)
                {
                    throw new InvalidOperationException("A dictionary entry names id " + entry.Id + " where " + id + " was due.");
                }

                return entry;
            }
        }

        throw new InvalidOperationException("No run holds the dictionary entry of id " + id + ".");
    }

    private RdfTerm Canonical(Run[] runs, ulong id)
    {
        int slot = (int)(id & (CacheSlots - 1));
        Cached? cached = _byId[slot];

        if (cached is not null && cached.Id == id)
        {
            return cached.Term;
        }

        Allocation entry = Entry(runs, id);
        RdfTerm term = entry.Term ?? RdfTerm.TripleTerm(Term(runs, entry.Subject), Term(runs, entry.Predicate), Term(runs, entry.Object));
        _byId[slot] = new Cached(term, id);
        return term;
    }

    internal static RdfTerm BlankTerm(long counter) =>
        RdfTerm.BlankNode(Encoding.UTF8.GetBytes("b" + counter.ToString(CultureInfo.InvariantCulture)));

    private sealed class Cached(RdfTerm term, ulong id)
    {
        internal RdfTerm Term { get; } = term;

        internal ulong Id { get; } = id;
    }
}
