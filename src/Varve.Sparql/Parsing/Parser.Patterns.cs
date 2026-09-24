// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>Group graph patterns, triples blocks and their nodes, and inline data (<c>docs/spec/sparql-algebra.md</c> §3.4, §4.5).</summary>
internal ref partial struct Parser
{
    // --- groups ------------------------------------------------------------

    /// <summary><c>[55] GroupGraphPattern</c> translated: the group with its own filters applied.</summary>
    private QueryPattern ParseGroupGraphPattern()
    {
        Token open = _token;
        QueryPattern pattern = ParseGroup(out Expression? filter);
        return filter is null ? pattern : new Filter(filter, pattern) { Span = From(open) };
    }

    /// <summary>
    /// A group and, separately, the conjunction of its own FILTERs, so that
    /// OPTIONAL can take the filter as its condition (§18.3.2.7 with
    /// simplification last, §4.5 of the algebra specification).
    /// </summary>
    private QueryPattern ParseGroup(out Expression? filter)
    {
        Token open = Expect(TokenKind.LeftBrace, "'{'");

        if (IsWord("SELECT"u8))
        {
            QueryPattern sub = ParseSubSelect();
            Expect(TokenKind.RightBrace, "'}'");
            filter = null;
            return sub;
        }

        int previousGroup = EnterGroup();
        QueryPattern? savedGroup = _group;
        PooledList<TriplePattern> savedTriples = _triples;
        _group = null;
        _triples = default;
        PooledList<Expression> filters = default;
        TripleMode mode = TripleMode.Pattern;

        try
        {
            while (!Is(TokenKind.RightBrace))
            {
                if (StartsTriples())
                {
                    ParseTriplesBlock(in mode);

                    // [56]: one TriplesBlock, then a '.' or another kind of element.
                    if (StartsTriples())
                    {
                        throw Expected("'.' between triples, or '}'");
                    }

                    continue;
                }

                Token at = _token;

                if (AcceptWord("OPTIONAL"u8))
                {
                    CloseLabelScope();
                    QueryPattern body = ParseGroup(out Expression? condition);
                    _group = new LeftJoin(_group ?? Empty(at), body, condition) { Span = From(at) };
                }
                else if (AcceptWord("MINUS"u8))
                {
                    CloseLabelScope();
                    QueryPattern body = ParseGroupGraphPattern();
                    _group = new Minus(_group ?? Empty(at), body) { Span = From(at) };
                }
                else if (AcceptWord("BIND"u8))
                {
                    Expect(TokenKind.LeftParen, "'('");
                    Expression expression = ParseExpression();
                    ExpectWord("AS"u8);
                    Token variableToken = _token;
                    Variable variable = ParseVariable();
                    Expect(TokenKind.RightParen, "')'");
                    CheckBindScope(variable, variableToken);
                    CloseLabelScope();
                    _group = new Extend(_group ?? Empty(at), variable, expression) { Span = From(at) };
                }
                else if (AcceptWord("VALUES"u8))
                {
                    CloseLabelScope();
                    AddToGroup(ParseDataBlock(at));
                }
                else if (AcceptWord("GRAPH"u8))
                {
                    CloseLabelScope();
                    PatternTerm name = ParseVarOrIri();
                    QueryPattern body = ParseGroupGraphPattern();
                    AddToGroup(new Graph(name, body) { Span = From(at) });
                }
                else if (AcceptWord("SERVICE"u8))
                {
                    CloseLabelScope();
                    bool silent = AcceptWord("SILENT"u8);
                    PatternTerm name = ParseVarOrIri();
                    QueryPattern body = ParseGroupGraphPattern();
                    AddToGroup(new Service(name, body, silent) { Span = From(at) });
                }
                else if (AcceptWord("FILTER"u8))
                {
                    filters.Add(ParseConstraint());
                }
                else if (Is(TokenKind.LeftBrace))
                {
                    CloseLabelScope();
                    QueryPattern union = ParseGroupGraphPattern();

                    while (AcceptWord("UNION"u8))
                    {
                        union = new Union(union, ParseGroupGraphPattern()) { Span = From(at) };
                    }

                    AddToGroup(union);
                }
                else
                {
                    throw Expected("a triple pattern, OPTIONAL, MINUS, BIND, VALUES, GRAPH, SERVICE, FILTER, '{' or '}'");
                }

                Accept(TokenKind.Dot);
            }

            Advance();
            FlushTriples();

            filter = null;

            foreach (Expression condition in filters.Span)
            {
                filter = filter is null ? condition : new BinaryExpression(BinaryOperator.And, filter, condition) { Span = condition.Span };
            }

            return _group ?? Empty(open);
        }
        finally
        {
            filters.Dispose();
            _triples.Dispose();
            _triples = savedTriples;
            _group = savedGroup;
            LeaveGroup(previousGroup);
        }
    }

    private static Bgp Empty(Token at) => new(default) { Span = at.Span };

    /// <summary>Joins an element into the group; an empty BGP is the identity and is dropped (§18.3.2.9).</summary>
    private void AddToGroup(QueryPattern element)
    {
        if (element is Bgp { Triples.IsEmpty: true })
        {
            return;
        }

        _group = _group is null ? element : new Join(_group, element) { Span = new SourceSpan(_group.Span.Start, element.Span.End, _group.Span.Line, _group.Span.Column) };
    }

    private void FlushTriples()
    {
        if (_triples.Count == 0)
        {
            return;
        }

        TriplePattern first = _triples[0];
        TriplePattern last = _triples[_triples.Count - 1];
        AddToGroup(new Bgp(_triples.Drain()) { Span = new SourceSpan(first.Span.Start, last.Span.End, first.Span.Line, first.Span.Column) });
    }

    /// <summary>
    /// A non-triples element closes the run of triples before it for blank
    /// node labels (grammar §3.6): a label written after this point is in a
    /// separate basic graph pattern, which is what the 1.0 suite's "OPTIONAL
    /// breaks BGP" cases pin. A property path does not close it, nor does
    /// FILTER: the run of triples and paths is one scope, as the suites'
    /// collections with paths inside them require.
    /// </summary>
    private void CloseLabelScope()
    {
        FlushTriples();
        _groupId = ++_nextGroupId;
    }

    /// <summary>BIND's variable must not be in scope from the preceding elements of the group, the pending triples included.</summary>
    private readonly void CheckBindScope(Variable variable, Token at)
    {
        PooledList<Variable> scope = default;

        try
        {
            if (_group is not null)
            {
                CollectScope(_group, ref scope);
            }

            foreach (TriplePattern triple in _triples.Span)
            {
                CollectScope(triple.Subject, ref scope);
                CollectScope(triple.Predicate, ref scope);
                CollectScope(triple.Object, ref scope);
            }

            if (Contains(scope.Span, variable))
            {
                throw Fail(SparqlErrorKind.VariableScope, at, "The variable " + variable + " is already in scope in the group and cannot be bound with BIND (SPARQL 1.2 Query §18.3.1).");
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    // --- triples blocks ----------------------------------------------------

    private readonly bool StartsTriples() => _token.Kind switch
    {
        TokenKind.Variable or TokenKind.Iri or TokenKind.PrefixedName or TokenKind.BlankNodeLabel or TokenKind.Anon
            or TokenKind.Nil or TokenKind.String or TokenKind.Integer or TokenKind.Decimal or TokenKind.Double
            or TokenKind.LeftBracket or TokenKind.LeftParen or TokenKind.ReifiedOpen or TokenKind.TripleTermOpen => true,
        TokenKind.Word => IsBooleanLiteral(),
        _ => false,
    };

    /// <summary><c>[57] TriplesBlock ::= TriplesSameSubjectPath ( '.' TriplesBlock? )?</c></summary>
    private void ParseTriplesBlock(in TripleMode mode)
    {
        while (true)
        {
            ParseTriplesSameSubject(in mode);

            if (!Accept(TokenKind.Dot) || !StartsTriples())
            {
                return;
            }
        }
    }

    /// <summary><c>[54] TriplesTemplate ::= TriplesSameSubject ( '.' TriplesTemplate? )?</c></summary>
    private void ParseTriplesTemplate(in TripleMode mode) => ParseTriplesBlock(in mode);

    /// <summary><c>[81] TriplesSameSubject</c> and <c>[87] TriplesSameSubjectPath</c>.</summary>
    private void ParseTriplesSameSubject(in TripleMode mode)
    {
        if (Is(TokenKind.LeftBracket) || Is(TokenKind.LeftParen))
        {
            PatternTerm node = ParseTriplesNode(in mode);

            if (StartsVerb(in mode))
            {
                ParsePropertyListNotEmpty(node, in mode);
            }

            return;
        }

        if (Is(TokenKind.ReifiedOpen))
        {
            PatternTerm reifier = ParseReifiedTriple(in mode);

            if (StartsVerb(in mode))
            {
                ParsePropertyListNotEmpty(reifier, in mode);
            }

            return;
        }

        PatternTerm subject = ParseVarOrTerm(in mode);
        ParsePropertyListNotEmpty(subject, in mode);
    }

    private readonly bool StartsVerb(in TripleMode mode)
    {
        if (Is(TokenKind.Variable) || Is(TokenKind.Iri) || Is(TokenKind.PrefixedName) || IsWord("a"u8))
        {
            return true;
        }

        return mode.AllowPaths && (Is(TokenKind.Caret) || Is(TokenKind.Bang) || Is(TokenKind.LeftParen));
    }

    /// <summary><c>[83] PropertyListNotEmpty</c> and <c>[89] PropertyListPathNotEmpty</c>: verbs, object lists, semicolons.</summary>
    private void ParsePropertyListNotEmpty(PatternTerm subject, in TripleMode mode)
    {
        while (true)
        {
            Token verbToken = _token;
            PatternTerm? predicate = null;
            PropertyPath? path = null;
            bool inverse = false;

            if (Is(TokenKind.Variable))
            {
                predicate = new VariablePattern(ParseVariable()) { Span = verbToken.Span };
            }
            else if (mode.AllowPaths)
            {
                PropertyPath parsed = ParsePath();

                switch (parsed)
                {
                    case PredicatePath simple:
                        predicate = new TermPattern(simple.Predicate) { Span = simple.Span };
                        break;
                    case InversePath { Inner: PredicatePath inner }:
                        predicate = new TermPattern(inner.Predicate) { Span = inner.Span };
                        inverse = true;
                        break;
                    default:
                        path = parsed;
                        break;
                }
            }
            else
            {
                predicate = ParseVerb();
            }

            // Object list.
            while (true)
            {
                PatternTerm @object = ParseGraphNode(in mode);

                if (path is not null)
                {
                    FlushTriples();
                    AddToGroup(new PathPattern(subject, path, @object) { Span = new SourceSpan(subject.Span.Start, _lastEnd, subject.Span.Line, subject.Span.Column) });

                    if (Is(TokenKind.Tilde) || Is(TokenKind.AnnotationOpen))
                    {
                        throw Fail(SparqlErrorKind.Syntax, _token, "A reifier or annotation is allowed only after a triple whose predicate is an IRI, 'a' or a variable, not after a property path (SPARQL 1.2 Query §19.7).");
                    }
                }
                else if (inverse)
                {
                    Emit(@object, predicate!, subject);

                    if (Is(TokenKind.Tilde) || Is(TokenKind.AnnotationOpen))
                    {
                        throw Fail(SparqlErrorKind.Syntax, _token, "A reifier or annotation is allowed only after a triple whose predicate is an IRI, 'a' or a variable, not after an inverse path (SPARQL 1.2 Query §19.7).");
                    }
                }
                else
                {
                    Emit(subject, predicate!, @object);
                    ParseAnnotations(subject, predicate!, @object, in mode);
                }

                if (!Accept(TokenKind.Comma))
                {
                    break;
                }
            }

            if (!Accept(TokenKind.Semicolon))
            {
                return;
            }

            // A trailing ';' before '.' or ']' is allowed.
            if (!StartsVerb(in mode))
            {
                return;
            }
        }
    }

    /// <summary><c>[84] Verb ::= VarOrIri | 'a'</c></summary>
    private PatternTerm ParseVerb()
    {
        Token token = _token;

        if (AcceptWord("a"u8))
        {
            return new TermPattern(RdfTypeTerm) { Span = token.Span };
        }

        return ParseVarOrIri();
    }

    /// <summary><c>[125] VarOrIri ::= Var | iri</c></summary>
    private PatternTerm ParseVarOrIri()
    {
        Token token = _token;

        if (Is(TokenKind.Variable))
        {
            return new VariablePattern(ParseVariable()) { Span = token.Span };
        }

        if (Is(TokenKind.Iri) || Is(TokenKind.PrefixedName))
        {
            return new TermPattern(ParseIri()) { Span = token.Span };
        }

        throw Expected("a variable or an IRI");
    }

    private void Emit(PatternTerm subject, PatternTerm predicate, PatternTerm @object) =>
        _triples.Add(new TriplePattern(subject, predicate, @object)
        {
            Span = new SourceSpan(subject.Span.Start, Math.Max(@object.Span.End, _lastEnd), subject.Span.Line, subject.Span.Column),
        });

    // --- nodes -------------------------------------------------------------

    /// <summary><c>[115] VarOrTerm</c>: a variable, an IRI, a literal, a blank node, NIL, or a triple term.</summary>
    private PatternTerm ParseVarOrTerm(in TripleMode mode)
    {
        Token token = _token;

        switch (token.Kind)
        {
            case TokenKind.Variable:
                if (!mode.AllowVariables)
                {
                    throw Fail(SparqlErrorKind.Syntax, token, "A variable is not allowed in INSERT DATA or DELETE DATA (SPARQL 1.2 Query §19.7).");
                }

                return new VariablePattern(ParseVariable()) { Span = token.Span };
            case TokenKind.Iri:
            case TokenKind.PrefixedName:
                return new TermPattern(ParseIri()) { Span = token.Span };
            case TokenKind.String:
                return new TermPattern(ParseRdfLiteral()) { Span = From(token) };
            case TokenKind.Integer:
            case TokenKind.Decimal:
            case TokenKind.Double:
                return new TermPattern(ParseNumericLiteral()) { Span = token.Span };
            case TokenKind.BlankNodeLabel:
                return ParseBlankNodeLabel(in mode);
            case TokenKind.Anon:
                Advance();
                return FreshBlankNode(in mode, token);
            case TokenKind.Nil:
                Advance();
                return new TermPattern(RdfNilTerm) { Span = token.Span };
            case TokenKind.TripleTermOpen:
                return ParseTripleTerm(in mode);
            case TokenKind.Word when IsBooleanLiteral():
                return new TermPattern(ParseBooleanLiteral()) { Span = token.Span };
            default:
                throw Expected("a term");
        }
    }

    /// <summary><c>[113] GraphNode ::= VarOrTerm | TriplesNode | ReifiedTriple</c>, and its path form.</summary>
    private PatternTerm ParseGraphNode(in TripleMode mode)
    {
        if (Is(TokenKind.LeftBracket) || Is(TokenKind.LeftParen))
        {
            return ParseTriplesNode(in mode);
        }

        if (Is(TokenKind.ReifiedOpen))
        {
            return ParseReifiedTriple(in mode);
        }

        return ParseVarOrTerm(in mode);
    }

    /// <summary><c>[103] TriplesNode ::= Collection | BlankNodePropertyList</c>, emitting the triples they stand for.</summary>
    private BlankNodePattern ParseTriplesNode(in TripleMode mode)
    {
        Token open = _token;

        if (Accept(TokenKind.LeftBracket))
        {
            BlankNodePattern node = FreshBlankNode(in mode, open);
            ParsePropertyListNotEmpty(node, in mode);
            Expect(TokenKind.RightBracket, "']'");
            return node;
        }

        Expect(TokenKind.LeftParen, "'('");

        // Collection: rdf:first / rdf:rest cells, the head is the node.
        BlankNodePattern head = FreshBlankNode(in mode, open);
        BlankNodePattern cell = head;

        while (true)
        {
            PatternTerm element = ParseGraphNode(in mode);
            Emit(cell, new TermPattern(RdfFirstTerm) { Span = cell.Span }, element);

            if (Accept(TokenKind.RightParen))
            {
                Emit(cell, new TermPattern(RdfRestTerm) { Span = cell.Span }, new TermPattern(RdfNilTerm) { Span = cell.Span });
                return head;
            }

            BlankNodePattern next = FreshBlankNode(in mode, _token);
            Emit(cell, new TermPattern(RdfRestTerm) { Span = cell.Span }, next);
            cell = next;
        }
    }

    // --- SPARQL 1.2 triple terms, reified triples, annotations -------------

    /// <summary><c>[119] TripleTerm ::= '&lt;&lt;(' TripleTermSubject Verb TripleTermObject ')&gt;&gt;'</c></summary>
    private PatternTerm ParseTripleTerm(in TripleMode mode)
    {
        Token open = Expect(TokenKind.TripleTermOpen, "'<<('");
        Require(SparqlVersion.Sparql12, open, "A triple term");
        PatternTerm subject = ParseTripleTermPart(in mode);
        PatternTerm predicate = ParseVerb();
        PatternTerm @object = ParseTripleTermPart(in mode);
        Expect(TokenKind.TripleTermClose, "')>>'");
        return MakeTripleTerm(subject, predicate, @object, From(open));
    }

    private static PatternTerm MakeTripleTerm(PatternTerm subject, PatternTerm predicate, PatternTerm @object, SourceSpan span)
    {
        if (subject is TermPattern s && predicate is TermPattern p && @object is TermPattern o)
        {
            return new TermPattern(RdfTerm.TripleTerm(s.Term, p.Term, o.Term)) { Span = span };
        }

        return new TripleTermPattern(subject, predicate, @object) { Span = span };
    }

    /// <summary><c>[120]</c>/<c>[121]</c>: a term, a variable, a blank node, or a nested triple term.</summary>
    private PatternTerm ParseTripleTermPart(in TripleMode mode)
    {
        if (Is(TokenKind.Nil) || Is(TokenKind.LeftBracket) || Is(TokenKind.LeftParen) || Is(TokenKind.ReifiedOpen))
        {
            throw Expected("a term inside a triple term: a variable, an IRI, a literal, a blank node or a nested triple term");
        }

        return ParseVarOrTerm(in mode);
    }

    /// <summary>
    /// <c>[116] ReifiedTriple ::= '&lt;&lt;' ReifiedTripleSubject Verb ReifiedTripleObject Reifier? '&gt;&gt;'</c>,
    /// expanded per §4.3.1: the result is the reifier, and the reifying triple is emitted.
    /// </summary>
    private PatternTerm ParseReifiedTriple(in TripleMode mode)
    {
        Token open = Expect(TokenKind.ReifiedOpen, "'<<'");
        Require(SparqlVersion.Sparql12, open, "A reified triple");
        PatternTerm subject = ParseReifiedTriplePart(in mode);
        PatternTerm predicate = ParseVerb();
        PatternTerm @object = ParseReifiedTriplePart(in mode);
        PatternTerm reifier = Is(TokenKind.Tilde) ? ParseReifier(in mode) : FreshBlankNode(in mode, open);
        Expect(TokenKind.ReifiedClose, "'>>'");
        Emit(reifier, new TermPattern(RdfReifiesTerm) { Span = open.Span }, MakeTripleTerm(subject, predicate, @object, From(open)));
        return reifier;
    }

    /// <summary><c>[117]</c>/<c>[118]</c>: as a triple term part, plus a nested reified triple.</summary>
    private PatternTerm ParseReifiedTriplePart(in TripleMode mode)
    {
        if (Is(TokenKind.ReifiedOpen))
        {
            return ParseReifiedTriple(in mode);
        }

        return ParseTripleTermPart(in mode);
    }

    /// <summary><c>[70] Reifier ::= '~' VarOrReifierId?</c></summary>
    private PatternTerm ParseReifier(in TripleMode mode)
    {
        Token tilde = Expect(TokenKind.Tilde, "'~'");
        Require(SparqlVersion.Sparql12, tilde, "A reifier");

        if (Is(TokenKind.Variable) || Is(TokenKind.Iri) || Is(TokenKind.PrefixedName) || Is(TokenKind.BlankNodeLabel) || Is(TokenKind.Anon))
        {
            return ParseVarOrTerm(in mode);
        }

        return FreshBlankNode(in mode, tilde);
    }

    /// <summary><c>[111] Annotation ::= ( Reifier | AnnotationBlock )*</c> after an object, expanded per §4.3.2.</summary>
    private void ParseAnnotations(PatternTerm subject, PatternTerm predicate, PatternTerm @object, in TripleMode mode)
    {
        PatternTerm? pendingReifier = null;

        while (true)
        {
            Token at = _token;

            if (Is(TokenKind.Tilde))
            {
                PatternTerm reifier = ParseReifier(in mode);
                Emit(reifier, new TermPattern(RdfReifiesTerm) { Span = at.Span }, MakeTripleTerm(subject, predicate, @object, From(at)));
                pendingReifier = reifier;
            }
            else if (Accept(TokenKind.AnnotationOpen))
            {
                Require(SparqlVersion.Sparql12, at, "An annotation block");
                PatternTerm reifier;

                if (pendingReifier is not null)
                {
                    reifier = pendingReifier;
                    pendingReifier = null;
                }
                else
                {
                    reifier = FreshBlankNode(in mode, at);
                    Emit(reifier, new TermPattern(RdfReifiesTerm) { Span = at.Span }, MakeTripleTerm(subject, predicate, @object, From(at)));
                }

                ParsePropertyListNotEmpty(reifier, in mode);
                Expect(TokenKind.AnnotationClose, "'|}'");
            }
            else
            {
                return;
            }
        }
    }

    // --- inline data -------------------------------------------------------

    /// <summary><c>[66] DataBlock ::= InlineDataOneVar | InlineDataFull</c></summary>
    private Values ParseDataBlock(Token start)
    {
        PooledList<Variable> variables = default;
        PooledList<AlgebraList<RdfTerm?>> rows = default;
        PooledList<RdfTerm?> row = default;

        try
        {
            if (Is(TokenKind.Variable))
            {
                variables.Add(ParseVariable());
                Expect(TokenKind.LeftBrace, "'{'");

                while (!Accept(TokenKind.RightBrace))
                {
                    row.Add(ParseDataBlockValue());
                    rows.Add(row.Drain());
                }
            }
            else
            {
                if (!Accept(TokenKind.Nil))
                {
                    Expect(TokenKind.LeftParen, "'(' or a variable");

                    while (!Accept(TokenKind.RightParen))
                    {
                        Token at = _token;
                        Variable variable = ParseVariable();

                        if (Contains(variables.Span, variable))
                        {
                            throw Fail(SparqlErrorKind.Values, at, "The variable " + variable + " is listed twice in VALUES (SPARQL 1.2 Query §19.7).");
                        }

                        variables.Add(variable);
                    }
                }

                Expect(TokenKind.LeftBrace, "'{'");

                while (!Accept(TokenKind.RightBrace))
                {
                    Token rowToken = _token;

                    if (!Accept(TokenKind.Nil))
                    {
                        Expect(TokenKind.LeftParen, "'(' to begin a row");

                        while (!Accept(TokenKind.RightParen))
                        {
                            row.Add(ParseDataBlockValue());
                        }
                    }

                    if (row.Count != variables.Count)
                    {
                        throw Fail(SparqlErrorKind.Values, rowToken, "A VALUES row has " + row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " values for " + variables.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " variables (SPARQL 1.2 Query §19.7).");
                    }

                    rows.Add(row.Drain());
                }
            }

            return new Values(variables.Drain(), rows.Drain()) { Span = From(start) };
        }
        finally
        {
            variables.Dispose();
            rows.Dispose();
            row.Dispose();
        }
    }

    /// <summary><c>[69] DataBlockValue ::= iri | RDFLiteral | NumericLiteral | BooleanLiteral | 'UNDEF' | TripleTermData</c></summary>
    private RdfTerm? ParseDataBlockValue()
    {
        Token token = _token;

        switch (token.Kind)
        {
            case TokenKind.Iri:
            case TokenKind.PrefixedName:
                return ParseIri();
            case TokenKind.String:
                return ParseRdfLiteral();
            case TokenKind.Integer:
            case TokenKind.Decimal:
            case TokenKind.Double:
                return ParseNumericLiteral();
            case TokenKind.TripleTermOpen:
                return ParseTripleTermData();
            case TokenKind.Word when IsBooleanLiteral():
                return ParseBooleanLiteral();
            case TokenKind.Word when AcceptWord("UNDEF"u8):
                return null;
            default:
                throw Expected("an IRI, a literal, UNDEF or a triple term");
        }
    }

    /// <summary><c>[122] TripleTermData ::= '&lt;&lt;(' TripleTermDataSubject ( iri | 'a' ) TripleTermDataObject ')&gt;&gt;'</c></summary>
    private RdfTerm ParseTripleTermData()
    {
        Token open = Expect(TokenKind.TripleTermOpen, "'<<('");
        Require(SparqlVersion.Sparql12, open, "A triple term");
        RdfTerm subject = ParseIri();
        RdfTerm predicate = AcceptWord("a"u8) ? RdfTypeTerm : ParseIri();
        RdfTerm @object = _token.Kind switch
        {
            TokenKind.Iri or TokenKind.PrefixedName => ParseIri(),
            TokenKind.String => ParseRdfLiteral(),
            TokenKind.Integer or TokenKind.Decimal or TokenKind.Double => ParseNumericLiteral(),
            TokenKind.TripleTermOpen => ParseTripleTermData(),
            TokenKind.Word when IsBooleanLiteral() => ParseBooleanLiteral(),
            _ => throw Expected("an IRI, a literal or a triple term"),
        };
        Expect(TokenKind.TripleTermClose, "')>>'");
        return RdfTerm.TripleTerm(subject, predicate, @object);
    }
}
