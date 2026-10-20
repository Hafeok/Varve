// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Varve.Xsd;

/// <summary>
/// A recursive-descent parser of F&amp;O 3.1 §5.6.1's grammar (XML Schema
/// 1.1 Part 2 Appendix G with §5.6.1's extensions) that writes the .NET
/// equivalent as it goes. Every construct with a different meaning in .NET is
/// written out explicitly — character classes as exact sets, anchors as
/// lookarounds, case-insensitivity as case-variant sets — so the result needs
/// no <c>RegexOptions</c> but <c>CultureInvariant</c> (ADR 0153).
/// </summary>
internal sealed class XPathRegexParser
{
    private readonly string _pattern;
    private readonly bool _dotAll;
    private readonly bool _multiline;
    private readonly bool _ignoreCase;
    private readonly StringBuilder _output;
    private readonly List<bool> _closed = [];
    private int _position;
    private XPathRegexError _error;

    internal XPathRegexParser(string pattern, bool dotAll, bool multiline, bool ignoreCase, StringBuilder output)
    {
        _pattern = pattern;
        _dotAll = dotAll;
        _multiline = multiline;
        _ignoreCase = ignoreCase;
        _output = output;
    }

    internal int GroupCount => _closed.Count;

    internal bool TryParse(out XPathRegexError error)
    {
        bool parsed = RegExp() && (AtEnd || Fail("unbalanced ')'"));
        error = _error;
        return parsed;
    }

    private bool AtEnd => _position >= _pattern.Length;

    private char Peek => _pattern[_position];

    private bool Fail(string message)
    {
        _error = new XPathRegexError(XPathRegex.InvalidPattern, Math.Min(_position, _pattern.Length), message);
        return false;
    }

    // regExp ::= branch ( '|' branch )*
    private bool RegExp()
    {
        if (!Branch())
        {
            return false;
        }

        while (!AtEnd && Peek == '|')
        {
            _position++;
            _output.Append('|');
            if (!Branch())
            {
                return false;
            }
        }

        return true;
    }

    // branch ::= piece*
    private bool Branch()
    {
        while (!AtEnd && Peek is not ('|' or ')'))
        {
            if (!Piece())
            {
                return false;
            }
        }

        return true;
    }

    // piece ::= atom quantifier?
    private bool Piece()
    {
        int start = _position;
        if (!Atom(out bool quantifiable))
        {
            return false;
        }

        if (AtEnd || Peek is not ('?' or '*' or '+' or '{'))
        {
            return true;
        }

        if (!quantifiable)
        {
            _position = start;
            return Fail("an anchor cannot be quantified");
        }

        return Quantifier();
    }

    // quantifier ::= ( [?*+] | '{' quantity '}' ) '?'?
    private bool Quantifier()
    {
        char c = Peek;
        if (c is '?' or '*' or '+')
        {
            _position++;
            _output.Append(c);
        }
        else
        {
            _position++;
            if (!Number(out int min))
            {
                return Fail("a '{' must start a quantifier {n}, {n,} or {n,m}");
            }

            int? max = min;
            if (!AtEnd && Peek == ',')
            {
                _position++;
                if (!AtEnd && char.IsAsciiDigit(Peek))
                {
                    if (!Number(out int m))
                    {
                        return false;
                    }

                    if (m < min)
                    {
                        return Fail("a quantifier's maximum is below its minimum");
                    }

                    max = m;
                }
                else
                {
                    max = null;
                }
            }

            if (AtEnd || Peek != '}')
            {
                return Fail("a quantifier must end with '}'");
            }

            _position++;
            _output.Append('{').Append(min.ToString(CultureInfo.InvariantCulture));
            if (max != min)
            {
                _output.Append(',');
                if (max is int bounded)
                {
                    _output.Append(bounded.ToString(CultureInfo.InvariantCulture));
                }
            }

            _output.Append('}');
        }

        if (!AtEnd && Peek == '?')
        {
            _position++;
            _output.Append('?');
        }

        if (!AtEnd && Peek is '?' or '*' or '+' or '{')
        {
            return Fail("a quantifier cannot be quantified");
        }

        return true;
    }

    private bool Number(out int value)
    {
        value = 0;
        int start = _position;
        while (!AtEnd && char.IsAsciiDigit(Peek))
        {
            if (value > 100_000_000)
            {
                return Fail("a quantifier is too large");
            }

            value = (value * 10) + (Peek - '0');
            _position++;
        }

        return _position > start;
    }

