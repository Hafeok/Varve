// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Xsd;

/// <summary>
/// The regular expressions of XPath and XQuery Functions and Operators 3.1
/// §5.6 — the syntax of <c>fn:matches</c>, <c>fn:replace</c>, SPARQL's
/// <c>REGEX</c> and <c>REPLACE</c>, and SHACL's <c>sh:pattern</c> — translated
/// to .NET's dialect (ADR 0153). Translation never touches
/// <c>System.Text.RegularExpressions</c>; only <see cref="TryCompile"/> does.
/// </summary>
public static class XPathRegex
{
    /// <summary>F&amp;O's error code for invalid flags.</summary>
    internal const string InvalidFlags = "FORX0001";

    /// <summary>F&amp;O's error code for an invalid pattern.</summary>
    internal const string InvalidPattern = "FORX0002";

    /// <summary>F&amp;O's error code for an invalid replacement string.</summary>
    internal const string InvalidReplacement = "FORX0004";

    /// <summary>
    /// Translates <paramref name="pattern"/> under <paramref name="flags"/>
    /// (F&amp;O 3.1 §5.6.2: <c>s</c>, <c>m</c>, <c>i</c>, <c>x</c>, <c>q</c>).
    /// False, with the error, for anything §5.6.1 does not define.
    /// </summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public static bool TryTranslate(string pattern, string flags, out XPathRegexTranslation translation, out XPathRegexError error)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(flags);
        translation = default;
        bool dotAll = false, multiline = false, ignoreCase = false, extended = false, literal = false;
        for (int i = 0; i < flags.Length; i++)
        {
            switch (flags[i])
            {
                case 's':
                    dotAll = true;
                    break;
                case 'm':
                    multiline = true;
                    break;
                case 'i':
                    ignoreCase = true;
                    break;
                case 'x':
                    extended = true;
                    break;
                case 'q':
                    literal = true;
                    break;
                default:
                    error = new XPathRegexError(InvalidFlags, i, "unknown flag '" + flags[i] + "'");
                    return false;
            }
        }

        StringBuilder output = new(pattern.Length * 2);
        if (literal)
        {
            // §5.6.2: q treats every character as itself; m, s and x have no effect, i does.
            for (int i = 0; i < pattern.Length; i++)
            {
                if (ignoreCase)
                {
                    int codePoint = char.ConvertToUtf32(pattern, i);
                    if (codePoint > 0xFFFF)
                    {
                        i++;
                    }

                    CaseVariants.Close(CodePointSet.Single(codePoint)).WriteDotNet(output);
                }
                else
                {
                    AppendLiteral(output, pattern, ref i);
                }
            }

            translation = new XPathRegexTranslation(output.ToString(), groupCount: 0);
            error = default;
            return true;
        }

        if (extended)
        {
            pattern = RemoveWhitespace(pattern);
        }

        XPathRegexParser parser = new(pattern, dotAll, multiline, ignoreCase, output);
        if (!parser.TryParse(out error))
        {
            return false;
        }

