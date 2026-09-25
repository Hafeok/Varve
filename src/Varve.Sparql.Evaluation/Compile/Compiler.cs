// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Expressions;
using Varve.Sparql.Evaluation.Operators;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Compile;

/// <summary>A query compiled for one source: the operator tree, its slots, and what the query form needs.</summary>
internal sealed class CompiledQuery
{
    internal required Operator Root { get; init; }

    internal required int Width { get; init; }

    internal required IReadOnlyDictionary<string, int> Slots { get; init; }

    /// <summary>A SELECT's columns: its projected variables, in order, and their slots.</summary>
    internal Variable[] Columns { get; init; } = [];

    internal int[] ColumnSlots { get; init; } = [];
}

/// <summary>
/// From the algebra to operators (<c>sparql-evaluation.md</c> §5.3): the
/// variable table, the constants resolved through the source once, the
/// aggregates above each <c>Group</c> extracted (ADR 0053), and the tree of
/// operators. One compilation per execution, because constants are the source's.
/// </summary>
internal sealed class Compiler
{
    private readonly IQuadSource _source;
    private readonly EvaluationOptions _options;
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
    private int _hidden;
    private Project? _columnsOnly;

    internal Compiler(IQuadSource source, EvaluationOptions options)
    {
        _source = source;
        _options = options;
    }

    internal CompiledQuery Compile(Query query)
    {
        // A SELECT's own projection, under nothing that compares whole rows,
        // restricts nothing anyone reads: the results read the projected
        // columns only. Compiled away, it saves a row per solution (§11).
        _columnsOnly = query is SelectQuery ? query.Pattern switch
        {
            Project own => own,
            Slice { Inner: Project own } => own,
            _ => null,
        } : null;

        Operator root = CompilePattern(query.Pattern, null);
        Variable[] columns = [];
        int[] columnSlots = [];
        if (query is SelectQuery && FindProject(query.Pattern) is { } project)
        {
            columns = project.Variables.ToArray();
            columnSlots = [.. columns.Select(v => Slot(v.Name))];
        }

        return new CompiledQuery { Root = root, Width = _slots.Count, Slots = _slots, Columns = columns, ColumnSlots = columnSlots };
    }

    /// <summary>The slot of a variable name, allocated on first use.</summary>
    internal int Slot(string name)
    {
        if (!_slots.TryGetValue(name, out int slot))
        {
            slot = _slots.Count;
            _slots.Add(name, slot);
        }

        return slot;
    }

