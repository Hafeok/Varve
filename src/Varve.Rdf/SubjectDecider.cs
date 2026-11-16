// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// Decides, for one binding of one classification, whether an interned
/// subject set lies within a <see cref="SubjectScope"/> (ADR 0235 point 3):
/// once per set id, and each member subject once, both memoised for the
/// decider's lifetime. A decision is a pure function of the source, the
/// classification and the scope, so two threads deciding one set agree.
/// </summary>
internal sealed class SubjectDecider
{
    private static readonly RdfTerm RdfType = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);

    private readonly IQuadSource _source;
    private readonly IRowClassification _rows;
    private readonly BindingIndex _binding;
    [Memoized]
    private readonly ConcurrentDictionary<SubjectSetId, bool> _sets = new();
    [Memoized]
    private readonly ConcurrentDictionary<TermHandle, bool> _subjects;

    internal SubjectDecider(IQuadSource source, IRowClassification rows, BindingIndex binding, SubjectScope scope)
    {
        _source = source;
        _rows = rows;
        _binding = binding;
        Scope = scope;
        _subjects = new ConcurrentDictionary<TermHandle, bool>(source.TermComparer);
    }

    internal SubjectScope Scope { get; }

    /// <summary>Whether every member of <paramref name="set"/> is in the scope; the empty set always is. A decider is built only for a scope that is not every subject.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Allows(SubjectSetId set) => set.IsNone || BySet(set);

    // Decided once per interned set: the first quad of a set pays its
    // members' decisions; every later one is a lookup.
    [DesignDecision(typeof(TheRowScopedQuadSource.SubjectSetDecisionMemoisedPerSet), Scope = ExceptionScope.HotPath)]
    private bool BySet(SubjectSetId set) =>
        _sets.GetOrAdd(set, static (id, self) => self.Decide(id), this);

    private bool Decide(SubjectSetId set)
    {
        foreach (TermHandle subject in _rows.Members(_binding, set))
        {
            if (!AllowsSubject(subject))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one subject is in the scope: by name, by class or by link.</summary>
    internal bool AllowsSubject(TermHandle subject) =>
        Scope.IsAll || _subjects.GetOrAdd(subject, static (handle, self) => self.DecideSubject(handle), this);

    private bool DecideSubject(TermHandle subject)
    {
        if (!_source.TryExternalise(subject, out RdfTerm? term))
        {
            return false;
        }

        if (Scope.AllowsByName(term))
        {
            return true;
        }

        if (!Scope.HasRules || !_source.TryInternalise(RdfType, out TermHandle type))
        {
            return Scope.HasRules && HasAnyLink(subject);
        }

        foreach (RdfTerm @class in Scope.Classes)
        {
            if (_source.TryInternalise(@class, out TermHandle handle) && InBinding(subject, type, handle))
            {
                return true;
            }
        }

        return HasAnyLink(subject);
    }

    private bool HasAnyLink(TermHandle subject)
    {
        foreach (SubjectLink link in Scope.Links)
        {
            if (_source.TryInternalise(link.Predicate, out TermHandle predicate)
                && _source.TryInternalise(link.Target, out TermHandle target)
                && InBinding(subject, predicate, target))
            {
                return true;
            }
        }

        return false;
    }

    // A rule form reads only the subject's own row in the binding's graphs
    // (ADR 0234 point 2), so a quad elsewhere never counts.
    private bool InBinding(TermHandle subject, TermHandle predicate, TermHandle @object)
    {
        using IQuadCursor cursor = _source.Match(subject, predicate, @object, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            if (_rows.BindingOf(cursor.Current.Graph) == _binding)
            {
                return true;
            }
        }

        return false;
    }
}
