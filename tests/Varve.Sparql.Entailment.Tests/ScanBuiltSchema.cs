// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Entailment.Closure;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>
/// The schema as the first build of #170 computed it, kept as the oracle of
/// the probe-built one (ADR 0193 point 2): under RDFS, the closure of every
/// triple of the scoping graph, with the axioms of every <c>rdf:_n</c> it
/// names. That build reached it through shape-driven scans of every triple,
/// which the container-membership families requested on every schema; a
/// check over 2,000 generated graphs confirmed the two are the same set
/// before the build was replaced.
/// </summary>
internal static class ScanBuiltSchema
{
    internal static TripleClosure Saturate(IQuadSource source, IReadOnlyList<GraphPattern> graphs, RuleSet rules)
    {
        TripleClosure closure = new(rules);
        closure.AddAxioms();
        List<RdfTerm> terms = [];
        foreach (GraphPattern graph in graphs)
        {
            using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, graph);
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                if (source.TryExternalise(quad.Subject, out RdfTerm? s) && source.TryExternalise(quad.Predicate, out RdfTerm? p) && source.TryExternalise(quad.Object, out RdfTerm? o))
                {
                    closure.Add(new Triple(s, p, o));
                    terms.AddRange([s, p, o]);
                }
            }
        }

        closure.AddContainerMembershipAxioms(terms);
        closure.Close();
        return closure;
    }
}
