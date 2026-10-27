// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Entailment.Closure;

namespace Varve.Sparql.Entailment;

/// <summary>
/// The saturated schema Σ of one scoping graph (ADR 0193 point 2): its
/// triples under the schema predicates, found to a fixpoint; the typing of its
/// container membership properties and datatypes, which rdfs12 and rdfs13 turn
/// into schema triples; the regime's axiomatic triples; all closed under the
/// regime's rules. Every other entailed triple is the image of one asserted
/// triple, which <see cref="Shapes"/> compiles from it.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.SchemaSaturatedPerScopingGraph), Role = "the schema of one scoping graph, saturated")]
internal sealed class Schema
{
    private Schema(RuleSet rules, TripleClosure closure)
    {
        Rules = rules;
        Closure = closure;
        SchemaPredicates = new HashSet<RdfTerm>(RdfTerm.Comparer);
        TypeLike = new HashSet<RdfTerm>(RdfTerm.Comparer) { Vocabulary.Type };
        KnownPredicates = new HashSet<RdfTerm>(RdfTerm.Comparer);
        KnownClasses = new HashSet<RdfTerm>(RdfTerm.Comparer);
        Classify();
    }

    internal RuleSet Rules { get; }

    /// <summary>The branches Σ compiles to (ADR 0193 point 3).</summary>
    internal List<Branch> Branches { get; private set; } = [];

    /// <summary>
    /// Every scan the saturation read, to its fixpoint. A graph none of them
    /// matches adds nothing to any, so its own saturation with the same TBox
    /// reaches this Σ.
    /// </summary>
    internal List<Scan> Scans { get; private init; } = [];

    /// <summary>Whether the scoping graph was read whole, its schema redefining the vocabulary.</summary>
    internal bool ReadWhole { get; private init; }

    /// <summary>Σ, closed.</summary>
    internal TripleClosure Closure { get; }

    /// <summary>The schema predicates: the four of RDFS and their sub-properties.</summary>
    internal HashSet<RdfTerm> SchemaPredicates { get; }

    /// <summary><c>rdf:type</c> and its sub-properties.</summary>
    internal HashSet<RdfTerm> TypeLike { get; }

    /// <summary>The other predicates Σ says something about: a domain, a range, a super-property, a type.</summary>
    internal HashSet<RdfTerm> KnownPredicates { get; }

    /// <summary>The classes Σ says something about.</summary>
    internal HashSet<RdfTerm> KnownClasses { get; }

