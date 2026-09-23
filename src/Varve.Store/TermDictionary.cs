// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
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
/// The dictionary <c>D</c>: canonical ids injective over terms, blank ids as
/// their own identity, inline ids with no entry at all (ADR 0012).
/// </summary>
/// <remarks>
/// <para>
/// Written only by the sequencer, and only after a commit is closed; read
/// concurrently by every view. A view carries the counters of its position —
/// <c>D_P</c> is a prefix of <c>D</c>, because ids are counters and never
/// reused — and treats any id above them as unknown, so later commits change
/// nothing it can see.
/// </para>
/// <para>
/// Blank nodes are externalised with a label derived from the id, so that two
/// externalisations of one node agree. The label is not an identity to send
/// back: a blank node in a request is always fresh (ADR 0044).
/// </para>
/// </remarks>
internal sealed class TermDictionary
{
    private const int ChunkBits = 12;
    private const int ChunkSize = 1 << ChunkBits;

    private readonly ConcurrentDictionary<RdfTerm, ulong> _terms = new(RdfTerm.Comparer);
    private readonly ConcurrentDictionary<(ulong, ulong, ulong), ulong> _triples = new();
    private readonly ConcurrentDictionary<long, (ulong S, ulong P, ulong O)> _tripleComponents = new();
    private RdfTerm[][] _chunks = [];
    private long _canonicalCount;
    private long _blankCount;

    internal long CanonicalCount => Volatile.Read(ref _canonicalCount);

    internal long BlankCount => Volatile.Read(ref _blankCount);

    /// <summary>
    /// The id a term already has, at or below the given canonical counter.
    /// A blank node has none: a label is not a store identity (ADR 0044).
    /// </summary>
    internal bool TryFind(RdfTerm term, long canonicalLimit, out ulong id)
    {
        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                id = 0;
                return false;

            case RdfTermKind.TripleTerm:
                if (TryFind(term.Subject!, canonicalLimit, out ulong s)
                    && TryFind(term.Predicate!, canonicalLimit, out ulong p)
                    && TryFind(term.Object!, canonicalLimit, out ulong o)
                    && _triples.TryGetValue((s, p, o), out id)
                    && TermIds.Counter(id) <= canonicalLimit)
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

                if (_terms.TryGetValue(term, out id) && TermIds.Counter(id) <= canonicalLimit)
                {
                    return true;
                }

                id = 0;
                return false;
        }
    }

    /// <summary>The id a triple term with these components already has.</summary>
    internal bool TryFindTriple(ulong subject, ulong predicate, ulong @object, out ulong id) =>
        _triples.TryGetValue((subject, predicate, @object), out id);

    /// <summary>Whether an id names something in <c>D</c> at the given counters.</summary>
    [HotPath]
    internal static bool IsKnown(ulong id, long canonicalLimit, long blankLimit) =>
        TermIds.ClassOf(id) switch
        {
            IdClass.Canonical => id != 0 && TermIds.Counter(id) <= canonicalLimit,
            IdClass.Blank => TermIds.Counter(id) >= 1 && TermIds.Counter(id) <= blankLimit,
            IdClass.Inline => TermIds.IsValidInline(id),
            _ => false,
        };

    /// <summary>The term an id stands for, when it is known at the given counters.</summary>
    internal bool TryTerm(ulong id, long canonicalLimit, long blankLimit, [MaybeNullWhen(false)] out RdfTerm term)
    {
        if (!IsKnown(id, canonicalLimit, blankLimit))
        {
            term = null;
            return false;
        }

        term = Term(id);
        return true;
    }

    /// <summary>The term for an id known to be in <c>D</c>.</summary>
    internal RdfTerm Term(ulong id) => TermIds.ClassOf(id) switch
    {
        IdClass.Canonical => Canonical(TermIds.Counter(id)),
        IdClass.Blank => BlankTerm(TermIds.Counter(id)),
        IdClass.Inline => TermIds.InlineTerm(id),
        _ => throw new InvalidOperationException("Private terms are not allocated before erasure mode exists."),
    };

    /// <summary>The components of a canonical triple term, or false for any other entry.</summary>
    internal bool TryComponents(long counter, out (ulong S, ulong P, ulong O) components) =>
        _tripleComponents.TryGetValue(counter, out components);

    internal static RdfTerm BlankTerm(long counter) =>
        RdfTerm.BlankNode(Encoding.UTF8.GetBytes("b" + counter.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Adds a closed commit's allocations. Called by the sequencer alone, in id
    /// order, before the readable head moves past the commit — so a view at the
    /// new head always finds what the commit refers to.
    /// </summary>
    internal void Publish(ReadOnlySpan<Allocation> allocations)
    {
        foreach (Allocation allocation in allocations)
        {
            switch (TermIds.ClassOf(allocation.Id))
            {
                case IdClass.Blank:
                    if (TermIds.Counter(allocation.Id) != _blankCount + 1)
                    {
                        throw new InvalidOperationException("Blank ids must be allocated densely and in order.");
                    }

                    Volatile.Write(ref _blankCount, _blankCount + 1);
                    break;

                case IdClass.Canonical:
                    long counter = TermIds.Counter(allocation.Id);

                    if (counter != _canonicalCount + 1)
                    {
                        throw new InvalidOperationException("Canonical ids must be allocated densely and in order.");
                    }

                    // A triple term decoded from the log carries only its component
                    // ids; its components are published already, being older.
                    RdfTerm term = allocation.Term
                        ?? RdfTerm.TripleTerm(Term(allocation.Subject), Term(allocation.Predicate), Term(allocation.Object));

                    Store(counter, term);

                    if (allocation.IsTriple)
                    {
                        _tripleComponents[counter] = (allocation.Subject, allocation.Predicate, allocation.Object);
                        _triples[(allocation.Subject, allocation.Predicate, allocation.Object)] = allocation.Id;
                    }
                    else
                    {
                        _terms[term] = allocation.Id;
                    }

                    Volatile.Write(ref _canonicalCount, counter);
                    break;

                default:
                    throw new InvalidOperationException("Only canonical and blank ids are allocated.");
            }
        }
    }

    private RdfTerm Canonical(long counter)
    {
        RdfTerm[][] chunks = Volatile.Read(ref _chunks);
        long index = counter - 1;
        return chunks[index >> ChunkBits][index & (ChunkSize - 1)];
    }

    private void Store(long counter, RdfTerm term)
    {
        long index = counter - 1;
        long chunk = index >> ChunkBits;
        RdfTerm[][] chunks = _chunks;

        if (chunk >= chunks.Length)
        {
            // Readers holding the old outer array still see every chunk it had;
            // the chunks themselves are shared, never copied.
            RdfTerm[][] larger = new RdfTerm[Math.Max(4, chunks.Length * 2)][];
            chunks.CopyTo(larger, 0);

            for (int i = chunks.Length; i < larger.Length; i++)
            {
                larger[i] = new RdfTerm[ChunkSize];
            }

            Volatile.Write(ref _chunks, larger);
            chunks = larger;
        }

        chunks[chunk][index & (ChunkSize - 1)] = term;
    }
}
