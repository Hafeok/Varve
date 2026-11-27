// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Sparql.Algebra;
using Varve.Sparql.Parsing;

namespace Varve.Sparql;

/// <summary>
/// The terminals of a SPARQL text, from the parser's own lexer
/// (<c>docs/spec/sparql-grammar.md</c> §10): for colouring a query with the
/// same tokens the parser reads.
/// </summary>
/// <remarks>
/// <para>
/// A token's <see cref="SparqlToken.Start"/> and <see cref="SparqlToken.End"/>
/// are in the units of the input: bytes over UTF-8, UTF-16 code units over
/// UTF-16. Whitespace is no token: it is the gap between two. Comments are
/// tokens.
/// </para>
/// <para>
/// The tokens end at the end of the text, or at the first ill-formed terminal,
/// whose error is then <see cref="SparqlTokens.Error"/>: the error the parser
/// reports for it, in the parser's units (bytes), whichever input was given.
/// There is no resynchronisation.
/// </para>
/// </remarks>
[Contract(typeof(APublicSparqlTokenizer.TheTokenizerIsTheParsersLexer), Role = "the parser's lexer, reachable, with comments as tokens")]
public static class SparqlTokenizer
{
    /// <summary>Tokenizes UTF-8; positions are bytes.</summary>
    public static SparqlTokens Tokenize(ReadOnlySpan<byte> utf8) => Run(utf8, default, utf16: false, default);

