// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>Prologue, the four query forms, and the assembly of a query level (<c>docs/spec/sparql-algebra.md</c> §4.6, §4.7).</summary>
internal ref partial struct Parser
{
    /// <summary><c>[1] QueryUnit ::= Query</c>, then the end of the input.</summary>
    internal Query ParseQueryUnit()
    {
        Token start = _token;
        Prologue prologue = ParsePrologue();
        Query query;

        if (IsWord("SELECT"u8))
        {
            query = ParseSelectQuery(prologue, start);
        }
        else if (IsWord("CONSTRUCT"u8))
        {
            query = ParseConstructQuery(prologue, start);
        }
        else if (IsWord("ASK"u8))
        {
            query = ParseAskQuery(prologue, start);
        }
        else if (IsWord("DESCRIBE"u8))
        {
            query = ParseDescribeQuery(prologue, start);
        }
        else
        {
            throw Expected("SELECT, CONSTRUCT, ASK or DESCRIBE");
        }

        if (!Is(TokenKind.End))
        {
            throw Expected("the end of the query");
        }

        return query;
    }

    // --- prologue ----------------------------------------------------------

    /// <summary><c>[4] Prologue ::= ( BaseDecl | PrefixDecl | VersionDecl )*</c></summary>
    private Prologue ParsePrologue()
    {
        Token start = _token;
        RdfTerm? baseIri = null;

        while (true)
        {
            Token at = _token;

            if (AcceptWord("BASE"u8))
            {
                baseIri = ParseIriRef();
                DeclareBase(baseIri);
            }
            else if (AcceptWord("PREFIX"u8))
            {
                Token name = Expect(TokenKind.PrefixedName, "a prefix name ending in ':'");
                RdfTerm iri = ParseIriRef();
                DeclarePrefix(name, iri, at);
            }
            else if (IsWord("VERSION"u8))
            {
                Require(SparqlVersion.Sparql12Basic, at, "A VERSION declaration");
                Advance();
                ParseVersionSpecifier();
            }
            else
            {
                break;
            }
        }

        // A second prologue in an update request adds to the first; the
        // prefixes accumulate and the last base wins.
        return new Prologue(baseIri, AlgebraList.From(_prefixes.Span), _declaredVersion) { Span = From(start) };
    }

    /// <summary><c>[8] VersionSpecifier ::= STRING_LITERAL1 | STRING_LITERAL2</c>: a short string naming a version no wider than the caller's.</summary>
    private void ParseVersionSpecifier()
    {
        if (!Is(TokenKind.String))
        {
            throw Expected("a version label in single or double quotes");
        }

        ReadOnlySpan<byte> raw = Text(_token);

        if (raw.Length >= 6 && raw[1] == raw[0] && raw[2] == raw[0])
        {
            throw Fail(SparqlErrorKind.Syntax, _token, "A version label is a short string, not a triple-quoted one (production [8]).");
        }

        string label = ParseShortString(out Token token);
        SparqlVersion declared = label switch
        {
            "1.1" => SparqlVersion.Sparql11,
            "1.2-basic" => SparqlVersion.Sparql12Basic,
            "1.2" => SparqlVersion.Sparql12,
            _ => throw Fail(SparqlErrorKind.Version, token, "Unknown version label '" + label + "'; the labels are \"1.1\", \"1.2-basic\" and \"1.2\"."),
        };

        if (declared > _widest)
        {
            throw Fail(SparqlErrorKind.Version, token, "VERSION \"" + label + "\" is wider than the " + Label(_widest) + " the caller asked for.");
        }

        _version = declared;
        _declaredVersion = declared;
    }

    // --- query forms -------------------------------------------------------

    private readonly struct SelectClause
    {
        internal SelectClause(bool star, bool distinct, bool reduced)
        {
            Star = star;
            Distinct = distinct;
            Reduced = reduced;
        }

        internal bool Star { get; }

        internal bool Distinct { get; }

        internal bool Reduced { get; }
    }

    /// <summary>A projected item: a bare variable, or <c>(expr AS ?v)</c>.</summary>
    private readonly struct SelectItem
    {
        internal SelectItem(Variable variable, Expression? expression, Token at)
        {
            Variable = variable;
            Expression = expression;
            At = at;
        }

        internal Variable Variable { get; }

        internal Expression? Expression { get; }

        internal Token At { get; }
    }

    /// <summary><c>[9] SelectQuery ::= SelectClause DatasetClause* WhereClause SolutionModifier</c>, then the trailing <c>ValuesClause</c>.</summary>
    private SelectQuery ParseSelectQuery(Prologue prologue, Token start)
    {
        Level saved = _level;
        _level = default;
        PooledList<SelectItem> items = default;

        try
        {
            SelectClause clause = ParseSelectClause(ref items);
            DatasetSpec? dataset = ParseDatasetClauses();
            QueryPattern where = ParseWhereClause();
            QueryPattern pattern = ParseModifiersAndBuild(where, clause, ref items, topLevel: true);
            return new SelectQuery(prologue, dataset, pattern) { Span = From(start) };
        }
        finally
        {
            items.Dispose();
            _level = saved;
        }
    }

    /// <summary><c>[10] SubSelect ::= SelectClause WhereClause SolutionModifier ValuesClause</c>, inside braces the caller consumes.</summary>
    private QueryPattern ParseSubSelect()
    {
        Level saved = _level;
        _level = default;
        PooledList<SelectItem> items = default;
        int previousGroup = EnterGroup();

        try
        {
            SelectClause clause = ParseSelectClause(ref items);
            QueryPattern where = ParseWhereClause();
            return ParseModifiersAndBuild(where, clause, ref items, topLevel: true);
        }
        finally
        {
            LeaveGroup(previousGroup);
            items.Dispose();
            _level = saved;
        }
    }

    /// <summary><c>[11] SelectClause ::= 'SELECT' ( 'DISTINCT' | 'REDUCED' )? ( ( Var | ( '(' Expression 'AS' Var ')' ) )+ | '*' )</c></summary>
    private SelectClause ParseSelectClause(ref PooledList<SelectItem> items)
    {
        ExpectWord("SELECT"u8);
        bool distinct = AcceptWord("DISTINCT"u8);
        bool reduced = !distinct && AcceptWord("REDUCED"u8);

        if (Accept(TokenKind.Star))
        {
            return new SelectClause(true, distinct, reduced);
        }

        _level.AggregatesAllowed = true;

        while (true)
        {
            Token at = _token;

            if (Is(TokenKind.Variable))
            {
                items.Add(new SelectItem(ParseVariable(), null, at));
            }
            else if (Accept(TokenKind.LeftParen))
            {
                Expression expression = ParseExpression();
                ExpectWord("AS"u8);
                Variable variable = ParseVariable();
                Expect(TokenKind.RightParen, "')'");
                items.Add(new SelectItem(variable, expression, at));
            }
            else if (items.Count == 0)
            {
                throw Expected("a variable, '(expression AS ?variable)' or '*'");
            }
            else
            {
                break;
            }
        }

        _level.AggregatesAllowed = false;
        return new SelectClause(false, distinct, reduced);
    }

    /// <summary><c>[12] ConstructQuery</c>, both forms.</summary>
    private ConstructQuery ParseConstructQuery(Prologue prologue, Token start)
    {
        Level saved = _level;
        _level = default;
        PooledList<SelectItem> noItems = default;

        try
        {
            ExpectWord("CONSTRUCT"u8);

            if (Is(TokenKind.LeftBrace))
            {
                AlgebraList<TriplePattern> template = ParseConstructTemplate();
                DatasetSpec? dataset = ParseDatasetClauses();
                QueryPattern where = ParseWhereClause();
                QueryPattern pattern = ParseModifiersAndBuild(where, default, ref noItems, topLevel: true);
                return new ConstructQuery(prologue, dataset, template, pattern) { Span = From(start) };
            }
            else
            {
                DatasetSpec? dataset = ParseDatasetClauses();
                ExpectWord("WHERE"u8);
                Token open = _token;
                int previousGroup = EnterGroup();
                AlgebraList<TriplePattern> template = ParseConstructTemplate();
                LeaveGroup(previousGroup);
                QueryPattern where = new Bgp(template) { Span = From(open) };
                QueryPattern pattern = ParseModifiersAndBuild(where, default, ref noItems, topLevel: true);
                return new ConstructQuery(prologue, dataset, template, pattern) { Span = From(start) };
            }
        }
        finally
        {
            _level = saved;
        }
    }

    /// <summary><c>[79] ConstructTemplate ::= '{' ConstructTriples? '}'</c></summary>
    private AlgebraList<TriplePattern> ParseConstructTemplate()
    {
        Expect(TokenKind.LeftBrace, "'{'");
        PooledList<TriplePattern> saved = _triples;
        _triples = default;

        try
        {
            TripleMode mode = TripleMode.Template(allowVariables: true, allowBlankNodes: true);

            if (StartsTriples())
            {
                ParseTriplesTemplate(in mode);
            }

            Expect(TokenKind.RightBrace, "'}'");
            return _triples.Drain();
        }
        finally
        {
            _triples.Dispose();
            _triples = saved;
        }
    }

    /// <summary><c>[14] AskQuery ::= 'ASK' DatasetClause* WhereClause SolutionModifier</c></summary>
    private AskQuery ParseAskQuery(Prologue prologue, Token start)
    {
        Level saved = _level;
        _level = default;
        PooledList<SelectItem> noItems = default;

        try
        {
            ExpectWord("ASK"u8);
            DatasetSpec? dataset = ParseDatasetClauses();
            QueryPattern where = ParseWhereClause();
            QueryPattern pattern = ParseModifiersAndBuild(where, default, ref noItems, topLevel: true);
            return new AskQuery(prologue, dataset, pattern) { Span = From(start) };
        }
        finally
        {
            _level = saved;
        }
    }

    /// <summary><c>[13] DescribeQuery ::= 'DESCRIBE' ( VarOrIri+ | '*' ) DatasetClause* WhereClause? SolutionModifier</c></summary>
    private DescribeQuery ParseDescribeQuery(Prologue prologue, Token start)
    {
        Level saved = _level;
        _level = default;
        PooledList<SelectItem> noItems = default;
        PooledList<PatternTerm> resources = default;

        try
        {
            ExpectWord("DESCRIBE"u8);
            bool star = Accept(TokenKind.Star);

            if (!star)
            {
                do
                {
                    resources.Add(ParseVarOrIri());
                }
                while (Is(TokenKind.Variable) || Is(TokenKind.Iri) || Is(TokenKind.PrefixedName));
            }

            DatasetSpec? dataset = ParseDatasetClauses();
            QueryPattern where = Is(TokenKind.LeftBrace) || IsWord("WHERE"u8) ? ParseWhereClause() : new Bgp(default) { Span = From(start) };

            if (star)
            {
                PooledList<Variable> scope = default;

                try
                {
                    CollectScope(where, ref scope);

                    foreach (Variable variable in scope.Span)
                    {
                        resources.Add(new VariablePattern(variable) { Span = where.Span });
                    }
                }
                finally
                {
                    scope.Dispose();
                }
            }

            QueryPattern pattern = ParseModifiersAndBuild(where, default, ref noItems, topLevel: true);
            return new DescribeQuery(prologue, dataset, resources.Drain(), pattern) { Span = From(start) };
        }
        finally
        {
            resources.Dispose();
            _level = saved;
        }
    }

    /// <summary><c>[15] DatasetClause ::= 'FROM' ( DefaultGraphClause | NamedGraphClause )</c>, repeated.</summary>
    private DatasetSpec? ParseDatasetClauses()
    {
        if (!IsWord("FROM"u8))
        {
            return null;
        }

        Token start = _token;
        PooledList<RdfTerm> defaults = default;
        PooledList<RdfTerm> named = default;

        try
        {
            while (AcceptWord("FROM"u8))
            {
                if (AcceptWord("NAMED"u8))
                {
                    named.Add(ParseIri());
                }
                else
                {
                    defaults.Add(ParseIri());
                }
            }

            return new DatasetSpec(defaults.Drain(), named.Drain()) { Span = From(start) };
        }
        finally
        {
            defaults.Dispose();
            named.Dispose();
        }
    }

    /// <summary><c>[19] WhereClause ::= 'WHERE'? GroupGraphPattern</c></summary>
    private QueryPattern ParseWhereClause()
    {
        AcceptWord("WHERE"u8);

        if (!Is(TokenKind.LeftBrace))
        {
            throw Expected("'{' to begin the WHERE clause");
        }

        return ParseGroupGraphPattern();
    }

    // --- solution modifiers and the level ----------------------------------

    /// <summary>
    /// <c>[20] SolutionModifier</c> and the trailing <c>ValuesClause</c>, then
    /// the assembly of §18.3.4 and §18.3.5: Group, HAVING as Filters, VALUES,
    /// SELECT expressions as Extends, OrderBy, Project, Distinct or Reduced,
    /// Slice.
    /// </summary>
    private QueryPattern ParseModifiersAndBuild(QueryPattern where, SelectClause clause, ref PooledList<SelectItem> items, bool topLevel)
    {
        PooledList<GroupKey> keys = default;
        PooledList<Expression> having = default;
        PooledList<OrderCondition> order = default;
        PooledList<Variable> projected = default;
        PooledList<Variable> scope = default;

        try
        {
            Token groupToken = _token;
            bool hasGroupBy = false;

            if (AcceptWord("GROUP"u8))
            {
                ExpectWord("BY"u8);
                hasGroupBy = true;

                do
                {
                    keys.Add(ParseGroupCondition(where));
                }
                while (StartsGroupCondition());
            }

            _level.AggregatesAllowed = true;

            if (AcceptWord("HAVING"u8))
            {
                do
                {
                    having.Add(ParseConstraint());
                }
                while (StartsConstraint());
            }

            if (AcceptWord("ORDER"u8))
            {
                ExpectWord("BY"u8);

                do
                {
                    order.Add(ParseOrderCondition());
                }
                while (StartsOrderCondition());
            }

            _level.AggregatesAllowed = false;

            long offset = 0;
            long? limit = null;
            bool sawLimit = false;
            bool sawOffset = false;

            for (int i = 0; i < 2; i++)
            {
                if (!sawLimit && AcceptWord("LIMIT"u8))
                {
                    limit = ParseUnsignedInteger("LIMIT");
                    sawLimit = true;
                }
                else if (!sawOffset && AcceptWord("OFFSET"u8))
                {
                    offset = ParseUnsignedInteger("OFFSET");
                    sawOffset = true;
                }
            }

            Values? values = null;

            if (topLevel && IsWord("VALUES"u8))
            {
                Token at = _token;
                Advance();
                values = ParseDataBlock(at);
            }

            // --- assembly ---

            QueryPattern pattern = where;
            bool grouped = hasGroupBy || _level.SawAggregate;

            if (grouped)
            {
                if (clause.Star)
                {
                    throw Fail(SparqlErrorKind.Aggregate, groupToken, "SELECT * is not allowed with GROUP BY or with an aggregate in HAVING or ORDER BY (SPARQL 1.2 Query §19.7).");
                }

                pattern = new Group(pattern, keys.Drain()) { Span = From(groupToken) };
            }

            foreach (Expression condition in having.Span)
            {
                pattern = new Filter(condition, pattern) { Span = condition.Span };
            }

            if (values is not null)
            {
                pattern = new Join(pattern, values) { Span = values.Span };
            }

            // VS': what the pattern makes visible. After a Group that is the
            // keys; the AS variables must not be among them nor repeat.
            CollectScope(pattern, ref scope);

            if (grouped)
            {
                CheckGroupedLevel(pattern, ref items, having.Span, order.Span, ref scope);
            }

            if (clause.Star)
            {
                foreach (Variable variable in scope.Span)
                {
                    projected.Add(variable);
                }
            }

            foreach (SelectItem item in items.Span)
            {
                if (item.Expression is null)
                {
                    if (!Contains(projected.Span, item.Variable))
                    {
                        projected.Add(item.Variable);
                    }

                    continue;
                }

                if (Contains(scope.Span, item.Variable))
                {
                    throw Fail(SparqlErrorKind.VariableScope, item.At, "The variable " + item.Variable + " is already in scope in the pattern and cannot be assigned with AS (SPARQL 1.2 Query §18.3.4.4).");
                }

                if (Contains(projected.Span, item.Variable))
                {
                    throw Fail(SparqlErrorKind.VariableScope, item.At, "The variable " + item.Variable + " is already projected and cannot be assigned again with AS (SPARQL 1.2 Query §18.3.4.4).");
                }

                pattern = new Extend(pattern, item.Variable, item.Expression) { Span = From(item.At) };
                scope.Add(item.Variable);
                projected.Add(item.Variable);
            }

            if (order.Count > 0)
            {
                pattern = new OrderBy(pattern, order.Drain()) { Span = pattern.Span };
            }

            if (clause.Star || items.Count > 0)
            {
                pattern = new Project(pattern, projected.Drain()) { Span = pattern.Span };
            }

            if (clause.Distinct)
            {
                pattern = new Distinct(pattern) { Span = pattern.Span };
            }
            else if (clause.Reduced)
            {
                pattern = new Reduced(pattern) { Span = pattern.Span };
            }

            if (sawLimit || sawOffset)
            {
                pattern = new Slice(pattern, offset, limit) { Span = pattern.Span };
            }

            return pattern;
        }
        finally
        {
            keys.Dispose();
            having.Dispose();
            order.Dispose();
            projected.Dispose();
            scope.Dispose();
        }
    }

    /// <summary>
    /// SPARQL 1.2 Query §11.4: at a level that aggregates, a variable outside an
    /// aggregate in SELECT, HAVING or ORDER BY must be a group key.
    /// </summary>
    private readonly void CheckGroupedLevel(
        QueryPattern pattern, ref PooledList<SelectItem> items, ReadOnlySpan<Expression> having, ReadOnlySpan<OrderCondition> order, ref PooledList<Variable> keys)
    {
        _ = pattern;

        foreach (SelectItem item in items.Span)
        {
            if (item.Expression is null)
            {
                if (!Contains(keys.Span, item.Variable))
                {
                    throw Fail(SparqlErrorKind.Aggregate, item.At, "The variable " + item.Variable + " is projected but is not a GROUP BY key of a level that aggregates (SPARQL 1.2 Query §11.4).");
                }
            }
            else
            {
                CheckVariablesAreKeys(item.Expression, ref keys, item.At);
            }
        }

        foreach (Expression condition in having)
        {
            CheckVariablesAreKeys(condition, ref keys, _token);
        }

        foreach (OrderCondition condition in order)
        {
            CheckVariablesAreKeys(condition.Expression, ref keys, _token);
        }
    }

    private static void CheckVariablesAreKeys(Expression expression, ref PooledList<Variable> keys, Token at)
    {
        switch (expression)
        {
            case VariableExpression variable:
                if (!Contains(keys.Span, variable.Variable))
                {
                    throw Fail(SparqlErrorKind.Aggregate, at, "The variable " + variable.Variable + " is used outside an aggregate but is not a GROUP BY key of a level that aggregates (SPARQL 1.2 Query §11.4).");
                }

                break;
            case UnaryExpression unary:
                CheckVariablesAreKeys(unary.Operand, ref keys, at);
                break;
            case BinaryExpression binary:
                CheckVariablesAreKeys(binary.Left, ref keys, at);
                CheckVariablesAreKeys(binary.Right, ref keys, at);
                break;
            case FunctionCall call:
                if (call.Function == BuiltInFunction.Bound)
                {
                    break;
                }

                foreach (Expression argument in call.Arguments)
                {
                    CheckVariablesAreKeys(argument, ref keys, at);
                }

                break;
            case CustomFunctionCall custom:
                foreach (Expression argument in custom.Arguments)
                {
                    CheckVariablesAreKeys(argument, ref keys, at);
                }

                break;
            default:
                // Aggregates hide their variables; EXISTS is its own scope; constants have none.
                break;
        }
    }

    /// <summary><c>[22] GroupCondition ::= BuiltInCall | FunctionCall | '(' Expression ( 'AS' Var )? ')' | Var</c></summary>
    private GroupKey ParseGroupCondition(QueryPattern where)
    {
        Token start = _token;

        if (Is(TokenKind.Variable))
        {
            Variable variable = ParseVariable();
            return new GroupKey(new VariableExpression(variable) { Span = start.Span }, variable) { Span = From(start) };
        }

        if (Accept(TokenKind.LeftParen))
        {
            Expression expression = ParseExpression();
            Variable? named = null;

            if (AcceptWord("AS"u8))
            {
                Token at = _token;
                Variable variable = ParseVariable();
                PooledList<Variable> scope = default;

                try
                {
                    CollectScope(where, ref scope);

                    if (Contains(scope.Span, variable))
                    {
                        throw Fail(SparqlErrorKind.VariableScope, at, "The variable " + variable + " is already in scope and cannot be assigned with AS in GROUP BY (SPARQL 1.2 Query §18.3.1).");
                    }
                }
                finally
                {
                    scope.Dispose();
                }

                named = variable;
            }

            Expect(TokenKind.RightParen, "')'");
            return new GroupKey(expression, named) { Span = From(start) };
        }

        Expression call = ParseBuiltInOrFunctionCall();
        return new GroupKey(call, null) { Span = From(start) };
    }

    private readonly bool StartsGroupCondition() =>
        Is(TokenKind.Variable) || Is(TokenKind.LeftParen) || Is(TokenKind.Iri) || Is(TokenKind.PrefixedName) || IsBuiltInWord();

    /// <summary><c>[26] OrderCondition ::= ( ( 'ASC' | 'DESC' ) BrackettedExpression ) | ( Constraint | Var )</c></summary>
    private OrderCondition ParseOrderCondition()
    {
        Token start = _token;

        if (AcceptWord("ASC"u8))
        {
            return new OrderCondition(ParseBrackettedExpression(), false) { Span = From(start) };
        }

        if (AcceptWord("DESC"u8))
        {
            return new OrderCondition(ParseBrackettedExpression(), true) { Span = From(start) };
        }

        if (Is(TokenKind.Variable))
        {
            Variable variable = ParseVariable();
            return new OrderCondition(new VariableExpression(variable) { Span = start.Span }, false) { Span = From(start) };
        }

        return new OrderCondition(ParseConstraint(), false) { Span = From(start) };
    }

    private readonly bool StartsOrderCondition() =>
        IsWord("ASC"u8) || IsWord("DESC"u8) || Is(TokenKind.Variable) || StartsConstraint();

    private long ParseUnsignedInteger(string keyword)
    {
        Token token = _token;

        if (!Is(TokenKind.Integer) || Text(token)[0] is (byte)'+' or (byte)'-')
        {
            throw Expected("an unsigned integer after " + keyword);
        }

        Advance();

        if (!long.TryParse(Text(token), NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            throw Fail(SparqlErrorKind.Range, token, keyword + " must fit in a signed 64-bit integer.");
        }

        return value;
    }

    // --- scope -------------------------------------------------------------

    private static bool Contains(ReadOnlySpan<Variable> variables, Variable variable)
    {
        foreach (Variable candidate in variables)
        {
            if (candidate == variable)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddScope(ref PooledList<Variable> scope, Variable variable)
    {
        if (!Contains(scope.Span, variable))
        {
            scope.Add(variable);
        }
    }

    /// <summary>The in-scope variables of a pattern (SPARQL 1.2 Query §18.3.1), in order of first appearance.</summary>
    private static void CollectScope(QueryPattern pattern, ref PooledList<Variable> scope)
    {
        switch (pattern)
        {
            case Bgp bgp:
                foreach (TriplePattern triple in bgp.Triples)
                {
                    CollectScope(triple.Subject, ref scope);
                    CollectScope(triple.Predicate, ref scope);
                    CollectScope(triple.Object, ref scope);
                }

                break;
            case PathPattern path:
                CollectScope(path.Subject, ref scope);
                CollectScope(path.Object, ref scope);
                break;
            case Join join:
                CollectScope(join.Left, ref scope);
                CollectScope(join.Right, ref scope);
                break;
            case LeftJoin leftJoin:
                CollectScope(leftJoin.Left, ref scope);
                CollectScope(leftJoin.Right, ref scope);
                break;
            case Union union:
                CollectScope(union.Left, ref scope);
                CollectScope(union.Right, ref scope);
                break;
            case Filter filter:
                CollectScope(filter.Inner, ref scope);
                break;
            case Graph graph:
                CollectScope(graph.Name, ref scope);
                CollectScope(graph.Inner, ref scope);
                break;
            case Service service:
                CollectScope(service.Name, ref scope);
                CollectScope(service.Inner, ref scope);
                break;
            case Extend extend:
                CollectScope(extend.Inner, ref scope);
                AddScope(ref scope, extend.Variable);
                break;
            case Minus minus:
                CollectScope(minus.Left, ref scope);
                break;
            case Values values:
                foreach (Variable variable in values.Variables)
                {
                    AddScope(ref scope, variable);
                }

                break;
            case Group group:
                foreach (GroupKey key in group.Keys)
                {
                    if (key.Variable is Variable named)
                    {
                        AddScope(ref scope, named);
                    }
                }

                break;
            case Project project:
                foreach (Variable variable in project.Variables)
                {
                    AddScope(ref scope, variable);
                }

                break;
            case OrderBy orderBy:
                CollectScope(orderBy.Inner, ref scope);
                break;
            case Distinct distinct:
                CollectScope(distinct.Inner, ref scope);
                break;
            case Reduced reduced:
                CollectScope(reduced.Inner, ref scope);
                break;
            case Slice slice:
                CollectScope(slice.Inner, ref scope);
                break;
            default:
                break;
        }
    }

    private static void CollectScope(PatternTerm term, ref PooledList<Variable> scope)
    {
        switch (term)
        {
            case VariablePattern variable:
                AddScope(ref scope, variable.Variable);
                break;
            case TripleTermPattern triple:
                CollectScope(triple.Subject, ref scope);
                CollectScope(triple.Predicate, ref scope);
                CollectScope(triple.Object, ref scope);
                break;
            default:
                break;
        }
    }
}
