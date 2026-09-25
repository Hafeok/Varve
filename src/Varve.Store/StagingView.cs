// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// A read of one position that can also name terms the position does not
/// hold (ADR 0058): what a caller needs to compose several changes — the
/// operations of one SPARQL Update request — over an overlay, before
/// submitting them as one commit.
/// </summary>
/// <remarks>
/// <para>
/// Reads are the underlying view's: the same quads, the same handles, the
/// same equality. What it adds is <em>provisional handles</em>, from
/// <see cref="Stage"/>, <see cref="StageBlank"/> and <see cref="StageTriple"/>,
/// for a new IRI, literal, blank node or triple term. No quad of the view
/// mentions one; an overlay's delta may.
/// </para>
/// <para>
/// <strong>A provisional handle reaches the dictionary or the log only
/// through the commit that maps it</strong> with <see cref="ToRequestTerm"/>.
/// Given to <see cref="Dataset.CommitAsync"/> directly, as an existing
/// handle, it is an unknown id and the request fails. A staging view
/// allocates nothing in the dictionary, and dropping one discards its terms
/// (I3).
/// </para>
/// </remarks>
public sealed class StagingView : IQuadSource
{
    private readonly DatasetView _view;
    private readonly TermView _terms;
    private readonly Dictionary<RdfTerm, ulong> _byTerm = new(RdfTerm.Comparer);
    private readonly Dictionary<long, RdfTerm> _staged = [];
    private readonly Dictionary<(ulong S, ulong P, ulong O), ulong> _tripleByParts = [];
    private readonly Dictionary<ulong, (ulong S, ulong P, ulong O)> _triples = [];
    private long _canonical;
    private long _blanks;

    internal StagingView(DatasetView view, TermView terms)
    {
        _view = view;
        _terms = terms;
    }

    /// <summary>The position the view reads.</summary>
    public long Position => _view.Position;

    /// <inheritdoc />
    public IEqualityComparer<TermHandle> TermComparer => _view.TermComparer;