    // atom ::= NormalChar | charClass | ( '(' '?:'? regExp ')' ) | backReference, and '^' and '$' (§5.6.1.1)
    private bool Atom(out bool quantifiable)
    {
        quantifiable = true;
        char c = Peek;
        switch (c)
        {
            case '(':
                return Group();
            case '[':
                {
                    if (!ClassExpression(out ClassExpr expression))
                    {
                        return false;
                    }

                    WriteClass(expression);
                    return true;
                }

            case '.':
                _position++;
                (_dotAll ? CodePointSet.All : CodePointSet.Of([(0x0A, 0x0A), (0x0D, 0x0D)]).Complement()).WriteDotNet(_output);
                return true;
            case '^':
                _position++;
                quantifiable = false;

                // §5.6.2: in multi-line mode, after a newline that is not the last character.
                _output.Append(_multiline ? "(?:\\A|(?<=\\n)(?!\\z))" : "\\A");
                return true;
            case '$':
                _position++;
                quantifiable = false;

                // §5.6.2: before a newline, or at the end when the input does not end with one.
                _output.Append(_multiline ? "(?:(?=\\n)|\\z(?<!\\n))" : "\\z");
                return true;
            case '\\':
                return Escape();
            case ')':
                return Fail("unbalanced ')'");
            case '?' or '*' or '+':
                return Fail("a quantifier must follow something to quantify");
            case '{' or '}' or ']':
                return Fail("'" + c + "' must be escaped");
            default:
                WriteCharacter(ReadCodePoint());
                return true;
        }
    }

    private bool Group()
    {
        _position++;
        bool capturing = true;
        if (!AtEnd && Peek == '?')
        {
            if (_position + 1 < _pattern.Length && _pattern[_position + 1] == ':')
            {
                _position += 2;
                capturing = false;
            }
            else
            {
                return Fail("'(?' is only allowed as '(?:'");
            }
        }

        int group = -1;
        if (capturing)
        {
            group = _closed.Count;
            _closed.Add(false);
            _output.Append('(');
        }
        else
        {
            _output.Append("(?:");
        }

        if (!RegExp())
        {
            return false;
        }

        if (AtEnd || Peek != ')')
        {
            return Fail("missing ')'");
        }

        _position++;
        _output.Append(')');
        if (group >= 0)
        {
            _closed[group] = true;
        }

        return true;
    }

    private bool Escape()
    {
        int start = _position;
        _position++;
        if (AtEnd)
        {
            return Fail("a pattern cannot end with '\\'");
        }

        char c = Peek;
        if (c is >= '1' and <= '9')
        {
            return BackReference(start);
        }

        _position = start;
        if (!ClassEscape(out ClassExpr? expression, out int single))
        {
            return false;
        }

        if (expression is null)
        {
            WriteCharacter(single);
        }
        else
        {
            WriteClass(expression);
        }

        return true;
    }

    // backReference ::= '\' [1-9][0-9]* (§5.6.1.4)
    private bool BackReference(int start)
    {
        int number = Peek - '0';
        _position++;
        if (number > _closed.Count || !_closed[number - 1])
        {
            _position = start;
            return Fail("a back-reference must name a group closed before it");
        }

        while (!AtEnd && char.IsAsciiDigit(Peek))
        {
            int longer = (number * 10) + (Peek - '0');
            if (longer > _closed.Count || !_closed[longer - 1])
            {
                break;
            }

            number = longer;
            _position++;
        }

        // §5.6.1.4: a group that matched nothing makes the reference match the empty string;
        // §5.6.2: under 'i' the comparison is case-blind.
        string reference = "\\" + number.ToString(CultureInfo.InvariantCulture);
        _output.Append("(?(").Append(number.ToString(CultureInfo.InvariantCulture)).Append(')')
            .Append(_ignoreCase ? "(?i:" + reference + ")" : reference)
            .Append("|)");
        return true;
    }

