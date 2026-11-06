// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Varve.Rdf;
using Varve.Sparql.Entailment.Closure;
using Varve.Sparql.Entailment.Model;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>One position of a generated triple pattern: a variable, a blank node of the query, or a term.</summary>
internal sealed record Slot(string? Variable, string? Blank, RdfTerm? Term)
{
    internal string Text => Variable is not null ? "?" + Variable : Blank is not null ? "_:" + Blank : Literalise(Term!);

    private static string Literalise(RdfTerm term) => Support.Text(term);
}

/// <summary>
/// ADR 0193 point 9's oracle: the forward-chained closure of a scoping graph
/// (RDF 1.1 Semantics, Appendix A, over generalised triples), and a basic
/// graph pattern evaluated over it under the regime's answer conditions —
/// no literal in a variable's subject position, a variable bound only to a
/// term of the graph or of the regime's vocabulary, once per distinct
/// blank-node mapping.
/// </summary>
internal static class Oracle
{
    /// <summary>The solutions of <paramref name="bgp"/> over the scoping graph <paramref name="graph"/>, as sorted lines.</summary>
    internal static List<string> Solutions(EntailmentRegime regime, IReadOnlyList<(RdfTerm S, RdfTerm P, RdfTerm O)> graph, IReadOnlyList<Slot[]> bgp, string? graphVariable = null, RdfTerm? graphName = null)
    {
        RuleSet rules = EntailmentRewriter.RulesOf(regime);

        // Under D, a literal is its value: every literal, the graph's and the
        // query's, in its canonical form (Entailment Regimes §5.1).
        if (regime == EntailmentRegime.D)
        {
            graph = [.. graph.Select(t => (LiteralValues.Canonical(t.S), t.P, LiteralValues.Canonical(t.O)))];
            bgp = [.. bgp.Select(p => p.Select(s => s.Term is { } term ? s with { Term = LiteralValues.Canonical(term) } : s).ToArray())];
        }

        TripleClosure closure = new(rules);
        closure.AddAxioms();
        HashSet<RdfTerm> vocabulary = new(RdfTerm.Comparer);
        foreach (Triple axiom in closure.All)
        {
            Note(vocabulary, axiom, includeDatatypes: false);
        }

        // The regime's vocabulary less rdf:_n: the axioms' terms, and under D
        // the datatype map (dV-Minus); xsd:string is in neither under RDF or RDFS.
        if (regime == EntailmentRegime.D)
        {
            foreach (RdfTerm datatype in rules.Recognised)
            {
                vocabulary.Add(datatype);
            }
        }
        else
        {
            vocabulary.Remove(IriOf("http://www.w3.org/2001/XMLSchema#string"));
        }

        vocabulary.Add(IriOf("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"));
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in graph)
        {
            closure.Add(s, p, o);
            vocabulary.Add(s);
            vocabulary.Add(p);
            vocabulary.Add(o);
        }

        List<RdfTerm> named = [];
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in graph)
        {
            named.Add(s);
            named.Add(p);
            named.Add(o);
        }

        foreach (Slot[] pattern in bgp)
        {
            foreach (Slot slot in pattern)
            {
                if (slot.Term is { } term)
                {
                    named.Add(term);
                }
            }
        }

        closure.AddContainerMembershipAxioms(named);
        closure.Close();
        List<Triple> triples = [.. closure.All];

        HashSet<string> distinct = [];
        List<string> rows = [];
        Match(triples, bgp, 0, [], vocabulary, distinct, rows, graphVariable, graphName);
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static void Match(List<Triple> triples, IReadOnlyList<Slot[]> bgp, int index, Dictionary<string, RdfTerm> binding, HashSet<RdfTerm> vocabulary, HashSet<string> distinct, List<string> rows, string? graphVariable, RdfTerm? graphName)
    {
        if (index == bgp.Count)
        {
            string full = string.Join(" ", binding.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => b.Key + "=" + Support.Text(b.Value)));
            if (!distinct.Add(full))
            {
                return;
            }

            List<string> cells = [];
            foreach ((string name, RdfTerm term) in binding)
            {
                if (!name.StartsWith("_:", StringComparison.Ordinal))
                {
                    cells.Add("?" + name + "=" + Support.Text(term));
                }
            }

            if (graphVariable is not null)
            {
                cells.Add("?" + graphVariable + "=" + Support.Text(graphName!));
            }

            cells.Sort(StringComparer.Ordinal);
            rows.Add(string.Join(" ", cells));
            return;
        }

        Slot[] pattern = bgp[index];
        foreach (Triple t in triples)
        {
            RdfTerm[] terms = [t.S, t.P, t.O];
            Dictionary<string, RdfTerm> next = new(binding);
            bool ok = true;
            for (int i = 0; i < 3 && ok; i++)
            {
                Slot slot = pattern[i];
                RdfTerm term = terms[i];
                if (slot.Term is { } constant)
                {
                    ok = constant.Equals(term);
                    continue;
                }

                string key = slot.Variable ?? "_:" + slot.Blank;
                bool variable = slot.Variable is not null;
                // C2 for a variable, and ADR 0193's reading of σ for a blank node of the
                // query: the scoping graph's terms and the regime's vocabulary only.
                if (!vocabulary.Contains(term) || (variable && i == 0 && term.Kind == RdfTermKind.Literal))
                {
                    ok = false;
                    continue;
                }

                if (next.TryGetValue(key, out RdfTerm? earlier))
                {
                    ok = earlier.Equals(term);
                }
                else
                {
                    next[key] = term;
                }
            }

            if (ok)
            {
                Match(triples, bgp, index + 1, next, vocabulary, distinct, rows, graphVariable, graphName);
            }
        }
    }

    private static void Note(HashSet<RdfTerm> vocabulary, Triple triple, bool includeDatatypes)
    {
        _ = includeDatatypes;
        vocabulary.Add(triple.S);
        vocabulary.Add(triple.P);
        vocabulary.Add(triple.O);
    }

    private static RdfTerm IriOf(string iri) => Support.IriOf(iri);
}
