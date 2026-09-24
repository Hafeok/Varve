// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>SPARQL Update, <c>[31]</c> to <c>[54]</c>: a record of the request (<c>docs/spec/sparql-algebra.md</c> §5).</summary>
internal ref partial struct Parser
{
    /// <summary><c>[3] UpdateUnit ::= Update</c>, where <c>[31] Update ::= Prologue ( Update1 ( ';' Update )? )?</c>.</summary>
    internal Update ParseUpdateUnit()
    {
        Token start = _token;
        PooledList<UpdateOperation> operations = default;

        try
        {
            Prologue prologue = ParsePrologue();

            while (!Is(TokenKind.End))
            {
                operations.Add(ParseUpdate1());

                if (!Accept(TokenKind.Semicolon))
                {
                    break;
                }

                // Another prologue may follow the semicolon: its prefixes add
                // to the first, and the tree keeps one prologue with them all.
                prologue = ParsePrologue();
            }

            if (!Is(TokenKind.End))
            {
                throw Expected("the end of the request");
            }

            return new Update(prologue, operations.Drain()) { Span = From(start) };
        }
        finally
        {
            operations.Dispose();
        }
    }

    /// <summary><c>[32] Update1</c></summary>
    private UpdateOperation ParseUpdate1()
    {
        Token start = _token;

        if (AcceptWord("LOAD"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            RdfTerm source = ParseIri();
            RdfTerm? graph = null;

            if (AcceptWord("INTO"u8))
            {
                ExpectWord("GRAPH"u8);
                graph = ParseIri();
            }

            return new Load(source, graph, silent) { Span = From(start) };
        }

        if (AcceptWord("CLEAR"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            return new Clear(ParseGraphRefAll(), silent) { Span = From(start) };
        }

        if (AcceptWord("DROP"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            return new Drop(ParseGraphRefAll(), silent) { Span = From(start) };
        }

        if (AcceptWord("CREATE"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            ExpectWord("GRAPH"u8);
            return new Create(ParseIri(), silent) { Span = From(start) };
        }

        if (AcceptWord("ADD"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            GraphOrDefault from = ParseGraphOrDefault();
            ExpectWord("TO"u8);
            return new Add(from, ParseGraphOrDefault(), silent) { Span = From(start) };
        }

        if (AcceptWord("MOVE"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            GraphOrDefault from = ParseGraphOrDefault();
            ExpectWord("TO"u8);
            return new Move(from, ParseGraphOrDefault(), silent) { Span = From(start) };
        }

        if (AcceptWord("COPY"u8))
        {
            bool silent = AcceptWord("SILENT"u8);
            GraphOrDefault from = ParseGraphOrDefault();
            ExpectWord("TO"u8);
            return new Copy(from, ParseGraphOrDefault(), silent) { Span = From(start) };
        }

        if (AcceptWord("INSERT"u8))
        {
            if (AcceptWord("DATA"u8))
            {
                int previousGroup = EnterGroup();
                AlgebraList<QuadPattern> quads = ParseQuads(TripleMode.Data(allowBlankNodes: true));
                LeaveGroup(previousGroup);
                return new InsertData(quads) { Span = From(start) };
            }

            AlgebraList<QuadPattern> insert = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: true));
            return ParseModifyTail(start, null, default, insert);
        }

        if (AcceptWord("DELETE"u8))
        {
            if (AcceptWord("DATA"u8))
            {
                int previousGroup = EnterGroup();
                AlgebraList<QuadPattern> quads = ParseQuads(TripleMode.Data(allowBlankNodes: false));
                LeaveGroup(previousGroup);
                return new DeleteData(quads) { Span = From(start) };
            }

            if (AcceptWord("WHERE"u8))
            {
                int previousGroup = EnterGroup();
                AlgebraList<QuadPattern> quads = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: false));
                LeaveGroup(previousGroup);
                return new DeleteWhere(quads) { Span = From(start) };
            }

            AlgebraList<QuadPattern> delete = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: false));
            AlgebraList<QuadPattern> insert = default;

            if (AcceptWord("INSERT"u8))
            {
                insert = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: true));
            }

            return ParseModifyTail(start, null, delete, insert);
        }

        if (AcceptWord("WITH"u8))
        {
            RdfTerm with = ParseIri();
            AlgebraList<QuadPattern> delete = default;
            AlgebraList<QuadPattern> insert = default;

            if (AcceptWord("DELETE"u8))
            {
                delete = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: false));

                if (AcceptWord("INSERT"u8))
                {
                    insert = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: true));
                }
            }
            else
            {
                ExpectWord("INSERT"u8);
                insert = ParseQuads(TripleMode.Template(allowVariables: true, allowBlankNodes: true));
            }

            return ParseModifyTail(start, with, delete, insert);
        }

        throw Expected("an update operation: LOAD, CLEAR, DROP, CREATE, ADD, MOVE, COPY, INSERT, DELETE or WITH");
    }

    /// <summary>The rest of <c>[43] Modify</c>: <c>UsingClause* 'WHERE' GroupGraphPattern</c>.</summary>
    private Modify ParseModifyTail(Token start, RdfTerm? with, AlgebraList<QuadPattern> delete, AlgebraList<QuadPattern> insert)
    {
        DatasetSpec? @using = ParseUsingClauses();
        ExpectWord("WHERE"u8);

        Level saved = _level;
        _level = default;

        try
        {
            if (!Is(TokenKind.LeftBrace))
            {
                throw Expected("'{' to begin the WHERE clause");
            }

            QueryPattern where = ParseGroupGraphPattern();
            return new Modify(with, delete, insert, @using, where) { Span = From(start) };
        }
        finally
        {
            _level = saved;
        }
    }

    /// <summary><c>[46] UsingClause ::= 'USING' ( iri | 'NAMED' iri )</c>, repeated.</summary>
    private DatasetSpec? ParseUsingClauses()
    {
        if (!IsWord("USING"u8))
        {
            return null;
        }

        Token start = _token;
        PooledList<RdfTerm> defaults = default;
        PooledList<RdfTerm> named = default;

        try
        {
            while (AcceptWord("USING"u8))
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

    /// <summary><c>[49] GraphRefAll ::= GraphRef | 'DEFAULT' | 'NAMED' | 'ALL'</c></summary>
    private GraphTarget ParseGraphRefAll()
    {
        if (AcceptWord("DEFAULT"u8))
        {
            return GraphTarget.Default;
        }

        if (AcceptWord("NAMED"u8))
        {
            return GraphTarget.Named;
        }

        if (AcceptWord("ALL"u8))
        {
            return GraphTarget.All;
        }

        ExpectWord("GRAPH"u8);
        return GraphTarget.Of(ParseIri());
    }

    /// <summary><c>[47] GraphOrDefault ::= 'DEFAULT' | 'GRAPH'? iri</c></summary>
    private GraphOrDefault ParseGraphOrDefault()
    {
        if (AcceptWord("DEFAULT"u8))
        {
            return GraphOrDefault.Default;
        }

        AcceptWord("GRAPH"u8);
        return GraphOrDefault.Of(ParseIri());
    }

    /// <summary>
    /// <c>[50] QuadPattern</c> and <c>[51] QuadData</c>, both <c>'{' Quads '}'</c> with
    /// <c>[52] Quads ::= TriplesTemplate? ( QuadsNotTriples '.'? TriplesTemplate? )*</c>.
    /// </summary>
    private AlgebraList<QuadPattern> ParseQuads(TripleMode mode)
    {
        Expect(TokenKind.LeftBrace, "'{'");
        PooledList<TriplePattern> savedTriples = _triples;
        _triples = default;
        PooledList<QuadPattern> quads = default;

        try
        {
            if (StartsTriples())
            {
                ParseTriplesTemplate(in mode);
                MoveTriplesToQuads(ref quads, null);
            }

            while (!Accept(TokenKind.RightBrace))
            {
                Token at = _token;
                ExpectWord("GRAPH"u8);
                PatternTerm graph = ParseVarOrIri();

                if (graph is VariablePattern && !mode.AllowVariables)
                {
                    throw Fail(SparqlErrorKind.Syntax, at, "A variable is not allowed as the graph of INSERT DATA or DELETE DATA (SPARQL 1.2 Query §19.7).");
                }

                Expect(TokenKind.LeftBrace, "'{'");

                if (StartsTriples())
                {
                    ParseTriplesTemplate(in mode);
                }

                Expect(TokenKind.RightBrace, "'}'");
                MoveTriplesToQuads(ref quads, graph);
                Accept(TokenKind.Dot);

                if (StartsTriples())
                {
                    ParseTriplesTemplate(in mode);
                    MoveTriplesToQuads(ref quads, null);
                }
            }

            return quads.Drain();
        }
        finally
        {
            quads.Dispose();
            _triples.Dispose();
            _triples = savedTriples;
        }
    }

    private void MoveTriplesToQuads(ref PooledList<QuadPattern> quads, PatternTerm? graph)
    {
        foreach (TriplePattern triple in _triples.Span)
        {
            quads.Add(new QuadPattern(triple.Subject, triple.Predicate, triple.Object, graph) { Span = triple.Span });
        }

        _triples.Clear();
    }
}
