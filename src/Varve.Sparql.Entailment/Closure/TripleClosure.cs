// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Sparql.Entailment.Closure;

/// <summary>
/// A set of generalised triples closed under a <see cref="RuleSet"/> by
/// forward chaining, each new triple joined once with every triple before it
/// (semi-naive). It may stand on a closed parent it never changes, so that
/// the closure of one triple more than a closed schema costs that triple's
/// consequences alone (ADR 0193 point 3).
/// </summary>
/// <remarks>
/// It works on terms, not handles: a schema is small, and the test oracle
/// runs it over a whole generated graph. Nothing here runs per quad of a
/// store.
/// </remarks>
internal sealed class TripleClosure
{
    private readonly TripleClosure? _parent;
    private readonly RuleSet _rules;
    private readonly HashSet<Triple> _triples = [];
    private readonly Dictionary<RdfTerm, List<Triple>> _byPredicate = new(RdfTerm.Comparer);
    private readonly Dictionary<(RdfTerm, RdfTerm), List<RdfTerm>> _objects = [];
    private readonly Dictionary<(RdfTerm, RdfTerm), List<RdfTerm>> _subjects = [];
    private readonly Queue<Triple> _pending = new();

    internal TripleClosure(RuleSet rules, TripleClosure? parent = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _parent = parent;
    }

    /// <summary>The triples this closure added beyond its parent's.</summary>
    internal IReadOnlyCollection<Triple> Own => _triples;

    /// <summary>Every triple, the parent's and its own.</summary>
    internal IEnumerable<Triple> All
    {
        get
        {
            if (_parent is not null)
            {
                foreach (Triple triple in _parent.All)
                {
                    yield return triple;
                }
            }

            foreach (Triple triple in _triples)
            {
                yield return triple;
            }
        }
    }

    internal bool Contains(Triple triple) => _triples.Contains(triple) || (_parent?.Contains(triple) ?? false);

    /// <summary>The objects of <paramref name="subject"/> under <paramref name="predicate"/>.</summary>
    internal IEnumerable<RdfTerm> Objects(RdfTerm subject, RdfTerm predicate)
    {
        if (_parent is not null)
        {
            foreach (RdfTerm term in _parent.Objects(subject, predicate))
            {
                yield return term;
            }
        }

        if (_objects.TryGetValue((subject, predicate), out List<RdfTerm>? own))
        {
            for (int i = 0; i < own.Count; i++)
            {
                yield return own[i];
            }
        }
    }

    /// <summary>The subjects that have <paramref name="object"/> under <paramref name="predicate"/>.</summary>
    internal IEnumerable<RdfTerm> Subjects(RdfTerm predicate, RdfTerm @object)
    {
        if (_parent is not null)
        {
            foreach (RdfTerm term in _parent.Subjects(predicate, @object))
            {
                yield return term;
            }
        }

        if (_subjects.TryGetValue((predicate, @object), out List<RdfTerm>? own))
        {
            for (int i = 0; i < own.Count; i++)
            {
                yield return own[i];
            }
        }
    }

    /// <summary>The triples under <paramref name="predicate"/>.</summary>
    internal IEnumerable<Triple> WithPredicate(RdfTerm predicate)
    {
        if (_parent is not null)
        {
            foreach (Triple triple in _parent.WithPredicate(predicate))
            {
                yield return triple;
            }
        }

        if (_byPredicate.TryGetValue(predicate, out List<Triple>? own))
        {
            for (int i = 0; i < own.Count; i++)
            {
                yield return own[i];
            }
        }
    }

    /// <summary>Adds a triple; <see cref="Close"/> derives its consequences.</summary>
    internal void Add(RdfTerm s, RdfTerm p, RdfTerm o) => Add(new Triple(s, p, o));

    internal void Add(Triple triple)
    {
        if (Contains(triple))
        {
            return;
        }

        _triples.Add(triple);
        Index(triple);
        _pending.Enqueue(triple);
    }

