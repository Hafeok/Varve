// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Compile;

/// <summary>
/// A <c>CONSTRUCT</c> template (§16.2, <c>sparql-evaluation.md</c> §9.1):
/// instantiated per solution, a blank node minted afresh for each solution, a
/// triple with an unbound position, a literal subject or a non-IRI predicate
/// left out, and each triple returned once.
/// </summary>
internal sealed class Template
{
    private readonly Item[][] _triples;

    private Template(Item[][] triples) => _triples = triples;

    internal static Template Compile(AlgebraList<TriplePattern> template, Compiler compiler)
    {
        Item[][] triples = new Item[template.Count][];
        for (int i = 0; i < triples.Length; i++)
        {
            TriplePattern triple = template[i];
            triples[i] = [Item.Of(triple.Subject, compiler), Item.Of(triple.Predicate, compiler), Item.Of(triple.Object, compiler)];
        }

        return new Template(triples);
    }

    internal IEnumerator<(RdfTerm, RdfTerm, RdfTerm)> Instantiate(Exec exec, IEnumerator<ulong[]> solutions)
    {
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> seen = [];
        using (solutions)
        {
            while (solutions.MoveNext())
            {
                exec.Check();
                Dictionary<string, RdfTerm> blankNodes = [];
                foreach (Item[] triple in _triples)
                {
                    RdfTerm? s = triple[0].Instantiate(exec, solutions.Current, blankNodes);
                    RdfTerm? p = triple[1].Instantiate(exec, solutions.Current, blankNodes);
                    RdfTerm? o = triple[2].Instantiate(exec, solutions.Current, blankNodes);
                    if (s is null || p is null || o is null
                        || s.Kind is RdfTermKind.Literal or RdfTermKind.TripleTerm
                        || p.Kind != RdfTermKind.Iri)
                    {
                        continue;
                    }

                    if (seen.Add((s, p, o)))
                    {
                        yield return (s, p, o);
                    }
                }
            }
        }
    }

    private sealed class Item
    {
        private int _slot = -1;
        private string? _blank;
        private RdfTerm? _constant;
        private Item[]? _nested;

        internal static Item Of(PatternTerm term, Compiler compiler) => term switch
        {
            VariablePattern variable => new Item { _slot = compiler.Slot(variable.Variable.Name) },
            BlankNodePattern blank => new Item { _blank = blank.Label },
            TermPattern constant => new Item { _constant = constant.Term },
            TripleTermPattern triple => new Item { _nested = [Of(triple.Subject, compiler), Of(triple.Predicate, compiler), Of(triple.Object, compiler)] },
            _ => new Item(),
        };

        internal RdfTerm? Instantiate(Exec exec, ulong[] row, Dictionary<string, RdfTerm> blankNodes)
        {
            if (_constant is not null)
            {
                return _constant;
            }

            if (_blank is not null)
            {
                if (!blankNodes.TryGetValue(_blank, out RdfTerm? node))
                {
                    node = exec.MintBlankNode();
                    blankNodes.Add(_blank, node);
                }

                return node;
            }

            if (_nested is not null)
            {
                RdfTerm? s = _nested[0].Instantiate(exec, row, blankNodes);
                RdfTerm? p = _nested[1].Instantiate(exec, row, blankNodes);
                RdfTerm? o = _nested[2].Instantiate(exec, row, blankNodes);
                return s is null || p is null || o is null || p.Kind != RdfTermKind.Iri || s.Kind is RdfTermKind.Literal
                    ? null
                    : RdfTerm.TripleTerm(s, p, o);
            }

            if (_slot < 0)
            {
                return null;
            }

            TermRef value = Rows.Get(row, exec.Width, _slot);
            return value.IsBound ? exec.Materialise(value) : null;
        }
    }
}
