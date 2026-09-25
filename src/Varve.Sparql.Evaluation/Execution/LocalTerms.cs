// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Execution;

/// <summary>
/// The execution's own terms: everything a solution holds that the source does
/// not (<c>sparql-evaluation.md</c> §4.1). Interned by term equality, so equal
/// terms have equal indexes, and each keeps its parsed numeric value once
/// asked, so a constant is parsed once per execution.
/// </summary>
internal sealed class LocalTerms
{
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<RdfTerm, int> _index = new(RdfTerm.Comparer);

    internal int Count => _entries.Count;

    /// <summary>The raw value (index from 1) of a term, interning it.</summary>
    internal ulong Intern(RdfTerm term)
    {
        if (!_index.TryGetValue(term, out int index))
        {
            index = _entries.Count;
            _entries.Add(new Entry(term));
            _index.Add(term, index);
        }

        return (ulong)index + 1;
    }

    /// <summary>
    /// Interns a term the source handed out, remembering its handle: the
    /// materialised arm (ADR 0050) holds owned terms, but a scan still takes a
    /// handle, and a blank node's label does not internalise back to one.
    /// </summary>
    internal ulong Intern(RdfTerm term, TermHandle origin)
    {
        ulong raw = Intern(term);
        Entry entry = _entries[(int)raw - 1];
        if (entry.Origin.IsNone)
        {
            entry.Origin = origin;
        }

        return raw;
    }

    internal RdfTerm Get(ulong raw) => _entries[(int)raw - 1].Term;

    /// <summary>The source handle a term was materialised from, if it was.</summary>
    internal bool TryGetOrigin(ulong raw, out TermHandle origin)
    {
        origin = _entries[(int)raw - 1].Origin;
        return !origin.IsNone;
    }

    /// <summary>The term's numeric value, parsed once.</summary>
    internal bool TryGetNumeric(ulong raw, out XsdNumeric value)
    {
        Entry entry = _entries[(int)raw - 1];
        if (entry.State == 0)
        {
            entry.State = Terms.TryNumeric(entry.Term, out entry.Numeric) ? (byte)1 : (byte)2;
        }

        value = entry.Numeric;
        return entry.State == 1;
    }

    private sealed class Entry
    {
        internal Entry(RdfTerm term) => Term = term;

        internal RdfTerm Term { get; }

        internal byte State;

        internal XsdNumeric Numeric;

        internal TermHandle Origin;
    }
}
