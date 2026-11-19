// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The graphs a query names by IRI in <c>GRAPH</c> patterns, its own and its
/// subqueries', and whether one names a variable (ADR 0197, amended
/// 2026-11-19): what <c>owl2-rl</c> and a view check against the bindings.
/// </summary>
internal sealed class GraphsNamed : AlgebraRewriter
{
    private GraphsNamed()
    {
    }

    /// <summary>The IRIs <c>GRAPH</c> names.</summary>
    internal List<RdfTerm> Graphs { get; } = [];

    /// <summary>Whether some <c>GRAPH</c> names a variable.</summary>
    internal bool AnyVariable { get; private set; }

    /// <summary>Walks <paramref name="query"/>.</summary>
    internal static GraphsNamed Of(Query query)
    {
        GraphsNamed named = new();
        named.Rewrite(query);
        return named;
    }

    protected override QueryPattern RewriteGraph(Graph pattern)
    {
        switch (pattern.Name)
        {
            case TermPattern { Term: { Kind: RdfTermKind.Iri } term }:
                Graphs.Add(term);
                break;
            case VariablePattern:
                AnyVariable = true;
                break;
        }

        return base.RewriteGraph(pattern);
    }
}
