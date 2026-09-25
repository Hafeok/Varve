// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Algebra;

/// <summary>
/// Rewrites an algebra tree, one virtual method per node type
/// (<c>docs/spec/sparql-algebra.md</c> §7).
/// </summary>
/// <remarks>
/// <para>
/// The base implementation walks the tree and rebuilds a node only when one of
/// its children changed, returning the same instance otherwise, so that an
/// unchanged subtree is shared and a rewriter that touches nothing allocates
/// nothing. Override the method for a node type to replace it; call the base
/// to keep walking into its children.
/// </para>
/// <para>
/// Dispatch is a type switch over sealed hierarchies: no reflection, no
/// visitor interface, ordinary code under Native AOT. A new node type is a
/// compile error here until its method exists, which is the point.
/// </para>
/// </remarks>
public abstract class AlgebraRewriter
{
    /// <summary>Rewrites a query.</summary>
    public virtual Query Rewrite(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query switch
        {
            SelectQuery select => RewriteSelectQuery(select),
            ConstructQuery construct => RewriteConstructQuery(construct),
            AskQuery ask => RewriteAskQuery(ask),
            DescribeQuery describe => RewriteDescribeQuery(describe),
            _ => throw Unknown(query),
        };
    }

    /// <summary>Rewrites an update request.</summary>
    public virtual Update Rewrite(Update update)
    {
        ArgumentNullException.ThrowIfNull(update);
        AlgebraList<UpdateOperation> operations = RewriteOperations(update.Operations);
        return operations == update.Operations ? update : update with { Operations = operations };
    }

    /// <summary>Rewrites one update operation.</summary>
    public virtual UpdateOperation Rewrite(UpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation switch
        {
            InsertData insertData => RewriteInsertData(insertData),
            DeleteData deleteData => RewriteDeleteData(deleteData),
            DeleteWhere deleteWhere => RewriteDeleteWhere(deleteWhere),
            Modify modify => RewriteModify(modify),
            Load load => RewriteLoad(load),
            Clear clear => RewriteClear(clear),
            Drop drop => RewriteDrop(drop),
            Create create => RewriteCreate(create),
            Add add => RewriteAdd(add),
            Move move => RewriteMove(move),
            Copy copy => RewriteCopy(copy),
            _ => throw Unknown(operation),
        };
    }

    /// <summary>Rewrites a graph pattern.</summary>
    public virtual QueryPattern Rewrite(QueryPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return pattern switch
        {
            Bgp bgp => RewriteBgp(bgp),
            PathPattern path => RewritePathPattern(path),
            Join join => RewriteJoin(join),
            LeftJoin leftJoin => RewriteLeftJoin(leftJoin),
            Filter filter => RewriteFilter(filter),
            Union union => RewriteUnion(union),
            Graph graph => RewriteGraph(graph),
            Extend extend => RewriteExtend(extend),
            Minus minus => RewriteMinus(minus),
            Values values => RewriteValues(values),
            Service service => RewriteService(service),
            Group group => RewriteGroup(group),
            OrderBy orderBy => RewriteOrderBy(orderBy),
            Project project => RewriteProject(project),
            Distinct distinct => RewriteDistinct(distinct),
            Reduced reduced => RewriteReduced(reduced),
            Slice slice => RewriteSlice(slice),
            _ => throw Unknown(pattern),
        };
    }

    /// <summary>Rewrites a property path.</summary>
    public virtual PropertyPath Rewrite(PropertyPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path switch
        {
            PredicatePath predicate => RewritePredicatePath(predicate),
            InversePath inverse => RewriteInversePath(inverse),
            SequencePath sequence => RewriteSequencePath(sequence),
            AlternativePath alternative => RewriteAlternativePath(alternative),
            ZeroOrMorePath zeroOrMore => RewriteZeroOrMorePath(zeroOrMore),
            OneOrMorePath oneOrMore => RewriteOneOrMorePath(oneOrMore),
            ZeroOrOnePath zeroOrOne => RewriteZeroOrOnePath(zeroOrOne),
            NegatedPropertySet negated => RewriteNegatedPropertySet(negated),
            _ => throw Unknown(path),
        };
    }

