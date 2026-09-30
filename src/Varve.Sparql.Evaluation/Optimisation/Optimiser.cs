// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Xsd;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Compile;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Expressions;

namespace Varve.Sparql.Evaluation.Optimisation;

/// <summary>How often each rewrite of <c>sparql-evaluation.md</c> §8 fired: what keeps the equivalence property from passing vacuously (§8.6).</summary>
internal sealed class OptimiserCounts
{
    /// <summary>§8.1: conjuncts moved below the operator they were written over.</summary>
    internal int FiltersPlaced { get; set; }

    /// <summary>§8.2: <c>Bgp</c>s whose triple patterns were put in a different order.</summary>
    internal int BgpsReordered { get; set; }

    /// <summary>§8.3: join chains rebuilt in a different order.</summary>
    internal int JoinsReordered { get; set; }

    /// <summary>§8.4: expressions replaced by their value.</summary>
    internal int ConstantsFolded { get; set; }

    /// <summary>§8.5: joins removed or merged.</summary>
    internal int TrivialJoins { get; set; }
}

/// <summary>
/// The optimiser of <c>sparql-evaluation.md</c> §8 (ADR 0048): a pass from
/// <see cref="Query"/> to <see cref="Query"/> over the normalised tree that
/// changes no answer. Its rewrites run in a fixed order — constants are folded
/// first so that a folded condition can be placed, trivial joins go before
/// filters are placed so that adjacent groups merge, filters are placed before
/// joins are ordered so that a filter travels with its operand, and triple
/// patterns are ordered last.
/// </summary>
internal sealed class Optimiser
{
    private readonly IQuadSource _source;
    private readonly EvaluationOptions _options;

    internal Optimiser(IQuadSource source, EvaluationOptions options)
    {
        _source = source;
        _options = options;
    }

    internal OptimiserCounts Counts { get; } = new();

    internal static Query Optimise(Query query, IQuadSource source, EvaluationOptions options) =>
        new Optimiser(source, options).Optimise(query);

    internal Query Optimise(Query query)
    {
        Estimates estimates = new(_source);
        query = new ConstantFolder(_source, _options, query.Prologue.Base, Counts).Rewrite(query);
        query = new TrivialJoins(Counts).Rewrite(query);
        query = new FilterPlacer(Counts).Rewrite(query);
        query = new JoinOrderer(estimates, Counts).Rewrite(query);
        return new BgpOrderer(estimates, Counts).Rewrite(query);
    }

    // ------------------------------------------------------------ analysis

    /// <summary>
    /// The variables a pattern's every solution binds (§8.1's "certainly
    /// binds"); blank nodes of a <c>Bgp</c> are named <c>_:label</c>, so that the
    /// triple order of §8.2 can treat them as the variables they are.
    /// </summary>
    internal static HashSet<string> Certain(QueryPattern pattern)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        switch (pattern)
        {
            case Bgp bgp:
                foreach (TriplePattern triple in bgp.Triples)
                {
                    AddTerm(triple.Subject, names, false);
                    AddTerm(triple.Predicate, names, false);
                    AddTerm(triple.Object, names, false);
                }

                break;
            case PathPattern path:
                AddTerm(path.Subject, names, false);
                AddTerm(path.Object, names, false);
                break;
            case Join join:
                names.UnionWith(Certain(join.Left));
                names.UnionWith(Certain(join.Right));
                break;
            case LeftJoin leftJoin:
                return Certain(leftJoin.Left);
            case Minus minus:
                return Certain(minus.Left);
            case Filter filter:
                return Certain(filter.Inner);
            case Union union:
                names.UnionWith(Certain(union.Left));
                names.IntersectWith(Certain(union.Right));
                break;
            case Graph graph:
                names.UnionWith(Certain(graph.Inner));
                AddTerm(graph.Name, names, false);
                break;
            case Extend extend:
                return Certain(extend.Inner);
            case Values values:
                foreach (Variable variable in values.Variables)
                {
                    names.Add(variable.Name);
                }

                for (int i = 0; i < values.Variables.Count; i++)
                {
                    foreach (AlgebraList<RdfTerm?> row in values.Rows)
                    {
                        if (row[i] is null)
                        {
                            names.Remove(values.Variables[i].Name);
                            break;
                        }
                    }
                }

                break;
            case Group group:
                foreach (GroupKey key in group.Keys)
                {
                    if (key.Variable is { } alias)
                    {
                        names.Add(alias.Name);
                    }
                    else if (key.Expression is VariableExpression variable)
                    {
                        names.Add(variable.Variable.Name);
                    }
                }

                break;
            case Project project:
                {
                    HashSet<string> inner = Certain(project.Inner);
                    foreach (Variable variable in project.Variables)
                    {
                        if (inner.Contains(variable.Name))
                        {
                            names.Add(variable.Name);
                        }
                    }

                    break;
                }

            case OrderBy orderBy:
                return Certain(orderBy.Inner);
            case Distinct distinct:
                return Certain(distinct.Inner);
            case Reduced reduced:
                return Certain(reduced.Inner);
            case Slice slice:
                return Certain(slice.Inner);
            default:
                // Service, and anything added later: nothing is certain.
                break;
        }

