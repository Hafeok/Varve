// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;

namespace Varve.Protocol.Client;

/// <summary>
/// The graphs a migration reads and writes, for its <c>Varve-If-Graphs-Match</c>
/// (ADR 0126; ADR 0130, amended): every graph its operations name, the default
/// graph where a pattern or a template is outside <c>GRAPH</c>, and
/// <c>varve:setup</c>, where its record goes. A migration whose graphs are not
/// all named — a <c>GRAPH ?g</c>, a <c>LOAD</c>, a <c>CLEAR ALL</c> — has no
/// such scope, and is sent under the dataset-wide <c>If-Match</c> as before.
/// </summary>
internal static class MigrationScope
{
    /// <summary>The migration's graphs, a null entry the default graph; false when they cannot all be named.</summary>
    internal static bool TryOf(string update, out HashSet<string?> graphs)
    {
        graphs = new HashSet<string?>(StringComparer.Ordinal) { SparqlHttpClient.SetupGraph };

        if (!SparqlParser.TryParseUpdate(update.AsSpan(), default, out Update? parsed, out _))
        {
            return false;
        }

        foreach (UpdateOperation operation in parsed.Operations)
        {
            if (!Add(operation, graphs))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Add(UpdateOperation operation, HashSet<string?> graphs)
    {
        switch (operation)
        {
            case InsertData insert:
                return Quads(insert.Quads, null, graphs);
            case DeleteData delete:
                return Quads(delete.Quads, null, graphs);
            case DeleteWhere where:
                return Quads(where.Quads, null, graphs);
            case Modify modify:
                RdfTerm? with = modify.With;

                if (with is not null)
                {
                    graphs.Add(Name(with));
                }

                if (modify.Using is { } dataset)
                {
                    foreach (RdfTerm graph in dataset.DefaultGraphs)
                    {
                        graphs.Add(Name(graph));
                    }

                    foreach (RdfTerm graph in dataset.NamedGraphs)
                    {
                        graphs.Add(Name(graph));
                    }
                }

                // A pattern outside GRAPH reads the default graph, or what
                // USING or WITH make it.
                bool reads = Pattern(modify.Where, graphs, out bool outside);

                if (outside && modify.Using is null)
                {
                    graphs.Add(with is null ? null : Name(with));
                }

                return reads && Quads(modify.Delete, with, graphs) && Quads(modify.Insert, with, graphs);
            case Clear clear:
                return Target(clear.Target, graphs);
            case Drop drop:
                return Target(drop.Target, graphs);
            case Create create:
                graphs.Add(Name(create.Graph));
                return true;
            case Add add:
                graphs.Add(add.From.IsDefault ? null : Name(add.From.Graph!));
                graphs.Add(add.To.IsDefault ? null : Name(add.To.Graph!));
                return true;
            case Move move:
                graphs.Add(move.From.IsDefault ? null : Name(move.From.Graph!));
                graphs.Add(move.To.IsDefault ? null : Name(move.To.Graph!));
                return true;
            case Copy copy:
                graphs.Add(copy.From.IsDefault ? null : Name(copy.From.Graph!));
                graphs.Add(copy.To.IsDefault ? null : Name(copy.To.Graph!));
                return true;
            default:
                // LOAD, and anything this list does not name.
                return false;
        }
    }

    private static bool Target(GraphTarget target, HashSet<string?> graphs)
    {
        switch (target.Kind)
        {
            case GraphTargetKind.Graph:
                graphs.Add(Name(target.Graph!));
                return true;
            case GraphTargetKind.Default:
                graphs.Add(null);
                return true;
            default:
                return false;
        }
    }

    private static bool Quads(AlgebraList<QuadPattern> quads, RdfTerm? with, HashSet<string?> graphs)
    {
        foreach (QuadPattern quad in quads)
        {
            switch (quad.Graph)
            {
                case null:
                    graphs.Add(with is null ? null : Name(with));
                    break;
                case TermPattern { Term: { Kind: RdfTermKind.Iri } iri }:
                    graphs.Add(Name(iri));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    // The graphs a pattern names; false for a graph it does not name.
    // `outside` says whether some part of it reads the active default graph.
    private static bool Pattern(QueryPattern pattern, HashSet<string?> graphs, out bool outside)
    {
        outside = false;

        switch (pattern)
        {
            case Graph graph:
                if (graph.Name is not TermPattern { Term: { Kind: RdfTermKind.Iri } iri })
                {
                    return false;
                }

                graphs.Add(Name(iri));
                return Pattern(graph.Inner, graphs, out _);
            case Service:
                return false;
            case Bgp or PathPattern:
                outside = true;
                return true;
            case Join join:
                return Both(join.Left, join.Right, graphs, out outside);
            case LeftJoin left:
                return Both(left.Left, left.Right, graphs, out outside);
            case Union union:
                return Both(union.Left, union.Right, graphs, out outside);
            case Minus minus:
                return Both(minus.Left, minus.Right, graphs, out outside);
            case Filter filter:
                // An EXISTS inside a filter may read any graph: the dataset-wide
                // precondition covers it.
                return !Exists(filter.Condition) && Pattern(filter.Inner, graphs, out outside);
            case Extend extend:
                return !Exists(extend.Expression) && Pattern(extend.Inner, graphs, out outside);
            case Project project:
                return Pattern(project.Inner, graphs, out outside);
            case Distinct distinct:
                return Pattern(distinct.Inner, graphs, out outside);
            case Reduced reduced:
                return Pattern(reduced.Inner, graphs, out outside);
            case Slice slice:
                return Pattern(slice.Inner, graphs, out outside);
            case OrderBy order:
                return Pattern(order.Inner, graphs, out outside);
            case Group group:
                return Pattern(group.Inner, graphs, out outside);
            case Values:
                return true;
            default:
                return false;
        }
    }

    private static bool Exists(Expression expression) => expression switch
    {
        ExistsExpression => true,
        UnaryExpression unary => Exists(unary.Operand),
        BinaryExpression binary => Exists(binary.Left) || Exists(binary.Right),
        FunctionCall call => Any(call.Arguments),
        CustomFunctionCall call => Any(call.Arguments),
        _ => false,
    };

    private static bool Any(AlgebraList<Expression> arguments)
    {
        foreach (Expression argument in arguments)
        {
            if (Exists(argument))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Both(QueryPattern left, QueryPattern right, HashSet<string?> graphs, out bool outside)
    {
        bool ok = Pattern(left, graphs, out bool a) & Pattern(right, graphs, out bool b);
        outside = a || b;
        return ok;
    }

    private static string Name(RdfTerm graph) => Encoding.UTF8.GetString(graph.Lexical);
}
