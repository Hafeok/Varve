// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Shacl.Sparql;

/// <summary>
/// SHACL 1.2 SPARQL-Related Features, Appendix A: pre-binding as Values
/// Insertion — every basic graph pattern, property path and
/// <c>Graph(?var, pattern)</c> joined with the one-row table of the bindings —
/// and the restrictions a query must keep for it to mean pre-binding.
/// </summary>
internal static class PreBinding
{
    /// <summary>Why a query cannot be pre-bound over <paramref name="variables"/>, or null when it can (Appendix A).</summary>
    internal static string? Violation(Query query, IReadOnlySet<string> variables)
    {
        Checker checker = new(variables);
        checker.Rewrite(query);
        return checker.Problem;
    }

    /// <summary>The query with <paramref name="bindings"/> inserted by Appendix A's <c>Replace</c>.</summary>
    internal static Query Insert(Query query, IReadOnlyList<(string Variable, RdfTerm Value)> bindings)
    {
        Variable[] variables = new Variable[bindings.Count];
        RdfTerm?[] row = new RdfTerm?[bindings.Count];
        for (int i = 0; i < bindings.Count; i++)
        {
            variables[i] = new Variable(bindings[i].Variable);
            row[i] = bindings[i].Value;
        }

        Values table = new(AlgebraList.From<Variable>(variables), AlgebraList.From<AlgebraList<RdfTerm?>>([AlgebraList.From<RdfTerm?>(row)]));
        return new Inserter(table).Rewrite(query);
    }

    /// <summary>Replace(X, μ): each BGP, path and Graph(Var, pattern) Y becomes Join(Y, Table(μ)).</summary>
    private sealed class Inserter(Values table) : AlgebraRewriter
    {
        public override QueryPattern Rewrite(QueryPattern pattern) => pattern switch
        {
            Bgp or PathPattern => new Join(pattern, table),
            Graph { Name: VariablePattern } graph => new Join(graph with { Inner = Rewrite(graph.Inner) }, table),
            _ => base.Rewrite(pattern),
        };
    }

    /// <summary>Finds the first violation of Appendix A's restrictions.</summary>
    private sealed class Checker(IReadOnlySet<string> variables) : AlgebraRewriter
    {
        internal string? Problem { get; private set; }

        public override QueryPattern Rewrite(QueryPattern pattern)
        {
            switch (pattern)
            {
                case Minus:
                    Problem ??= "a query with pre-bound variables must not contain MINUS";
                    break;
                case Service:
                    Problem ??= "a query with pre-bound variables must not contain SERVICE, which Varve does not permit";
                    break;
                case Values values:
                    foreach (Variable variable in values.Variables)
                    {
                        if (variables.Contains(variable.Name))
                        {
                            Problem ??= "a query must not contain a VALUES clause that mentions the pre-bound variable ?" + variable.Name;
                        }
                    }

                    break;
                case Extend extend when variables.Contains(extend.Variable.Name):
                    Problem ??= "a query must not use AS ?" + extend.Variable.Name + " for a pre-bound variable";
                    break;
            }

            return base.Rewrite(pattern);
        }
    }
}
