// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;

namespace Varve.Events;

/// <summary>
/// A declaration's <c>SELECT</c>, checked against the form of
/// docs/spec/events.md §5.1 and ready to be bound to one event node: the
/// pattern under the projection is joined with <c>VALUES ?event { e }</c>.
/// </summary>
internal sealed class CompiledSelect
{
    internal static readonly Variable EventVariable = new("event");

    private readonly SelectQuery _query;
    private readonly Func<QueryPattern, QueryPattern> _rebuild;
    private readonly QueryPattern _where;

    private CompiledSelect(SelectQuery query, QueryPattern where, Func<QueryPattern, QueryPattern> rebuild)
    {
        _query = query;
        _where = where;
        _rebuild = rebuild;
    }

    /// <summary>The query with <c>?event</c> bound to <paramref name="eventNode"/>.</summary>
    internal SelectQuery BindEvent(RdfTerm eventNode)
    {
        Values binding = new(AlgebraList.Of(EventVariable), AlgebraList.Of(AlgebraList.Of<RdfTerm?>(eventNode)));
        return _query with { Pattern = _rebuild(new Join(binding, _where)) };
    }

    /// <summary>
    /// Parses and checks <paramref name="text"/>: a SPARQL <c>SELECT</c> whose
    /// projection is exactly <paramref name="projected"/>, with no dataset
    /// clause, and no grouping, aggregate, ordering, slice or <c>SERVICE</c>,
    /// that mentions <c>?event</c>. Answers the reason when it is not.
    /// </summary>
    internal static bool TryCompile(string text, ISet<string> projected, out CompiledSelect? compiled, out string? reason)
    {
        compiled = null;

        if (!SparqlParser.TryParseQuery(text.AsSpan(), default, out Query? query, out SparqlParseError error))
        {
            reason = "the query does not parse: " + error.Message;
            return false;
        }

        if (query is not SelectQuery select)
        {
            reason = "the query is not a SELECT";
            return false;
        }

        if (select.Dataset is not null)
        {
            reason = "the query names a dataset (FROM or FROM NAMED)";
            return false;
        }

        // The solution modifiers a declaration may use sit above the
        // projection's inner pattern: DISTINCT or REDUCED, then the projection.
        List<Func<QueryPattern, QueryPattern>> wrappers = [];
        QueryPattern pattern = select.Pattern;

        while (pattern is Distinct or Reduced)
        {
            QueryPattern node = pattern;
            wrappers.Add(inner => node is Distinct ? new Distinct(inner) : new Reduced(inner));
            pattern = node is Distinct d ? d.Inner : ((Reduced)node).Inner;
        }

        if (pattern is not Project project)
        {
            reason = "the query has no explicit projection, or orders or limits its solutions";
            return false;
        }

        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (Variable variable in project.Variables)
        {
            names.Add(variable.Name);
        }

        if (!names.SetEquals(projected))
        {
            reason = "the projection is not exactly the field names and the subject variable";
            return false;
        }

        string? forbidden = Forbidden(project.Inner);

        if (forbidden is not null)
        {
            reason = "the query uses " + forbidden;
            return false;
        }

        if (!Mentions(project.Inner, EventVariable))
        {
            reason = "the query does not mention ?event";
            return false;
        }

        AlgebraList<Variable> projection = project.Variables;
        Func<QueryPattern, QueryPattern> rebuild = inner =>
        {
            QueryPattern result = new Project(inner, projection);

            for (int i = wrappers.Count - 1; i >= 0; i--)
            {
                result = wrappers[i](result);
            }

            return result;
        };

        compiled = new CompiledSelect(select, project.Inner, rebuild);
        reason = null;
        return true;
    }

    private static string? Forbidden(QueryPattern pattern) => pattern switch
    {
        Group => "GROUP BY or an aggregate",
        OrderBy => "ORDER BY",
        Slice => "LIMIT or OFFSET",
        Service => "SERVICE",
        Join j => Forbidden(j.Left) ?? Forbidden(j.Right),
        LeftJoin l => Forbidden(l.Left) ?? Forbidden(l.Right),
        Union u => Forbidden(u.Left) ?? Forbidden(u.Right),
        Minus m => Forbidden(m.Left) ?? Forbidden(m.Right),
        Filter f => Forbidden(f.Inner),
        Graph g => Forbidden(g.Inner),
        Extend e => Forbidden(e.Inner),
        Project p => Forbidden(p.Inner),
        Distinct d => Forbidden(d.Inner),
        Reduced r => Forbidden(r.Inner),
        _ => null,
    };

    private static bool Mentions(QueryPattern pattern, Variable variable) => pattern switch
    {
        Bgp bgp => Any(bgp.Triples, variable),
        PathPattern path => Is(path.Subject, variable) || Is(path.Object, variable),
        Join j => Mentions(j.Left, variable) || Mentions(j.Right, variable),
        LeftJoin l => Mentions(l.Left, variable) || Mentions(l.Right, variable),
        Union u => Mentions(u.Left, variable) || Mentions(u.Right, variable),
        Minus m => Mentions(m.Left, variable),
        Filter f => Mentions(f.Inner, variable),
        Graph g => Is(g.Name, variable) || Mentions(g.Inner, variable),
        Extend e => e.Variable.Equals(variable) || Mentions(e.Inner, variable),
        Project p => Mentions(p.Inner, variable),
        Distinct d => Mentions(d.Inner, variable),
        Reduced r => Mentions(r.Inner, variable),
        Values v => Contains(v.Variables, variable),
        _ => false,
    };

    private static bool Any(AlgebraList<TriplePattern> triples, Variable variable)
    {
        foreach (TriplePattern triple in triples)
        {
            if (Is(triple.Subject, variable) || Is(triple.Predicate, variable) || Is(triple.Object, variable))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(AlgebraList<Variable> variables, Variable variable)
    {
        foreach (Variable candidate in variables)
        {
            if (candidate.Equals(variable))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Is(PatternTerm term, Variable variable) => term switch
    {
        VariablePattern v => v.Variable.Equals(variable),
        TripleTermPattern t => Is(t.Subject, variable) || Is(t.Predicate, variable) || Is(t.Object, variable),
        _ => false,
    };
}
