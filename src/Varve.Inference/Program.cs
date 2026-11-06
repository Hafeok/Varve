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
            throw new NotSupportedException("the OWL 2 RL/RDF rules are not built yet");
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
    }
}
