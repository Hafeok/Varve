// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Derivation;
using Varve.Derivation.Model;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// A profile's rules and axioms over one source's handles, compiled once:
/// every constant a rule names is a handle by then (ADR 0197).
/// </summary>
internal sealed class Program
{
    private Program(Rule[] rules, Fact[] axioms)
    {
        Rules = rules;
        Axioms = axioms;
        Dependencies = new DependencyGraph<Rule>(1);
        foreach (Rule rule in rules)
        {
            // One vertex, the facts, with an edge per rule: every rule reads
            // facts and concludes facts, so the program is one component,
            // closed semi-naively (ADR 0196 point 1).
            Dependencies.Add(0, 0, Polarity.Positive, rule);
        }

        Strata = Dependencies.Stratify();
    }

    internal Rule[] Rules { get; }

    /// <summary>The facts every closure of the profile holds, whatever the graph.</summary>
    internal Fact[] Axioms { get; }

    internal DependencyGraph<Rule> Dependencies { get; }

    internal Stratification<Rule> Strata { get; }

    internal static Program Of(InferenceProfile profile, IInferenceTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        RuleBook book = new(terms);
        book.Rdfs();
        if (profile == InferenceProfile.Owl2Rl)
        {
            book.Owl2Rl();
        }

        return new Program([.. book.Rules], [.. book.Axioms]);
    }

    /// <summary>The rules of a profile, written as their specifications give them.</summary>
    private sealed class RuleBook(IInferenceTerms terms)
    {
        private static readonly Slot X = Slot.Var(0);
        private static readonly Slot Y = Slot.Var(1);
        private static readonly Slot Z = Slot.Var(2);
        private static readonly Slot A = Slot.Var(3);
        private static readonly Slot B = Slot.Var(4);
        private static readonly Slot U = Slot.Var(5);
        private static readonly Slot V = Slot.Var(6);
        private static readonly Slot W = Slot.Var(7);

        internal List<Rule> Rules { get; } = [];

        internal List<Fact> Axioms { get; } = [];

        private Slot C(RdfTerm term) => Slot.Const(terms.Handle(term));

        private void Rule(string name, Atom[] body, params Atom[] head) => Rules.Add(new PatternRule(name, body, head));

        private void Axiom(RdfTerm s, RdfTerm p, RdfTerm o) => Axioms.Add(new Fact(terms.Handle(s), terms.Handle(p), terms.Handle(o)));

        /// <summary>RDF 1.1 Semantics §9.2.1, with §8's RDF rules, bounded as ADR 0197 point 2 has it.</summary>
        internal void Rdfs()
        {
            Slot type = C(Vocabulary.Type), subClass = C(Vocabulary.SubClassOf), subProperty = C(Vocabulary.SubPropertyOf);
            Slot resource = C(Vocabulary.Resource), cls = C(Vocabulary.Class), property = C(Vocabulary.Property);

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Vocabulary.RdfAxioms())
            {
                Axiom(s, p, o);
            }

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Vocabulary.RdfsAxioms())
            {
                Axiom(s, p, o);
            }

            HashSet<ulong> recognised = [];
            foreach (RdfTerm datatype in Vocabulary.RdfsDatatypes)
            {
                Axiom(datatype, Vocabulary.Type, Vocabulary.Datatype); // rdfs1
                recognised.Add(terms.Handle(datatype).Value);
            }

            Rules.Add(new LiteralTyping(terms, terms.Handle(Vocabulary.Type), recognised));
            Rules.Add(new ContainerMembershipAxioms(terms,
            [
                (terms.Handle(Vocabulary.Type), terms.Handle(Vocabulary.Property)),
                (terms.Handle(Vocabulary.Type), terms.Handle(Vocabulary.ContainerMembershipProperty)),
                (terms.Handle(Vocabulary.Domain), terms.Handle(Vocabulary.Resource)),
                (terms.Handle(Vocabulary.Range), terms.Handle(Vocabulary.Resource)),
            ]));

