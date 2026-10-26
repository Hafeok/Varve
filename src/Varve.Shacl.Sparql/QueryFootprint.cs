// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Derivation;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Sparql.Algebra;
using SparqlPath = Varve.Sparql.Algebra.PropertyPath;

namespace Varve.Shacl.Sparql;

/// <summary>
/// What a SPARQL-based constraint reads (ADR 0171 §4), from its algebra: a
/// join graph whose vertices are the query's variables and whose edges are
/// its triple patterns and property paths, wherever they stand. When every
/// edge is joined to <c>$this</c> or <c>$value</c>, a changed triple's
/// anchors are found by evaluating the join path from the patterns it
/// matches back to the root, filters dropped; otherwise the constraint is
/// full. It also finds the functions whose value depends on the clock or on
/// chance (ADR 0169).
/// </summary>
internal sealed class QueryFootprint
{
    private const string This = "this";
    private const string Value = "value";

    private readonly List<Edge> _edges = [];
    private readonly Dictionary<string, (Edge Edge, string Toward)> _parent = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bound;
    private readonly bool _valueRoot;
    private int _scopes;
    private bool _topProjected;

    private QueryFootprint(Query query, IReadOnlyCollection<string> prebound)
    {
        _bound = new HashSet<string>(prebound, StringComparer.Ordinal);

        // $value is a root only where it is pre-bound, in an ASK validator (§4.3); in a SELECT it is a result variable.
        _valueRoot = _bound.Contains(Value);
        Walk(query.Pattern, null);
        Maintainability = Unanalysable is null ? Maintainability.Scoped : Maintainability.Full;
        if (Unanalysable is null)
        {
            Connect();
        }
    }

    internal Maintainability Maintainability { get; private set; }

    /// <summary>Why the query cannot be analysed, or null when it can.</summary>
    internal string? Unanalysable { get; private set; }

    /// <summary>A function whose value depends on the clock or on chance, by name, or null.</summary>
    internal string? NonDeterministic { get; private set; }

    internal static QueryFootprint Of(Query query, IReadOnlyCollection<string> prebound) => new(query, prebound);

    /// <summary>The focus nodes and value nodes whose solutions the changed triple may change, searched in the context's data.</summary>
    [DesignDecision(typeof(WhichShapesAreIncrementallyMaintainable.SparqlByJoinGraph), Scope = ExceptionScope.HotPath)]
    internal void Anchors(FootprintContext context)
    {
        HandleGraph graph = new(context.Data, context.DataGraph);
        if (!context.Data.TryExternalise(context.Predicate, out RdfTerm? predicate))
        {
            return;
        }

        foreach (Edge edge in _edges)
        {
            List<(string Vertex, List<TermHandle> Nodes)> seeds = [];
            edge.Seeds(graph, context.Subject, predicate, context.Predicate, context.Object, seeds);
            foreach ((string vertex, List<TermHandle> nodes) in seeds)
            {
                Climb(graph, vertex, nodes, context);
            }
        }
    }

    /// <summary>Evaluates the join path from <paramref name="vertex"/> to its root, and adds what it reaches there.</summary>
    private void Climb(HandleGraph graph, string vertex, List<TermHandle> nodes, FootprintContext context)
    {
        string at = vertex;
        List<TermHandle> current = nodes;
        while (!IsRoot(at))
        {
            if (current.Count == 0 || !_parent.TryGetValue(at, out (Edge Edge, string Toward) step))
            {
                return;
            }

            current = step.Edge.Across(graph, at, step.Toward, current);
            at = step.Toward;
        }

        foreach (TermHandle node in current)
        {
            if (at == This)
            {
                context.Add(node);
            }
            else
            {
                context.AddValueNode(node);
            }
        }
    }

    private bool IsRoot(string vertex) => vertex == This || (_valueRoot && vertex == Value);

    /// <summary>Breadth first from the roots: each vertex's edge toward the nearest root. An edge none reaches makes the query full.</summary>
    private void Connect()
    {
        Dictionary<string, List<Edge>> incident = new(StringComparer.Ordinal);
        foreach (Edge edge in _edges)
        {
            foreach (string vertex in edge.Vertices)
            {
                if (!incident.TryGetValue(vertex, out List<Edge>? list))
                {
                    list = [];
                    incident[vertex] = list;
                }

                list.Add(edge);
            }
        }

        HashSet<string> reached = new(StringComparer.Ordinal) { This };
        Queue<string> pending = new([This]);
        if (_valueRoot)
        {
            reached.Add(Value);
            pending.Enqueue(Value);
        }
        while (pending.Count > 0)
        {
            string vertex = pending.Dequeue();
            if (!incident.TryGetValue(vertex, out List<Edge>? edges))
            {
                continue;
            }

            foreach (Edge edge in edges)
            {
                foreach (string other in edge.Vertices)
                {
                    if (reached.Add(other))
                    {
                        _parent[other] = (edge, vertex);
                        pending.Enqueue(other);
                    }
                }
            }
        }

        foreach (Edge edge in _edges)
        {
            bool joined = false;
            foreach (string vertex in edge.Vertices)
            {
                joined |= reached.Contains(vertex);
            }

            if (!joined)
            {
                Unanalysable = "the pattern " + edge + " is not joined to $this";
                Maintainability = Maintainability.Full;
                return;
            }
        }
    }