    private int Hidden(string kind) => Slot("." + kind + _hidden++.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Project? FindProject(QueryPattern pattern) => pattern switch
    {
        Project project => project,
        Distinct distinct => FindProject(distinct.Inner),
        Reduced reduced => FindProject(reduced.Inner),
        Slice slice => FindProject(slice.Inner),
        _ => null,
    };

    // --------------------------------------------------------------- patterns

    internal Operator CompilePattern(QueryPattern pattern, AggregateScope? scope)
    {
        if (scope is null && ReachesGroup(pattern))
        {
            AggregateScope chain = new();
            return CompileChain(pattern, chain);
        }

        switch (pattern)
        {
            case Bgp bgp:
                return CompileBgp(bgp);
            case PathPattern path:
                return CompilePath(path);
            case Join join:
                {
                    Operator left = CompilePattern(join.Left, null);
                    Operator right = CompilePattern(join.Right, null);
                    return new JoinOperator(left, right) { Certain = Union(left.Certain, right.Certain) };
                }

            case LeftJoin leftJoin:
                {
                    Operator left = CompilePattern(leftJoin.Left, null);
                    Operator right = CompilePattern(leftJoin.Right, null);
                    Expr? condition = leftJoin.Condition is null ? null : CompileExpression(leftJoin.Condition, null);
                    return new LeftJoinOperator(left, right, condition) { Certain = left.Certain };
                }

            case Filter filter:
                {
                    Operator inner = CompilePattern(filter.Inner, null);
                    return new FilterOperator(CompileExpression(filter.Condition, null), inner) { Certain = inner.Certain };
                }

            case Union union:
                {
                    Operator left = CompilePattern(union.Left, null);
                    Operator right = CompilePattern(union.Right, null);
                    return new UnionOperator(left, right) { Certain = Intersect(left.Certain, right.Certain) };
                }

            case Graph graph:
                {
                    Operator inner = CompilePattern(graph.Inner, null);
                    if (graph.Name is VariablePattern variable)
                    {
                        int slot = Slot(variable.Variable.Name);
                        HashSet<string> mentioned = new(StringComparer.Ordinal);
                        CollectVariables(graph.Inner, mentioned);
                        return new GraphOperator(inner, null, slot, mentioned.Contains(variable.Variable.Name)) { Certain = Union(inner.Certain, [slot]) };
                    }

                    return new GraphOperator(inner, ((TermPattern)graph.Name).Term, -1, false) { Certain = inner.Certain };
                }

            case Extend extend:
                {
                    Operator inner = CompilePattern(extend.Inner, null);
                    return new ExtendOperator(inner, Slot(extend.Variable.Name), CompileExpression(extend.Expression, null)) { Certain = inner.Certain };
                }

            case Minus minus:
                {
                    Operator left = CompilePattern(minus.Left, null);
                    return new MinusOperator(left, CompilePattern(minus.Right, null)) { Certain = left.Certain };
                }

            case Values values:
                return CompileValues(values);
            case Service service:
                return CompileService(service);
            case OrderBy orderBy:
                {
                    Operator inner = CompilePattern(orderBy.Inner, null);
                    return CompileOrderBy(orderBy, inner, null);
                }

            case Project project:
                {
                    Operator inner = CompilePattern(project.Inner, null);
                    int[] slots = [.. project.Variables.ToArray().Select(v => Slot(v.Name))];
                    return ReferenceEquals(project, _columnsOnly)
                        ? inner
                        : new ProjectOperator(inner, slots) { Certain = Intersect(inner.Certain, slots) };
                }

            case Distinct distinct:
                {
                    Operator inner = CompilePattern(distinct.Inner, null);
                    return new DistinctOperator(inner) { Certain = inner.Certain };
                }

            case Reduced reduced:
                {
                    Operator inner = CompilePattern(reduced.Inner, null);
                    return new DistinctOperator(inner) { Certain = inner.Certain };
                }

            case Slice slice:
                {
                    Operator inner = CompilePattern(slice.Inner, null);
                    return new SliceOperator(inner, slice.Offset, slice.Limit) { Certain = inner.Certain };
                }

            default:
                throw new QueryEvaluationException("The evaluator has no operator for " + pattern.GetType().Name + ".");
        }
    }

    private BgpOperator CompileBgp(Bgp bgp)
    {
        TriplePatternSpec[] specs = new TriplePatternSpec[bgp.Triples.Count];
        List<int> certain = [];
        for (int i = 0; i < specs.Length; i++)
        {
            TriplePattern triple = bgp.Triples[i];
            specs[i] = new TriplePatternSpec(Position(triple.Subject, certain), Position(triple.Predicate, certain), Position(triple.Object, certain));
        }

        return new BgpOperator(specs) { Certain = [.. certain.Distinct()] };
    }

    private PatternPosition Position(PatternTerm term, List<int>? certain)
    {
        switch (term)
        {
            case VariablePattern variable:
                {
                    int slot = Slot(variable.Variable.Name);
                    certain?.Add(slot);
                    return PatternPosition.OfSlot(slot);
                }

            case BlankNodePattern blank:
                {
                    // sparql-algebra.md §3.1: a blank node in a pattern is a variable never projected.
                    int slot = Slot("_:" + blank.Label);
                    certain?.Add(slot);
                    return PatternPosition.OfSlot(slot);
                }

            case TermPattern constant:
                return _source.TryInternalise(constant.Term, out TermHandle handle)
                    ? PatternPosition.OfConstant(handle, constant.Term)
                    : PatternPosition.OfNever(constant.Term);
            case TripleTermPattern nested:
                return PatternPosition.OfNested(new NestedPattern(Position(nested.Subject, certain), Position(nested.Predicate, certain), Position(nested.Object, certain)));
            default:
                throw new QueryEvaluationException("Unknown pattern term " + term.GetType().Name + ".");
        }
    }

    private PathOperator CompilePath(PathPattern path)
    {
        PathEnd subject = End(path.Subject, out int s);
        PathEnd @object = End(path.Object, out int o);
        int[] certain = [.. new[] { s, o }.Where(x => x >= 0).Distinct()];
        return new PathOperator(subject, path.Path, @object) { Certain = certain };
    }

    private PathEnd End(PatternTerm term, out int slot)
    {
        switch (term)
        {
            case VariablePattern variable:
                slot = Slot(variable.Variable.Name);
                return PathEnd.OfSlot(slot);
            case BlankNodePattern blank:
                slot = Slot("_:" + blank.Label);
                return PathEnd.OfSlot(slot);
            case TermPattern constant:
                slot = -1;
                return PathEnd.OfConstant(constant.Term);
            default:
                throw new QueryEvaluationException("A path's end is a variable, a blank node or a term.");
        }
    }

    private ValuesOperator CompileValues(Values values)
    {
        int[] slots = [.. values.Variables.ToArray().Select(v => Slot(v.Name))];
        RdfTerm?[][] rows = new RdfTerm?[values.Rows.Count][];
        List<int> certain = [];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = values.Rows[i].ToArray();
        }

        for (int j = 0; j < slots.Length; j++)
        {
            if (rows.Length > 0 && rows.All(r => r[j] is not null))
            {
                certain.Add(slots[j]);
            }
        }

        return new ValuesOperator(rows, slots) { Certain = [.. certain] };
    }