    /// <summary>Rewrites an expression.</summary>
    public virtual Expression Rewrite(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression switch
        {
            VariableExpression variable => RewriteVariableExpression(variable),
            ConstantExpression constant => RewriteConstantExpression(constant),
            UnaryExpression unary => RewriteUnaryExpression(unary),
            BinaryExpression binary => RewriteBinaryExpression(binary),
            FunctionCall call => RewriteFunctionCall(call),
            CustomFunctionCall custom => RewriteCustomFunctionCall(custom),
            ExistsExpression exists => RewriteExistsExpression(exists),
            AggregateExpression aggregate => RewriteAggregateExpression(aggregate),
            _ => throw Unknown(expression),
        };
    }

    /// <summary>Rewrites a term in a pattern.</summary>
    public virtual PatternTerm Rewrite(PatternTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return term switch
        {
            VariablePattern variable => RewriteVariablePattern(variable),
            TermPattern constant => RewriteTermPattern(constant),
            BlankNodePattern blank => RewriteBlankNodePattern(blank),
            TripleTermPattern triple => RewriteTripleTermPattern(triple),
            _ => throw Unknown(term),
        };
    }

    /// <summary>Rewrites a triple pattern.</summary>
    public virtual TriplePattern Rewrite(TriplePattern triple)
    {
        ArgumentNullException.ThrowIfNull(triple);
        PatternTerm subject = Rewrite(triple.Subject);
        PatternTerm predicate = Rewrite(triple.Predicate);
        PatternTerm @object = Rewrite(triple.Object);

        return ReferenceEquals(subject, triple.Subject) && ReferenceEquals(predicate, triple.Predicate) && ReferenceEquals(@object, triple.Object)
            ? triple
            : triple with { Subject = subject, Predicate = predicate, Object = @object };
    }

    /// <summary>Rewrites a quad pattern.</summary>
    public virtual QuadPattern Rewrite(QuadPattern quad)
    {
        ArgumentNullException.ThrowIfNull(quad);
        PatternTerm subject = Rewrite(quad.Subject);
        PatternTerm predicate = Rewrite(quad.Predicate);
        PatternTerm @object = Rewrite(quad.Object);
        PatternTerm? graph = quad.Graph is null ? null : Rewrite(quad.Graph);

        return ReferenceEquals(subject, quad.Subject) && ReferenceEquals(predicate, quad.Predicate)
            && ReferenceEquals(@object, quad.Object) && ReferenceEquals(graph, quad.Graph)
            ? quad
            : quad with { Subject = subject, Predicate = predicate, Object = @object, Graph = graph };
    }

    // --- queries -----------------------------------------------------------

    /// <summary>Rewrites a <c>SELECT</c> query.</summary>
    protected virtual Query RewriteSelectQuery(SelectQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        QueryPattern pattern = Rewrite(query.Pattern);
        return ReferenceEquals(pattern, query.Pattern) ? query : query with { Pattern = pattern };
    }

    /// <summary>Rewrites a <c>CONSTRUCT</c> query.</summary>
    protected virtual Query RewriteConstructQuery(ConstructQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        AlgebraList<TriplePattern> template = RewriteTriples(query.Template);
        QueryPattern pattern = Rewrite(query.Pattern);

        return template == query.Template && ReferenceEquals(pattern, query.Pattern)
            ? query
            : query with { Template = template, Pattern = pattern };
    }

    /// <summary>Rewrites an <c>ASK</c> query.</summary>
    protected virtual Query RewriteAskQuery(AskQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        QueryPattern pattern = Rewrite(query.Pattern);
        return ReferenceEquals(pattern, query.Pattern) ? query : query with { Pattern = pattern };
    }

    /// <summary>Rewrites a <c>DESCRIBE</c> query.</summary>
    protected virtual Query RewriteDescribeQuery(DescribeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        AlgebraList<PatternTerm> resources = RewriteTerms(query.Resources);
        QueryPattern pattern = Rewrite(query.Pattern);

        return resources == query.Resources && ReferenceEquals(pattern, query.Pattern)
            ? query
            : query with { Resources = resources, Pattern = pattern };
    }