    private void Walk(QueryPattern pattern, Scope? scope)
    {
        switch (pattern)
        {
            case Bgp bgp:
                foreach (TriplePattern triple in bgp.Triples)
                {
                    _edges.Add(new TripleEdge(Vertex(triple.Subject, scope), Constant(triple.Subject), Vertex(triple.Predicate, scope), Constant(triple.Predicate), Vertex(triple.Object, scope), Constant(triple.Object)));
                }

                break;
            case PathPattern path:
                if (Regular(path.Path) is { } regular)
                {
                    _edges.Add(new PathEdge(Vertex(path.Subject, scope), Vertex(path.Object, scope), regular.Compile(), RegularPath.Inverse(regular).Compile(), path.Path));
                }
                else
                {
                    Unanalysable ??= "a negated property set";
                }

                break;
            case Join join:
                Walk(join.Left, scope);
                Walk(join.Right, scope);
                break;
            case LeftJoin left:
                Walk(left.Left, scope);
                Walk(left.Right, scope);
                Expression(left.Condition, scope, reachesResult: false);
                break;
            case Union union:
                Walk(union.Left, scope);
                Walk(union.Right, scope);
                break;
            case Minus minus:
                Walk(minus.Left, scope);
                Walk(minus.Right, scope);
                break;
            case Filter filter:
                Expression(filter.Condition, scope, reachesResult: false);
                Walk(filter.Inner, scope);
                break;
            case Extend extend:
                Expression(extend.Expression, scope, reachesResult: true);
                Walk(extend.Inner, scope);
                break;
            case Group group:
                foreach (GroupKey key in group.Keys)
                {
                    Expression(key.Expression, scope, reachesResult: true);
                }

                Walk(group.Inner, scope);
                break;
            case OrderBy order:
                foreach (OrderCondition condition in order.Conditions)
                {
                    Expression(condition.Expression, scope, reachesResult: false);
                }

                Walk(order.Inner, scope);
                break;
            case Distinct distinct:
                Walk(distinct.Inner, scope);
                break;
            case Reduced reduced:
                Walk(reduced.Inner, scope);
                break;
            case Slice slice:
                Walk(slice.Inner, scope);
                break;
            case Project project:
                // The top projection names the query's own variables; a sub-select's are its own unless projected.
                if (scope is null && !_topProjected)
                {
                    _topProjected = true;
                    Walk(project.Inner, null);
                }
                else
                {
                    HashSet<string> projected = new(StringComparer.Ordinal);
                    foreach (Variable variable in project.Variables)
                    {
                        projected.Add(variable.Name);
                    }

                    Walk(project.Inner, new Scope("s" + (++_scopes).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":", projected, scope));
                }

                break;
            case Graph:
                Unanalysable ??= "a GRAPH block, which reads outside the data graph";
                break;
            case Service:
                Unanalysable ??= "SERVICE";
                break;
            case Values:
                break;
            default:
                Unanalysable ??= "a pattern of kind " + pattern.GetType().Name;
                break;
        }
    }

    private void Expression(Expression? expression, Scope? scope, bool reachesResult)
    {
        switch (expression)
        {
            case null:
            case VariableExpression:
            case ConstantExpression:
                return;
            case UnaryExpression unary:
                Expression(unary.Operand, scope, reachesResult);
                return;
            case BinaryExpression binary:
                Expression(binary.Left, scope, reachesResult);
                Expression(binary.Right, scope, reachesResult);
                return;
            case FunctionCall call:
                NonDeterministic ??= call.Function switch
                {
                    BuiltInFunction.Now => "NOW()",
                    BuiltInFunction.Rand => "RAND()",
                    BuiltInFunction.Uuid => "UUID()",
                    BuiltInFunction.StrUuid => "STRUUID()",
                    BuiltInFunction.BNode when reachesResult => "BNODE()",
                    _ => null,
                };
                foreach (Expression argument in call.Arguments)
                {
                    Expression(argument, scope, reachesResult);
                }

                return;
            case CustomFunctionCall custom:
                foreach (Expression argument in custom.Arguments)
                {
                    Expression(argument, scope, reachesResult);
                }

                return;
            case ExistsExpression exists:
                Walk(exists.Pattern, scope);
                return;
            case AggregateExpression aggregate:
                Expression(aggregate.Argument, scope, reachesResult);
                return;
        }
    }

