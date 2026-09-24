// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>Property paths, <c>[94]</c> to <c>[102]</c>, translated by the table of §18.3.2.4.</summary>
internal ref partial struct Parser
{
    /// <summary><c>[94] Path ::= PathAlternative</c></summary>
    private PropertyPath ParsePath()
    {
        Token start = _token;
        PropertyPath path = ParsePathSequence();

        while (Accept(TokenKind.Pipe))
        {
            path = new AlternativePath(path, ParsePathSequence()) { Span = From(start) };
        }

        return path;
    }

    /// <summary><c>[96] PathSequence ::= PathEltOrInverse ( '/' PathEltOrInverse )*</c></summary>
    private PropertyPath ParsePathSequence()
    {
        Token start = _token;
        PropertyPath path = ParsePathEltOrInverse();

        while (Accept(TokenKind.Slash))
        {
            path = new SequencePath(path, ParsePathEltOrInverse()) { Span = From(start) };
        }

        return path;
    }

    /// <summary><c>[98] PathEltOrInverse ::= PathElt | '^' PathElt</c></summary>
    private PropertyPath ParsePathEltOrInverse()
    {
        Token start = _token;

        if (Accept(TokenKind.Caret))
        {
            return new InversePath(ParsePathElt()) { Span = From(start) };
        }

        return ParsePathElt();
    }

    /// <summary><c>[97] PathElt ::= PathPrimary PathMod?</c></summary>
    private PropertyPath ParsePathElt()
    {
        Token start = _token;
        PropertyPath primary = ParsePathPrimary();

        if (Accept(TokenKind.Question))
        {
            return new ZeroOrOnePath(primary) { Span = From(start) };
        }

        if (Accept(TokenKind.Star))
        {
            return new ZeroOrMorePath(primary) { Span = From(start) };
        }

        if (Accept(TokenKind.Plus))
        {
            return new OneOrMorePath(primary) { Span = From(start) };
        }

        return primary;
    }

    /// <summary><c>[100] PathPrimary ::= iri | 'a' | '!' PathNegatedPropertySet | '(' Path ')'</c></summary>
    private PropertyPath ParsePathPrimary()
    {
        Token start = _token;

        if (AcceptWord("a"u8))
        {
            return new PredicatePath(RdfTypeTerm) { Span = start.Span };
        }

        if (Is(TokenKind.Iri) || Is(TokenKind.PrefixedName))
        {
            return new PredicatePath(ParseIri()) { Span = start.Span };
        }

        if (Accept(TokenKind.Bang))
        {
            return ParseNegatedPropertySet(start);
        }

        if (Accept(TokenKind.LeftParen))
        {
            PropertyPath inner = ParsePath();
            Expect(TokenKind.RightParen, "')'");
            return inner;
        }

        throw Expected("a property path: an IRI, 'a', '!' or '('");
    }

    /// <summary><c>[101] PathNegatedPropertySet ::= PathOneInPropertySet | '(' ( PathOneInPropertySet ( '|' PathOneInPropertySet )* )? ')'</c></summary>
    private NegatedPropertySet ParseNegatedPropertySet(Token start)
    {
        PooledList<RdfTerm> forward = default;
        PooledList<RdfTerm> inverse = default;

        try
        {
            if (Accept(TokenKind.Nil))
            {
                // '!()' — the lexer reads the empty parentheses as NIL.
                return new NegatedPropertySet(default, default) { Span = From(start) };
            }

            if (Accept(TokenKind.LeftParen))
            {
                if (!Accept(TokenKind.RightParen))
                {
                    do
                    {
                        ParsePathOneInPropertySet(ref forward, ref inverse);
                    }
                    while (Accept(TokenKind.Pipe));

                    Expect(TokenKind.RightParen, "')'");
                }
            }
            else
            {
                ParsePathOneInPropertySet(ref forward, ref inverse);
            }

            return new NegatedPropertySet(forward.Drain(), inverse.Drain()) { Span = From(start) };
        }
        finally
        {
            forward.Dispose();
            inverse.Dispose();
        }
    }

    /// <summary><c>[102] PathOneInPropertySet ::= iri | 'a' | '^' ( iri | 'a' )</c></summary>
    private void ParsePathOneInPropertySet(ref PooledList<RdfTerm> forward, ref PooledList<RdfTerm> inverse)
    {
        bool inverted = Accept(TokenKind.Caret);
        RdfTerm iri = AcceptWord("a"u8) ? RdfTypeTerm : ParseIri();

        if (inverted)
        {
            inverse.Add(iri);
        }
        else
        {
            forward.Add(iri);
        }
    }
}