    // --- update operations -------------------------------------------------

    /// <summary>Rewrites <c>INSERT DATA</c>.</summary>
    protected virtual UpdateOperation RewriteInsertData(InsertData operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        AlgebraList<QuadPattern> quads = RewriteQuads(operation.Quads);
        return quads == operation.Quads ? operation : operation with { Quads = quads };
    }

    /// <summary>Rewrites <c>DELETE DATA</c>.</summary>
    protected virtual UpdateOperation RewriteDeleteData(DeleteData operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        AlgebraList<QuadPattern> quads = RewriteQuads(operation.Quads);
        return quads == operation.Quads ? operation : operation with { Quads = quads };
    }

    /// <summary>Rewrites <c>DELETE WHERE</c>.</summary>
    protected virtual UpdateOperation RewriteDeleteWhere(DeleteWhere operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        AlgebraList<QuadPattern> quads = RewriteQuads(operation.Quads);
        return quads == operation.Quads ? operation : operation with { Quads = quads };
    }

    /// <summary>Rewrites <c>DELETE … INSERT … WHERE</c>.</summary>
    protected virtual UpdateOperation RewriteModify(Modify operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        AlgebraList<QuadPattern> delete = RewriteQuads(operation.Delete);
        AlgebraList<QuadPattern> insert = RewriteQuads(operation.Insert);
        QueryPattern where = Rewrite(operation.Where);

        return delete == operation.Delete && insert == operation.Insert && ReferenceEquals(where, operation.Where)
            ? operation
            : operation with { Delete = delete, Insert = insert, Where = where };
    }

    /// <summary>Rewrites <c>LOAD</c>.</summary>
    protected virtual UpdateOperation RewriteLoad(Load operation) => operation;

    /// <summary>Rewrites <c>CLEAR</c>.</summary>
    protected virtual UpdateOperation RewriteClear(Clear operation) => operation;

    /// <summary>Rewrites <c>DROP</c>.</summary>
    protected virtual UpdateOperation RewriteDrop(Drop operation) => operation;

    /// <summary>Rewrites <c>CREATE</c>.</summary>
    protected virtual UpdateOperation RewriteCreate(Create operation) => operation;

    /// <summary>Rewrites <c>ADD</c>.</summary>
    protected virtual UpdateOperation RewriteAdd(Add operation) => operation;

    /// <summary>Rewrites <c>MOVE</c>.</summary>
    protected virtual UpdateOperation RewriteMove(Move operation) => operation;

    /// <summary>Rewrites <c>COPY</c>.</summary>
    protected virtual UpdateOperation RewriteCopy(Copy operation) => operation;

    // --- graph patterns ----------------------------------------------------

    /// <summary>Rewrites a basic graph pattern.</summary>
    protected virtual QueryPattern RewriteBgp(Bgp pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        AlgebraList<TriplePattern> triples = RewriteTriples(pattern.Triples);
        return triples == pattern.Triples ? pattern : pattern with { Triples = triples };
    }

    /// <summary>Rewrites a property path pattern.</summary>
    protected virtual QueryPattern RewritePathPattern(PathPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        PatternTerm subject = Rewrite(pattern.Subject);
        PropertyPath path = Rewrite(pattern.Path);
        PatternTerm @object = Rewrite(pattern.Object);

        return ReferenceEquals(subject, pattern.Subject) && ReferenceEquals(path, pattern.Path) && ReferenceEquals(@object, pattern.Object)
            ? pattern
            : pattern with { Subject = subject, Path = path, Object = @object };
    }

    /// <summary>Rewrites a join.</summary>
    protected virtual QueryPattern RewriteJoin(Join pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern left = Rewrite(pattern.Left);
        QueryPattern right = Rewrite(pattern.Right);
        return ReferenceEquals(left, pattern.Left) && ReferenceEquals(right, pattern.Right) ? pattern : pattern with { Left = left, Right = right };
    }