    /// <summary>
    /// An escape outside or inside a class: a single character escape (in
    /// <paramref name="single"/>, with <paramref name="expression"/> null), or
    /// a multi-character or category escape as a class.
    /// </summary>
    private bool ClassEscape(out ClassExpr? expression, out int single)
    {
        expression = null;
        single = 0;
        _position++;
        if (AtEnd)
        {
            return Fail("a pattern cannot end with '\\'");
        }

        char c = Peek;
        _position++;
        switch (c)
        {
            case 'n':
                single = '\n';
                return true;
            case 'r':
                single = '\r';
                return true;
            case 't':
                single = '\t';
                return true;
            case '\\' or '|' or '.' or '?' or '*' or '+' or '(' or ')' or '{' or '}' or '$' or '-' or '[' or ']' or '^':
                single = c;
                return true;
            case 's':
                expression = ClassExpr.Of(Whitespace);
                return true;
            case 'S':
                expression = ClassExpr.Of(Whitespace.Complement());
                return true;
            case 'd' or 'D' or 'w' or 'W':
                {
                    CodePointSet set = c is 'd' or 'D' ? Category("Nd") : Category("P").Union(Category("Z")).Union(Category("C")).Complement();
                    expression = ClassExpr.Of(c is 'D' or 'W' ? set.Complement() : set);
                    return true;
                }

            case 'i' or 'I':
                expression = ClassExpr.Of(c == 'i' ? NameStart : NameStart.Complement());
                return true;
            case 'c' or 'C':
                expression = ClassExpr.Of(c == 'c' ? NameChar : NameChar.Complement());
                return true;
            case 'p' or 'P':
                return Property(c == 'P', out expression);
            default:
                _position--;
                return Fail("'\\" + c + "' is not an escape XPath defines");
        }
    }

    // catEsc ::= '\p{' charProp '}'; complEsc ::= '\P{' charProp '}'
    private bool Property(bool complement, out ClassExpr? expression)
    {
        expression = null;
        if (AtEnd || Peek != '{')
        {
            return Fail("'\\p' and '\\P' must be followed by '{'");
        }

        int close = _pattern.IndexOf('}', _position);
        if (close < 0)
        {
            return Fail("a property escape must end with '}'");
        }

        string name = _pattern.Substring(_position + 1, close - _position - 1);
        if (name.StartsWith("Is", StringComparison.Ordinal) && name.Length > 2)
        {
            foreach (char ch in name.AsSpan(2))
            {
                if (!char.IsAsciiLetterOrDigit(ch) && ch != '-')
                {
                    return Fail("'" + name + "' is not a block name");
                }
            }

            // §5.6.1.5: a block .NET does not know fails at compilation, as FORX0002.
            _position = close + 1;
            expression = ClassExpr.OfBlock(name, complement);
            return true;
        }

        if (!UnicodeCategories.TryGet(name, out CodePointSet set))
        {
            return Fail("'" + name + "' is not a Unicode general category");
        }

        _position = close + 1;
        expression = ClassExpr.Of(complement ? set.Complement() : set);
        return true;
    }

    // charClassExpr ::= '[' charGroup ']'; charGroup ::= ( posCharGroup | negCharGroup ) ( '-' charClassExpr )?
    private bool ClassExpression(out ClassExpr expression)
    {
        expression = ClassExpr.Of(CodePointSet.Empty);
        _position++;
        bool negated = false;
        if (!AtEnd && Peek == '^')
        {
            negated = true;
            _position++;
        }

        ClassExpr group = ClassExpr.Of(CodePointSet.Empty);
        bool any = false;
        while (true)
        {
            if (AtEnd)
            {
                return Fail("a character class must end with ']'");
            }

            char c = Peek;
            if (c == ']')
            {
                if (!any)
                {
                    return Fail("a character class cannot be empty");
                }

                _position++;
                break;
            }

            if (c == '-' && _position + 1 < _pattern.Length && _pattern[_position + 1] == '[')
            {
                if (!any)
                {
                    return Fail("a subtraction needs something to subtract from");
                }

                _position++;
                if (!ClassExpression(out ClassExpr subtracted))
                {
                    return false;
                }

                if (AtEnd || Peek != ']')
                {
                    return Fail("a subtraction must end its character class");
                }

                _position++;
                expression = ClassExpr.Subtract(group.Negate(negated), subtracted);
                return true;
            }

            if (c == '[')
            {
                return Fail("'[' must be escaped inside a character class");
            }

            if (c == '\\')
            {
                if (_position + 1 < _pattern.Length && char.IsAsciiDigit(_pattern[_position + 1]))
                {
                    _position++;
                    return Fail("a back-reference cannot appear in a character class");
                }

                if (!ClassEscape(out ClassExpr? escaped, out int single))
                {
                    return false;
                }

                if (escaped is not null)
                {
                    group = group.Union(escaped);
                    any = true;
                    continue;
                }

                if (!RangeFrom(single, ref group))
                {
                    return false;
                }

                any = true;
                continue;
            }

            if (c == '-')
            {
                // A hyphen is itself at the start of a group and just before its end.
                bool atStart = !any;
                bool atEnd = _position + 1 < _pattern.Length && _pattern[_position + 1] == ']';
                if (!atStart && !atEnd)
                {
                    return Fail("'-' must be escaped inside a character class unless it starts or ends it");
                }

                _position++;
                group = group.Union(ClassExpr.Of(Characters(CodePointSet.Single('-'))));
                any = true;
                continue;
            }

            if (!RangeFrom(ReadCodePoint(), ref group))
            {
                return false;
            }

            any = true;
        }

        expression = group.Negate(negated);
        return true;
    }