    /// <summary>A pattern term's vertex: a variable or blank node, renamed inside a sub-select unless projected; a pre-bound parameter is a constant.</summary>
    private string? Vertex(PatternTerm term, Scope? scope)
    {
        string? name = term switch
        {
            VariablePattern variable => variable.Variable.Name,
            BlankNodePattern blank => "_:" + blank.Label,
            _ => null,
        };
        if (name is null || (name != This && !(_valueRoot && name == Value) && _bound.Contains(name)))
        {
            return null;
        }

        return scope is null ? name : scope.Resolve(name);
    }

    private static RdfTerm? Constant(PatternTerm term) => term is TermPattern constant ? constant.Term : null;

    /// <summary>A SPARQL property path as a regular path, or null for a negated property set.</summary>
    private static RegularPath<RdfTerm>? Regular(SparqlPath path) => path switch
    {
        PredicatePath predicate => RegularPath.Edge(predicate.Predicate),
        InversePath inverse => Regular(inverse.Inner) is { } inner ? RegularPath.Inverse(inner) : null,
        SequencePath sequence => Regular(sequence.Left) is { } l && Regular(sequence.Right) is { } r ? RegularPath.Sequence([l, r]) : null,
        AlternativePath alternative => Regular(alternative.Left) is { } l && Regular(alternative.Right) is { } r ? RegularPath.Alternative([l, r]) : null,
        ZeroOrMorePath star => Regular(star.Inner) is { } inner ? RegularPath.ZeroOrMore(inner) : null,
        OneOrMorePath plus => Regular(plus.Inner) is { } inner ? RegularPath.OneOrMore(inner) : null,
        ZeroOrOnePath optional => Regular(optional.Inner) is { } inner ? RegularPath.ZeroOrOne(inner) : null,
        _ => null,
    };

    /// <summary>A sub-select: its variables are its own unless it projects them, when they are the enclosing scope's.</summary>
    private sealed class Scope(string prefix, HashSet<string> projected, Scope? parent)
    {
        internal string Resolve(string name) =>
            !projected.Contains(name) ? prefix + name : parent is null ? name : parent.Resolve(name);
    }

    /// <summary>An edge of the join graph.</summary>
    private abstract class Edge
    {
        internal abstract IEnumerable<string> Vertices { get; }

        /// <summary>Where the changed triple binds this edge's vertices, if it matches.</summary>
        internal abstract void Seeds(HandleGraph graph, TermHandle subject, RdfTerm predicate, TermHandle predicateHandle, TermHandle @object, List<(string, List<TermHandle>)> seeds);

        /// <summary>The nodes at <paramref name="to"/> joined to <paramref name="nodes"/> at <paramref name="from"/> through this edge.</summary>
        internal abstract List<TermHandle> Across(HandleGraph graph, string from, string to, List<TermHandle> nodes);
    }

    private sealed class TripleEdge(string? s, RdfTerm? sConstant, string? p, RdfTerm? pConstant, string? o, RdfTerm? oConstant) : Edge
    {
        internal override IEnumerable<string> Vertices
        {
            get
            {
                if (s is not null)
                {
                    yield return s;
                }

                if (p is not null)
                {
                    yield return p;
                }

                if (o is not null)
                {
                    yield return o;
                }
            }
        }

        internal override void Seeds(HandleGraph graph, TermHandle subject, RdfTerm predicate, TermHandle predicateHandle, TermHandle @object, List<(string, List<TermHandle>)> seeds)
        {
            if ((pConstant is not null && !pConstant.Equals(predicate))
                || (sConstant is not null && !graph.Is(subject, sConstant))
                || (oConstant is not null && !graph.Is(@object, oConstant)))
            {
                return;
            }

            if (s is not null)
            {
                seeds.Add((s, [subject]));
            }

            if (p is not null)
            {
                seeds.Add((p, [predicateHandle]));
            }

            if (o is not null)
            {
                seeds.Add((o, [@object]));
            }
        }

