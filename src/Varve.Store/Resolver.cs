// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// Spec T1 step 2: a request's terms to ids. Unknown terms get provisional ids;
/// each distinct blank label in the request is one fresh node.
/// </summary>
/// <remarks>
/// <para>
/// Provisional ids are numbered past the head's counters in the order terms
/// are met. <see cref="Finalise"/> keeps only those the effective delta and
/// the metadata still refer to — directly, or as a triple term's component —
/// and renumbers them densely in the same order. So an assert-then-retract of
/// a new term allocates nothing, no counter has a gap, and the same requests
/// allocate the same ids on every machine.
/// </para>
/// <para>
/// A rejected or empty request drops the resolver and leaves no trace (I3).
/// </para>
/// </remarks>
internal sealed class Resolver
{
    private readonly TermDictionary _dictionary;
    private readonly long _canonicalLimit;
    private readonly long _blankLimit;
    private readonly List<Allocation> _allocations = [];
    private readonly Dictionary<RdfTerm, ulong> _terms = new(RdfTerm.Comparer);
    private readonly Dictionary<(ulong, ulong, ulong), ulong> _triples = [];
    private readonly Dictionary<RdfTerm, ulong> _labels = new(RdfTerm.Comparer);
    private readonly Dictionary<ulong, RdfTerm> _fresh = [];
    private long _nextCanonical;
    private long _nextBlank;

    internal Resolver(TermDictionary dictionary, long canonicalLimit, long blankLimit)
    {
        _dictionary = dictionary;
        _canonicalLimit = canonicalLimit;
        _blankLimit = blankLimit;
        _nextCanonical = canonicalLimit + 1;
        _nextBlank = blankLimit + 1;
    }

    /// <summary>The allocations, in id order within each class. Final after <see cref="Finalise"/>.</summary>
    internal IReadOnlyList<Allocation> Allocations => _allocations;

    internal ulong Resolve(RequestTerm term)
    {
        if (term.IsNone)
        {
            return 0;
        }

        if (term.IsExisting)
        {
            ulong id = term.Handle.Value;

            if (!TermDictionary.IsKnown(id, _canonicalLimit, _blankLimit))
            {
                throw new ArgumentException(
                    "The handle " + id + " was not issued by this dataset at its head. An existing term is "
                    + "addressed by a handle from a read of the same dataset (ADR 0044).",
                    nameof(term));
            }

            return id;
        }

        return Resolve(term.Term!);
    }

    internal ulong Resolve(RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                if (!_labels.TryGetValue(term, out ulong blank))
                {
                    blank = TermIds.Blank(_nextBlank++);
                    _labels[term] = blank;
                    _allocations.Add(new Allocation(blank, null));
                }

                return blank;

            case RdfTermKind.TripleTerm:
                ulong s = Resolve(term.Subject!);
                ulong p = Resolve(term.Predicate!);
                ulong o = Resolve(term.Object!);

                if (_triples.TryGetValue((s, p, o), out ulong triple) || _dictionary.TryFindTriple(s, p, o, out triple))
                {
                    return triple;
                }

                triple = TermIds.Canonical(_nextCanonical++);
                _triples[(s, p, o)] = triple;
                _allocations.Add(new Allocation(triple, null, s, p, o));
                return triple;

            default:
                if (_dictionary.TryFind(term, _canonicalLimit, out ulong existing) || _terms.TryGetValue(term, out existing))
                {
                    return existing;
                }

                ulong fresh = TermIds.Canonical(_nextCanonical++);
                _terms[term] = fresh;
                _allocations.Add(new Allocation(fresh, term));
                return fresh;
        }
    }

    /// <summary>
    /// Keeps the provisional ids that are referenced and renumbers them densely,
    /// in the order they were met. Returns the mapping for the caller to apply
    /// to its quads and metadata; ids that were not provisional map to themselves.
    /// </summary>
    internal Func<ulong, ulong> Finalise(HashSet<ulong> referenced)
    {
        // Components come before the triple terms that contain them, so a
        // backwards pass sees every triple before its components.
        for (int i = _allocations.Count - 1; i >= 0; i--)
        {
            Allocation allocation = _allocations[i];

            if (allocation.IsTriple && referenced.Contains(allocation.Id))
            {
                referenced.Add(allocation.Subject);
                referenced.Add(allocation.Predicate);
                referenced.Add(allocation.Object);
            }
        }

        Dictionary<ulong, ulong> map = [];
        long canonical = _canonicalLimit;
        long blank = _blankLimit;

        foreach (Allocation allocation in _allocations)
        {
            if (referenced.Contains(allocation.Id))
            {
                map[allocation.Id] = TermIds.ClassOf(allocation.Id) == IdClass.Blank
                    ? TermIds.Blank(++blank)
                    : TermIds.Canonical(++canonical);
            }
        }

        ulong Map(ulong id) => map.TryGetValue(id, out ulong final) ? final : id;

        List<Allocation> kept = [];
        _terms.Clear();
        _triples.Clear();
        _fresh.Clear();

        foreach (Allocation allocation in _allocations)
        {
            if (!map.TryGetValue(allocation.Id, out ulong id))
            {
                continue;
            }

            Allocation final = allocation.IsTriple
                ? new Allocation(id, null, Map(allocation.Subject), Map(allocation.Predicate), Map(allocation.Object))
                : new Allocation(id, allocation.Term);
            kept.Add(Materialise(final));
        }

        _allocations.Clear();
        _allocations.AddRange(kept);
        _labels.Clear();
        _nextCanonical = canonical + 1;
        _nextBlank = blank + 1;
        return Map;
    }

    // A final entry with its term filled in, and indexed for later resolution.
    private Allocation Materialise(Allocation allocation)
    {
        RdfTerm? term = allocation.Term;

        if (allocation.IsTriple)
        {
            term = RdfTerm.TripleTerm(TermOf(allocation.Subject), TermOf(allocation.Predicate), TermOf(allocation.Object));
            _triples[(allocation.Subject, allocation.Predicate, allocation.Object)] = allocation.Id;
        }
        else if (term is not null)
        {
            _terms[term] = allocation.Id;
        }

        Allocation final = new(allocation.Id, term, allocation.Subject, allocation.Predicate, allocation.Object);

        if (term is not null)
        {
            _fresh[allocation.Id] = term;
        }

        return final;
    }

    /// <summary>
    /// After <see cref="Finalise"/>: resolves a term with final ids directly,
    /// for what arrives after validation — a validator's attachment.
    /// </summary>
    internal ulong ResolveFinal(RdfTerm term)
    {
        int before = _allocations.Count;
        ulong id = Resolve(term);

        for (int i = before; i < _allocations.Count; i++)
        {
            _allocations[i] = Materialise(_allocations[i]);
        }

        return id;
    }

    private RdfTerm TermOf(ulong id) =>
        _fresh.TryGetValue(id, out RdfTerm? term) ? term
        : TermIds.ClassOf(id) == IdClass.Blank && TermIds.Counter(id) > _blankLimit ? TermDictionary.BlankTerm(TermIds.Counter(id))
        : _dictionary.Term(id);
}