    /// <summary>
    /// Saturates the schema of the scoping graph made of <paramref name="graphs"/>
    /// in <paramref name="source"/> (a merge, so the TBox is in each).
    /// </summary>
    /// <remarks>
    /// <paramref name="named"/> are the <c>rdf:_n</c> the query names: their
    /// axioms hold whether or not the graph names them, and act on every
    /// pattern of the query (Entailment Regimes §3.4).
    /// </remarks>
    internal static Schema Saturate(IQuadSource source, IReadOnlyList<GraphPattern> graphs, RuleSet rules, IReadOnlyCollection<RdfTerm> named, CancellationToken cancellationToken)
    {
        List<Triple> found = [];
        HashSet<Triple> seen = [];
        HashSet<RdfTerm> scannedPredicates = new(RdfTerm.Comparer);
        HashSet<(RdfTerm, RdfTerm)> scannedTypes = [];
        List<Scan> scans = [];

        // A triple about the vocabulary itself is meta-modelling: read once,
        // by subject and by object, so that Σ holds what it says.
        if (rules.Rdfs)
        {
            foreach (RdfTerm term in Vocabulary.Terms)
            {
                Read(null, null, term);
                Read(null, term);
            }
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TripleClosure closure = new(rules);
            closure.AddAxioms();
            foreach (Triple triple in found)
            {
                closure.Add(triple);
            }

            closure.AddContainerMembershipAxioms(Terms(found));
            closure.AddContainerMembershipAxioms(named);
            closure.Close();
            Schema schema = new(rules, closure);

            // A schema that says something of the vocabulary itself — rdf:type
            // a sub-property of a schema predicate, rdfs:member with a domain —
            // makes any triple a possible schema triple, which no bounded
            // probe finds: its scoping graph is read whole (ADR 0193 point 2).
            if (rules.Rdfs && RedefinesVocabulary(schema, found))
            {
                return Whole(source, graphs, rules, named, cancellationToken);
            }

            bool more = false;
            foreach (RdfTerm predicate in schema.SchemaPredicates)
            {
                if (scannedPredicates.Add(predicate))
                {
                    more |= Read(predicate, null);
                }
            }

            if (rules.Rdfs)
            {
                // rdfs12 and rdfs13: what is typed a container membership
                // property or a datatype gains a schema triple.
                foreach (RdfTerm meta in schema.MetaClasses())
                {
                    foreach (RdfTerm type in schema.TypeLike)
                    {
                        if (scannedTypes.Add((type, meta)))
                        {
                            more |= Read(type, meta);
                        }
                    }

                    // A triple under a property whose domain or range is a meta
                    // class, or under any of its sub-properties, types its
                    // subject or object into it.
                    foreach (RdfTerm property in (IEnumerable<RdfTerm>)[.. closure.Subjects(Vocabulary.Domain, meta), .. closure.Subjects(Vocabulary.Range, meta)])
                    {
                        foreach (RdfTerm sub in (IEnumerable<RdfTerm>)[property, .. closure.Subjects(Vocabulary.SubPropertyOf, property)])
                        {
                            if (scannedPredicates.Add(sub))
                            {
                                more |= Read(sub, null);
                            }
                        }

                    }
                }
            }

            if (!more)
            {
                Schema done = new(rules, closure) { Scans = scans };
                done.Branches = Shapes.Compile(done);
                return done;
            }
        }

        bool Read(RdfTerm? predicate, RdfTerm? @object, RdfTerm? subject = null)
        {
            scans.Add(new Scan(subject, predicate, @object));
            return Scan(source, graphs, subject, predicate, @object, found, seen);
        }
    }

    /// <summary>The classes whose instances gain a schema triple: those under <c>rdfs:ContainerMembershipProperty</c> and <c>rdfs:Datatype</c>.</summary>
    internal IEnumerable<RdfTerm> MetaClasses()
    {
        HashSet<RdfTerm> meta = new(RdfTerm.Comparer) { Vocabulary.ContainerMembershipProperty, Vocabulary.Datatype };
        foreach (RdfTerm c in Closure.Subjects(Vocabulary.SubClassOf, Vocabulary.ContainerMembershipProperty))
        {
            meta.Add(c);
        }

        foreach (RdfTerm c in Closure.Subjects(Vocabulary.SubClassOf, Vocabulary.Datatype))
        {
            meta.Add(c);
        }

        return meta;
    }

    private void Classify()
    {
        foreach (RdfTerm predicate in Vocabulary.SchemaPredicates)
        {
            SchemaPredicates.Add(predicate);
            foreach (RdfTerm sub in Closure.Subjects(Vocabulary.SubPropertyOf, predicate))
            {
                SchemaPredicates.Add(sub);
            }
        }

        foreach (RdfTerm sub in Closure.Subjects(Vocabulary.SubPropertyOf, Vocabulary.Type))
        {
            TypeLike.Add(sub);
        }

        foreach (Triple t in Closure.All)
        {
            if (t.P.Equals(Vocabulary.Domain) || t.P.Equals(Vocabulary.Range) || t.P.Equals(Vocabulary.SubPropertyOf))
            {
                AddPredicate(t.S);
                if (t.P.Equals(Vocabulary.SubPropertyOf))
                {
                    AddPredicate(t.O);
                }
                else
                {
                    AddClass(t.O);
                }
            }
            else if (t.P.Equals(Vocabulary.SubClassOf))
            {
                AddClass(t.S);
                AddClass(t.O);
            }
            else if (t.P.Equals(Vocabulary.Type))
            {
                AddClass(t.O);
            }
        }

        // Every term Σ names is known in each role its families give it: a
        // triple whose predicate or class is one of them meets what Σ says
        // of that term, which no generic representative can (ADR 0193 point 3).
        foreach (Triple t in Closure.All)
        {
            foreach (RdfTerm term in (RdfTerm[])[t.S, t.P, t.O])
            {
                AddPredicate(term);
                AddClass(term);
            }
        }
    }

