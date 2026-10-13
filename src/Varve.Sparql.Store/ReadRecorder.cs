// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Store.Log;

namespace Varve.Sparql.Store;

/// <summary>
/// The graphs a request asked for (ADR 0057, amended 2026-10-13): a quad source
/// over the staging view, below every other wrapper, that passes everything
/// through and records which graphs were asked for — whether or not anything
/// was found there.
/// </summary>
/// <remarks>
/// <para>
/// Recorded: the graph of every <c>Match</c>, <c>Estimate</c> and
/// <c>Contains</c>; the default graph; "every graph" for a match over any
/// named graph or over every graph, unless the operation declared its named
/// graphs with <c>USING NAMED</c>, when it is that set; and every IRI the
/// source could not internalise. The last is how a pattern over a graph the
/// store has never held is recorded: the evaluator asks for the IRI, finds no
/// handle, and never matches. <c>FILTER NOT EXISTS { GRAPH &lt;h&gt; … }</c>
/// over an absent <c>h</c> is a read of <c>h</c>, and a commit that creates
/// <c>h</c> meanwhile must conflict with it. An IRI recorded that way that was
/// not a graph name widens the scope by a graph no commit touches.
/// </para>
/// </remarks>
internal sealed class ReadRecorder(IQuadSource inner) : IQuadSource
{
    private readonly HashSet<RdfTerm> _unknown = new(RdfTerm.Comparer);

    // The named graphs asked for, distinct, in a pooled array: Contains runs
    // per quad, so recording allocates per graph, never per quad.
    private ulong[] _graphs = ArrayPool<ulong>.Shared.Rent(8);
    private int _count;
    private ulong _last;
    private bool _default;

    /// <summary>True once a read covered every graph: the scope is every graph.</summary>
    internal bool Everything { get; private set; }

    /// <summary>
    /// The named graphs <c>USING NAMED</c> declared for the operation being
    /// evaluated, which a match over any named graph is confined to; null
    /// when none were declared.
    /// </summary>
    internal IReadOnlyList<RdfTerm>? DeclaredNamed { get; set; }

    public IEqualityComparer<TermHandle> TermComparer => inner.TermComparer;

    public bool TryInternalise(RdfTerm term, out TermHandle handle)
    {
        if (inner.TryInternalise(term, out handle))
        {
            return true;
        }

        if (term.Kind == RdfTermKind.Iri)
        {
            _unknown.Add(term);
        }

        return false;
    }

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => inner.TryExternalise(handle, out term);

