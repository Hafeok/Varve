// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A caller's subject sets, one per classifier binding (ADR 0230 point 4):
/// a binding the scope does not name is every subject, so
/// <see cref="All"/> restricts nothing and builds no wrapper.
/// </summary>
[Contract(typeof(TheRowScopedQuadSource.SubjectScopeAndRowScope), Role = "a subject set per binding")]
public sealed class RowScope
{
    private readonly Dictionary<RdfTerm, SubjectScope> _byBinding;

    private RowScope(Dictionary<RdfTerm, SubjectScope> byBinding) => _byBinding = byBinding;

    /// <summary>No binding restricted: every row of every binding.</summary>
    public static RowScope All { get; } = new(new Dictionary<RdfTerm, SubjectScope>(RdfTerm.Comparer));

    /// <summary>Whether no binding is restricted.</summary>
    public bool IsAll => _byBinding.Count == 0;

    /// <summary>The restricted bindings, by IRI.</summary>
    public IEnumerable<RdfTerm> Bindings => _byBinding.Keys;

    /// <summary>The scope restricting each binding of <paramref name="restrictions"/>; a subject set of every subject restricts nothing and is dropped.</summary>
    /// <exception cref="ArgumentException">A binding is not an IRI, or appears twice.</exception>
    public static RowScope Of(IEnumerable<KeyValuePair<RdfTerm, SubjectScope>> restrictions)
    {
        ArgumentNullException.ThrowIfNull(restrictions);
        Dictionary<RdfTerm, SubjectScope> byBinding = new(RdfTerm.Comparer);

        foreach ((RdfTerm binding, SubjectScope subjects) in restrictions)
        {
            if (binding is not { Kind: RdfTermKind.Iri })
            {
                throw new ArgumentException("A classifier binding is named by an IRI.", nameof(restrictions));
            }

            ArgumentNullException.ThrowIfNull(subjects);

            if (subjects.IsAll)
            {
                continue;
            }

            if (!byBinding.TryAdd(binding, subjects))
            {
                throw new ArgumentException("A binding appears once in a row scope.", nameof(restrictions));
            }
        }

        return byBinding.Count == 0 ? All : new RowScope(byBinding);
    }

    /// <summary>The subject set for <paramref name="binding"/>: every subject when the scope does not restrict it.</summary>
    public SubjectScope For(RdfTerm binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _byBinding.TryGetValue(binding, out SubjectScope? subjects) ? subjects : SubjectScope.All;
    }
}