    /// <summary>A single character just read, or the range it starts: <c>charOrEsc '-' charOrEsc</c>.</summary>
    private bool RangeFrom(int first, ref ClassExpr group)
    {
        if (!AtEnd && Peek == '-' && _position + 1 < _pattern.Length && _pattern[_position + 1] is not (']' or '['))
        {
            _position++;
            int last;
            if (Peek == '\\')
            {
                if (!ClassEscape(out ClassExpr? escaped, out last))
                {
                    return false;
                }

                if (escaped is not null)
                {
                    return Fail("a range cannot end with a multi-character escape");
                }
            }
            else if (Peek == '-')
            {
                return Fail("a range cannot end with an unescaped '-'");
            }
            else
            {
                last = ReadCodePoint();
            }

            if (last < first)
            {
                return Fail("a range's end is below its start");
            }

            group = group.Union(ClassExpr.Of(Characters(CodePointSet.Range(first, last))));
            return true;
        }

        group = group.Union(ClassExpr.Of(Characters(CodePointSet.Single(first))));
        return true;
    }

    /// <summary>Characters and ranges under the <c>i</c> flag gain their case-variants; escapes do not (§5.6.2).</summary>
    private CodePointSet Characters(CodePointSet set) => _ignoreCase ? CaseVariants.Close(set) : set;

    private void WriteCharacter(int codePoint)
    {
        if (_ignoreCase)
        {
            CodePointSet variants = CaseVariants.Close(CodePointSet.Single(codePoint));
            if (variants.Ranges.Count != 1 || variants.Ranges[0].First != variants.Ranges[0].Last)
            {
                variants.WriteDotNet(_output);
                return;
            }
        }

        string text = char.ConvertFromUtf32(codePoint);
        int i = 0;
        XPathRegex.AppendLiteral(_output, text, ref i);
    }

    private void WriteClass(ClassExpr expression)
    {
        if (expression.TryExact(out CodePointSet? set))
        {
            set.WriteDotNet(_output);
            return;
        }

        // A class naming a Unicode block is written in .NET's own syntax, which
        // knows only blocks of the BMP and reads them per UTF-16 unit; its
        // members outside the BMP are exact, and written beside it (ADR 0153).
        CodePointSet astral = expression.Astral();
        if (astral.IsEmpty)
        {
            expression.WriteNative(_output);
            return;
        }

        _output.Append("(?:");
        astral.WriteDotNet(_output);
        _output.Append('|');
        expression.WriteNative(_output);
        _output.Append(')');
    }

    private int ReadCodePoint()
    {
        char c = _pattern[_position++];
        if (char.IsHighSurrogate(c) && !AtEnd && char.IsLowSurrogate(Peek))
        {
            return char.ConvertToUtf32(c, _pattern[_position++]);
        }

        return c;
    }

    private static CodePointSet Category(string name)
    {
        UnicodeCategories.TryGet(name, out CodePointSet set);
        return set;
    }

    // \s: [#x20\t\n\r] (XML Schema 1.1 Part 2 §G.4.2.5).
    private static readonly CodePointSet Whitespace = CodePointSet.Of([(0x09, 0x0A), (0x0D, 0x0D), (0x20, 0x20)]);

    // \i: XML 1.0 (fifth edition) NameStartChar.
    private static readonly CodePointSet NameStart = CodePointSet.Of(
    [
        (':', ':'), ('A', 'Z'), ('_', '_'), ('a', 'z'), (0xC0, 0xD6), (0xD8, 0xF6), (0xF8, 0x2FF), (0x370, 0x37D),
        (0x37F, 0x1FFF), (0x200C, 0x200D), (0x2070, 0x218F), (0x2C00, 0x2FEF), (0x3001, 0xD7FF), (0xF900, 0xFDCF),
        (0xFDF0, 0xFFFD), (0x10000, 0xEFFFF),
    ]);

    // \c: XML 1.0 (fifth edition) NameChar.
    private static readonly CodePointSet NameChar = NameStart.Union(CodePointSet.Of(
    [
        ('-', '-'), ('.', '.'), ('0', '9'), (0xB7, 0xB7), (0x300, 0x36F), (0x203F, 0x2040),
    ]));