    /// <summary>Derives every consequence of the triples added since the last call.</summary>
    internal void Close()
    {
        // Snapshots of the partner lists are taken before adding to them: a
        // triple derived while iterating is queued and joined in its turn.
        List<RdfTerm> partners = [];
        List<Triple> joined = [];
        while (_pending.Count > 0)
        {
            Triple t = _pending.Dequeue();
            RdfTerm s = t.S, p = t.P, o = t.O;

            // rdfD2: the predicate of every triple is a property.
            Add(p, Vocabulary.Type, Vocabulary.Property);

            // GrdfD1: a literal of a recognised datatype has that type.
            if (o.Kind == RdfTermKind.Literal && DatatypeOf(o) is { } datatype && _rules.Recognised.Contains(datatype))
            {
                Add(o, Vocabulary.Type, datatype);
            }

            if (!_rules.Rdfs)
            {
                continue;
            }

            // rdfs4a and rdfs4b.
            Add(s, Vocabulary.Type, Vocabulary.Resource);
            Add(o, Vocabulary.Type, Vocabulary.Resource);

            // rdfs2, rdfs3 and rdfs7, with t as the triple a schema triple acts on.
            Fill(partners, Objects(p, Vocabulary.Domain));
            foreach (RdfTerm c in partners)
            {
                Add(s, Vocabulary.Type, c);
            }

            Fill(partners, Objects(p, Vocabulary.Range));
            foreach (RdfTerm c in partners)
            {
                Add(o, Vocabulary.Type, c);
            }

            Fill(partners, Objects(p, Vocabulary.SubPropertyOf));
            foreach (RdfTerm q in partners)
            {
                Add(s, q, o);
            }

            if (p.Equals(Vocabulary.Type))
            {
                // rdfs9, t the instance.
                Fill(partners, Objects(o, Vocabulary.SubClassOf));
                foreach (RdfTerm d in partners)
                {
                    Add(s, Vocabulary.Type, d);
                }

                if (o.Equals(Vocabulary.Property))
                {
                    Add(s, Vocabulary.SubPropertyOf, s); // rdfs6
                }
                else if (o.Equals(Vocabulary.Class))
                {
                    Add(s, Vocabulary.SubClassOf, Vocabulary.Resource); // rdfs8
                    Add(s, Vocabulary.SubClassOf, s); // rdfs10
                }
                else if (o.Equals(Vocabulary.ContainerMembershipProperty))
                {
                    Add(s, Vocabulary.SubPropertyOf, Vocabulary.Member); // rdfs12
                }
                else if (o.Equals(Vocabulary.Datatype))
                {
                    Add(s, Vocabulary.SubClassOf, Vocabulary.Literal); // rdfs13
                }
            }
            else if (p.Equals(Vocabulary.Domain))
            {
                // rdfs2, t the schema triple.
                Fill(joined, WithPredicate(s));
                foreach (Triple x in joined)
                {
                    Add(x.S, Vocabulary.Type, o);
                }
            }
            else if (p.Equals(Vocabulary.Range))
            {
                // rdfs3, t the schema triple.
                Fill(joined, WithPredicate(s));
                foreach (Triple x in joined)
                {
                    Add(x.O, Vocabulary.Type, o);
                }
            }
            else if (p.Equals(Vocabulary.SubPropertyOf))
            {
                // rdfs7, t the schema triple; rdfs5 both ways.
                Fill(joined, WithPredicate(s));
                foreach (Triple x in joined)
                {
                    Add(x.S, o, x.O);
                }

                Fill(partners, Objects(o, Vocabulary.SubPropertyOf));
                foreach (RdfTerm r in partners)
                {
                    Add(s, Vocabulary.SubPropertyOf, r);
                }

                Fill(partners, Subjects(Vocabulary.SubPropertyOf, s));
                foreach (RdfTerm q in partners)
                {
                    Add(q, Vocabulary.SubPropertyOf, o);
                }
            }
            else if (p.Equals(Vocabulary.SubClassOf))
            {
                // rdfs9, t the schema triple; rdfs11 both ways.
                Fill(partners, Subjects(Vocabulary.Type, s));
                foreach (RdfTerm x in partners)
                {
                    Add(x, Vocabulary.Type, o);
                }

                Fill(partners, Objects(o, Vocabulary.SubClassOf));
                foreach (RdfTerm e in partners)
                {
                    Add(s, Vocabulary.SubClassOf, e);
                }

                Fill(partners, Subjects(Vocabulary.SubClassOf, s));
                foreach (RdfTerm c in partners)
                {
                    Add(c, Vocabulary.SubClassOf, o);
                }
            }
        }
    }

    /// <summary>Adds the regime's axiomatic triples, and rdfs1 for each recognised datatype.</summary>
    internal void AddAxioms()
    {
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Vocabulary.RdfAxioms())
        {
            Add(s, p, o);
        }

        if (!_rules.Rdfs)
        {
            return;
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Vocabulary.RdfsAxioms())
        {
            Add(s, p, o);
        }

        foreach (RdfTerm datatype in _rules.Recognised)
        {
            Add(datatype, Vocabulary.Type, Vocabulary.Datatype); // rdfs1
        }
    }

    /// <summary>Adds the axioms of <c>rdf:_n</c> for each one <paramref name="terms"/> holds.</summary>
    internal void AddContainerMembershipAxioms(IEnumerable<RdfTerm> terms)
    {
        foreach (RdfTerm term in terms)
        {
            if (Vocabulary.IsContainerMembership(term))
            {
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Vocabulary.ContainerMembershipAxioms(term, _rules.Rdfs))
                {
                    Add(s, p, o);
                }
            }
        }
    }

    /// <summary>The datatype of a literal: its own, or <c>rdf:langString</c> for a language-tagged one.</summary>
    internal static RdfTerm? DatatypeOf(RdfTerm literal)
    {
        if (literal.Kind != RdfTermKind.Literal)
        {
            return null;
        }

        return RdfTerm.Iri(literal.DatatypeIri);
    }

    private void Index(Triple t)
    {
        if (!_byPredicate.TryGetValue(t.P, out List<Triple>? list))
        {
            _byPredicate[t.P] = list = [];
        }

        list.Add(t);
        if (!_objects.TryGetValue((t.S, t.P), out List<RdfTerm>? objects))
        {
            _objects[(t.S, t.P)] = objects = [];
        }

        objects.Add(t.O);
        if (!_subjects.TryGetValue((t.P, t.O), out List<RdfTerm>? subjects))
        {
            _subjects[(t.P, t.O)] = subjects = [];
        }

        subjects.Add(t.S);
    }

    private static void Fill<T>(List<T> into, IEnumerable<T> from)
    {
        into.Clear();
        into.AddRange(from);
    }
}