    public bool Contains(in Quad quad)
    {
        Record(quad.Graph);
        return inner.Contains(in quad);
    }

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        Record(graph);
        return inner.Match(subject, predicate, @object, graph);
    }

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        Record(graph);
        return inner.Estimate(subject, predicate, @object, graph);
    }

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => inner.TryGetInlineValue(handle, out value);

    /// <summary>
    /// The graphs a pattern asks for, read from the pattern itself: every
    /// basic graph pattern and path, in the graph it is matched in, inside
    /// <c>EXISTS</c> too. A pattern with a constant the store has never held
    /// is never matched — the evaluator finds no handle and stops — yet what
    /// it would have matched is what a concurrent commit can change, so the
    /// scope cannot come from the matches alone. <c>WITH</c> and <c>USING</c>
    /// name the graphs the default graph stands for; <c>USING NAMED</c> the
    /// graphs <c>GRAPH ?g</c> ranges over. A <c>SERVICE</c> reads elsewhere.
    /// </summary>
    internal void Asked(QueryPattern where, RdfTerm? with, DatasetSpec? dataset) =>
        Walk(where, Context.Default, with, dataset);

    private enum Context
    {
        Default,
        Named,
        AnyNamed,
    }

    private void Walk(QueryPattern pattern, Context context, RdfTerm? graph, DatasetSpec? dataset)
    {
        switch (pattern)
        {
            case Bgp bgp:
                if (bgp.Triples.Count > 0 || context != Context.Default)
                {
                    Leaf(context, graph, dataset);
                }

                break;

            case PathPattern:
                Leaf(context, graph, dataset);
                break;

            case Algebra.Graph named:
                if (named.Name is TermPattern constant)
                {
                    Walk(named.Inner, Context.Named, constant.Term, dataset);
                }
                else
                {
                    Walk(named.Inner, Context.AnyNamed, null, dataset);
                }

                break;

            case Join join:
                Walk(join.Left, context, graph, dataset);
                Walk(join.Right, context, graph, dataset);
                break;

            case LeftJoin left:
                Walk(left.Left, context, graph, dataset);
                Walk(left.Right, context, graph, dataset);
                Walk(left.Condition, context, graph, dataset);
                break;

            case Union union:
                Walk(union.Left, context, graph, dataset);
                Walk(union.Right, context, graph, dataset);
                break;

            case Minus minus:
                Walk(minus.Left, context, graph, dataset);
                Walk(minus.Right, context, graph, dataset);
                break;

            case Filter filter:
                Walk(filter.Condition, context, graph, dataset);
                Walk(filter.Inner, context, graph, dataset);
                break;

            case Extend extend:
                Walk(extend.Expression, context, graph, dataset);
                Walk(extend.Inner, context, graph, dataset);
                break;

            case Group group:
                foreach (GroupKey key in group.Keys)
                {
                    Walk(key.Expression, context, graph, dataset);
                }

                Walk(group.Inner, context, graph, dataset);
                break;

            case OrderBy order:
                foreach (OrderCondition condition in order.Conditions)
                {
                    Walk(condition.Expression, context, graph, dataset);
                }

                Walk(order.Inner, context, graph, dataset);
                break;

            case Distinct distinct:
                Walk(distinct.Inner, context, graph, dataset);
                break;

            case Reduced reduced:
                Walk(reduced.Inner, context, graph, dataset);
                break;

            case Project project:
                Walk(project.Inner, context, graph, dataset);
                break;

            case Slice slice:
                Walk(slice.Inner, context, graph, dataset);
                break;

            case Service:
            case Values:
                break;

            default:
                // A pattern this walk does not know: every graph, never fewer.
                Everything = true;
                break;
        }
    }

    private void Walk(Expression? expression, Context context, RdfTerm? graph, DatasetSpec? dataset)
    {
        switch (expression)
        {
            case null:
            case ConstantExpression:
            case VariableExpression:
                break;

            case ExistsExpression exists:
                Walk(exists.Pattern, context, graph, dataset);
                break;

            case BinaryExpression binary:
                Walk(binary.Left, context, graph, dataset);
                Walk(binary.Right, context, graph, dataset);
                break;

            case UnaryExpression unary:
                Walk(unary.Operand, context, graph, dataset);
                break;

            case FunctionCall call:
                foreach (Expression argument in call.Arguments)
                {
                    Walk(argument, context, graph, dataset);
                }

                break;

            case CustomFunctionCall custom:
                foreach (Expression argument in custom.Arguments)
                {
                    Walk(argument, context, graph, dataset);
                }

                break;

            case AggregateExpression aggregate:
                Walk(aggregate.Argument, context, graph, dataset);
                break;

            default:
                Everything = true;
                break;
        }
    }

    // A leaf matched in its context: the graphs that context stands for.
    private void Leaf(Context context, RdfTerm? graph, DatasetSpec? dataset)
    {
        switch (context)
        {
            case Context.Named:
                _unknown.Add(graph!);
                break;

            case Context.AnyNamed when dataset is not null:
                foreach (RdfTerm named in dataset.NamedGraphs)
                {
                    _unknown.Add(named);
                }

                break;

            case Context.AnyNamed:
                Everything = true;
                break;

            case Context.Default when dataset is not null:
                foreach (RdfTerm term in dataset.DefaultGraphs)
                {
                    _unknown.Add(term);
                }

                break;

            case Context.Default when graph is not null:
                _unknown.Add(graph);
                break;

            default:
                _default = true;
                break;
        }
    }

    /// <summary>
    /// The scope: every graph asked for and every graph the delta writes, as
    /// request terms the sequencer resolves; null when it is every graph.
    /// </summary>
    internal List<RequestTerm>? Scope(IEnumerable<TermHandle> written)
    {
        if (Everything)
        {
            return null;
        }

        HashSet<TermHandle> handles = [.. written];

        for (int i = 0; i < _count; i++)
        {
            handles.Add(new TermHandle(_graphs[i]));
        }

        List<RequestTerm> scope = [];
        bool includesDefault = _default;

        foreach (TermHandle graph in handles)
        {
            if (graph.IsNone)
            {
                includesDefault = true;
            }
            else if (inner.TryExternalise(graph, out RdfTerm? term))
            {
                scope.Add(term);
            }
        }

        foreach (RdfTerm term in _unknown)
        {
            scope.Add(term);
        }

        if (includesDefault)
        {
            scope.Add(RequestTerm.None);
        }

        return scope;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Record(TermHandle graph)
    {
        if (graph.IsNone)
        {
            _default = true;
            return;
        }

        ulong id = graph.Value;

        if (id == _last)
        {
            return;
        }

        _last = id;
        Span<ulong> seen = _graphs.AsSpan(0, _count);

        if (seen.Contains(id))
        {
            return;
        }

        if (_count == _graphs.Length)
        {
            ulong[] larger = ArrayPool<ulong>.Shared.Rent(_count * 2);
            seen.CopyTo(larger);
            ArrayPool<ulong>.Shared.Return(_graphs);
            _graphs = larger;
        }

        _graphs[_count++] = id;
    }

    private void Record(GraphPattern graph)
    {
        switch (graph.Match)
        {
            case GraphMatch.DefaultGraph:
                _default = true;
                break;

            case GraphMatch.Named:
                Record(graph.Graph);
                break;

            case GraphMatch.AnyNamed when DeclaredNamed is { } declared:
                foreach (RdfTerm named in declared)
                {
                    if (inner.TryInternalise(named, out TermHandle handle))
                    {
                        Record(handle);
                    }
                    else
                    {
                        _unknown.Add(named);
                    }
                }

                break;

            default:
                Everything = true;
                break;
        }
    }
}