        return names;
    }

    /// <summary>The variables a term of a pattern names, and with <paramref name="blankNodes"/> its blank nodes as <c>_:label</c>.</summary>
    internal static void AddTerm(PatternTerm term, HashSet<string> names, bool blankNodes)
    {
        switch (term)
        {
            case VariablePattern variable:
                names.Add(variable.Variable.Name);
                break;
            case BlankNodePattern blank when blankNodes:
                names.Add("_:" + blank.Label);
                break;
            case TripleTermPattern triple:
                AddTerm(triple.Subject, names, blankNodes);
                AddTerm(triple.Predicate, names, blankNodes);
                AddTerm(triple.Object, names, blankNodes);
                break;
            default:
                break;
        }
    }

    /// <summary>The variables an expression reads, outside any <c>EXISTS</c>.</summary>
    internal static void Read(Expression expression, HashSet<string> names)
    {
        switch (expression)
        {
            case VariableExpression variable:
                names.Add(variable.Variable.Name);
                break;
            case UnaryExpression unary:
                Read(unary.Operand, names);
                break;
            case BinaryExpression binary:
                Read(binary.Left, names);
                Read(binary.Right, names);
                break;
            case FunctionCall call:
                foreach (Expression argument in call.Arguments)
                {
                    Read(argument, names);
                }

                break;
            case CustomFunctionCall custom:
                foreach (Expression argument in custom.Arguments)
                {
                    Read(argument, names);
                }

                break;
            case AggregateExpression aggregate when aggregate.Argument is not null:
                Read(aggregate.Argument, names);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Whether an expression's value depends on nothing but the variables it
    /// reads, each of which it needs bound: no <c>EXISTS</c>, no aggregate, no
    /// <c>BOUND</c>, <c>COALESCE</c> or <c>IF</c>, nothing non-deterministic,
    /// and no extension function (§8.1, §8.4).
    /// </summary>
    internal static bool IsPure(Expression expression)
    {
        switch (expression)
        {
            case VariableExpression or ConstantExpression:
                return true;
            case UnaryExpression unary:
                return IsPure(unary.Operand);
            case BinaryExpression binary:
                return IsPure(binary.Left) && IsPure(binary.Right);
            case FunctionCall call:
                if (call.Function is BuiltInFunction.Bound or BuiltInFunction.Coalesce or BuiltInFunction.If
                    or BuiltInFunction.Rand or BuiltInFunction.Now or BuiltInFunction.Uuid or BuiltInFunction.StrUuid
                    or BuiltInFunction.BNode)
                {
                    return false;
                }

                foreach (Expression argument in call.Arguments)
                {
                    if (!IsPure(argument))
                    {
                        return false;
                    }
                }

                return true;
            case CustomFunctionCall custom:
                if (!IsCast(custom))
                {
                    return false;
                }

                return IsPure(custom.Arguments[0]);
            default:
                // EXISTS, aggregates.
                return false;
        }
    }

    /// <summary>An XSD constructor call, which the compiler takes before any extension function of the same IRI.</summary>
    internal static bool IsCast(CustomFunctionCall call) =>
        call.Arguments.Count == 1 && XsdDatatypes.FromIri(call.Function.Lexical) != XsdDatatype.None;

    // ------------------------------------------------------------ §8.4

    /// <summary>
    /// §8.4: an expression whose operands are all constants, and which is pure,
    /// is evaluated once with the evaluator's own code and replaced by its
    /// value's term — only when it evaluates without error.
    /// </summary>
    private sealed class ConstantFolder(IQuadSource source, EvaluationOptions options, RdfTerm? baseIri, OptimiserCounts counts) : AlgebraRewriter
    {
        private Exec? _exec;

        protected override Expression RewriteUnaryExpression(UnaryExpression expression) => Fold(base.RewriteUnaryExpression(expression));

        protected override Expression RewriteBinaryExpression(BinaryExpression expression) => Fold(base.RewriteBinaryExpression(expression));

        protected override Expression RewriteFunctionCall(FunctionCall expression) => Fold(base.RewriteFunctionCall(expression));

        protected override Expression RewriteCustomFunctionCall(CustomFunctionCall expression) => Fold(base.RewriteCustomFunctionCall(expression));

        private Expression Fold(Expression expression)
        {
            if (!IsPure(expression) || !OperandsAreConstants(expression))
            {
                return expression;
            }

            _exec ??= new Exec(source, options, 0, CancellationToken.None) { BaseIri = baseIri };
            Expr compiled = new Compiler(source, options).CompileExpression(expression, null);
            Value value = compiled.Eval(_exec, _exec.NewRow(), ActiveGraph.Default);
            if (Semantics.AsTerm(_exec, value) is not { } term)
            {
                return expression;
            }

            counts.ConstantsFolded++;
            return new ConstantExpression(term) { Span = expression.Span };
        }

        private static bool OperandsAreConstants(Expression expression) => expression switch
        {
            UnaryExpression unary => unary.Operand is ConstantExpression,
            BinaryExpression binary => binary.Left is ConstantExpression && binary.Right is ConstantExpression,
            FunctionCall call => AllConstants(call.Arguments),
            CustomFunctionCall custom => AllConstants(custom.Arguments),
            _ => false,
        };

        private static bool AllConstants(AlgebraList<Expression> arguments)
        {
            foreach (Expression argument in arguments)
            {
                if (argument is not ConstantExpression)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // ------------------------------------------------------------ §8.5

    /// <summary>
    /// §8.5: a join with the empty group, or with a <c>VALUES</c> of one row
    /// that binds nothing, is its other operand — both are the join's identity,
    /// the one solution that binds nothing; and two <c>Bgp</c>s joined are one
    /// (§18.3: a basic graph pattern is a set of triple patterns, and the
    /// grammar keeps blank node labels from being shared between two).
    /// </summary>
    private sealed class TrivialJoins(OptimiserCounts counts) : AlgebraRewriter
    {
        protected override QueryPattern RewriteJoin(Join pattern)
        {
            QueryPattern rewritten = base.RewriteJoin(pattern);
            if (rewritten is not Join join)
            {
                return rewritten;
            }

            if (IsIdentity(join.Left))
            {
                counts.TrivialJoins++;
                return join.Right;
            }

            if (IsIdentity(join.Right))
            {
                counts.TrivialJoins++;
                return join.Left;
            }

            if (join.Left is Bgp left && join.Right is Bgp right)
            {
                counts.TrivialJoins++;
                TriplePattern[] triples = [.. left.Triples.ToArray(), .. right.Triples.ToArray()];
                return new Bgp(AlgebraList.From(triples)) { Span = join.Span };
            }

            return join;
        }

        private static bool IsIdentity(QueryPattern pattern)
        {
            switch (pattern)
            {
                case Bgp bgp:
                    return bgp.Triples.Count == 0;
                case Values values when values.Rows.Count == 1:
                    foreach (RdfTerm? term in values.Rows[0])
                    {
                        if (term is not null)
                        {
                            return false;
                        }
                    }

                    return true;
                default:
                    return false;
            }
        }
    }

    // ------------------------------------------------------------ §8.1

    /// <summary>
    /// §8.1: each pure conjunct of a filter moves down as far as it soundly
    /// can. Through <c>Union</c>, <c>Extend</c> (of a variable it does not read)
    /// and <c>Filter</c> it always can — each passes every solution of its
    /// input through, one to one or not at all, without changing what the
    /// conjunct reads. Into an operand of <c>Join</c>, the left of
    /// <c>LeftJoin</c> and <c>Minus</c>, and the pattern of <c>Graph</c>, only
    /// when that operand certainly binds every variable the conjunct reads:
    /// then each output solution agrees with the operand's solution it came
    /// from on those variables.
    /// </summary>
    private sealed class FilterPlacer(OptimiserCounts counts) : AlgebraRewriter
    {
        protected override QueryPattern RewriteFilter(Filter pattern)
        {
            QueryPattern inner = Rewrite(pattern.Inner);
            Expression condition = Rewrite(pattern.Condition);
            List<Expression> kept = [];
            foreach (Expression conjunct in Conjuncts(condition))
            {
                if (IsPure(conjunct))
                {
                    HashSet<string> reads = new(StringComparer.Ordinal);
                    Read(conjunct, reads);
                    if (TryPush(conjunct, reads, ref inner))
                    {
                        counts.FiltersPlaced++;
                        continue;
                    }
                }

                kept.Add(conjunct);
            }

            if (kept.Count == 0)
            {
                return inner;
            }

            Expression remaining = kept[0];
            for (int i = 1; i < kept.Count; i++)
            {
                remaining = new BinaryExpression(BinaryOperator.And, remaining, kept[i]) { Span = pattern.Condition.Span };
            }

            return ReferenceEquals(inner, pattern.Inner) && ReferenceEquals(remaining, pattern.Condition)
                ? pattern
                : new Filter(remaining, inner) { Span = pattern.Span };
        }

        private static IEnumerable<Expression> Conjuncts(Expression expression)
        {
            if (expression is BinaryExpression { Operator: BinaryOperator.And } and)
            {
                foreach (Expression left in Conjuncts(and.Left))
                {
                    yield return left;
                }

                foreach (Expression right in Conjuncts(and.Right))
                {
                    yield return right;
                }
            }
            else
            {
                yield return expression;
            }
        }

        /// <summary>Places the conjunct strictly below <paramref name="pattern"/>, or reports that it cannot.</summary>
        private static bool TryPush(Expression conjunct, HashSet<string> reads, ref QueryPattern pattern)
        {
            switch (pattern)
            {
                case Join join when Certain(join.Left).IsSupersetOf(reads):
                    pattern = join with { Left = Place(conjunct, reads, join.Left) };
                    return true;
                case Join join when Certain(join.Right).IsSupersetOf(reads):
                    pattern = join with { Right = Place(conjunct, reads, join.Right) };
                    return true;
                case LeftJoin leftJoin when Certain(leftJoin.Left).IsSupersetOf(reads):
                    pattern = leftJoin with { Left = Place(conjunct, reads, leftJoin.Left) };
                    return true;
                case Minus minus when Certain(minus.Left).IsSupersetOf(reads):
                    pattern = minus with { Left = Place(conjunct, reads, minus.Left) };
                    return true;
                case Graph graph when Certain(graph.Inner).IsSupersetOf(reads):
                    pattern = graph with { Inner = Place(conjunct, reads, graph.Inner) };
                    return true;
                case Union union:
                    pattern = union with { Left = Place(conjunct, reads, union.Left), Right = Place(conjunct, reads, union.Right) };
                    return true;
                case Extend extend when !reads.Contains(extend.Variable.Name):
                    pattern = extend with { Inner = Place(conjunct, reads, extend.Inner) };
                    return true;
                case Filter filter:
                    {
                        QueryPattern inner = filter.Inner;
                        if (!TryPush(conjunct, reads, ref inner))
                        {
                            return false;
                        }

                        pattern = filter with { Inner = inner };
                        return true;
                    }

                default:
                    return false;
            }
        }

        private static QueryPattern Place(Expression conjunct, HashSet<string> reads, QueryPattern pattern) =>
            TryPush(conjunct, reads, ref pattern) ? pattern : new Filter(conjunct, pattern) { Span = conjunct.Span };
    }

    // ------------------------------------------------------------ estimates

    /// <summary>The source's estimates (ADR 0049), of triple patterns given their constants, each asked once.</summary>
    private sealed class Estimates(IQuadSource source)
    {
        /// <summary>What an unknown estimate sorts as: after every known one.</summary>
        internal const long Large = long.MaxValue / 4;

        private readonly Dictionary<(TriplePattern, bool), long> _cache = [];

        internal long Of(TriplePattern triple, bool named)
        {
            if (_cache.TryGetValue((triple, named), out long cached))
            {
                return cached;
            }

            long estimate;
            if (!Handle(triple.Subject, out TermHandle subject) || !Handle(triple.Predicate, out TermHandle predicate) || !Handle(triple.Object, out TermHandle @object))
            {
                // A constant the source has never seen: the pattern matches nothing.
                estimate = 0;
            }
            else
            {
                CardinalityEstimate reported = source.Estimate(subject, predicate, @object, named ? GraphPattern.AnyNamed : GraphPattern.DefaultGraph);
                estimate = reported.IsUnknown ? Large : reported.Count.Value;
            }

            _cache[(triple, named)] = estimate;
            return estimate;
        }

        /// <summary>A pattern's handle: a constant's, or the wildcard for a variable, a blank node or a triple term pattern.</summary>
        private bool Handle(PatternTerm term, out TermHandle handle)
        {
            handle = TermHandle.None;
            return term is not TermPattern constant || source.TryInternalise(constant.Term, out handle);
        }

        /// <summary>§8.3: an operand's estimate is its smallest pattern's.</summary>
        internal long Of(QueryPattern pattern, bool named)
        {
            switch (pattern)
            {
                case Bgp bgp:
                    {
                        long smallest = bgp.Triples.Count == 0 ? 1 : Large;
                        foreach (TriplePattern triple in bgp.Triples)
                        {
                            smallest = Math.Min(smallest, Of(triple, named));
                        }

                        return smallest;
                    }

                case Values values:
                    return values.Rows.Count;
                case Graph graph:
                    return Of(graph.Inner, true);
                case Filter filter:
                    return Of(filter.Inner, named);
                default:
                    // A closure or a negated property set: nothing to go on.
                    return Large;
            }
        }
    }

    // ------------------------------------------------------------ §8.3

    /// <summary>
    /// §8.3: a chain of joins whose every operand is a <c>Bgp</c>, a path, a
    /// <c>Values</c>, or a <c>Graph</c> or <c>Filter</c> over one of those, is
    /// rebuilt left-deep, greedily: first the operand with the smallest
    /// estimate, then each time the smallest of those sharing a certain
    /// variable with the ones placed, else the smallest; ties keep the written
    /// order. Join is associative and commutative over multisets (§18.5), so
    /// the answer is the same; its order is not, which §15.1 leaves open
    /// without an <c>ORDER BY</c>.
    /// </summary>
    private sealed class JoinOrderer(Estimates estimates, OptimiserCounts counts) : AlgebraRewriter
    {
        private bool _named;

        protected override QueryPattern RewriteGraph(Graph pattern)
        {
            bool outer = _named;
            _named = true;
            try
            {
                return base.RewriteGraph(pattern);
            }
            finally
            {
                _named = outer;
            }
        }

        protected override QueryPattern RewriteJoin(Join pattern)
        {
            QueryPattern rewritten = base.RewriteJoin(pattern);
            if (rewritten is not Join join)
            {
                return rewritten;
            }

            List<QueryPattern> operands = [];
            Flatten(join, operands);
            foreach (QueryPattern operand in operands)
            {
                if (!Reorderable(operand))
                {
                    return join;
                }
            }

            List<int> remaining = [];
            for (int i = 0; i < operands.Count; i++)
            {
                remaining.Add(i);
            }

            long[] cost = new long[operands.Count];
            HashSet<string>[] certain = new HashSet<string>[operands.Count];
            for (int i = 0; i < operands.Count; i++)
            {
                cost[i] = estimates.Of(operands[i], _named);
                certain[i] = Certain(operands[i]);
            }

            HashSet<string> bound = new(StringComparer.Ordinal);
            List<int> order = [];
            while (remaining.Count > 0)
            {
                int best = -1;
                bool bestShares = false;
                foreach (int candidate in remaining)
                {
                    bool shares = bound.Overlaps(certain[candidate]);
                    if (best < 0 || (shares && !bestShares) || (shares == bestShares && cost[candidate] < cost[best]))
                    {
                        best = candidate;
                        bestShares = shares;
                    }
                }

                order.Add(best);
                remaining.Remove(best);
                bound.UnionWith(certain[best]);
            }

            bool changed = false;
            for (int i = 0; i < order.Count; i++)
            {
                changed |= order[i] != i;
            }

            if (!changed)
            {
                return join;
            }

            counts.JoinsReordered++;
            QueryPattern result = operands[order[0]];
            for (int i = 1; i < order.Count; i++)
            {
                result = new Join(result, operands[order[i]]) { Span = join.Span };
            }

            return result;
        }

        private static void Flatten(QueryPattern pattern, List<QueryPattern> operands)
        {
            if (pattern is Join join)
            {
                Flatten(join.Left, operands);
                Flatten(join.Right, operands);
            }
            else
            {
                operands.Add(pattern);
            }
        }

        private static bool Reorderable(QueryPattern pattern) => pattern switch
        {
            Bgp or PathPattern or Values => true,
            Graph graph => Reorderable(graph.Inner),
            Filter filter => Reorderable(filter.Inner),
            _ => false,
        };
    }

    // ------------------------------------------------------------ §8.2

    /// <summary>
    /// §8.2: a <c>Bgp</c>'s triple patterns in greedy order — first the one
    /// with the smallest estimate given its constants, then each time the
    /// smallest of those sharing a variable or blank node with the ones placed,
    /// else the smallest; an unknown estimate sorts as large, and ties keep the
    /// written order. A <c>Bgp</c>'s solutions are a set of matches (§18.3),
    /// the same in any order.
    /// </summary>
    private sealed class BgpOrderer(Estimates estimates, OptimiserCounts counts) : AlgebraRewriter
    {
        private bool _named;

        protected override QueryPattern RewriteGraph(Graph pattern)
        {
            bool outer = _named;
            _named = true;
            try
            {
                return base.RewriteGraph(pattern);
            }
            finally
            {
                _named = outer;
            }
        }

        protected override QueryPattern RewriteBgp(Bgp pattern)
        {
            int count = pattern.Triples.Count;
            if (count < 2)
            {
                return pattern;
            }

            long[] cost = new long[count];
            HashSet<string>[] names = new HashSet<string>[count];
            List<int> remaining = [];
            for (int i = 0; i < count; i++)
            {
                TriplePattern triple = pattern.Triples[i];
                cost[i] = estimates.Of(triple, _named);
                names[i] = new HashSet<string>(StringComparer.Ordinal);
                AddTerm(triple.Subject, names[i], true);
                AddTerm(triple.Predicate, names[i], true);
                AddTerm(triple.Object, names[i], true);
                remaining.Add(i);
            }

            HashSet<string> bound = new(StringComparer.Ordinal);
            TriplePattern[] ordered = new TriplePattern[count];
            bool changed = false;
            for (int position = 0; position < count; position++)
            {
                int best = -1;
                bool bestShares = false;
                foreach (int candidate in remaining)
                {
                    bool shares = bound.Overlaps(names[candidate]);
                    if (best < 0 || (shares && !bestShares) || (shares == bestShares && cost[candidate] < cost[best]))
                    {
                        best = candidate;
                        bestShares = shares;
                    }
                }

                ordered[position] = pattern.Triples[best];
                changed |= best != position;
                remaining.Remove(best);
                bound.UnionWith(names[best]);
            }

            if (!changed)
            {
                return pattern;
            }

            counts.BgpsReordered++;
            return new Bgp(AlgebraList.From(ordered)) { Span = pattern.Span };
        }
    }
}