    private void AddPredicate(RdfTerm term)
    {
        if (term.Kind == RdfTermKind.Iri && !SchemaPredicates.Contains(term) && !TypeLike.Contains(term))
        {
            KnownPredicates.Add(term);
        }
    }

    // A literal is a class Σ may know too: one a range makes a datatype.
    private void AddClass(RdfTerm term) => KnownClasses.Add(term);

    // One predicate-bound match per graph of the scoping graph; the terms are
    // externalised, since the schema is small and closed as terms.
    private static bool Scan(IQuadSource source, IReadOnlyList<GraphPattern> graphs, RdfTerm? subject, RdfTerm? predicate, RdfTerm? @object, List<Triple> into, HashSet<Triple> seen)
    {
        TermHandle p = TermHandle.None;
        if (predicate is not null && !source.TryInternalise(predicate, out p))
        {
            return false;
        }

        TermHandle o = TermHandle.None;
        if (@object is not null && !source.TryInternalise(@object, out o))
        {
            return false;
        }

        TermHandle s = TermHandle.None;
        if (subject is not null && !source.TryInternalise(subject, out s))
        {
            return false;
        }

        bool added = false;
        foreach (GraphPattern graph in graphs)
        {
            using IQuadCursor cursor = source.Match(s, p, o, graph);
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                if (source.TryExternalise(quad.Subject, out RdfTerm? qs)
                    && source.TryExternalise(quad.Predicate, out RdfTerm? qp)
                    && source.TryExternalise(quad.Object, out RdfTerm? qo))
                {
                    Triple triple = new(qs, qp, qo);
                    if (seen.Add(triple))
                    {
                        into.Add(triple);
                        added = true;
                    }
                }
            }
        }

        return added;
    }

    // A triple of Σ whose subject is the vocabulary's, but for an rdf:_n,
    // under a schema or type-like predicate.
    private static bool RedefinesVocabulary(Schema schema, List<Triple> found)
    {
        foreach (Triple t in found)
        {
            // An rdf:_n is one term like any the user names, which the probes follow.
            if (Vocabulary.IsVocabulary(t.S) && !Vocabulary.IsContainerMembership(t.S) && (schema.SchemaPredicates.Contains(t.P) || schema.TypeLike.Contains(t.P)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Σ of a scoping graph read whole: every triple, and the axioms of every <c>rdf:_n</c> it names.</summary>
    private static Schema Whole(IQuadSource source, IReadOnlyList<GraphPattern> graphs, RuleSet rules, IReadOnlyCollection<RdfTerm> named, CancellationToken cancellationToken)
    {
        List<Triple> all = [];
        Scan(source, graphs, null, null, null, all, []);
        cancellationToken.ThrowIfCancellationRequested();
        TripleClosure closure = new(rules);
        closure.AddAxioms();
        foreach (Triple triple in all)
        {
            closure.Add(triple);
        }

        closure.AddContainerMembershipAxioms(Terms(all));
        closure.AddContainerMembershipAxioms(named);
        closure.Close();
        Schema schema = new(rules, closure) { Scans = [new Scan(null, null, null)], ReadWhole = true };
        schema.Branches = Shapes.Compile(schema);
        return schema;
    }

    private static IEnumerable<RdfTerm> Terms(List<Triple> triples)
    {
        foreach (Triple t in triples)
        {
            yield return t.S;
            yield return t.P;
            yield return t.O;
        }
    }
}
