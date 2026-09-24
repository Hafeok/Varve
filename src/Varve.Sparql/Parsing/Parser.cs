// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using Varve.Iri;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>
/// Recursive descent over the terminals, producing the algebra directly
/// (<c>docs/spec/sparql-algebra.md</c> §2.1). One instance parses one text;
/// the first error ends the parse with an exception the entry points turn into
/// a <see cref="SparqlParseError"/>.
/// </summary>
/// <remarks>
/// No method here carries <c>[HotPath]</c>: a parser whose output is a tree
/// allocates that tree, and the attribute would be false on every method that
/// builds one (<c>docs/spec/sparql-grammar.md</c> §7). What is held to instead
/// is that nothing but the tree is allocated per pattern, which the pooled
/// builders and the interned names below are for.
/// </remarks>
internal ref partial struct Parser
{
    private static readonly RdfTerm XsdIntegerTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8);
    private static readonly RdfTerm XsdDecimalTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#decimal"u8);
    private static readonly RdfTerm XsdDoubleTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#double"u8);
    private static readonly RdfTerm XsdBooleanTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8);
    private static readonly RdfTerm RdfTypeTerm = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
    private static readonly RdfTerm RdfFirstTerm = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8);
    private static readonly RdfTerm RdfRestTerm = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8);
    private static readonly RdfTerm RdfNilTerm = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8);
    private static readonly RdfTerm RdfReifiesTerm = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8);

    private Lexer _lexer;
    private Token _token;
    private int _lastEnd;

    private readonly SparqlVersion _widest;
    private SparqlVersion _version;
    private SparqlVersion? _declaredVersion;
    private RdfTerm? _declaredBase;

    private PooledBytes _scratch;
    private PooledBytes _resolved;
    private byte[]? _base;
    private PooledList<PrefixDeclaration> _prefixes;
    private PooledList<byte[]> _prefixNames;

    // Interned names: a variable's or a label's string is allocated once per
    // parse, however often it occurs. Looked up by chars decoded on the stack.
    private Dictionary<string, string>? _names;
    private Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _namesByChars;

    // Blank node labels are scoped to the basic graph pattern they occur in;
    // a group graph pattern is the unit that scopes them here (grammar §3.6).
    private Dictionary<string, int>? _labelGroups;
    private int _groupId;
    private int _nextGroupId;
    private readonly string _freshPrefix;
    private int _freshCount;
    private bool _userLabelLooksFresh;

    // The group being built: the pattern so far (null is the empty BGP, Z)
    // and the triple patterns not yet flushed into it.
    private QueryPattern? _group;
    private PooledList<TriplePattern> _triples;

    // The query level being built: where aggregates may appear, and whether
    // one did.
    private Level _level;

    internal Parser(ReadOnlySpan<byte> text, SparqlParseOptions options, string freshPrefix)
    {
        _lexer = new Lexer(text);
        _widest = options.EffectiveVersion;
        _version = _widest;
        _freshPrefix = freshPrefix;
        _scratch = default;
        _resolved = default;
        _prefixes = default;
        _prefixNames = default;
        _triples = default;
        _level = default;

        if (!options.BaseIri.IsEmpty)
        {
            if (!IriRef.IsAbsolute(options.BaseIri.Span))
            {
                throw new SparqlParseException(new SparqlParseError(
                    SparqlErrorKind.RelativeIri, 0, 1, 1, "The base IRI given in the options is not an absolute IRI."));
            }

            _base = options.BaseIri.ToArray();
        }

        _token = _lexer.Next();
    }

    /// <summary>True when a label the author wrote has the shape of a generated one; the caller then parses again with a longer prefix.</summary>
    internal readonly bool FreshLabelsMayCollide => _userLabelLooksFresh && _freshCount > 0;

    internal void Dispose()
    {
        _scratch.Dispose();
        _resolved.Dispose();
        _prefixes.Dispose();
        _prefixNames.Dispose();
        _triples.Dispose();
    }

    private struct Level
    {
        internal bool AggregatesAllowed;
        internal bool SawAggregate;
        internal bool InAggregate;
    }

    // --- tokens ------------------------------------------------------------

    private readonly ReadOnlySpan<byte> Text(Token token) => _lexer.Slice(token);

    private void Advance()
    {
        _lastEnd = _token.End;
        _token = _lexer.Next();
    }

    private readonly bool Is(TokenKind kind) => _token.Kind == kind;

    private readonly bool IsWord(ReadOnlySpan<byte> keyword) =>
        _token.Kind == TokenKind.Word && Ascii.EqualsIgnoreCase(Text(_token), keyword);

    private bool Accept(TokenKind kind)
    {
        if (_token.Kind != kind)
        {
            return false;
        }

        Advance();
        return true;
    }

    private bool AcceptWord(ReadOnlySpan<byte> keyword)
    {
        if (!IsWord(keyword))
        {
            return false;
        }

        Advance();
        return true;
    }

    private Token Expect(TokenKind kind, string what)
    {
        if (_token.Kind != kind)
        {
            throw Expected(what);
        }

        Token token = _token;
        Advance();
        return token;
    }

    private Token ExpectWord(ReadOnlySpan<byte> keyword)
    {
        if (!IsWord(keyword))
        {
            throw Expected("'" + Encoding.UTF8.GetString(keyword) + "'");
        }

        Token token = _token;
        Advance();
        return token;
    }

    private readonly SourceSpan From(Token start) => new(start.Start, _lastEnd, start.Line, start.Column);

    // --- errors ------------------------------------------------------------

    private readonly SparqlParseException Expected(string what) =>
        _token.Kind == TokenKind.End
            ? Fail(SparqlErrorKind.UnexpectedEnd, _token, "Expected " + what + " but the input ended.")
            : Fail(SparqlErrorKind.Syntax, _token, "Expected " + what + ", found " + Describe(_token) + ".");

    private static SparqlParseException Fail(SparqlErrorKind kind, Token at, string message) =>
        new(new SparqlParseError(kind, at.Start, at.Line, at.Column, message));

    private readonly SparqlParseException FailAt(SparqlErrorKind kind, Token at, int offset, string message)
    {
        // An offset inside a token: the column moves along it, the line does
        // not unless the token contains a break, which only a long string can.
        int line = at.Line;
        int column = at.Column;
        ReadOnlySpan<byte> text = _lexer.Text;

        for (int i = at.Start; i < offset && i < text.Length; i++)
        {
            if (text[i] == 0x0A || (text[i] == 0x0D && (i + 1 >= text.Length || text[i + 1] != 0x0A)))
            {
                line++;
                column = 1;
            }
            else if (text[i] != 0x0D)
            {
                column++;
            }
        }

        return new SparqlParseException(new SparqlParseError(kind, offset, line, column, message));
    }

    private readonly string Describe(Token token) => token.Kind switch
    {
        TokenKind.End => "the end of the input",
        TokenKind.String => "a string literal",
        _ => "'" + Encoding.UTF8.GetString(Text(token)) + "'",
    };

    // --- versions ----------------------------------------------------------

    private readonly void Require(SparqlVersion needed, Token at, string construct)
    {
        if (_version < needed)
        {
            string label = needed == SparqlVersion.Sparql12 ? "1.2" : "1.2-basic";
            throw Fail(SparqlErrorKind.Version, at, construct + " needs SPARQL " + label + "; the version in force is " + Label(_version) + ".");
        }
    }

    private static string Label(SparqlVersion version) => version switch
    {
        SparqlVersion.Sparql11 => "1.1",
        SparqlVersion.Sparql12Basic => "1.2-basic",
        _ => "1.2",
    };

    // --- names -------------------------------------------------------------

    /// <summary>The one string for a name, looked up by its UTF-8 without allocating unless it is new.</summary>
    private string Intern(ReadOnlySpan<byte> utf8)
    {
        if (_names is null)
        {
            _names = new Dictionary<string, string>(StringComparer.Ordinal);
            _namesByChars = _names.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        char[]? rented = null;
        Span<char> chars = utf8.Length <= 256 ? stackalloc char[256] : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(utf8.Length));

        try
        {
            int count = Encoding.UTF8.GetChars(utf8, chars);
            ReadOnlySpan<char> key = chars[..count];

            if (_namesByChars.TryGetValue(key, out string? existing))
            {
                return existing;
            }

            string created = new(key);
            _names[created] = created;
            return created;
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private Variable ParseVariable()
    {
        Token token = Expect(TokenKind.Variable, "a variable");
        return new Variable(Intern(Text(token)[1..]));
    }

    // --- blank nodes -------------------------------------------------------

    private int EnterGroup()
    {
        int previous = _groupId;
        _groupId = ++_nextGroupId;
        return previous;
    }

    private void LeaveGroup(int previous) => _groupId = previous;

    private BlankNodePattern ParseBlankNodeLabel(in TripleMode mode)
    {
        Token token = _token;
        Advance();

        if (!mode.AllowBlankNodes)
        {
            throw Fail(SparqlErrorKind.BlankNode, token, "A blank node is not allowed here (SPARQL 1.2 Query §19.6).");
        }

        ReadOnlySpan<byte> raw = Text(token)[2..];
        string label = Intern(raw);

        if (!_userLabelLooksFresh && LooksFresh(raw))
        {
            _userLabelLooksFresh = true;
        }

        if (!mode.IsTemplate)
        {
            _labelGroups ??= new Dictionary<string, int>(StringComparer.Ordinal);

            if (_labelGroups.TryGetValue(label, out int group))
            {
                if (group != _groupId)
                {
                    throw Fail(SparqlErrorKind.BlankNode, token, "The blank node label '_:" + label + "' is used in two separate basic graph patterns (SPARQL 1.2 Query §19.6).");
                }
            }
            else
            {
                _labelGroups[label] = _groupId;
            }
        }

        return new BlankNodePattern(label) { Span = token.Span };
    }

    private readonly bool LooksFresh(ReadOnlySpan<byte> label)
    {
        if (label.Length <= _freshPrefix.Length)
        {
            return false;
        }

        for (int i = 0; i < _freshPrefix.Length; i++)
        {
            if (label[i] != _freshPrefix[i])
            {
                return false;
            }
        }

        for (int i = _freshPrefix.Length; i < label.Length; i++)
        {
            if (!Chars.IsAsciiDigit(label[i]))
            {
                return false;
            }
        }

        return true;
    }

    private BlankNodePattern FreshBlankNode(in TripleMode mode, Token at)
    {
        if (!mode.AllowBlankNodes)
        {
            throw Fail(SparqlErrorKind.BlankNode, at, "A blank node is not allowed here (SPARQL 1.2 Query §19.6), and this syntax creates one.");
        }

        string label = _freshPrefix + _freshCount.ToString(CultureInfo.InvariantCulture);
        _freshCount++;
        return new BlankNodePattern(label) { Span = at.Span };
    }

    // --- IRIs and prefixes -------------------------------------------------

    private RdfTerm ParseIri()
    {
        if (Is(TokenKind.Iri))
        {
            return ParseIriRef();
        }

        if (Is(TokenKind.PrefixedName))
        {
            return ParsePrefixedName();
        }

        throw Expected("an IRI");
    }

    private RdfTerm ParseIriRef()
    {
        Token token = Expect(TokenKind.Iri, "an IRI");
        ReadOnlySpan<byte> raw = Text(token)[1..^1];

        if (!Utf8.IsValid(raw))
        {
            throw Fail(SparqlErrorKind.InvalidEncoding, token, "The IRI is not valid UTF-8.");
        }

        _scratch.Clear();

        if (!Lexer.Decode(raw, Lexer.Escapes.Iri, ref _scratch, out int badEscape))
        {
            throw FailAt(SparqlErrorKind.InvalidEscape, token, token.Start + 1 + badEscape, "A numeric escape may not produce a surrogate code point (SPARQL 1.2 Query §19.2).");
        }

        // Backslashes in an IRIREF are UCHARs and nothing else: the lexer let
        // them through unchecked so that the error names the escape.
        ReadOnlySpan<byte> decoded = _scratch.Span;

        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'\\' && (i + 1 >= raw.Length || raw[i + 1] is not ((byte)'u' or (byte)'U')))
            {
                throw FailAt(SparqlErrorKind.InvalidEscape, token, token.Start + 1 + i, "Only \\u and \\U escapes are allowed in an IRI.");
            }
        }

        return ResolveIri(decoded, token);
    }

    private RdfTerm ResolveIri(ReadOnlySpan<byte> iri, Token at)
    {
        if (!IriRef.TryValidate(iri, out IriComponents components, out IriError error))
        {
            throw Fail(SparqlErrorKind.InvalidIri, at, "Not a valid IRI reference (RFC 3987 §2.2): " + error.Kind + " at byte " + error.Offset.ToString(CultureInfo.InvariantCulture) + " of the IRI.");
        }

        if (components.HasScheme)
        {
            return RdfTerm.Iri(iri);
        }

        if (_base is null)
        {
            throw Fail(SparqlErrorKind.RelativeIri, at, "A relative IRI with no BASE declaration and no base in the options.");
        }

        _resolved.Clear();
        int length = IriRef.ResolveLength(_base, iri);
        Span<byte> destination = _resolved.Reserve(length);

        if (!IriRef.TryResolve(_base, iri, destination, out int written))
        {
            throw Fail(SparqlErrorKind.InvalidIri, at, "The IRI could not be resolved against the base.");
        }

        return RdfTerm.Iri(destination[..written]);
    }

    private RdfTerm ParsePrefixedName()
    {
        Token token = Expect(TokenKind.PrefixedName, "a prefixed name");
        ReadOnlySpan<byte> raw = Text(token);
        int colon = raw.IndexOf((byte)':');
        ReadOnlySpan<byte> prefix = raw[..colon];
        ReadOnlySpan<byte> local = raw[(colon + 1)..];

        int index = -1;

        for (int i = _prefixNames.Count - 1; i >= 0; i--)
        {
            if (prefix.SequenceEqual(_prefixNames[i]))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw Fail(SparqlErrorKind.UndeclaredPrefix, token, "The prefix '" + Encoding.UTF8.GetString(prefix) + ":' is not declared.");
        }

        _scratch.Clear();
        _scratch.Add(_prefixes[index].Iri.Lexical);

        if (!Utf8.IsValid(local))
        {
            throw Fail(SparqlErrorKind.InvalidEncoding, token, "The local name is not valid UTF-8.");
        }

        Lexer.Decode(local, Lexer.Escapes.LocalName, ref _scratch, out _);
        return ResolveIri(_scratch.Span, token);
    }

    private void DeclarePrefix(Token nameToken, RdfTerm iri, Token start)
    {
        ReadOnlySpan<byte> raw = Text(nameToken);
        int colon = raw.IndexOf((byte)':');

        if (colon != raw.Length - 1)
        {
            throw Fail(SparqlErrorKind.Syntax, nameToken, "A PREFIX declaration names the prefix with a trailing colon and nothing after it.");
        }

        ReadOnlySpan<byte> name = raw[..colon];
        _prefixNames.Add(name.ToArray());
        _prefixes.Add(new PrefixDeclaration(Intern(name), iri) { Span = From(start) });
    }

    private void DeclareBase(RdfTerm iri)
    {
        // ResolveIri made it absolute, or failed.
        _base = iri.Lexical.ToArray();
    }

    // --- literals ----------------------------------------------------------

    private RdfTerm ParseNumericLiteral()
    {
        Token token = _token;
        RdfTerm datatype = token.Kind switch
        {
            TokenKind.Integer => XsdIntegerTerm,
            TokenKind.Decimal => XsdDecimalTerm,
            TokenKind.Double => XsdDoubleTerm,
            _ => throw Expected("a number"),
        };
        Advance();
        return RdfTerm.Literal(Text(token), datatype);
    }

    private RdfTerm ParseBooleanLiteral()
    {
        if (IsWord("true"u8))
        {
            Advance();
            return RdfTerm.Literal("true"u8, XsdBooleanTerm);
        }

        if (IsWord("false"u8))
        {
            Advance();
            return RdfTerm.Literal("false"u8, XsdBooleanTerm);
        }

        throw Expected("'true' or 'false'");
    }

    private readonly bool IsBooleanLiteral() => IsWord("true"u8) || IsWord("false"u8);

    private readonly bool IsNumericLiteral() => _token.Kind is TokenKind.Integer or TokenKind.Decimal or TokenKind.Double;

    /// <summary><c>[149] RDFLiteral ::= String ( LANG_DIR | '^^' iri )?</c></summary>
    private RdfTerm ParseRdfLiteral()
    {
        Token token = Expect(TokenKind.String, "a string literal");
        ReadOnlySpan<byte> raw = Text(token);
        int quotes = raw.Length >= 6 && raw[1] == raw[0] && raw[2] == raw[0] ? 3 : 1;
        ReadOnlySpan<byte> body = raw[quotes..^quotes];

        if (!Utf8.IsValid(body))
        {
            throw Fail(SparqlErrorKind.InvalidEncoding, token, "The string literal is not valid UTF-8.");
        }

        _scratch.Clear();

        if (!Lexer.Decode(body, Lexer.Escapes.String, ref _scratch, out int badEscape))
        {
            throw FailAt(SparqlErrorKind.InvalidEscape, token, token.Start + quotes + badEscape, "A numeric escape may not produce a surrogate code point (SPARQL 1.2 Query §19.2).");
        }

        ReadOnlySpan<byte> lexical = _scratch.Span;

        if (Is(TokenKind.LangTag))
        {
            Token tag = _token;
            Advance();
            ReadOnlySpan<byte> tagText = Text(tag)[1..];
            int dir = tagText.IndexOf("--"u8);
            ReadOnlySpan<byte> language = dir < 0 ? tagText : tagText[..dir];

            if (!LanguageTag.IsWellFormed(language))
            {
                throw Fail(SparqlErrorKind.InvalidLanguageTag, tag, "Not a well-formed BCP 47 language tag.");
            }

            if (dir < 0)
            {
                return RdfTerm.Literal(lexical, language);
            }

            Require(SparqlVersion.Sparql12Basic, tag, "A base direction on a language tag");
            ReadOnlySpan<byte> direction = tagText[(dir + 2)..];

            if (!LanguageTag.IsBaseDirection(direction))
            {
                throw Fail(SparqlErrorKind.InvalidLanguageTag, tag, "The base direction must be 'ltr' or 'rtl'.");
            }

            return RdfTerm.Literal(lexical, language, direction[0] == (byte)'l' ? TextDirection.LeftToRight : TextDirection.RightToLeft);
        }

        if (Accept(TokenKind.DoubleCaret))
        {
            Token datatypeToken = _token;

            // The datatype's expansion reuses the scratch buffer; keep the lexical form first.
            _resolved.Clear();
            _resolved.Add(lexical);
            RdfTerm datatype = ParseIri();
            ReadOnlySpan<byte> kept = _resolved.Span;

            if (datatype.Lexical.SequenceEqual(RdfVocabulary.RdfLangString) || datatype.Lexical.SequenceEqual(RdfVocabulary.RdfDirLangString))
            {
                throw Fail(SparqlErrorKind.Syntax, datatypeToken, "rdf:langString and rdf:dirLangString are the datatypes of language-tagged literals and cannot be written as '^^' datatypes.");
            }

            return RdfTerm.Literal(kept, datatype);
        }

        return RdfTerm.Literal(lexical);
    }

    private readonly bool IsStringStart() => _token.Kind == TokenKind.String;

    /// <summary>A short string as text: the <c>VERSION</c> label and <c>GROUP_CONCAT</c>'s separator.</summary>
    private string ParseShortString(out Token token)
    {
        token = _token;
        RdfTerm literal = ParseRdfLiteral();

        if (literal.Datatype is not null || literal.Language.Length > 0)
        {
            throw Fail(SparqlErrorKind.Syntax, token, "A plain string is required here.");
        }

        return Encoding.UTF8.GetString(literal.Lexical);
    }
}