        translation = new XPathRegexTranslation(output.ToString(), parser.GroupCount);
        return true;
    }

    /// <summary>
    /// The translation compiled, every match bounded by <paramref name="timeout"/>.
    /// Never <c>RegexOptions.Compiled</c>, which Native AOT and the browser
    /// ignore, and never <c>NonBacktracking</c>, which refuses back-references.
    /// False only for a Unicode block name .NET does not know.
    /// </summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public static bool TryCompile(XPathRegexTranslation translation, TimeSpan timeout, [NotNullWhen(true)] out Regex? regex, out XPathRegexError error)
    {
        if (translation.Pattern is null)
        {
            throw new ArgumentException("The translation is default; use TryTranslate.", nameof(translation));
        }

        try
        {
            // Every flag is already in the pattern (ADR 0153), so no option but CultureInvariant.
            regex = new Regex(translation.Pattern, RegexOptions.CultureInvariant, timeout);
            error = default;
            return true;
        }
        catch (ArgumentException e)
        {
            regex = null;
            error = new XPathRegexError(InvalidPattern, -1, e.Message);
            return false;
        }
    }

    /// <summary>
    /// <c>fn:replace</c>'s replacement string (F&amp;O 3.1 §5.6.3) in .NET's
    /// form: <c>\\</c> and <c>\$</c> are the characters, <c>$N</c> is group N
    /// taking as many digits as still name a group of the
    /// <paramref name="groupCount"/>, and a group above the count is empty.
    /// Anything else after a backslash or a dollar is <c>FORX0004</c>.
    /// </summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public static bool TryTranslateReplacement(string replacement, int groupCount, [NotNullWhen(true)] out string? translated, out XPathRegexError error)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        StringBuilder result = new(replacement.Length + 8);
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];
            if (c == '\\')
            {
                if (i + 1 >= replacement.Length || replacement[i + 1] is not ('\\' or '$'))
                {
                    translated = null;
                    error = new XPathRegexError(InvalidReplacement, i, "a backslash must escape '\\' or '$'");
                    return false;
                }

                result.Append(replacement[++i] == '$' ? "$$" : "\\");
            }
            else if (c == '$')
            {
                int at = i++;
                if (i >= replacement.Length || !char.IsAsciiDigit(replacement[i]))
                {
                    translated = null;
                    error = new XPathRegexError(InvalidReplacement, at, "'$' must be followed by a group number");
                    return false;
                }

                int group = replacement[i] - '0';
                while (i + 1 < replacement.Length && char.IsAsciiDigit(replacement[i + 1]) && (group * 10) + (replacement[i + 1] - '0') <= groupCount)
                {
                    group = (group * 10) + (replacement[++i] - '0');
                }

                if (group <= groupCount)
                {
                    result.Append("${").Append(group.ToString(CultureInfo.InvariantCulture)).Append('}');
                }
            }
            else
            {
                // .NET's replacement syntax is $-based; a lone character is itself.
                result.Append(c);
            }
        }

        translated = result.ToString();
        error = default;
        return true;
    }

    /// <summary>The character at <paramref name="i"/> (a surrogate pair as one) as a .NET literal atom.</summary>
    internal static void AppendLiteral(StringBuilder output, string text, ref int i)
    {
        char c = text[i];
        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            output.Append("(?:");
            CodePointSet.WriteClassChar(output, c);
            CodePointSet.WriteClassChar(output, text[++i]);
            output.Append(')');
            return;
        }

        if (c is '\\' or '*' or '+' or '?' or '|' or '{' or '}' or '[' or ']' or '(' or ')' or '^' or '$' or '.' or '#' or ' ' or '-'
            || char.IsControl(c) || char.IsSurrogate(c))
        {
            CodePointSet.WriteClassChar(output, c);
        }
        else
        {
            output.Append(c);
        }
    }

    /// <summary>
    /// The <c>x</c> flag (F&amp;O 3.1 §5.6.2): the four white space characters
    /// are removed, except inside a character class expression.
    /// </summary>
    private static string RemoveWhitespace(string pattern)
    {
        StringBuilder result = new(pattern.Length);
        int depth = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                result.Append(c).Append(pattern[++i]);
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']' && depth > 0)
            {
                depth--;
            }

            if (depth == 0 && c is ' ' or '\t' or '\n' or '\r')
            {
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }
}

/// <summary>
/// A pattern in .NET's dialect; build one with
/// <see cref="XPathRegex.TryTranslate"/>. It holds no .NET regex type, so a
/// caller can keep one without loading <c>System.Text.RegularExpressions</c>.
/// </summary>
public readonly struct XPathRegexTranslation : IEquatable<XPathRegexTranslation>
{
    internal XPathRegexTranslation(string pattern, int groupCount)
    {
        Pattern = pattern;
        GroupCount = groupCount;
    }

    /// <summary>
    /// The pattern in .NET's syntax, every flag already applied: compile it
    /// with <c>RegexOptions.CultureInvariant</c> and nothing else.
    /// </summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public string Pattern { get; }

    /// <summary>The number of capturing groups, for a replacement string.</summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public int GroupCount { get; }

    /// <inheritdoc/>
    public bool Equals(XPathRegexTranslation other) =>
        string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) && GroupCount == other.GroupCount;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XPathRegexTranslation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Pattern, GroupCount);

    /// <summary>Equality.</summary>
    public static bool operator ==(XPathRegexTranslation left, XPathRegexTranslation right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(XPathRegexTranslation left, XPathRegexTranslation right) => !left.Equals(right);
}

/// <summary>Why a pattern, its flags or a replacement string was refused: F&amp;O's code, where, and what.</summary>
public readonly struct XPathRegexError : IEquatable<XPathRegexError>
{
    internal XPathRegexError(string code, int position, string message)
    {
        Code = code;
        Position = position;
        Message = message;
    }

    /// <summary><c>FORX0001</c>, <c>FORX0002</c> or <c>FORX0004</c>.</summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public string Code { get; }

    /// <summary>The offset in the pattern, flags or replacement, or -1 when the error has none.</summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public int Position { get; }

    /// <summary>What is wrong, for a person.</summary>
    [DesignDecision(typeof(XpathRegularExpressionsInVarveXsd.RegexArgumentsAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public string Message { get; }

    /// <inheritdoc/>
    public override string ToString() => Position >= 0
        ? Code + " at " + Position.ToString(CultureInfo.InvariantCulture) + ": " + Message
        : Code + ": " + Message;

    /// <inheritdoc/>
    public bool Equals(XPathRegexError other) =>
        string.Equals(Code, other.Code, StringComparison.Ordinal) && Position == other.Position
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XPathRegexError other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Position, Message);

    /// <summary>Equality.</summary>
    public static bool operator ==(XPathRegexError left, XPathRegexError right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(XPathRegexError left, XPathRegexError right) => !left.Equals(right);
}