    /// <summary>
    /// Tokenizes UTF-16; positions are UTF-16 code units. A lone surrogate ends
    /// the tokens there, with the parser's <see cref="SparqlErrorKind.InvalidEncoding"/>.
    /// </summary>
    public static SparqlTokens Tokenize(ReadOnlySpan<char> text)
    {
        byte[]? rented = null;

        try
        {
            ReadOnlySpan<byte> utf8 = SparqlParser.TranscodePrefix(text, ref rented, out SparqlParseError encoding);
            return Run(utf8, text, utf16: true, encoding);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static SparqlTokens Run(ReadOnlySpan<byte> utf8, ReadOnlySpan<char> chars, bool utf16, SparqlParseError encoding)
    {
        Lexer lexer = new(utf8, comments: true);
        TokenPositions positions = new();
        List<SparqlToken> tokens = [];

        while (true)
        {
            Token next;

            try
            {
                next = lexer.Next();
            }
            catch (SparqlParseException failed)
            {
                return new SparqlTokens(tokens, SparqlTokens.Ending(failed.Error, encoding));
            }

            if (next.Kind == TokenKind.End)
            {
                // The text ran out: an encoding error, if there was one, is where it stopped.
                return new SparqlTokens(tokens, encoding);
            }

            SparqlTokenKind kind = TokenClassification.Classify(next, lexer.Slice(next));

            if (utf16)
            {
                long start = positions.CharsAt(chars, next.Start);
                tokens.Add(new SparqlToken(kind, start, positions.CharsAt(chars, next.End)));
            }
            else
            {
                tokens.Add(new SparqlToken(kind, next.Start, next.End));
            }
        }
    }
}

/// <summary>What <see cref="SparqlTokenizer"/> found: the tokens, in order, and the error that ended them, if one did.</summary>
[Contract(typeof(APublicSparqlTokenizer.TokenizerStopsAtTheFirstError), Role = "the tokens up to the first ill-formed terminal, and its error")]
public sealed class SparqlTokens
{
    internal SparqlTokens(IReadOnlyList<SparqlToken> tokens, SparqlParseError error)
    {
        Tokens = tokens;
        Error = error;
    }

    /// <summary>The tokens, in order of position.</summary>
    public IReadOnlyList<SparqlToken> Tokens { get; }

    /// <summary>The error that ended the tokens; its <see cref="SparqlParseError.Kind"/> is <see cref="SparqlErrorKind.None"/> when they ran to the end.</summary>
    public SparqlParseError Error { get; }

    /// <summary>Whether the tokens ran to the end of the text.</summary>
    public bool IsComplete => Error.Kind == SparqlErrorKind.None;

    // Over UTF-16 the parser transcodes before it reads, so a lone surrogate is
    // its error whatever is ill-formed before it; the tokens say the same.
    internal static SparqlParseError Ending(SparqlParseError lexical, SparqlParseError encoding) =>
        encoding.Kind != SparqlErrorKind.None ? encoding : lexical;
}

/// <summary>A byte offset of transcoded UTF-16 as a UTF-16 index. Tokens only move forward, so the walk resumes where it stopped: one pass in all.</summary>
[Contract(typeof(APublicSparqlTokenizer.TokenPositionsInTheInputsUnits), Role = "token positions mapped back to UTF-16 code units")]
internal sealed class TokenPositions
{
    private long _bytes;
    private int _chars;

    [DesignDecision(typeof(SyntaxModelSurfaces.SourceCoordinatesAreOffsetsLinesAndColumns), Scope = ExceptionScope.Boundary)]
    internal long CharsAt(ReadOnlySpan<char> text, long bytes)
    {
        while (_bytes < bytes && _chars < text.Length)
        {
            char c = text[_chars];

            if (char.IsHighSurrogate(c) && _chars + 1 < text.Length && char.IsLowSurrogate(text[_chars + 1]))
            {
                _bytes += 4;
                _chars += 2;
            }
            else
            {
                _bytes += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                _chars++;
            }
        }

        return _chars;
    }
}

/// <summary>The public kind of a lexer token: keywords, booleans and the rest, by the token alone.</summary>
[Contract(typeof(APublicSparqlTokenizer.WordsClassifiedWithoutContext), Role = "keywords, booleans and the rest, by the token alone")]
internal static class TokenClassification
{
    internal static SparqlTokenKind Classify(Token token, ReadOnlySpan<byte> text) => token.Kind switch
    {
        TokenKind.Iri => SparqlTokenKind.Iri,
        TokenKind.PrefixedName => SparqlTokenKind.PrefixedName,
        TokenKind.BlankNodeLabel => SparqlTokenKind.BlankNodeLabel,
        TokenKind.Anon => SparqlTokenKind.Anon,
        TokenKind.Nil => SparqlTokenKind.Nil,
        TokenKind.Variable => SparqlTokenKind.Variable,
        TokenKind.String => SparqlTokenKind.String,
        TokenKind.Integer => SparqlTokenKind.Integer,
        TokenKind.Decimal => SparqlTokenKind.Decimal,
        TokenKind.Double => SparqlTokenKind.Double,
        TokenKind.LangTag => SparqlTokenKind.LangTag,
        TokenKind.Word => Keywords.Classify(text),
        TokenKind.Comment => SparqlTokenKind.Comment,
        TokenKind.LeftBrace => SparqlTokenKind.OpenBrace,
        TokenKind.RightBrace => SparqlTokenKind.CloseBrace,
        TokenKind.LeftParen => SparqlTokenKind.OpenParen,
        TokenKind.RightParen => SparqlTokenKind.CloseParen,
        TokenKind.LeftBracket => SparqlTokenKind.OpenBracket,
        TokenKind.RightBracket => SparqlTokenKind.CloseBracket,
        TokenKind.Comma or TokenKind.Semicolon or TokenKind.Dot or TokenKind.DoubleCaret => SparqlTokenKind.Punctuation,
        TokenKind.ReifiedOpen => SparqlTokenKind.ReifiedOpen,
        TokenKind.ReifiedClose => SparqlTokenKind.ReifiedClose,
        TokenKind.TripleTermOpen => SparqlTokenKind.TripleTermOpen,
        TokenKind.TripleTermClose => SparqlTokenKind.TripleTermClose,
        TokenKind.AnnotationOpen => SparqlTokenKind.AnnotationOpen,
        TokenKind.AnnotationClose => SparqlTokenKind.AnnotationClose,
        _ => SparqlTokenKind.Operator,
    };

    /// <summary>The keywords quoted in the grammar of <c>sparql-grammar.md</c> §2, less <c>a</c>, <c>true</c> and <c>false</c>.</summary>
    private static class Keywords
    {
        private static readonly FrozenSet<string> Set = new[]
        {
            "ABS", "ADD", "ALL", "AS", "ASC", "ASK", "AVG", "BASE", "BIND", "BNODE", "BOUND", "BY", "CEIL", "CLEAR",
            "COALESCE", "CONCAT", "CONSTRUCT", "CONTAINS", "COPY", "COUNT", "CREATE", "DATA", "DATATYPE", "DAY",
            "DEFAULT", "DELETE", "DESC", "DESCRIBE", "DISTINCT", "DROP", "ENCODE_FOR_URI", "EXISTS", "FILTER", "FLOOR",
            "FROM", "GRAPH", "GROUP", "GROUP_CONCAT", "hasLANG", "hasLANGDIR", "HAVING", "HOURS", "IF", "IN", "INSERT",
            "INTO", "IRI", "isBLANK", "isIRI", "isLITERAL", "isNUMERIC", "isTRIPLE", "isURI", "LANG", "LANGDIR",
            "LANGMATCHES", "LCASE", "LIMIT", "LOAD", "MAX", "MD5", "MIN", "MINUS", "MINUTES", "MONTH", "MOVE", "NAMED",
            "NOT", "NOW", "OBJECT", "OFFSET", "OPTIONAL", "ORDER", "PREDICATE", "PREFIX", "RAND", "REDUCED", "REGEX",
            "REPLACE", "ROUND", "sameTerm", "SAMPLE", "SECONDS", "SELECT", "SEPARATOR", "SERVICE", "SHA1", "SHA256",
            "SHA384", "SHA512", "SILENT", "STR", "STRAFTER", "STRBEFORE", "STRDT", "STRENDS", "STRLANG", "STRLANGDIR",
            "STRLEN", "STRSTARTS", "STRUUID", "SUBJECT", "SUBSTR", "SUM", "TIMEZONE", "TO", "TRIPLE", "TZ", "UCASE",
            "UNDEF", "UNION", "URI", "USING", "UUID", "VALUES", "VERSION", "WHERE", "WITH", "YEAR",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Lookup = Set.GetAlternateLookup<ReadOnlySpan<char>>();

        // The longest keyword, ENCODE_FOR_URI, is 14 characters.
        private const int Longest = 14;

        internal static SparqlTokenKind Classify(ReadOnlySpan<byte> word)
        {
            if (word.Length == 1 && word[0] == (byte)'a')
            {
                return SparqlTokenKind.Keyword;
            }

            if (word.Length > Longest)
            {
                return SparqlTokenKind.Word;
            }

            Span<char> chars = stackalloc char[Longest];

            for (int i = 0; i < word.Length; i++)
            {
                if (word[i] >= 0x80)
                {
                    return SparqlTokenKind.Word;
                }

                chars[i] = (char)word[i];
            }

            ReadOnlySpan<char> name = chars[..word.Length];

            if (name.Equals("true", StringComparison.Ordinal) || name.Equals("false", StringComparison.Ordinal))
            {
                return SparqlTokenKind.Boolean;
            }

            return Lookup.Contains(name) ? SparqlTokenKind.Keyword : SparqlTokenKind.Word;
        }
    }
}
