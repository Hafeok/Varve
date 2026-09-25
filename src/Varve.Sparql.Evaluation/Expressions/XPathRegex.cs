// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Varve.Sparql.Evaluation.Expressions;

/// <summary>
/// XPath regular expressions (F&amp;O §5.6.1) on .NET's engine: the flags
/// translated, the <c>q</c> and <c>x</c> flags applied to the pattern, and
/// XPath's replacement syntax to .NET's. Never <c>RegexOptions.Compiled</c>,
/// which Native AOT and the browser ignore.
/// </summary>
internal static class XPathRegex
{
    /// <summary>The compiled pattern, or null for an invalid pattern or flag (an expression error).</summary>
    internal static Regex? Compile(string pattern, string flags, TimeSpan timeout)
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        bool literal = false, extended = false;
        foreach (char flag in flags)
        {
            switch (flag)
            {
                case 's':
                    options |= RegexOptions.Singleline;
                    break;
                case 'm':
                    options |= RegexOptions.Multiline;
                    break;
                case 'i':
                    options |= RegexOptions.IgnoreCase;
                    break;
                case 'x':
                    extended = true;
                    break;
                case 'q':
                    literal = true;
                    break;
                default:
                    return null;
            }
        }

        if (literal)
        {
            pattern = Regex.Escape(pattern);
        }
        else if (extended)
        {
            pattern = RemoveWhitespace(pattern);
        }

        try
        {
            return new Regex(pattern, options, timeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// XPath's replacement string to .NET's: <c>$</c> and <c>\</c> are the
    /// literal characters, <c>$N</c> is group N taking as many digits as name a
    /// group; anything else after a backslash or a dollar is F&amp;O's
    /// <c>FORX0004</c>, an error (null).
    /// </summary>
    internal static string? TranslateReplacement(string replacement, Regex regex)
    {
        int groups = regex.GetGroupNumbers().Length - 1;
        StringBuilder result = new(replacement.Length + 8);
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];
            if (c == '\\')
            {
                if (i + 1 >= replacement.Length || replacement[i + 1] is not ('\\' or '$'))
                {
                    return null;
                }

                result.Append(replacement[++i] == '$' ? "$$" : "\\");
            }
            else if (c == '$')
            {
                i++;
                if (i >= replacement.Length || !char.IsAsciiDigit(replacement[i]))
                {
                    return null;
                }

                int group = replacement[i] - '0';
                while (i + 1 < replacement.Length && char.IsAsciiDigit(replacement[i + 1]) && (group * 10) + (replacement[i + 1] - '0') <= groups)
                {
                    group = (group * 10) + (replacement[++i] - '0');
                }

                result.Append(group <= groups ? "${" + group.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}" : string.Empty);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>The <c>x</c> flag: white space is removed, except inside a character class.</summary>
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