    /// <summary>
    /// The handle of a term: the view's own when it holds the term, and
    /// otherwise a provisional one, the same for the same term.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The term is a blank node, or a triple term with a blank node inside:
    /// a new blank node is fresh, not looked up (<see cref="StageBlank"/>).
    /// </exception>
    public TermHandle Stage(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        if (_view.TryInternalise(term, out TermHandle handle))
        {
            return handle;
        }

        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                throw new ArgumentException(
                    "A blank node is not staged by value: StageBlank gives a fresh one (ADR 0044).", nameof(term));

            case RdfTermKind.TripleTerm:
                return StageTriple(Stage(term.Subject!), Stage(term.Predicate!), Stage(term.Object!));

            default:
                if (_byTerm.TryGetValue(term, out ulong id))
                {
                    return new TermHandle(id);
                }

                _staged[++_canonical] = term;
                id = TermIds.Canonical(TermIds.ProvisionalBit | _canonical);
                _byTerm[term] = id;
                return new TermHandle(id);
        }
    }

    /// <summary>A new blank node, distinct from every other.</summary>
    public TermHandle StageBlank() => new(TermIds.Blank(TermIds.ProvisionalBit | ++_blanks));

    /// <summary>
    /// The triple term over three handles of this view: the view's own when
    /// it holds it, and otherwise a provisional one, the same for the same
    /// parts.
    /// </summary>
    /// <exception cref="ArgumentException">A handle is neither the view's nor provisional.</exception>
    public TermHandle StageTriple(TermHandle subject, TermHandle predicate, TermHandle @object)
    {
        ulong s = Known(subject, nameof(subject));
        ulong p = Known(predicate, nameof(predicate));
        ulong o = Known(@object, nameof(@object));

        if (!TermIds.IsProvisional(s) && !TermIds.IsProvisional(p) && !TermIds.IsProvisional(o)
            && _terms.Dictionary.TryFindTriple(s, p, o, out ulong existing)
            && TermDictionary.IsKnown(existing, _terms.CanonicalCount, _terms.BlankCount))
        {
            return new TermHandle(existing);
        }

        if (!_tripleByParts.TryGetValue((s, p, o), out ulong id))
        {
            id = TermIds.Canonical(TermIds.ProvisionalBit | ++_canonical);
            _tripleByParts[(s, p, o)] = id;
            _triples[id] = (s, p, o);
        }

        return new TermHandle(id);
    }

    /// <summary>
    /// What a <see cref="CommitRequest"/> takes for a handle of this view: the
    /// view's own handle as <see cref="RequestTerm.Existing"/>, and a
    /// provisional one as its term — a provisional blank node as a blank node
    /// whose label is unique within this staging view, so that all its
    /// occurrences in one request are one fresh node (T1 step 2).
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// A provisional triple term contains one of the view's blank nodes: a
    /// request cannot build a new triple term around an existing blank node
    /// (ADR 0044's stated limit).
    /// </exception>
    public RequestTerm ToRequestTerm(TermHandle handle)
    {
        ulong id = Known(handle, nameof(handle));
        return TermIds.IsProvisional(id) ? RequestTerm.FromTerm(Provisional(id, forRequest: true)) : RequestTerm.Existing(handle);
    }

    /// <inheritdoc />
    /// <remarks>True for a term the view holds or that has been staged; never stages.</remarks>
    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        ArgumentNullException.ThrowIfNull(term);

        if (_view.TryInternalise(term, out handle))
        {
            return true;
        }

        if (term.Kind != RdfTermKind.BlankNode && _byTerm.TryGetValue(term, out ulong id))
        {
            handle = new TermHandle(id);
            return true;
        }

        if (term.Kind == RdfTermKind.TripleTerm
            && TryInternalise(term.Subject!, out TermHandle s)
            && TryInternalise(term.Predicate!, out TermHandle p)
            && TryInternalise(term.Object!, out TermHandle o)
            && _tripleByParts.TryGetValue((s.Value, p.Value, o.Value), out id))
        {
            handle = new TermHandle(id);
            return true;
        }

        handle = TermHandle.None;
        return false;
    }

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        if (!TermIds.IsProvisional(handle.Value))
        {
            return _view.TryExternalise(handle, out term);
        }

        if (!IsStaged(handle.Value))
        {
            term = null;
            return false;
        }

        term = Provisional(handle.Value, forRequest: false);
        return true;
    }

    /// <inheritdoc />
    public bool Contains(in Quad quad) => _view.Contains(in quad);

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _view.Match(subject, predicate, @object, graph);

    /// <inheritdoc />
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _view.Estimate(subject, predicate, @object, graph);

    /// <inheritdoc />
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _view.TryGetInlineValue(handle, out value);

    // The label a provisional blank node externalises to, and the label it
    // carries into a request: "staged" cannot be a view's own ("b<n>").
    private static RdfTerm StagedBlank(long counter) =>
        RdfTerm.BlankNode(Encoding.UTF8.GetBytes("staged" + counter.ToString(CultureInfo.InvariantCulture)));

    private RdfTerm Provisional(ulong id, bool forRequest)
    {
        long counter = TermIds.Counter(id) & ~TermIds.ProvisionalBit;

        if (TermIds.ClassOf(id) == IdClass.Blank)
        {
            return StagedBlank(counter);
        }

        if (_triples.TryGetValue(id, out (ulong S, ulong P, ulong O) parts))
        {
            return RdfTerm.TripleTerm(Part(parts.S, forRequest), Part(parts.P, forRequest), Part(parts.O, forRequest));
        }

        return _staged[counter];
    }

    private RdfTerm Part(ulong id, bool forRequest)
    {
        if (TermIds.IsProvisional(id))
        {
            return Provisional(id, forRequest);
        }

        if (!_view.TryExternalise(new TermHandle(id), out RdfTerm? term))
        {
            throw new InvalidOperationException("A staged triple term's part is not in the view.");
        }

        if (forRequest && HasBlank(term))
        {
            throw new NotSupportedException(
                "A request cannot build a new triple term around an existing blank node (ADR 0044).");
        }

        return term;
    }

    private static bool HasBlank(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.BlankNode => true,
        RdfTermKind.TripleTerm => HasBlank(term.Subject!) || HasBlank(term.Predicate!) || HasBlank(term.Object!),
        _ => false,
    };

    private bool IsStaged(ulong id)
    {
        long counter = TermIds.Counter(id) & ~TermIds.ProvisionalBit;
        return counter >= 1 && counter <= (TermIds.ClassOf(id) == IdClass.Blank ? _blanks : _canonical);
    }

    private ulong Known(TermHandle handle, string name)
    {
        ulong id = handle.Value;
        bool known = TermIds.IsProvisional(id)
            ? IsStaged(id)
            : TermDictionary.IsKnown(id, _terms.CanonicalCount, _terms.BlankCount);

        return known ? id : throw new ArgumentException("Not a handle of this staging view.", name);
    }
}
