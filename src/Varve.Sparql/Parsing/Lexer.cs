// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;

namespace Varve.Sparql.Parsing;

/// <summary>
/// The terminals, one at a time, from UTF-8. Longest match; keywords are
/// words the parser recognises in context (<c>docs/spec/sparql-grammar.md</c>
/// §3.4); escapes are left in the token and processed by
/// <see cref="Decode"/> when the parser wants the value (§3.2).
/// </summary>
internal ref struct Lexer
{
    private int _position;
    private int _line;
    private int _lineStart;

    internal Lexer(ReadOnlySpan<byte> text)
    {
        Text = text;
        _position = 0;
        _line = 1;
        _lineStart = 0;
    }

    internal ReadOnlySpan<byte> Text { get; }

    internal readonly ReadOnlySpan<byte> Slice(Token token) => Text.Slice(token.Start, token.Length);

    /// <summary>The next terminal. Throws on an ill-formed one, at the offending byte.</summary>
    internal Token Next()
    {
        SkipTrivia();

        if (_position >= Text.Length)
        {
            return Make(TokenKind.End, _position, 0);
        }

        int start = _position;
        byte b = Text[start];

        switch (b)
        {
            case (byte)'{':
                return Punct(Peek(1) == (byte)'|' ? TokenKind.AnnotationOpen : TokenKind.LeftBrace, Peek(1) == (byte)'|' ? 2 : 1);
            case (byte)'}':
                return Punct(TokenKind.RightBrace, 1);
            case (byte)'|':
                if (Peek(1) == (byte)'}')
                {
                    return Punct(TokenKind.AnnotationClose, 2);
                }

                return Peek(1) == (byte)'|' ? Punct(TokenKind.OrOr, 2) : Punct(TokenKind.Pipe, 1);
            case (byte)'(':
                return LexNilOr(TokenKind.LeftParen);
            case (byte)')':
                return Peek(1) == (byte)'>' && Peek(2) == (byte)'>' ? Punct(TokenKind.TripleTermClose, 3) : Punct(TokenKind.RightParen, 1);
            case (byte)'[':
                return LexAnonOr();
            case (byte)']':
                return Punct(TokenKind.RightBracket, 1);
            case (byte)',':
                return Punct(TokenKind.Comma, 1);
            case (byte)';':
                return Punct(TokenKind.Semicolon, 1);
            case (byte)'.':
                return Chars.IsAsciiDigit(Peek(1)) ? LexNumber(start) : Punct(TokenKind.Dot, 1);
            case (byte)'^':
                return Peek(1) == (byte)'^' ? Punct(TokenKind.DoubleCaret, 2) : Punct(TokenKind.Caret, 1);
            case (byte)'/':
                return Punct(TokenKind.Slash, 1);
            case (byte)'*':
                return Punct(TokenKind.Star, 1);
            case (byte)'+':
            case (byte)'-':
                if (Chars.IsAsciiDigit(Peek(1)) || (Peek(1) == (byte)'.' && Chars.IsAsciiDigit(Peek(2))))
                {
                    return LexNumber(start);
                }

                return Punct(b == (byte)'+' ? TokenKind.Plus : TokenKind.Minus, 1);
            case (byte)'?':
            case (byte)'$':
                return LexVariableOr(b == (byte)'?' ? TokenKind.Question : TokenKind.End);
            case (byte)'!':
                return Peek(1) == (byte)'=' ? Punct(TokenKind.NotEqual, 2) : Punct(TokenKind.Bang, 1);
            case (byte)'=':
                return Punct(TokenKind.Equal, 1);
            case (byte)'<':
                return LexLess();
            case (byte)'>':
                if (Peek(1) == (byte)'>')
                {
                    return Punct(TokenKind.ReifiedClose, 2);
                }

                return Peek(1) == (byte)'=' ? Punct(TokenKind.GreaterOrEqual, 2) : Punct(TokenKind.Greater, 1);
            case (byte)'&':
                if (Peek(1) == (byte)'&')
                {
                    return Punct(TokenKind.AndAnd, 2);
                }

                throw Error(SparqlErrorKind.Syntax, start, "Unexpected '&'; the operator is '&&'.");
            case (byte)'~':
                return Punct(TokenKind.Tilde, 1);
            case (byte)'"':
            case (byte)'\'':
                return LexString(start);
            case (byte)'@':
                return LexLangTag(start);
            case (byte)'_':
                if (Peek(1) == (byte)':')
                {
                    return LexBlankNodeLabel(start);
                }

                throw Error(SparqlErrorKind.Syntax, start, "Unexpected '_'; a blank node label is written '_:label'.");
            case (byte)':':
                return LexPrefixedName(start, start);
            default:
                if (Chars.IsAsciiDigit(b))
                {
                    return LexNumber(start);
                }

                return LexWordOrPrefixedName(start);
        }
    }

    // --- trivia ------------------------------------------------------------

    private void SkipTrivia()
    {
        while (_position < Text.Length)
        {
            byte b = Text[_position];

            if (b == 0x0A)
            {
                _position++;
                NewLine();
            }
            else if (b == 0x0D)
            {
                _position++;

                if (_position < Text.Length && Text[_position] == 0x0A)
                {
                    _position++;
                }

                NewLine();
            }
            else if (b is 0x20 or 0x09)
            {
                _position++;
            }
            else if (b == (byte)'#')
            {
                while (_position < Text.Length && Text[_position] is not (0x0A or 0x0D))
                {
                    _position++;
                }
            }
            else
            {
                return;
            }
        }
    }

    private void NewLine()
    {
        _line++;
        _lineStart = _position;
    }

    private readonly byte Peek(int ahead) => _position + ahead < Text.Length ? Text[_position + ahead] : (byte)0;

    private Token Punct(TokenKind kind, int length)
    {
        Token token = Make(kind, _position, length);
        _position += length;
        return token;
    }

    private readonly Token Make(TokenKind kind, int start, int length) =>
        new(kind, start, length, _line, start - _lineStart + 1);

    // --- the harder terminals ----------------------------------------------

    private Token LexNilOr(TokenKind otherwise)
    {
        int at = _position + 1;

        while (at < Text.Length && Chars.IsWhitespace(Text[at]))
        {
            at++;
        }

        if (at < Text.Length && Text[at] == (byte)')')
        {
            Token token = Make(TokenKind.Nil, _position, at + 1 - _position);
            AdvanceOver(at + 1);
            return token;
        }

        return Punct(otherwise, 1);
    }

    private Token LexAnonOr()
    {
        int at = _position + 1;

        while (at < Text.Length && Chars.IsWhitespace(Text[at]))
        {
            at++;
        }

        if (at < Text.Length && Text[at] == (byte)']')
        {
            Token token = Make(TokenKind.Anon, _position, at + 1 - _position);
            AdvanceOver(at + 1);
            return token;
        }

        return Punct(TokenKind.LeftBracket, 1);
    }

    /// <summary>Moves past a token that may contain line breaks, keeping the line count right.</summary>
    private void AdvanceOver(int end)
    {
        while (_position < end)
        {
            byte b = Text[_position++];

            if (b == 0x0A)
            {
                NewLine();
            }
            else if (b == 0x0D)
            {
                if (_position < end && Text[_position] == 0x0A)
                {
                    _position++;
                }

                NewLine();
            }
        }
    }

    private Token LexVariableOr(TokenKind otherwise)
    {
        int start = _position;
        int at = start + 1;

        if (Chars.TryRune(Text, at, out int first, out int firstLength) && (Chars.IsPnCharsU(first) || first is >= '0' and <= '9'))
        {
            at += firstLength;

            while (Chars.TryRune(Text, at, out int c, out int length) && Chars.IsVarNameChar(c))
            {
                at += length;
            }

            Token token = Make(TokenKind.Variable, start, at - start);
            _position = at;
            return token;
        }

        if (otherwise == TokenKind.End)
        {
            throw Error(SparqlErrorKind.Syntax, start, "Unexpected '$'; a variable is written '$name'.");
        }

        return Punct(otherwise, 1);
    }

    private Token LexLess()
    {
        int start = _position;

        // An IRIREF wins when the run from '<' to '>' contains nothing the
        // production excludes; otherwise this is an operator or '<<'.
        int at = start + 1;

        while (at < Text.Length)
        {
            byte c = Text[at];

            if (c == (byte)'>')
            {
                Token iri = Make(TokenKind.Iri, start, at + 1 - start);
                _position = at + 1;
                return iri;
            }

            if (Chars.IsForbiddenInIri(c))
            {
                break;
            }

            at++;
        }

        if (Peek(1) == (byte)'<')
        {
            return Peek(2) == (byte)'(' ? Punct(TokenKind.TripleTermOpen, 3) : Punct(TokenKind.ReifiedOpen, 2);
        }

        return Peek(1) == (byte)'=' ? Punct(TokenKind.LessOrEqual, 2) : Punct(TokenKind.Less, 1);
    }

    private Token LexNumber(int start)
    {
        int at = start;

        if (Text[at] is (byte)'+' or (byte)'-')
        {
            at++;
        }

        int digitsBefore = 0;

        while (at < Text.Length && Chars.IsAsciiDigit(Text[at]))
        {
            at++;
            digitsBefore++;
        }

        TokenKind kind = TokenKind.Integer;

        if (at < Text.Length && Text[at] == (byte)'.')
        {
            int afterDot = at + 1;
            int digitsAfter = 0;

            while (afterDot < Text.Length && Chars.IsAsciiDigit(Text[afterDot]))
            {
                afterDot++;
                digitsAfter++;
            }

            bool exponentFollows = afterDot < Text.Length && Text[afterDot] is (byte)'e' or (byte)'E' && HasExponentDigits(afterDot);

            // DECIMAL needs digits after the dot; DOUBLE '1.e3' does not.
            // '1.' with neither is INTEGER followed by the dot.
            if (digitsAfter > 0 || (exponentFollows && digitsBefore > 0))
            {
                at = afterDot;
                kind = TokenKind.Decimal;
            }
        }

        if (at < Text.Length && Text[at] is (byte)'e' or (byte)'E' && HasExponentDigits(at))
        {
            at++;

            if (Text[at] is (byte)'+' or (byte)'-')
            {
                at++;
            }

            while (at < Text.Length && Chars.IsAsciiDigit(Text[at]))
            {
                at++;
            }

            kind = TokenKind.Double;
        }

        if (kind == TokenKind.Integer && digitsBefore == 0)
        {
            throw Error(SparqlErrorKind.Syntax, start, "Expected a number.");
        }

        Token token = Make(kind, start, at - start);
        _position = at;
        return token;
    }

    private readonly bool HasExponentDigits(int atExponent)
    {
        int at = atExponent + 1;

        if (at < Text.Length && Text[at] is (byte)'+' or (byte)'-')
        {
            at++;
        }

        return at < Text.Length && Chars.IsAsciiDigit(Text[at]);
    }

    private Token LexString(int start)
    {
        byte quote = Text[start];
        bool isLong = Peek(1) == quote && Peek(2) == quote;
        int at = start + (isLong ? 3 : 1);

        while (true)
        {
            if (at >= Text.Length)
            {
                throw Error(SparqlErrorKind.UnexpectedEnd, start, "Unterminated string literal.");
            }

            byte c = Text[at];

            if (c == (byte)'\\')
            {
                // Measured here so that the token has a definite end; decoded later.
                at += MeasureEscape(at, inString: true);
                continue;
            }

            if (c == quote)
            {
                if (!isLong)
                {
                    at++;
                    break;
                }

                if (at + 2 < Text.Length && Text[at + 1] == quote && Text[at + 2] == quote)
                {
                    at += 3;
                    break;
                }

                at++;
                continue;
            }

            if (!isLong && c is 0x0A or 0x0D)
            {
                throw Error(SparqlErrorKind.Syntax, at, "A line break inside a short string literal; use the triple-quoted form.");
            }

            at++;
        }

        Token token = Make(TokenKind.String, start, at - start);
        AdvanceOver(at);
        return token;
    }

    /// <summary>The length of the escape at a backslash, checked for shape only.</summary>
    private readonly int MeasureEscape(int at, bool inString)
    {
        if (at + 1 >= Text.Length)
        {
            throw Error(SparqlErrorKind.InvalidEscape, at, "A backslash at the end of the input.");
        }

        byte c = Text[at + 1];
        int digits = c switch
        {
            (byte)'u' => 4,
            (byte)'U' => 8,
            _ => 0,
        };

        if (digits == 0)
        {
            if (inString && c is (byte)'t' or (byte)'b' or (byte)'n' or (byte)'r' or (byte)'f' or (byte)'"' or (byte)'\'' or (byte)'\\')
            {
                return 2;
            }

            throw Error(SparqlErrorKind.InvalidEscape, at + 1, "Not an escape: '\\' may be followed by t, b, n, r, f, \", ', \\, uXXXX or UXXXXXXXX here.");
        }

        for (int i = at + 2; i < at + 2 + digits; i++)
        {
            if (i >= Text.Length || !Chars.IsHex(Text[i]))
            {
                throw Error(SparqlErrorKind.InvalidEscape, i, "A \\u escape needs four hexadecimal digits and \\U needs eight.");
            }
        }

        return 2 + digits;
    }

    private Token LexLangTag(int start)
    {
        int at = start + 1;

        if (at >= Text.Length || !Chars.IsAsciiLetter(Text[at]))
        {
            throw Error(SparqlErrorKind.InvalidLanguageTag, at, "A language tag needs at least one letter after '@'.");
        }

        while (at < Text.Length && Chars.IsAsciiLetter(Text[at]))
        {
            at++;
        }

        while (at + 1 < Text.Length && Text[at] == (byte)'-' && (Chars.IsAsciiLetter(Text[at + 1]) || Chars.IsAsciiDigit(Text[at + 1])))
        {
            at++;

            while (at < Text.Length && (Chars.IsAsciiLetter(Text[at]) || Chars.IsAsciiDigit(Text[at])))
            {
                at++;
            }
        }

        if (at + 2 < Text.Length && Text[at] == (byte)'-' && Text[at + 1] == (byte)'-' && Chars.IsAsciiLetter(Text[at + 2]))
        {
            at += 2;

            while (at < Text.Length && Chars.IsAsciiLetter(Text[at]))
            {
                at++;
            }
        }

        Token token = Make(TokenKind.LangTag, start, at - start);
        _position = at;
        return token;
    }

    private Token LexBlankNodeLabel(int start)
    {
        int at = start + 2;

        if (!Chars.TryRune(Text, at, out int first, out int firstLength) || !(Chars.IsPnCharsU(first) || first is >= '0' and <= '9'))
        {
            throw Error(SparqlErrorKind.Syntax, at, "A blank node label needs a name character after '_:'.");
        }

        at += firstLength;
        int lastGood = at;

        while (Chars.TryRune(Text, at, out int c, out int length) && (Chars.IsPnChars(c) || c == '.'))
        {
            at += length;

            if (c != '.')
            {
                lastGood = at;
            }
        }

        // A label may contain dots and may not end with one.
        Token token = Make(TokenKind.BlankNodeLabel, start, lastGood - start);
        _position = lastGood;
        return token;
    }

    private Token LexWordOrPrefixedName(int start)
    {
        int at = start;

        if (!Chars.TryRune(Text, at, out int first, out int firstLength))
        {
            throw Error(SparqlErrorKind.InvalidEncoding, at, "Not valid UTF-8.");
        }

        if (!Chars.IsPnCharsBase(first))
        {
            throw Error(SparqlErrorKind.Syntax, at, "Unexpected character.");
        }

        at += firstLength;
        int lastGood = at;

        while (Chars.TryRune(Text, at, out int c, out int length) && (Chars.IsPnChars(c) || c == '.'))
        {
            at += length;

            if (c != '.')
            {
                lastGood = at;
            }
        }

        if (lastGood < Text.Length && Text[lastGood] == (byte)':')
        {
            return LexPrefixedName(start, lastGood);
        }

        Token token = Make(TokenKind.Word, start, lastGood - start);
        _position = lastGood;
        return token;
    }

    /// <summary>From the colon of a prefixed name: the optional PN_LOCAL, with its escapes measured.</summary>
    private Token LexPrefixedName(int start, int colon)
    {
        int at = colon + 1;
        int lastGood = at;
        bool first = true;

        while (at < Text.Length)
        {
            byte b = Text[at];

            if (b == (byte)'%')
            {
                if (at + 2 >= Text.Length || !Chars.IsHex(Text[at + 1]) || !Chars.IsHex(Text[at + 2]))
                {
                    throw Error(SparqlErrorKind.InvalidEscape, at, "A '%' in a local name must be followed by two hexadecimal digits.");
                }

                at += 3;
                lastGood = at;
            }
            else if (b == (byte)'\\')
            {
                if (at + 1 >= Text.Length || !Chars.IsLocalEscape(Text[at + 1]))
                {
                    throw Error(SparqlErrorKind.InvalidEscape, at + 1, "Not a local name escape: '\\' may be followed by one of _~.-!$&'()*+,;=/?#@% here.");
                }

                at += 2;
                lastGood = at;
            }
            else if (b == (byte)':')
            {
                at++;
                lastGood = at;
            }
            else if (Chars.TryRune(Text, at, out int c, out int length))
            {
                bool allowed = first ? Chars.IsPnCharsU(c) || c is >= '0' and <= '9' : Chars.IsPnChars(c) || c == '.';

                if (!allowed)
                {
                    break;
                }

                at += length;

                if (c != '.')
                {
                    lastGood = at;
                }
            }
            else
            {
                break;
            }

            first = false;
        }

        Token token = Make(TokenKind.PrefixedName, start, lastGood - start);
        _position = lastGood;
        return token;
    }

    // --- decoding ----------------------------------------------------------

    internal enum Escapes : byte
    {
        Iri,
        String,
        LocalName,
    }

    /// <summary>
    /// Processes the escapes in a token's raw text into the buffer, each
    /// substituted once (§19.2). Returns false at an escape that produces a
    /// surrogate, with the offset of the backslash.
    /// </summary>
    internal static bool Decode(ReadOnlySpan<byte> raw, Escapes allowed, ref PooledBytes into, out int badEscapeAt)
    {
        badEscapeAt = -1;
        int i = 0;

        while (i < raw.Length)
        {
            byte b = raw[i];

            if (b != (byte)'\\')
            {
                into.Add(b);
                i++;
                continue;
            }

            byte c = raw[i + 1];

            if (allowed == Escapes.LocalName)
            {
                into.Add(c);
                i += 2;
                continue;
            }

            int digits = c switch
            {
                (byte)'u' => 4,
                (byte)'U' => 8,
                _ => 0,
            };

            if (digits == 0)
            {
                into.Add(c switch
                {
                    (byte)'t' => (byte)0x09,
                    (byte)'b' => (byte)0x08,
                    (byte)'n' => (byte)0x0A,
                    (byte)'r' => (byte)0x0D,
                    (byte)'f' => (byte)0x0C,
                    _ => c,
                });
                i += 2;
                continue;
            }

            int codePoint = 0;

            for (int d = 0; d < digits; d++)
            {
                codePoint = (codePoint << 4) | Chars.HexValue(raw[i + 2 + d]);
            }

            if (!Rune.TryCreate(codePoint, out Rune rune))
            {
                badEscapeAt = i;
                return false;
            }

            Span<byte> utf8 = into.Reserve(4);
            into.Commit(rune.EncodeToUtf8(utf8));
            i += 2 + digits;
        }

        return true;
    }

    // --- errors ------------------------------------------------------------

    internal readonly SparqlParseException Error(SparqlErrorKind kind, int at, string message)
    {
        // The line and column of an offset at or after the current position:
        // count the breaks between here and there.
        int line = _line;
        int lineStart = _lineStart;

        for (int i = _position; i < at && i < Text.Length; i++)
        {
            if (Text[i] == 0x0A || (Text[i] == 0x0D && (i + 1 >= Text.Length || Text[i + 1] != 0x0A)))
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new SparqlParseException(new SparqlParseError(kind, at, line, at - lineStart + 1, message));
    }
}