        internal override List<TermHandle> Across(HandleGraph graph, string from, string to, List<TermHandle> nodes)
        {
            TermHandle Fixed(string? vertex, RdfTerm? constant) => vertex is null && constant is not null && graph.TryHandle(constant, out TermHandle h) ? h : TermHandle.None;

            List<TermHandle> found = [];
            HashSet<TermHandle> seen = new(graph.Nodes);
            foreach (TermHandle node in nodes)
            {
                TermHandle sh = from == s ? node : Fixed(s, sConstant);
                TermHandle ph = from == p ? node : Fixed(p, pConstant);
                TermHandle oh = from == o ? node : Fixed(o, oConstant);
                if ((s is null && sConstant is not null && sh.IsNone) || (p is null && pConstant is not null && ph.IsNone) || (o is null && oConstant is not null && oh.IsNone))
                {
                    continue;
                }

                graph.Match(sh, ph, oh, to == s ? 0 : to == p ? 1 : 2, found, seen);
            }

            return found;
        }

        public override string ToString() => (s ?? sConstant?.ToString()) + " " + (p ?? pConstant?.ToString()) + " " + (o ?? oConstant?.ToString());
    }

    private sealed class PathEdge(string? s, string? o, PathAutomaton<RdfTerm> forward, PathAutomaton<RdfTerm> backward, SparqlPath path) : Edge
    {
        internal override IEnumerable<string> Vertices
        {
            get
            {
                if (s is not null)
                {
                    yield return s;
                }

                if (o is not null)
                {
                    yield return o;
                }
            }
        }

        internal override void Seeds(HandleGraph graph, TermHandle subject, RdfTerm predicate, TermHandle predicateHandle, TermHandle @object, List<(string, List<TermHandle>)> seeds)
        {
            if (s is not null)
            {
                List<TermHandle> starts = [];
                forward.Anchors(graph, subject, predicate, @object, starts, new HashSet<TermHandle>(graph.Nodes));
                seeds.Add((s, starts));
            }

            if (o is not null)
            {
                List<TermHandle> ends = [];
                backward.Anchors(graph, subject, predicate, @object, ends, new HashSet<TermHandle>(graph.Nodes));
                seeds.Add((o, ends));
            }
        }

        internal override List<TermHandle> Across(HandleGraph graph, string from, string to, List<TermHandle> nodes)
        {
            List<TermHandle> found = [];
            HashSet<TermHandle> seen = new(graph.Nodes);
            foreach (TermHandle node in nodes)
            {
                if (from == s)
                {
                    forward.Evaluate(graph, node, found, seen);
                }
                else
                {
                    forward.Reverse(graph, node, found, seen);
                }
            }

            return found;
        }

        public override string ToString() => (s ?? "a constant") + " " + path.GetType().Name + " " + (o ?? "a constant");
    }

    /// <summary>The data graph by handle, as a regular path walks it and as a pattern is matched.</summary>
    private sealed class HandleGraph(IQuadSource data, GraphPattern dataGraph) : IPathGraph<TermHandle, RdfTerm>
    {
        private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);

        public IEqualityComparer<TermHandle> Nodes => data.TermComparer;

        internal bool TryHandle(RdfTerm term, out TermHandle handle)
        {
            if (!_handles.TryGetValue(term, out handle))
            {
                if (term.Kind == RdfTermKind.BlankNode || !data.TryInternalise(term, out handle))
                {
                    handle = TermHandle.None;
                }

                _handles[term] = handle;
            }

            return !handle.IsNone;
        }

        internal bool Is(TermHandle handle, RdfTerm term) => TryHandle(term, out TermHandle h) && data.TermComparer.Equals(h, handle);

        internal void Match(TermHandle s, TermHandle p, TermHandle o, int position, List<TermHandle> into, HashSet<TermHandle> seen)
        {
            using IQuadCursor cursor = data.Match(s, p, o, dataGraph);
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                TermHandle found = position switch { 0 => quad.Subject, 1 => quad.Predicate, _ => quad.Object };
                if (seen.Add(found))
                {
                    into.Add(found);
                }
            }
        }

        [DesignDecision(typeof(WhichShapesAreIncrementallyMaintainable.SparqlByJoinGraph), Scope = ExceptionScope.HotPath)]
        public void Objects(TermHandle subject, RdfTerm label, List<TermHandle> into)
        {
            if (TryHandle(label, out TermHandle p))
            {
                Match(subject, p, TermHandle.None, 2, into, new HashSet<TermHandle>(data.TermComparer));
            }
        }

        [DesignDecision(typeof(WhichShapesAreIncrementallyMaintainable.SparqlByJoinGraph), Scope = ExceptionScope.HotPath)]
        public void Subjects(RdfTerm label, TermHandle obj, List<TermHandle> into)
        {
            if (TryHandle(label, out TermHandle p))
            {
                Match(TermHandle.None, p, obj, 0, into, new HashSet<TermHandle>(data.TermComparer));
            }
        }
    }
}
