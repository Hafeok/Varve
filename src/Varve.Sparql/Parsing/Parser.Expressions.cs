// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>Expressions, <c>[127]</c> to <c>[148]</c>, by precedence.</summary>
internal ref partial struct Parser
{
    /// <summary><c>[75] Constraint ::= BrackettedExpression | BuiltInCall | FunctionCall</c></summary>
    private Expression ParseConstraint()
    {
        if (Is(TokenKind.LeftParen))
        {
            return ParseBrackettedExpression();
        }

        return ParseBuiltInOrFunctionCall();
    }

    private readonly bool StartsConstraint() =>
        Is(TokenKind.LeftParen) || Is(TokenKind.Iri) || Is(TokenKind.PrefixedName) || IsBuiltInWord();

    private readonly bool IsBuiltInWord() =>
        _token.Kind == TokenKind.Word && (TryBuiltIn(Text(_token), out _, out _, out _) || TryAggregate(Text(_token), out _)
            || IsWord("NOT"u8) || IsWord("EXISTS"u8));

    /// <summary><c>[140] BrackettedExpression ::= '(' Expression ')'</c></summary>
    private Expression ParseBrackettedExpression()
    {
        Expect(TokenKind.LeftParen, "'('");
        Expression expression = ParseExpression();
        Expect(TokenKind.RightParen, "')'");
        return expression;
    }

    /// <summary><c>[127] Expression ::= ConditionalOrExpression</c></summary>
    private Expression ParseExpression()
    {
        Token start = _token;
        Expression left = ParseConditionalAnd();

        while (Accept(TokenKind.OrOr))
        {
            left = new BinaryExpression(BinaryOperator.Or, left, ParseConditionalAnd()) { Span = From(start) };
        }

        return left;
    }

    private Expression ParseConditionalAnd()
    {
        Token start = _token;
        Expression left = ParseRelational();

        while (Accept(TokenKind.AndAnd))
        {
            left = new BinaryExpression(BinaryOperator.And, left, ParseRelational()) { Span = From(start) };
        }

        return left;
    }

    /// <summary><c>[131] RelationalExpression</c>: one comparison, or IN / NOT IN.</summary>
    private Expression ParseRelational()
    {
        Token start = _token;
        Expression left = ParseAdditive();
        BinaryOperator op;

        switch (_token.Kind)
        {
            case TokenKind.Equal:
                op = BinaryOperator.Equal;
                break;
            case TokenKind.NotEqual:
                op = BinaryOperator.NotEqual;
                break;
            case TokenKind.Less:
                op = BinaryOperator.Less;
                break;
            case TokenKind.Greater:
                op = BinaryOperator.Greater;
                break;
            case TokenKind.LessOrEqual:
                op = BinaryOperator.LessOrEqual;
                break;
            case TokenKind.GreaterOrEqual:
                op = BinaryOperator.GreaterOrEqual;
                break;
            case TokenKind.Word when IsWord("IN"u8):
                Advance();
                return ParseInList(left, negated: false, start);
            case TokenKind.Word when IsWord("NOT"u8):
                Advance();
                ExpectWord("IN"u8);
                return ParseInList(left, negated: true, start);
            default:
                return left;
        }

        Advance();
        return new BinaryExpression(op, left, ParseAdditive()) { Span = From(start) };
    }

    private FunctionCall ParseInList(Expression tested, bool negated, Token start)
    {
        PooledList<Expression> arguments = default;

        try
        {
            arguments.Add(tested);
            ParseExpressionList(ref arguments);
            return new FunctionCall(negated ? BuiltInFunction.NotIn : BuiltInFunction.In, arguments.Drain()) { Span = From(start) };
        }
        finally
        {
            arguments.Dispose();
        }
    }

    /// <summary><c>[78] ExpressionList ::= NIL | '(' Expression ( ',' Expression )* ')'</c></summary>
    private void ParseExpressionList(ref PooledList<Expression> into)
    {
        if (Accept(TokenKind.Nil))
        {
            return;
        }

        Expect(TokenKind.LeftParen, "'('");

        do
        {
            into.Add(ParseExpression());
        }
        while (Accept(TokenKind.Comma));

        Expect(TokenKind.RightParen, "')'");
    }

    /// <summary>
    /// <c>[133] AdditiveExpression</c>, including the signed-number form the
    /// grammar spells out: <c>?x -1</c> is a subtraction of <c>1</c>, and a
    /// <c>*</c> or <c>/</c> after the signed number binds to it first.
    /// </summary>
    private Expression ParseAdditive()
    {
        Token start = _token;
        Expression left = ParseMultiplicative();

        while (true)
        {
            if (Accept(TokenKind.Plus))
            {
                left = new BinaryExpression(BinaryOperator.Add, left, ParseMultiplicative()) { Span = From(start) };
            }
            else if (Accept(TokenKind.Minus))
            {
                left = new BinaryExpression(BinaryOperator.Subtract, left, ParseMultiplicative()) { Span = From(start) };
            }
            else if (IsNumericLiteral() && Text(_token)[0] is (byte)'+' or (byte)'-')
            {
                Token number = _token;
                bool negative = Text(number)[0] == (byte)'-';
                Advance();
                RdfTerm datatype = number.Kind switch
                {
                    TokenKind.Integer => XsdIntegerTerm,
                    TokenKind.Decimal => XsdDecimalTerm,
                    _ => XsdDoubleTerm,
                };
                Expression right = new ConstantExpression(RdfTerm.Literal(Text(number)[1..], datatype)) { Span = number.Span };

                while (true)
                {
                    if (Accept(TokenKind.Star))
                    {
                        right = new BinaryExpression(BinaryOperator.Multiply, right, ParseUnary()) { Span = From(number) };
                    }
                    else if (Accept(TokenKind.Slash))
                    {
                        right = new BinaryExpression(BinaryOperator.Divide, right, ParseUnary()) { Span = From(number) };
                    }
                    else
                    {
                        break;
                    }
                }

                left = new BinaryExpression(negative ? BinaryOperator.Subtract : BinaryOperator.Add, left, right) { Span = From(start) };
            }
            else
            {
                return left;
            }
        }
    }

    /// <summary><c>[134] MultiplicativeExpression ::= UnaryExpression ( '*' UnaryExpression | '/' UnaryExpression )*</c></summary>
    private Expression ParseMultiplicative()
    {
        Token start = _token;
        Expression left = ParseUnary();

        while (true)
        {
            if (Accept(TokenKind.Star))
            {
                left = new BinaryExpression(BinaryOperator.Multiply, left, ParseUnary()) { Span = From(start) };
            }
            else if (Accept(TokenKind.Slash))
            {
                left = new BinaryExpression(BinaryOperator.Divide, left, ParseUnary()) { Span = From(start) };
            }
            else
            {
                return left;
            }
        }
    }

    /// <summary><c>[135] UnaryExpression ::= '!' UnaryExpression | '+' PrimaryExpression | '-' PrimaryExpression | PrimaryExpression</c></summary>
    private Expression ParseUnary()
    {
        Token start = _token;

        if (Accept(TokenKind.Bang))
        {
            return new UnaryExpression(UnaryOperator.Not, ParseUnary()) { Span = From(start) };
        }

        if (Accept(TokenKind.Plus))
        {
            return new UnaryExpression(UnaryOperator.Plus, ParsePrimary()) { Span = From(start) };
        }

        if (Accept(TokenKind.Minus))
        {
            return new UnaryExpression(UnaryOperator.Minus, ParsePrimary()) { Span = From(start) };
        }

        return ParsePrimary();
    }

    /// <summary><c>[136] PrimaryExpression</c></summary>
    private Expression ParsePrimary()
    {
        Token token = _token;

        switch (token.Kind)
        {
            case TokenKind.LeftParen:
                return ParseBrackettedExpression();
            case TokenKind.Variable:
                return new VariableExpression(ParseVariable()) { Span = token.Span };
            case TokenKind.String:
                return new ConstantExpression(ParseRdfLiteral()) { Span = From(token) };
            case TokenKind.Integer:
            case TokenKind.Decimal:
            case TokenKind.Double:
                return new ConstantExpression(ParseNumericLiteral()) { Span = token.Span };
            case TokenKind.Iri:
            case TokenKind.PrefixedName:
                return ParseIriOrFunction();
            case TokenKind.TripleTermOpen:
                return ParseExprTripleTerm();
            case TokenKind.Word:
                if (IsBooleanLiteral())
                {
                    return new ConstantExpression(ParseBooleanLiteral()) { Span = token.Span };
                }

                return ParseBuiltInOrFunctionCall();
            default:
                throw Expected("an expression");
        }
    }

    /// <summary><c>[148] iriOrFunction ::= iri ArgList?</c></summary>
    private Expression ParseIriOrFunction()
    {
        Token start = _token;
        RdfTerm iri = ParseIri();

        if (!Is(TokenKind.LeftParen) && !Is(TokenKind.Nil))
        {
            return new ConstantExpression(iri) { Span = start.Span };
        }

        return ParseCustomCall(iri, start);
    }

    /// <summary><c>[76] FunctionCall ::= iri ArgList</c>, with <c>[77] ArgList</c>'s DISTINCT making it a custom aggregate.</summary>
    private Expression ParseCustomCall(RdfTerm iri, Token start)
    {
        PooledList<Expression> arguments = default;

        try
        {
            if (Accept(TokenKind.Nil))
            {
                return new CustomFunctionCall(iri, default) { Span = From(start) };
            }

            Expect(TokenKind.LeftParen, "'('");

            if (AcceptWord("DISTINCT"u8))
            {
                // Only a custom aggregate may say DISTINCT (SPARQL 1.2 Query §19.7).
                NoteAggregate(start);
                Expression argument = ParseAggregateArgument();

                if (Accept(TokenKind.Comma))
                {
                    throw Fail(SparqlErrorKind.Syntax, _token, "A custom aggregate takes one argument.");
                }

                Expect(TokenKind.RightParen, "')'");
                return new AggregateExpression(AggregateFunction.Custom, argument, true, null, iri) { Span = From(start) };
            }

            do
            {
                arguments.Add(ParseExpression());
            }
            while (Accept(TokenKind.Comma));

            Expect(TokenKind.RightParen, "')'");
            return new CustomFunctionCall(iri, arguments.Drain()) { Span = From(start) };
        }
        finally
        {
            arguments.Dispose();
        }
    }

    /// <summary><c>[137] ExprTripleTerm</c>: <c>TRIPLE(s, p, o)</c> when a part is a variable, a constant when ground.</summary>
    private Expression ParseExprTripleTerm()
    {
        Token open = Expect(TokenKind.TripleTermOpen, "'<<('");
        Require(SparqlVersion.Sparql12, open, "A triple term");
        Expression subject = ParseExprTripleTermPart(subjectPosition: true);
        Token verb = _token;
        Expression predicate = AcceptWord("a"u8)
            ? new ConstantExpression(RdfTypeTerm) { Span = verb.Span }
            : Is(TokenKind.Variable) ? new VariableExpression(ParseVariable()) { Span = verb.Span } : new ConstantExpression(ParseIri()) { Span = verb.Span };
        Expression @object = ParseExprTripleTermPart(subjectPosition: false);
        Expect(TokenKind.TripleTermClose, "')>>'");
        SourceSpan span = From(open);

        if (subject is ConstantExpression s && predicate is ConstantExpression p && @object is ConstantExpression o)
        {
            return new ConstantExpression(RdfTerm.TripleTerm(s.Term, p.Term, o.Term)) { Span = span };
        }

        return new FunctionCall(BuiltInFunction.Triple, AlgebraList.Of(subject, predicate, @object)) { Span = span };
    }

    private Expression ParseExprTripleTermPart(bool subjectPosition)
    {
        Token token = _token;

        switch (token.Kind)
        {
            case TokenKind.Variable:
                return new VariableExpression(ParseVariable()) { Span = token.Span };
            case TokenKind.Iri:
            case TokenKind.PrefixedName:
                return new ConstantExpression(ParseIri()) { Span = token.Span };
            case TokenKind.String when !subjectPosition:
                return new ConstantExpression(ParseRdfLiteral()) { Span = From(token) };
            case TokenKind.Integer or TokenKind.Decimal or TokenKind.Double when !subjectPosition:
                return new ConstantExpression(ParseNumericLiteral()) { Span = token.Span };
            case TokenKind.TripleTermOpen when !subjectPosition:
                return ParseExprTripleTerm();
            case TokenKind.Word when !subjectPosition && IsBooleanLiteral():
                return new ConstantExpression(ParseBooleanLiteral()) { Span = token.Span };
            default:
                throw Expected(subjectPosition ? "an IRI or a variable" : "an IRI, a literal, a variable or a triple term");
        }
    }

    // --- built-in calls and aggregates -------------------------------------

    /// <summary><c>[141] BuiltInCall</c> or <c>[76] FunctionCall</c>, as a Constraint or a GroupCondition allows.</summary>
    private Expression ParseBuiltInOrFunctionCall()
    {
        Token start = _token;

        if (Is(TokenKind.Iri) || Is(TokenKind.PrefixedName))
        {
            RdfTerm iri = ParseIri();
            return ParseCustomCall(iri, start);
        }

        if (!Is(TokenKind.Word))
        {
            throw Expected("a function call");
        }

        ReadOnlySpan<byte> word = Text(start);

        if (Ascii.EqualsIgnoreCase(word, "EXISTS"u8))
        {
            Advance();
            return new ExistsExpression(ParseGroupGraphPattern(), false) { Span = From(start) };
        }

        if (Ascii.EqualsIgnoreCase(word, "NOT"u8))
        {
            Advance();
            ExpectWord("EXISTS"u8);
            return new ExistsExpression(ParseGroupGraphPattern(), true) { Span = From(start) };
        }

        if (TryAggregate(word, out AggregateFunction aggregate))
        {
            Advance();
            return ParseAggregate(aggregate, start);
        }

        if (!TryBuiltIn(word, out BuiltInFunction function, out int minimum, out int maximum))
        {
            throw Fail(SparqlErrorKind.Syntax, start, "Unknown function '" + Encoding.UTF8.GetString(word) + "'.");
        }

        Advance();

        if (function is BuiltInFunction.LangDir or BuiltInFunction.HasLang or BuiltInFunction.HasLangDir or BuiltInFunction.StrLangDir)
        {
            Require(SparqlVersion.Sparql12Basic, start, "The function " + Encoding.UTF8.GetString(word));
        }
        else if (function is BuiltInFunction.IsTriple or BuiltInFunction.Triple or BuiltInFunction.Subject or BuiltInFunction.Predicate or BuiltInFunction.Object)
        {
            Require(SparqlVersion.Sparql12, start, "The function " + Encoding.UTF8.GetString(word));
        }

        if (function == BuiltInFunction.Bound)
        {
            Expect(TokenKind.LeftParen, "'('");
            Token variableToken = _token;
            Variable variable = ParseVariable();
            Expect(TokenKind.RightParen, "')'");
            return new FunctionCall(function, AlgebraList.Of<Expression>(new VariableExpression(variable) { Span = variableToken.Span })) { Span = From(start) };
        }

        PooledList<Expression> arguments = default;

        try
        {
            if (maximum == 0)
            {
                Expect(TokenKind.Nil, "'()'");
                return new FunctionCall(function, default) { Span = From(start) };
            }

            if (minimum == 0 && Accept(TokenKind.Nil))
            {
                return new FunctionCall(function, default) { Span = From(start) };
            }

            Expect(TokenKind.LeftParen, "'('");

            do
            {
                arguments.Add(ParseExpression());
            }
            while (Accept(TokenKind.Comma));

            Expect(TokenKind.RightParen, "')'");

            if (arguments.Count < minimum || arguments.Count > maximum)
            {
                throw Fail(SparqlErrorKind.Syntax, start, Encoding.UTF8.GetString(word) + " takes " + Arity(minimum, maximum) + ", not " + arguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            }

            return new FunctionCall(function, arguments.Drain()) { Span = From(start) };
        }
        finally
        {
            arguments.Dispose();
        }
    }

    private static string Arity(int minimum, int maximum) =>
        minimum == maximum
            ? minimum.ToString(System.Globalization.CultureInfo.InvariantCulture) + (minimum == 1 ? " argument" : " arguments")
            : maximum == int.MaxValue
                ? "at least " + minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : minimum.ToString(System.Globalization.CultureInfo.InvariantCulture) + " to " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) + " arguments";

    /// <summary><c>[147] Aggregate</c></summary>
    private AggregateExpression ParseAggregate(AggregateFunction function, Token start)
    {
        NoteAggregate(start);
        Expect(TokenKind.LeftParen, "'('");
        bool distinct = AcceptWord("DISTINCT"u8);
        Expression? argument;

        if (function == AggregateFunction.Count && Accept(TokenKind.Star))
        {
            argument = null;
        }
        else
        {
            argument = ParseAggregateArgument();
        }

        string? separator = null;

        if (function == AggregateFunction.GroupConcat && Accept(TokenKind.Semicolon))
        {
            ExpectWord("SEPARATOR"u8);
            Expect(TokenKind.Equal, "'='");
            separator = ParseShortString(out _);
        }

        Expect(TokenKind.RightParen, "')'");
        return new AggregateExpression(function, argument, distinct, separator, null) { Span = From(start) };
    }

    /// <summary>An aggregate's argument may not itself contain an aggregate (SPARQL 1.2 Query §19.7).</summary>
    private Expression ParseAggregateArgument()
    {
        bool saved = _level.InAggregate;
        _level.InAggregate = true;

        try
        {
            return ParseExpression();
        }
        finally
        {
            _level.InAggregate = saved;
        }
    }

    private void NoteAggregate(Token at)
    {
        if (_level.InAggregate)
        {
            throw Fail(SparqlErrorKind.Aggregate, at, "An aggregate inside the argument of an aggregate (SPARQL 1.2 Query §19.7).");
        }

        if (!_level.AggregatesAllowed)
        {
            throw Fail(SparqlErrorKind.Aggregate, at, "An aggregate is allowed only in SELECT, HAVING and ORDER BY (SPARQL 1.2 Query §19.7).");
        }

        _level.SawAggregate = true;
    }

    private static bool TryAggregate(ReadOnlySpan<byte> word, out AggregateFunction function)
    {
        if (Ascii.EqualsIgnoreCase(word, "COUNT"u8))
        {
            function = AggregateFunction.Count;
        }
        else if (Ascii.EqualsIgnoreCase(word, "SUM"u8))
        {
            function = AggregateFunction.Sum;
        }
        else if (Ascii.EqualsIgnoreCase(word, "MIN"u8))
        {
            function = AggregateFunction.Min;
        }
        else if (Ascii.EqualsIgnoreCase(word, "MAX"u8))
        {
            function = AggregateFunction.Max;
        }
        else if (Ascii.EqualsIgnoreCase(word, "AVG"u8))
        {
            function = AggregateFunction.Avg;
        }
        else if (Ascii.EqualsIgnoreCase(word, "SAMPLE"u8))
        {
            function = AggregateFunction.Sample;
        }
        else if (Ascii.EqualsIgnoreCase(word, "GROUP_CONCAT"u8))
        {
            function = AggregateFunction.GroupConcat;
        }
        else
        {
            function = default;
            return false;
        }

        return true;
    }

    /// <summary>The keyword functions of <c>[141]</c> with their arities.</summary>
    private static bool TryBuiltIn(ReadOnlySpan<byte> word, out BuiltInFunction function, out int minimum, out int maximum)
    {
        (function, minimum, maximum) = (default, 0, 0);

        if (word.IsEmpty)
        {
            return false;
        }

        // Grouped by the upper-cased first letter to keep each chain short.
        switch (word[0] | 0x20)
        {
            case 'a':
                return Match(word, "ABS"u8, BuiltInFunction.Abs, 1, 1, ref function, ref minimum, ref maximum);
            case 'b':
                return Match(word, "BOUND"u8, BuiltInFunction.Bound, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "BNODE"u8, BuiltInFunction.BNode, 0, 1, ref function, ref minimum, ref maximum);
            case 'c':
                return Match(word, "CEIL"u8, BuiltInFunction.Ceil, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "CONCAT"u8, BuiltInFunction.Concat, 0, int.MaxValue, ref function, ref minimum, ref maximum)
                    || Match(word, "CONTAINS"u8, BuiltInFunction.Contains, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "COALESCE"u8, BuiltInFunction.Coalesce, 0, int.MaxValue, ref function, ref minimum, ref maximum);
            case 'd':
                return Match(word, "DATATYPE"u8, BuiltInFunction.Datatype, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "DAY"u8, BuiltInFunction.Day, 1, 1, ref function, ref minimum, ref maximum);
            case 'e':
                return Match(word, "ENCODE_FOR_URI"u8, BuiltInFunction.EncodeForUri, 1, 1, ref function, ref minimum, ref maximum);
            case 'f':
                return Match(word, "FLOOR"u8, BuiltInFunction.Floor, 1, 1, ref function, ref minimum, ref maximum);
            case 'h':
                return Match(word, "HOURS"u8, BuiltInFunction.Hours, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "hasLANG"u8, BuiltInFunction.HasLang, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "hasLANGDIR"u8, BuiltInFunction.HasLangDir, 1, 1, ref function, ref minimum, ref maximum);
            case 'i':
                return Match(word, "IRI"u8, BuiltInFunction.Iri, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "IF"u8, BuiltInFunction.If, 3, 3, ref function, ref minimum, ref maximum)
                    || Match(word, "isIRI"u8, BuiltInFunction.IsIri, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "isURI"u8, BuiltInFunction.IsIri, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "isBLANK"u8, BuiltInFunction.IsBlank, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "isLITERAL"u8, BuiltInFunction.IsLiteral, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "isNUMERIC"u8, BuiltInFunction.IsNumeric, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "isTRIPLE"u8, BuiltInFunction.IsTriple, 1, 1, ref function, ref minimum, ref maximum);
            case 'l':
                return Match(word, "LANG"u8, BuiltInFunction.Lang, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "LANGMATCHES"u8, BuiltInFunction.LangMatches, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "LANGDIR"u8, BuiltInFunction.LangDir, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "LCASE"u8, BuiltInFunction.LCase, 1, 1, ref function, ref minimum, ref maximum);
            case 'm':
                return Match(word, "MONTH"u8, BuiltInFunction.Month, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "MINUTES"u8, BuiltInFunction.Minutes, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "MD5"u8, BuiltInFunction.Md5, 1, 1, ref function, ref minimum, ref maximum);
            case 'n':
                return Match(word, "NOW"u8, BuiltInFunction.Now, 0, 0, ref function, ref minimum, ref maximum);
            case 'o':
                return Match(word, "OBJECT"u8, BuiltInFunction.Object, 1, 1, ref function, ref minimum, ref maximum);
            case 'p':
                return Match(word, "PREDICATE"u8, BuiltInFunction.Predicate, 1, 1, ref function, ref minimum, ref maximum);
            case 'r':
                return Match(word, "RAND"u8, BuiltInFunction.Rand, 0, 0, ref function, ref minimum, ref maximum)
                    || Match(word, "ROUND"u8, BuiltInFunction.Round, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "REPLACE"u8, BuiltInFunction.Replace, 3, 4, ref function, ref minimum, ref maximum)
                    || Match(word, "REGEX"u8, BuiltInFunction.Regex, 2, 3, ref function, ref minimum, ref maximum);
            case 's':
                return Match(word, "STR"u8, BuiltInFunction.Str, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "STRLEN"u8, BuiltInFunction.StrLen, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "SUBSTR"u8, BuiltInFunction.Substr, 2, 3, ref function, ref minimum, ref maximum)
                    || Match(word, "STRSTARTS"u8, BuiltInFunction.StrStarts, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "STRENDS"u8, BuiltInFunction.StrEnds, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "STRBEFORE"u8, BuiltInFunction.StrBefore, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "STRAFTER"u8, BuiltInFunction.StrAfter, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "SECONDS"u8, BuiltInFunction.Seconds, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "STRUUID"u8, BuiltInFunction.StrUuid, 0, 0, ref function, ref minimum, ref maximum)
                    || Match(word, "SHA1"u8, BuiltInFunction.Sha1, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "SHA256"u8, BuiltInFunction.Sha256, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "SHA384"u8, BuiltInFunction.Sha384, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "SHA512"u8, BuiltInFunction.Sha512, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "STRLANG"u8, BuiltInFunction.StrLang, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "STRLANGDIR"u8, BuiltInFunction.StrLangDir, 3, 3, ref function, ref minimum, ref maximum)
                    || Match(word, "STRDT"u8, BuiltInFunction.StrDt, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "sameTerm"u8, BuiltInFunction.SameTerm, 2, 2, ref function, ref minimum, ref maximum)
                    || Match(word, "SUBJECT"u8, BuiltInFunction.Subject, 1, 1, ref function, ref minimum, ref maximum);
            case 't':
                return Match(word, "TIMEZONE"u8, BuiltInFunction.Timezone, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "TZ"u8, BuiltInFunction.Tz, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "TRIPLE"u8, BuiltInFunction.Triple, 3, 3, ref function, ref minimum, ref maximum);
            case 'u':
                return Match(word, "URI"u8, BuiltInFunction.Iri, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "UCASE"u8, BuiltInFunction.UCase, 1, 1, ref function, ref minimum, ref maximum)
                    || Match(word, "UUID"u8, BuiltInFunction.Uuid, 0, 0, ref function, ref minimum, ref maximum);
            case 'y':
                return Match(word, "YEAR"u8, BuiltInFunction.Year, 1, 1, ref function, ref minimum, ref maximum);
            default:
                return false;
        }
    }

    private static bool Match(
        ReadOnlySpan<byte> word, ReadOnlySpan<byte> keyword, BuiltInFunction candidate, int min, int max,
        ref BuiltInFunction function, ref int minimum, ref int maximum)
    {
        if (!Ascii.EqualsIgnoreCase(word, keyword))
        {
            return false;
        }

        function = candidate;
        minimum = min;
        maximum = max;
        return true;
    }
}
