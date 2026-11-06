// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// The facts of a closure with the indices its rules join over: by subject,
/// by object, by predicate, by subject and predicate, and by predicate and
/// object (ADR 0195 point 2). In memory, over handles.
/// </summary>
internal sealed class FactSet
{
    private readonly HashSet<Fact> _all = [];
    private readonly Dictionary<ulong, HashSet<Fact>> _bySubject = [];
    private readonly Dictionary<ulong, HashSet<Fact>> _byPredicate = [];
    private readonly Dictionary<ulong, HashSet<Fact>> _byObject = [];
    private readonly Dictionary<(ulong, ulong), HashSet<Fact>> _bySubjectPredicate = [];
    private readonly Dictionary<(ulong, ulong), HashSet<Fact>> _byPredicateObject = [];

    internal int Count => _all.Count;

    internal IEnumerable<Fact> All => _all;

    internal bool Contains(in Fact fact) => _all.Contains(fact);

    internal bool Add(in Fact fact)
    {
        if (!_all.Add(fact))
        {
            return false;
        }

        Index(_bySubject, fact.Subject.Value, fact);
        Index(_byPredicate, fact.Predicate.Value, fact);
        Index(_byObject, fact.Object.Value, fact);
        Index(_bySubjectPredicate, (fact.Subject.Value, fact.Predicate.Value), fact);
        Index(_byPredicateObject, (fact.Predicate.Value, fact.Object.Value), fact);
        return true;
    }

    internal bool Remove(in Fact fact)
    {
        if (!_all.Remove(fact))
        {
            return false;
        }

        Unindex(_bySubject, fact.Subject.Value, fact);
        Unindex(_byPredicate, fact.Predicate.Value, fact);
        Unindex(_byObject, fact.Object.Value, fact);
        Unindex(_bySubjectPredicate, (fact.Subject.Value, fact.Predicate.Value), fact);
        Unindex(_byPredicateObject, (fact.Predicate.Value, fact.Object.Value), fact);
        return true;
    }

    /// <summary>
    /// The facts matching a pattern, <see cref="TermHandle.None"/> a wildcard,
    /// from the narrowest index that answers it. The caller does not change
    /// the set while it reads the answer.
    /// </summary>
    internal IEnumerable<Fact> Match(TermHandle subject, TermHandle predicate, TermHandle @object)
    {
        bool s = !subject.IsNone, p = !predicate.IsNone, o = !@object.IsNone;
        if (s && p && o)
        {
            Fact fact = new(subject, predicate, @object);
            return _all.Contains(fact) ? [fact] : [];
        }

        if (s && p)
        {
            return Of(_bySubjectPredicate, (subject.Value, predicate.Value));
        }

        if (p && o)
        {
            return Of(_byPredicateObject, (predicate.Value, @object.Value));
        }

        if (s)
        {
            return o ? Filter(Of(_bySubject, subject.Value), f => f.Object.Value == @object.Value) : Of(_bySubject, subject.Value);
        }

        if (p)
        {
            return Of(_byPredicate, predicate.Value);
        }

        return o ? Of(_byObject, @object.Value) : _all;
    }

    private static IEnumerable<Fact> Filter(IEnumerable<Fact> facts, System.Func<Fact, bool> keep)
    {
        foreach (Fact fact in facts)
        {
            if (keep(fact))
            {
                yield return fact;
            }
        }
    }

    private static IEnumerable<Fact> Of<TKey>(Dictionary<TKey, HashSet<Fact>> index, TKey key)
        where TKey : notnull =>
        index.TryGetValue(key, out HashSet<Fact>? facts) ? facts : System.Array.Empty<Fact>();

    private static void Index<TKey>(Dictionary<TKey, HashSet<Fact>> index, TKey key, in Fact fact)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out HashSet<Fact>? facts))
        {
            index[key] = facts = [];
        }

        facts.Add(fact);
    }

    private static void Unindex<TKey>(Dictionary<TKey, HashSet<Fact>> index, TKey key, in Fact fact)
        where TKey : notnull
    {
        if (index.TryGetValue(key, out HashSet<Fact>? facts) && facts.Remove(fact) && facts.Count == 0)
        {
            index.Remove(key);
        }
    }
}