    private ServiceOperator CompileService(Service service)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        CollectVariables(service.Inner, names);
        Variable[] variables = [.. names.OrderBy(n => n, StringComparer.Ordinal).Select(n => new Variable(n))];
        int[] slots = [.. variables.Select(v => Slot(v.Name))];
        return service.Name switch
        {
            VariablePattern variable => new ServiceOperator(service, null, Slot(variable.Variable.Name), variables, slots),
            TermPattern constant => new ServiceOperator(service, constant.Term, -1, variables, slots),
            _ => throw new QueryEvaluationException("SERVICE names a variable or an IRI."),
        };
    }

    private OrderByOperator CompileOrderBy(OrderBy orderBy, Operator inner, AggregateScope? scope)
    {
        Expr[] keys = new Expr[orderBy.Conditions.Count];
        bool[] descending = new bool[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = CompileExpression(orderBy.Conditions[i].Expression, scope);
            descending[i] = orderBy.Conditions[i].Descending;
        }

        return new OrderByOperator(inner, keys, descending) { Certain = inner.Certain };
    }

    // ------------------------------------------------------------- aggregates

    /// <summary>Whether a pattern is the top of a chain that ends in a <c>Group</c> (ADR 0053's extraction scope).</summary>
    private static bool ReachesGroup(QueryPattern pattern) => pattern switch
    {
        Group => true,
        Extend extend => ReachesGroup(extend.Inner),
        Filter filter => ReachesGroup(filter.Inner),
        OrderBy orderBy => ReachesGroup(orderBy.Inner),
        Join { Right: Values } join => ReachesGroup(join.Left),
        _ => false,
    };

    /// <summary>The operators between a <c>Group</c> and its <c>Project</c>, compiled bottom-up in one aggregate scope.</summary>
    private Operator CompileChain(QueryPattern pattern, AggregateScope scope)
    {
        switch (pattern)
        {
            case Group group:
                {
                    Operator inner = CompilePattern(group.Inner, null);
                    GroupKeySpec[] keys = new GroupKeySpec[group.Keys.Count];
                    for (int i = 0; i < keys.Length; i++)
                    {
                        GroupKey key = group.Keys[i];
                        Variable? variable = key.Variable ?? (key.Expression as VariableExpression)?.Variable;
                        int slot = variable is { } v ? Slot(v.Name) : Hidden("key");
                        keys[i] = new GroupKeySpec(CompileExpression(key.Expression, null), slot);
                        scope.Visible.Add(slot);
                    }

                    return new GroupOperator(inner, keys, scope.Aggregates) { Certain = [] };
                }

            case Extend extend:
                {
                    Operator inner = CompileChain(extend.Inner, scope);
                    Expr expression = CompileExpression(extend.Expression, scope);
                    int slot = Slot(extend.Variable.Name);
                    scope.Visible.Add(slot);
                    return new ExtendOperator(inner, slot, expression) { Certain = inner.Certain };
                }

            case Filter filter:
                {
                    Operator inner = CompileChain(filter.Inner, scope);
                    return new FilterOperator(CompileExpression(filter.Condition, scope), inner) { Certain = inner.Certain };
                }

            case OrderBy orderBy:
                return CompileOrderBy(orderBy, CompileChain(orderBy.Inner, scope), scope);
            case Join { Right: Values values } join:
                {
                    Operator left = CompileChain(join.Left, scope);
                    Operator right = CompileValues(values);
                    foreach (Variable variable in values.Variables)
                    {
                        scope.Visible.Add(Slot(variable.Name));
                    }

                    return new JoinOperator(left, right) { Certain = Union(left.Certain, right.Certain) };
                }

            default:
                return CompilePattern(pattern, null);
        }
    }

    /// <summary>
    /// An aggregate's hidden slot: one per distinct aggregate expression in the
    /// scope, which also makes <c>COUNT(?x)</c> written twice one accumulator.
    /// </summary>
    private int Aggregate(AggregateExpression aggregate, AggregateScope scope)
    {
        if (scope.Slots.TryGetValue(aggregate, out int slot))
        {
            return slot;
        }

        IExtensionAggregate? custom = null;
        if (aggregate.Function == AggregateFunction.Custom)
        {
            string iri = Encoding.UTF8.GetString(aggregate.CustomFunction!.Lexical);
            if (_options.Aggregates is null || !_options.Aggregates.TryGetValue(iri, out custom))
            {
                throw new QueryEvaluationException("The custom aggregate <" + iri + "> is not supplied: add it to EvaluationOptions.Aggregates.");
            }
        }

        slot = Hidden("agg");
        Expr? argument = aggregate.Argument is null ? null : CompileExpression(aggregate.Argument, null);
        scope.Aggregates.Add(new AggregateSpec(aggregate.Function, argument, aggregate.Distinct, aggregate.Separator ?? " ", custom, slot));
        scope.Slots.Add(aggregate, slot);
        return slot;
    }

    // ------------------------------------------------------------ expressions

    internal Expr CompileExpression(Expression expression, AggregateScope? scope)
    {
        switch (expression)
        {
            case VariableExpression variable:
                {
                    int slot = Slot(variable.Variable.Name);
                    if (scope is not null && !scope.Visible.Contains(slot))
                    {
                        // Above a Group, a variable that is not a key is read as SAMPLE(v) (sparql-algebra.md §4.6).
                        return new SlotExpr(Aggregate(new AggregateExpression(AggregateFunction.Sample, variable, false, null, null), scope));
                    }

                    return new SlotExpr(slot);
                }

            case ConstantExpression constant:
                return new ConstExpr(constant.Term);
            case UnaryExpression unary:
                {
                    Expr operand = CompileExpression(unary.Operand, scope);
                    return unary.Operator == UnaryOperator.Not ? new NotExpr(operand) : new SignExpr(unary.Operator == UnaryOperator.Minus, operand);
                }

            case BinaryExpression binary:
                {
                    Expr left = CompileExpression(binary.Left, scope);
                    Expr right = CompileExpression(binary.Right, scope);
                    return binary.Operator switch
                    {
                        BinaryOperator.Or => new LogicExpr(false, left, right),
                        BinaryOperator.And => new LogicExpr(true, left, right),
                        BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide => new ArithmeticExpr(binary.Operator, left, right),
                        _ => new CompareExpr(binary.Operator, left, right),
                    };
                }

            case FunctionCall call:
                {
                    int bound = -1;
                    if (call.Function == BuiltInFunction.Bound)
                    {
                        if (call.Arguments[0] is VariableExpression variable)
                        {
                            bound = Slot(variable.Variable.Name);
                            if (scope is not null && !scope.Visible.Contains(bound))
                            {
                                bound = Aggregate(new AggregateExpression(AggregateFunction.Sample, variable, false, null, null), scope);
                            }
                        }

                        return new FunctionExpr(BuiltInFunction.Bound, [], bound);
                    }

                    Expr[] arguments = new Expr[call.Arguments.Count];
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        arguments[i] = CompileExpression(call.Arguments[i], scope);
                    }

                    return new FunctionExpr(call.Function, arguments);
                }

            case CustomFunctionCall custom:
                return CompileCustom(custom, scope);
            case ExistsExpression exists:
                return new ExistsExpr(CompilePattern(exists.Pattern, null), exists.Negated);
            case AggregateExpression aggregate:
                if (scope is null)
                {
                    throw new QueryEvaluationException("An aggregate outside the scope of a GROUP BY.");
                }

                return new SlotExpr(Aggregate(aggregate, scope));
            default:
                throw new QueryEvaluationException("Unknown expression " + expression.GetType().Name + ".");
        }
    }

    private Expr CompileCustom(CustomFunctionCall call, AggregateScope? scope)
    {
        Expr[] arguments = new Expr[call.Arguments.Count];
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = CompileExpression(call.Arguments[i], scope);
        }

        ReadOnlySpan<byte> iri = call.Function.Lexical;
        XsdDatatype datatype = XsdDatatypes.FromIri(iri);
        if (datatype != XsdDatatype.None && arguments.Length == 1)
        {
            return new CastExpr(datatype, arguments[0]);
        }

        if (_options.Functions is { } functions && functions.TryGetValue(Encoding.UTF8.GetString(iri), out IExtensionFunction? function))
        {
            return new ExtensionExpr(function, arguments);
        }

        // §17.2.1: invoking a function the evaluator does not have fails, and the failure is an error.
        return ErrorExpr.Instance;
    }

    // --------------------------------------------------------------- helpers

    private static int[] Union(int[] left, int[] right) => [.. left.Union(right)];

    private static int[] Intersect(int[] left, int[] right) => [.. left.Intersect(right)];

    /// <summary>The variables a pattern mentions, for a SERVICE's columns.</summary>
    internal static void CollectVariables(QueryPattern pattern, HashSet<string> names) =>
        new VariableCollector(names).Rewrite(pattern);

    private sealed class VariableCollector(HashSet<string> names) : AlgebraRewriter
    {
        protected override PatternTerm RewriteVariablePattern(VariablePattern term)
        {
            names.Add(term.Variable.Name);
            return term;
        }

        protected override Expression RewriteVariableExpression(VariableExpression expression)
        {
            names.Add(expression.Variable.Name);
            return expression;
        }

        protected override QueryPattern RewriteExtend(Extend pattern)
        {
            names.Add(pattern.Variable.Name);
            return base.RewriteExtend(pattern);
        }

        protected override QueryPattern RewriteValues(Values pattern)
        {
            foreach (Variable variable in pattern.Variables)
            {
                names.Add(variable.Name);
            }

            return pattern;
        }
    }
}

/// <summary>
/// The operators between one <c>Group</c> and its <c>Project</c>: the
/// aggregates found there, and the variables visible above the group.
/// </summary>
internal sealed class AggregateScope
{
    internal List<AggregateSpec> Aggregates { get; } = [];

    internal Dictionary<AggregateExpression, int> Slots { get; } = [];

    internal HashSet<int> Visible { get; } = [];
}
