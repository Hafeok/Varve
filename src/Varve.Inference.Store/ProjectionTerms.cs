// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Inference.Store;

/// <summary>
/// The dataset's handles, as a binding's closures see them (ADR 0195 point
/// 2): a term the dataset holds is its own handle, which means the same term
/// at every later position; a term a rule names that the dataset does not
/// hold yet has a handle of the projection's own, which no dataset issues,
/// until a commit allocates the term and a generation recompiles the
/// program over the dataset's.
/// </summary>
/// <remarks>
/// <para>
/// A projection-local handle never leaves the projection as such: a read
/// that serves a row naming one stages the term in its own view (ADR 0058).
/// </para>
/// <para>
/// One table lives as long as its projection, so that a row held from an
/// earlier generation still names its term. A term the dataset holds is
/// always answered with the dataset's handle; a projection's own handle,
/// once given, keeps naming its term. Reads name local handles from any
/// thread, under the table's lock.
/// </para>
/// </remarks>
[Contract(typeof(TheInferenceProjection.RowsAreImplicitQuadsOverHandles), Role = "a dataset's handles as a binding's closures see them")]
internal sealed class ProjectionTerms(IQuadSource source) : IInferenceTerms
{
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    // In the canonical class, above the staging view's provisional bit and
    // every counter a dictionary reaches: a dataset never issues one.
    private const ulong LocalTag = (1UL << 61) | (1UL << 60);

    private readonly System.Threading.Lock _lock = new();
    private readonly Dictionary<RdfTerm, TermHandle> _local = new(RdfTerm.Comparer);
    private readonly List<RdfTerm> _localTerms = [];
    private readonly HashSet<RdfTerm> _active = new(RdfTerm.Comparer);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, RdfTerm> _known = new();

    /// <summary>The source the dataset's handles are looked up and named through: the view a commit or a read is applied over.</summary>
    internal IQuadSource Source { get; set; } = source;

    /// <summary>The terms the current program names by a handle of the projection's own: a commit that allocates one starts a generation.</summary>
    internal IReadOnlyCollection<RdfTerm> LocalTerms => _active;

    /// <summary>The projection's own terms, in the order their handles were given: what a saved state carries.</summary>
    internal IReadOnlyList<RdfTerm> LocalTable()
    {
        lock (_lock)
        {
            return [.. _localTerms];
        }
    }

    /// <summary>Replaces the projection's own terms with a saved table, so that each saved handle names its term again.</summary>
    internal void Restore(IReadOnlyList<RdfTerm> table)
    {
        lock (_lock)
        {
            _local.Clear();
            _localTerms.Clear();
            _active.Clear();
            foreach (RdfTerm term in table)
            {
                _localTerms.Add(term);
                _local[term] = new TermHandle(LocalTag | (uint)_localTerms.Count);
            }
        }
    }

    /// <summary>Starts a program: the terms it names by a local handle are counted afresh.</summary>
    internal void BeginProgram() => _active.Clear();

    public TermHandle Handle(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (Source.TryInternalise(term, out TermHandle handle))
        {
            return handle;
        }

        lock (_lock)
        {
            if (!_local.TryGetValue(term, out TermHandle local))
            {
                _localTerms.Add(term);
                local = new TermHandle(LocalTag | (uint)_localTerms.Count);
                _local[term] = local;
            }

            _active.Add(term);
            return local;
        }
    }

    /// <summary>Whether a handle is the projection's own, not the dataset's.</summary>
    internal static bool IsLocal(TermHandle handle) => (handle.Value & LocalTag) == LocalTag && (handle.Value >> 62) == 0;

    /// <summary>The term a handle stands for: the projection's own, or the dataset's.</summary>
    internal bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        if (IsLocal(handle))
        {
            int index = (int)(handle.Value & ~LocalTag) - 1;
            lock (_lock)
            {
                term = index >= 0 && index < _localTerms.Count ? _localTerms[index] : null;
            }

            return term is not null;
        }

        if (_known.TryGetValue(handle.Value, out term))
        {
            return true;
        }

        if (!Source.TryExternalise(handle, out term))
        {
            return false;
        }

        _known[handle.Value] = term;
        return true;
    }

    public bool IsLiteral(TermHandle handle) => TryExternalise(handle, out RdfTerm? term) && term.Kind == RdfTermKind.Literal;

    /// <summary>Whether a handle names an IRI: what a served row's predicate is.</summary>
    internal bool IsIri(TermHandle handle) => TryExternalise(handle, out RdfTerm? term) && term.Kind == RdfTermKind.Iri;

    public bool TryDatatype(TermHandle literal, out TermHandle datatype)
    {
        if (!TryExternalise(literal, out RdfTerm? term) || term.Kind != RdfTermKind.Literal)
        {
            datatype = TermHandle.None;
            return false;
        }

        datatype = Handle(RdfTerm.Iri(term.DatatypeIri));
        return true;
    }

    public bool IsContainerMembership(TermHandle handle)
    {
        if (!TryExternalise(handle, out RdfTerm? term) || term.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        string iri = Encoding.UTF8.GetString(term.Lexical);
        if (!iri.StartsWith(RdfNamespace + "_", StringComparison.Ordinal) || iri.Length == RdfNamespace.Length + 1)
        {
            return false;
        }

        string digits = iri[(RdfNamespace.Length + 1)..];
        return digits[0] != '0' && digits.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;
    }
}