    /// <summary>Rewrites a left join.</summary>
    protected virtual QueryPattern RewriteLeftJoin(LeftJoin pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern left = Rewrite(pattern.Left);
        QueryPattern right = Rewrite(pattern.Right);
        Expression? condition = pattern.Condition is null ? null : Rewrite(pattern.Condition);

        return ReferenceEquals(left, pattern.Left) && ReferenceEquals(right, pattern.Right) && ReferenceEquals(condition, pattern.Condition)
            ? pattern
            : pattern with { Left = left, Right = right, Condition = condition };
    }

    /// <summary>Rewrites a filter.</summary>
    protected virtual QueryPattern RewriteFilter(Filter pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Expression condition = Rewrite(pattern.Condition);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(condition, pattern.Condition) && ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Condition = condition, Inner = inner };
    }

    /// <summary>Rewrites a union.</summary>
    protected virtual QueryPattern RewriteUnion(Union pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern left = Rewrite(pattern.Left);
        QueryPattern right = Rewrite(pattern.Right);
        return ReferenceEquals(left, pattern.Left) && ReferenceEquals(right, pattern.Right) ? pattern : pattern with { Left = left, Right = right };
    }

    /// <summary>Rewrites a <c>GRAPH</c> pattern.</summary>
    protected virtual QueryPattern RewriteGraph(Graph pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        PatternTerm name = Rewrite(pattern.Name);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(name, pattern.Name) && ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Name = name, Inner = inner };
    }

    /// <summary>Rewrites an extend.</summary>
    protected virtual QueryPattern RewriteExtend(Extend pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        Expression expression = Rewrite(pattern.Expression);
        return ReferenceEquals(inner, pattern.Inner) && ReferenceEquals(expression, pattern.Expression) ? pattern : pattern with { Inner = inner, Expression = expression };
    }

    /// <summary>Rewrites a minus.</summary>
    protected virtual QueryPattern RewriteMinus(Minus pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern left = Rewrite(pattern.Left);
        QueryPattern right = Rewrite(pattern.Right);
        return ReferenceEquals(left, pattern.Left) && ReferenceEquals(right, pattern.Right) ? pattern : pattern with { Left = left, Right = right };
    }

    /// <summary>Rewrites inline data.</summary>
    protected virtual QueryPattern RewriteValues(Values pattern) => pattern;

    /// <summary>Rewrites a <c>SERVICE</c> pattern.</summary>
    protected virtual QueryPattern RewriteService(Service pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        PatternTerm name = Rewrite(pattern.Name);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(name, pattern.Name) && ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Name = name, Inner = inner };
    }

    /// <summary>Rewrites a group.</summary>
    protected virtual QueryPattern RewriteGroup(Group pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        AlgebraList<GroupKey> keys = RewriteKeys(pattern.Keys);
        return ReferenceEquals(inner, pattern.Inner) && keys == pattern.Keys ? pattern : pattern with { Inner = inner, Keys = keys };
    }

    /// <summary>Rewrites an order by.</summary>
    protected virtual QueryPattern RewriteOrderBy(OrderBy pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        AlgebraList<OrderCondition> conditions = RewriteConditions(pattern.Conditions);
        return ReferenceEquals(inner, pattern.Inner) && conditions == pattern.Conditions ? pattern : pattern with { Inner = inner, Conditions = conditions };
    }

    /// <summary>Rewrites a projection.</summary>
    protected virtual QueryPattern RewriteProject(Project pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Inner = inner };
    }

    /// <summary>Rewrites a distinct.</summary>
    protected virtual QueryPattern RewriteDistinct(Distinct pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Inner = inner };
    }

    /// <summary>Rewrites a reduced.</summary>
    protected virtual QueryPattern RewriteReduced(Reduced pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Inner = inner };
    }

    /// <summary>Rewrites a slice.</summary>
    protected virtual QueryPattern RewriteSlice(Slice pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        QueryPattern inner = Rewrite(pattern.Inner);
        return ReferenceEquals(inner, pattern.Inner) ? pattern : pattern with { Inner = inner };
    }

    // --- property paths ----------------------------------------------------

    /// <summary>Rewrites a predicate path.</summary>
    protected virtual PropertyPath RewritePredicatePath(PredicatePath path) => path;

    /// <summary>Rewrites an inverse path.</summary>
    protected virtual PropertyPath RewriteInversePath(InversePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PropertyPath inner = Rewrite(path.Inner);
        return ReferenceEquals(inner, path.Inner) ? path : path with { Inner = inner };
    }

    /// <summary>Rewrites a sequence path.</summary>
    protected virtual PropertyPath RewriteSequencePath(SequencePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PropertyPath left = Rewrite(path.Left);
        PropertyPath right = Rewrite(path.Right);
        return ReferenceEquals(left, path.Left) && ReferenceEquals(right, path.Right) ? path : path with { Left = left, Right = right };
    }

    /// <summary>Rewrites an alternative path.</summary>
    protected virtual PropertyPath RewriteAlternativePath(AlternativePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PropertyPath left = Rewrite(path.Left);
        PropertyPath right = Rewrite(path.Right);
        return ReferenceEquals(left, path.Left) && ReferenceEquals(right, path.Right) ? path : path with { Left = left, Right = right };
    }

    /// <summary>Rewrites a zero-or-more path.</summary>
    protected virtual PropertyPath RewriteZeroOrMorePath(ZeroOrMorePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PropertyPath inner = Rewrite(path.Inner);
        return ReferenceEquals(inner, path.Inner) ? path : path with { Inner = inner };
    }

    /// <summary>Rewrites a one-or-more path.</summary>
    protected virtual PropertyPath RewriteOneOrMorePath(OneOrMorePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PropertyPath inner = Rewrite(path.Inner);
        return ReferenceEquals(inner, path.Inner) ? path : path with { Inner = inner };
    }

    /// <summary>Rewrites a zero-or-one path.</summary>
    protected virtual PropertyPath RewriteZeroOrOnePath(ZeroOrOnePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PropertyPath inner = Rewrite(path.Inner);
        return ReferenceEquals(inner, path.Inner) ? path : path with { Inner = inner };
    }

    /// <summary>Rewrites a negated property set.</summary>
    protected virtual PropertyPath RewriteNegatedPropertySet(NegatedPropertySet path) => path;

    // --- expressions -------------------------------------------------------

    /// <summary>Rewrites a variable.</summary>
    protected virtual Expression RewriteVariableExpression(VariableExpression expression) => expression;

    /// <summary>Rewrites a constant.</summary>
    protected virtual Expression RewriteConstantExpression(ConstantExpression expression) => expression;

    /// <summary>Rewrites a unary expression.</summary>
    protected virtual Expression RewriteUnaryExpression(UnaryExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expression operand = Rewrite(expression.Operand);
        return ReferenceEquals(operand, expression.Operand) ? expression : expression with { Operand = operand };
    }

    /// <summary>Rewrites a binary expression.</summary>
    protected virtual Expression RewriteBinaryExpression(BinaryExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expression left = Rewrite(expression.Left);
        Expression right = Rewrite(expression.Right);
        return ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right) ? expression : expression with { Left = left, Right = right };
    }

    /// <summary>Rewrites a built-in function call.</summary>
    protected virtual Expression RewriteFunctionCall(FunctionCall expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        AlgebraList<Expression> arguments = RewriteExpressions(expression.Arguments);
        return arguments == expression.Arguments ? expression : expression with { Arguments = arguments };
    }

    /// <summary>Rewrites a custom function call.</summary>
    protected virtual Expression RewriteCustomFunctionCall(CustomFunctionCall expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        AlgebraList<Expression> arguments = RewriteExpressions(expression.Arguments);
        return arguments == expression.Arguments ? expression : expression with { Arguments = arguments };
    }

    /// <summary>Rewrites an <c>EXISTS</c>.</summary>
    protected virtual Expression RewriteExistsExpression(ExistsExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        QueryPattern pattern = Rewrite(expression.Pattern);
        return ReferenceEquals(pattern, expression.Pattern) ? expression : expression with { Pattern = pattern };
    }

    /// <summary>Rewrites an aggregate.</summary>
    protected virtual Expression RewriteAggregateExpression(AggregateExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expression? argument = expression.Argument is null ? null : Rewrite(expression.Argument);
        return ReferenceEquals(argument, expression.Argument) ? expression : expression with { Argument = argument };
    }

    // --- pattern terms -----------------------------------------------------

    /// <summary>Rewrites a variable in a pattern.</summary>
    protected virtual PatternTerm RewriteVariablePattern(VariablePattern term) => term;

    /// <summary>Rewrites a term in a pattern.</summary>
    protected virtual PatternTerm RewriteTermPattern(TermPattern term) => term;

    /// <summary>Rewrites a blank node in a pattern.</summary>
    protected virtual PatternTerm RewriteBlankNodePattern(BlankNodePattern term) => term;

    /// <summary>Rewrites a triple term with variables inside it.</summary>
    protected virtual PatternTerm RewriteTripleTermPattern(TripleTermPattern term)
    {
        ArgumentNullException.ThrowIfNull(term);
        PatternTerm subject = Rewrite(term.Subject);
        PatternTerm predicate = Rewrite(term.Predicate);
        PatternTerm @object = Rewrite(term.Object);

        return ReferenceEquals(subject, term.Subject) && ReferenceEquals(predicate, term.Predicate) && ReferenceEquals(@object, term.Object)
            ? term
            : term with { Subject = subject, Predicate = predicate, Object = @object };
    }

    // --- lists -------------------------------------------------------------
    // One loop per element type rather than a delegate-taking helper, so that
    // a rewriter that changes nothing allocates nothing.

    private AlgebraList<TriplePattern> RewriteTriples(AlgebraList<TriplePattern> list)
    {
        TriplePattern[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            TriplePattern item = list[i];
            TriplePattern rewritten = Rewrite(item);

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private AlgebraList<QuadPattern> RewriteQuads(AlgebraList<QuadPattern> list)
    {
        QuadPattern[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            QuadPattern item = list[i];
            QuadPattern rewritten = Rewrite(item);

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private AlgebraList<PatternTerm> RewriteTerms(AlgebraList<PatternTerm> list)
    {
        PatternTerm[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            PatternTerm item = list[i];
            PatternTerm rewritten = Rewrite(item);

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private AlgebraList<Expression> RewriteExpressions(AlgebraList<Expression> list)
    {
        Expression[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            Expression item = list[i];
            Expression rewritten = Rewrite(item);

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private AlgebraList<GroupKey> RewriteKeys(AlgebraList<GroupKey> list)
    {
        GroupKey[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            GroupKey item = list[i];
            Expression expression = Rewrite(item.Expression);
            GroupKey rewritten = ReferenceEquals(expression, item.Expression) ? item : item with { Expression = expression };

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private AlgebraList<OrderCondition> RewriteConditions(AlgebraList<OrderCondition> list)
    {
        OrderCondition[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            OrderCondition item = list[i];
            Expression expression = Rewrite(item.Expression);
            OrderCondition rewritten = ReferenceEquals(expression, item.Expression) ? item : item with { Expression = expression };

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private AlgebraList<UpdateOperation> RewriteOperations(AlgebraList<UpdateOperation> list)
    {
        UpdateOperation[]? changed = null;

        for (int i = 0; i < list.Count; i++)
        {
            UpdateOperation item = list[i];
            UpdateOperation rewritten = Rewrite(item);

            if (changed is null && !ReferenceEquals(item, rewritten))
            {
                changed = list.ToArray();
            }

            if (changed is not null)
            {
                changed[i] = rewritten;
            }
        }

        return changed is null ? list : AlgebraList.Own(changed);
    }

    private static InvalidOperationException Unknown(AlgebraNode node) =>
        new("The rewriter has no case for " + node.GetType().Name + ", which cannot happen: every node family is sealed.");
}