            Rule("rdfD2", [new(X, A, Y)], new Atom(A, type, property));
            Rule("rdfs2", [new(A, C(Vocabulary.Domain), X), new(Y, A, Z)], new Atom(Y, type, X));
            Rule("rdfs3", [new(A, C(Vocabulary.Range), X), new(Y, A, Z)], new Atom(Z, type, X));
            Rule("rdfs4a", [new(X, A, Y)], new Atom(X, type, resource));
            Rule("rdfs4b", [new(X, A, Y)], new Atom(Y, type, resource));
            Rule("rdfs5", [new(X, subProperty, Y), new(Y, subProperty, Z)], new Atom(X, subProperty, Z));
            Rule("rdfs6", [new(X, type, property)], new Atom(X, subProperty, X));
            Rule("rdfs7", [new(A, subProperty, B), new(X, A, Y)], new Atom(X, B, Y));
            Rule("rdfs8", [new(X, type, cls)], new Atom(X, subClass, resource));
            Rule("rdfs9", [new(X, subClass, Y), new(Z, type, X)], new Atom(Z, type, Y));
            Rule("rdfs10", [new(X, type, cls)], new Atom(X, subClass, X));
            Rule("rdfs11", [new(X, subClass, Y), new(Y, subClass, Z)], new Atom(X, subClass, Z));
            Rule("rdfs12", [new(X, type, C(Vocabulary.ContainerMembershipProperty))], new Atom(X, subProperty, C(Vocabulary.Member)));
            Rule("rdfs13", [new(X, type, C(Vocabulary.Datatype))], new Atom(X, subClass, C(Vocabulary.Literal)));
        }

        /// <summary>
        /// OWL 2 Profiles §4.3, tables 5 to 9, as rules that conclude facts:
        /// the equality rules of table 4 are off (ADR 0197 point 4), the
        /// rules with <c>false</c> heads are checks, and the rules the RDFS
        /// profile already has under another name (prp-dom, prp-rng,
        /// prp-spo1, cax-sco, scm-sco, scm-spo) are not added twice.
        /// </summary>
        internal void Owl2Rl()
        {
            Slot type = C(Vocabulary.Type), subClass = C(Vocabulary.SubClassOf), subProperty = C(Vocabulary.SubPropertyOf);
            Slot sameAs = C(Vocabulary.SameAs), thing = C(Vocabulary.Thing), nothing = C(Vocabulary.Nothing);
            Slot equivalentClass = C(Vocabulary.EquivalentClass), equivalentProperty = C(Vocabulary.EquivalentProperty);
            Slot onProperty = C(Vocabulary.OnProperty), someValuesFrom = C(Vocabulary.SomeValuesFrom);
            Slot allValuesFrom = C(Vocabulary.AllValuesFrom), hasValue = C(Vocabulary.HasValue), onClass = C(Vocabulary.OnClass);
            Slot domain = C(Vocabulary.Domain), range = C(Vocabulary.Range);

            // Table 5 and 6's axioms: prp-ap, cls-thing, cls-nothing1, and dt-type1 for the RDFS profile's datatypes.
            foreach (RdfTerm annotation in Vocabulary.AnnotationProperties)
            {
                Axiom(annotation, Vocabulary.Type, Vocabulary.AnnotationProperty);
            }

            Axiom(Vocabulary.Thing, Vocabulary.Type, Vocabulary.OwlClass);
            Axiom(Vocabulary.Nothing, Vocabulary.Type, Vocabulary.OwlClass);

            // Table 5: properties.
            Rule("prp-fp", [new(A, type, C(Vocabulary.FunctionalProperty)), new(X, A, Y), new(X, A, Z)], new Atom(Y, sameAs, Z));
            Rule("prp-ifp", [new(A, type, C(Vocabulary.InverseFunctionalProperty)), new(X, A, Z), new(Y, A, Z)], new Atom(X, sameAs, Y));
            Rule("prp-symp", [new(A, type, C(Vocabulary.SymmetricProperty)), new(X, A, Y)], new Atom(Y, A, X));
            Rule("prp-trp", [new(A, type, C(Vocabulary.TransitiveProperty)), new(X, A, Y), new(Y, A, Z)], new Atom(X, A, Z));
            Rule("prp-eqp1", [new(A, equivalentProperty, B), new(X, A, Y)], new Atom(X, B, Y));
            Rule("prp-eqp2", [new(A, equivalentProperty, B), new(X, B, Y)], new Atom(X, A, Y));
            Rule("prp-inv1", [new(A, C(Vocabulary.InverseOf), B), new(X, A, Y)], new Atom(Y, B, X));
            Rule("prp-inv2", [new(A, C(Vocabulary.InverseOf), B), new(X, B, Y)], new Atom(Y, A, X));

            // Table 6: classes.
            Rule("cls-svf1", [new(X, someValuesFrom, Y), new(X, onProperty, A), new(U, A, V), new(V, type, Y)], new Atom(U, type, X));
            Rule("cls-svf2", [new(X, someValuesFrom, thing), new(X, onProperty, A), new(U, A, V)], new Atom(U, type, X));
            Rule("cls-avf", [new(X, allValuesFrom, Y), new(X, onProperty, A), new(U, type, X), new(U, A, V)], new Atom(V, type, Y));
            Rule("cls-hv1", [new(X, hasValue, Y), new(X, onProperty, A), new(U, type, X)], new Atom(U, A, Y));
            Rule("cls-hv2", [new(X, hasValue, Y), new(X, onProperty, A), new(U, A, Y)], new Atom(U, type, X));
            Slot one = C(Vocabulary.Cardinality("1"));
            Rule("cls-maxc2", [new(X, C(Vocabulary.MaxCardinality), one), new(X, onProperty, A), new(U, type, X), new(U, A, Y), new(U, A, Z)], new Atom(Y, sameAs, Z));
            Rule("cls-maxqc3", [new(X, C(Vocabulary.MaxQualifiedCardinality), one), new(X, onProperty, A), new(X, onClass, B), new(U, type, X), new(U, A, Y), new(Y, type, B), new(U, A, Z), new(Z, type, B)], new Atom(Y, sameAs, Z));
            Rule("cls-maxqc4", [new(X, C(Vocabulary.MaxQualifiedCardinality), one), new(X, onProperty, A), new(X, onClass, thing), new(U, type, X), new(U, A, Y), new(U, A, Z)], new Atom(Y, sameAs, Z));

            // Table 7: class axioms.
            Rule("cax-eqc1", [new(X, equivalentClass, Y), new(Z, type, X)], new Atom(Z, type, Y));
            Rule("cax-eqc2", [new(X, equivalentClass, Y), new(Z, type, Y)], new Atom(Z, type, X));

            // Table 9: the schema.
            Rule("scm-cls", [new(X, type, C(Vocabulary.OwlClass))], new Atom(X, subClass, X), new Atom(X, equivalentClass, X), new Atom(X, subClass, thing), new Atom(nothing, subClass, X));
            Rule("scm-eqc1", [new(X, equivalentClass, Y)], new Atom(X, subClass, Y), new Atom(Y, subClass, X));
            Rule("scm-eqc2", [new(X, subClass, Y), new(Y, subClass, X)], new Atom(X, equivalentClass, Y));
            Rule("scm-op", [new(A, type, C(Vocabulary.ObjectProperty))], new Atom(A, subProperty, A), new Atom(A, equivalentProperty, A));
            Rule("scm-dp", [new(A, type, C(Vocabulary.DatatypeProperty))], new Atom(A, subProperty, A), new Atom(A, equivalentProperty, A));
            Rule("scm-eqp1", [new(A, equivalentProperty, B)], new Atom(A, subProperty, B), new Atom(B, subProperty, A));
            Rule("scm-eqp2", [new(A, subProperty, B), new(B, subProperty, A)], new Atom(A, equivalentProperty, B));
            Rule("scm-dom1", [new(A, domain, X), new(X, subClass, Y)], new Atom(A, domain, Y));
            Rule("scm-dom2", [new(B, domain, X), new(A, subProperty, B)], new Atom(A, domain, X));
            Rule("scm-rng1", [new(A, range, X), new(X, subClass, Y)], new Atom(A, range, Y));
            Rule("scm-rng2", [new(B, range, X), new(A, subProperty, B)], new Atom(A, range, X));
            Rule("scm-hv", [new(X, hasValue, W), new(X, onProperty, A), new(Y, hasValue, W), new(Y, onProperty, B), new(A, subProperty, B)], new Atom(X, subClass, Y));
            Rule("scm-svf1", [new(X, someValuesFrom, U), new(X, onProperty, A), new(Y, someValuesFrom, V), new(Y, onProperty, A), new(U, subClass, V)], new Atom(X, subClass, Y));
            Rule("scm-svf2", [new(X, someValuesFrom, U), new(X, onProperty, A), new(Y, someValuesFrom, U), new(Y, onProperty, B), new(A, subProperty, B)], new Atom(X, subClass, Y));
            Rule("scm-avf1", [new(X, allValuesFrom, U), new(X, onProperty, A), new(Y, allValuesFrom, V), new(Y, onProperty, A), new(U, subClass, V)], new Atom(X, subClass, Y));
            Rule("scm-avf2", [new(X, allValuesFrom, U), new(X, onProperty, A), new(Y, allValuesFrom, U), new(Y, onProperty, B), new(A, subProperty, B)], new Atom(Y, subClass, X));

            // The rules over lists.
            ListTerms list = new(terms.Handle(Vocabulary.First), terms.Handle(Vocabulary.Rest), terms.Handle(Vocabulary.Nil), terms.Handle(Vocabulary.Type));
            TermHandle intersectionOf = terms.Handle(Vocabulary.IntersectionOf), unionOf = terms.Handle(Vocabulary.UnionOf);
            Rules.Add(new IntersectionMembership(intersectionOf, list));
            Rules.Add(new IntersectionParts(intersectionOf, list));
            Rules.Add(new UnionMembership(unionOf, list));
            Rules.Add(new Enumeration(terms.Handle(Vocabulary.OneOf), list));
            Rules.Add(new ListSubClasses("scm-int", intersectionOf, list, terms.Handle(Vocabulary.SubClassOf), union: false));
            Rules.Add(new ListSubClasses("scm-uni", unionOf, list, terms.Handle(Vocabulary.SubClassOf), union: true));
            Rules.Add(new PropertyChain(terms.Handle(Vocabulary.PropertyChainAxiom), list));
            Rules.Add(new Key(terms.Handle(Vocabulary.HasKey), list, terms.Handle(Vocabulary.SameAs)));
        }
    }
}