    /// <summary>
    /// A character class as written: a set of characters, the blocks it
    /// names, whether its group is negated, and what it subtracts. Exact
    /// whenever it names no block.
    /// </summary>
    private sealed class ClassExpr
    {
        private readonly CodePointSet _set;
        private readonly List<(string Name, bool Complement)> _blocks;
        private readonly bool _negated;
        private readonly ClassExpr? _subtracted;
        private readonly ClassExpr? _from;

        private ClassExpr(CodePointSet set, List<(string, bool)> blocks, bool negated, ClassExpr? from, ClassExpr? subtracted)
        {
            _set = set;
            _blocks = blocks;
            _negated = negated;
            _from = from;
            _subtracted = subtracted;
        }

        internal static ClassExpr Of(CodePointSet set) => new(set, [], false, null, null);

        internal static ClassExpr OfBlock(string name, bool complement) => new(CodePointSet.Empty, [(name, complement)], false, null, null);

        internal static ClassExpr Subtract(ClassExpr from, ClassExpr subtracted) => new(CodePointSet.Empty, [], false, from, subtracted);

        internal ClassExpr Union(ClassExpr other)
        {
            if (_from is not null || other._from is not null || _negated || other._negated)
            {
                throw new InvalidOperationException("Only the members of one group are united.");
            }

            List<(string, bool)> blocks = [.. _blocks, .. other._blocks];
            return new ClassExpr(_set.Union(other._set), blocks, false, null, null);
        }

        internal ClassExpr Negate(bool negated) => negated ? new ClassExpr(_set, _blocks, true, null, null) : this;

        internal bool TryExact(out CodePointSet set)
        {
            if (_from is not null)
            {
                if (_from.TryExact(out CodePointSet from) && _subtracted!.TryExact(out CodePointSet subtracted))
                {
                    set = from.Subtract(subtracted);
                    return true;
                }

                set = CodePointSet.Empty;
                return false;
            }

            if (_blocks.Count > 0)
            {
                set = CodePointSet.Empty;
                return false;
            }

            set = _negated ? _set.Complement() : _set;
            return true;
        }

        /// <summary>The members outside the BMP, exactly: no block .NET knows has any.</summary>
        internal CodePointSet Astral()
        {
            CodePointSet supplementary = CodePointSet.Range(0x10000, CodePointSet.MaxCodePoint);
            if (_from is not null)
            {
                return _from.Astral().Subtract(_subtracted!.Astral());
            }

            CodePointSet inner = _set.Intersect(supplementary);
            if (_blocks.Exists(static b => b.Complement))
            {
                inner = supplementary;
            }

            return _negated ? supplementary.Subtract(inner) : inner;
        }

        /// <summary>
        /// The class in .NET's syntax over the BMP. Surrogate code units are
        /// subtracted, so it never matches half of a pair.
        /// </summary>
        internal void WriteNative(StringBuilder text)
        {
            if (_from is not null)
            {
                // .NET's subtraction has XML Schema's syntax: [group-[subtracted]].
                _from.WriteGroup(text);
                text.Append("-[");
                _subtracted!.WriteMembers(text, withSurrogates: true);
                text.Append("]]");
                return;
            }

            WriteGroup(text);
            text.Append("-[\\uD800-\\uDFFF]]");
        }

        /// <summary>The group without its closing bracket.</summary>
        private void WriteGroup(StringBuilder text)
        {
            text.Append('[');
            if (_negated)
            {
                text.Append('^');
            }

            WriteItems(text);
        }

        private void WriteMembers(StringBuilder text, bool withSurrogates)
        {
            if (_from is not null)
            {
                // A nested subtraction: written as is.
                _from.WriteGroup(text);
                text.Append("-[");
                _subtracted!.WriteMembers(text, withSurrogates: false);
                text.Append("]]");
                return;
            }

            if (_negated)
            {
                text.Append('^');
                WriteItems(text);
                return;
            }

            WriteItems(text);
            if (withSurrogates)
            {
                text.Append("\\uD800-\\uDFFF");
            }
        }

        private void WriteItems(StringBuilder text)
        {
            foreach ((int first, int last) in _set.Ranges)
            {
                if (first > 0xFFFF)
                {
                    break;
                }

                CodePointSet.WriteClassChar(text, first);
                int end = Math.Min(last, 0xFFFF);
                if (end > first)
                {
                    text.Append('-');
                    CodePointSet.WriteClassChar(text, end);
                }
            }

            foreach ((string name, bool complement) in _blocks)
            {
                text.Append(complement ? "\\P{" : "\\p{").Append(name).Append('}');
            }
        }
    }
}
