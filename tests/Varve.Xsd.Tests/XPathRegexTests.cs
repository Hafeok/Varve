// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.RegularExpressions;
using Xunit;

namespace Varve.Xsd.Tests;

/// <summary>
/// The XPath regular expression translator (ADR 0153): one example per rule
/// where XPath and .NET disagree, and one per construct refused, each with
/// the section of F&amp;O 3.1 §5.6 it follows.
/// </summary>
public class XPathRegexTests
{
    private static bool Matches(string input, string pattern, string flags = "")
    {
        Assert.True(XPathRegex.TryTranslate(pattern, flags, out XPathRegexTranslation translation, out XPathRegexError error), error.ToString());
        Assert.True(XPathRegex.TryCompile(translation, TimeSpan.FromSeconds(5), out Regex? regex, out error), error.ToString());
        return regex.IsMatch(input);
    }

    private static string Refused(string pattern, string flags = "")
    {
        Assert.False(XPathRegex.TryTranslate(pattern, flags, out _, out XPathRegexError error), "translated: " + pattern);
        return error.Code;
    }

    [Theory]
    [InlineData("abc", "b", true)]
    [InlineData("abc", "^b", false)]
    [InlineData("abc", "^a.c$", true)]
    [InlineData("a|b", "a\\|b", true)]
    [InlineData("aaa", "^a{2,3}$", true)]
    [InlineData("aaaa", "^a{2,3}$", false)]
    [InlineData("aa", "^a{2,}$", true)]
    [InlineData("ab", "^(?:a)(b)$", true)]
    [InlineData("x-y", "^x\\-y$", true)]
    [InlineData("$", "^\\$$", true)]
    public void ordinary_patterns_mean_what_they_say(string input, string pattern, bool expected) =>
        Assert.Equal(expected, Matches(input, pattern));

    [Fact]
    public void dot_excludes_both_newline_and_carriage_return_without_s() // §5.6.2, s
    {
        Assert.False(Matches("a\rb", "^a.b$"));
        Assert.False(Matches("a\nb", "^a.b$"));
        Assert.True(Matches("a\rb", "^a.b$", "s"));
        Assert.True(Matches("a\nb", "^a.b$", "s"));
    }

    [Fact]
    public void dot_matches_a_character_outside_the_bmp_as_one() // §5.6.1: characters, not code units
    {
        Assert.True(Matches("\U0001F600", "^.$"));
        Assert.False(Matches("\U0001F600", "^..$"));
        Assert.True(Matches("a\U0001F600b", "^a[^x]b$"));
        Assert.True(Matches("\U0001F601", "^[\U0001F600-\U0001F602]$"));
        Assert.False(Matches("\U0001F603", "^[\U0001F600-\U0001F602]$"));
    }

    [Fact]
    public void dollar_is_the_end_of_the_input_without_m() // §5.6.1.1
    {
        Assert.False(Matches("abc\n", "c$"));
        Assert.True(Matches("abc", "c$"));
    }

    [Fact]
    public void multiline_anchors_follow_the_newline_rules() // §5.6.2, m
    {
        Assert.True(Matches("ab\ncd", "^cd", "m"));
        Assert.True(Matches("ab\ncd", "ab$", "m"));
        Assert.False(Matches("ab\ncd", "^cd"));

        // $ is not at the very end when the input ends with a newline; ^ is not after it.
        Assert.False(Matches("ab\n", "\n$", "m"));
        Assert.False(Matches("ab\n", "\n^", "m"));
    }

    [Theory]
    [InlineData(" ", "\\s", false)] // §G.4.2.5: \s is four characters
    [InlineData(" ", "\\s", true)]
    [InlineData("+", "\\w", true)] // \w excludes only \p{P}, \p{Z}, \p{C}: a symbol is a word character
    [InlineData("_", "\\w", false)] // _ is \p{Pc}
    [InlineData(",", "\\w", false)]
    [InlineData("٣", "\\d", true)] // ARABIC-INDIC DIGIT THREE is \p{Nd}
    [InlineData("\U0001D7CE", "^\\d$", true)] // MATHEMATICAL BOLD DIGIT ZERO, outside the BMP
    [InlineData("\U0001D400", "^\\p{Lu}$", true)] // MATHEMATICAL BOLD CAPITAL A
    [InlineData("a", "\\P{L}", false)]
    public void multi_character_and_category_escapes_are_xpaths(string input, string pattern, bool expected) =>
        Assert.Equal(expected, Matches(input, pattern));

    [Theory]
    [InlineData("_x", "^\\i\\c*$", true)]
    [InlineData("1x", "^\\i\\c*$", false)]
    [InlineData("x.y-z", "^\\i\\c*$", true)]
    [InlineData(" ", "^\\I$", true)]
    public void name_escapes_are_xml_names(string input, string pattern, bool expected) =>
        Assert.Equal(expected, Matches(input, pattern));

    [Theory]
    [InlineData("e", "^[a-z-[aeiou]]$", false)] // subtraction
    [InlineData("b", "^[a-z-[aeiou]]$", true)]
    [InlineData("-", "^[-a]$", true)] // a hyphen first is itself
    [InlineData("-", "^[a-]$", true)] // and last
    [InlineData("^", "^[a^]$", true)]
    [InlineData("q", "^[^abc]$", true)]
    public void character_classes(string input, string pattern, bool expected) =>
        Assert.Equal(expected, Matches(input, pattern));

    [Fact]
    public void case_insensitivity_adds_case_variants_to_characters_and_ranges_only() // §5.6.2, i
    {
        Assert.True(Matches("Z", "z", "i"));
        Assert.True(Matches("K", "^[A-Z]$", "i")); // KELVIN SIGN lower-cases to k
        Assert.False(Matches("i", "^[A-Z-[IO]]$", "i"));
        Assert.False(Matches("q", "^[^Q]$", "i"));
        Assert.False(Matches("a", "^\\p{Lu}$", "i")); // escapes are unaffected
    }

    [Fact]
    public void back_references() // §5.6.1.4
    {
        Assert.True(Matches("'abc'", "^('|\").*\\1$"));
        Assert.False(Matches("'abc\"", "^('|\").*\\1$"));
        Assert.True(Matches("Mum", "^([md])[aeiou]\\1$", "i"));
        Assert.True(Matches("x", "^(a)?x\\1$")); // an unmatched group's reference matches the empty string
        Assert.True(Matches("abcdefghijkk", "^(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k)\\11$"));
        Assert.True(Matches("aa0", "^(a)\\10$")); // \10 with one group is \1 then 0
    }

    [Fact]
    public void flags_x_and_q() // §5.6.2
    {
        Assert.True(Matches("helloworld", "hello world", "x"));
        Assert.True(Matches("hello world", "hello[ ]world", "x"));
        Assert.True(Matches("a.b*", "a.b*", "q"));
        Assert.False(Matches("axb", "a.b", "q"));
        Assert.True(Matches("A.B", "a.b", "qi"));
    }

    [Theory]
    [InlineData("\\b")]
    [InlineData("\\A")]
    [InlineData("\\z")]
    [InlineData("\\x41")]
    [InlineData("\\u0041")]
    [InlineData("\\0")]
    [InlineData("(?=a)")]
    [InlineData("(?<n>a)")]
    [InlineData("(?i)a")]
    [InlineData("a{")]
    [InlineData("a{2,1}")]
    [InlineData("a}")]
    [InlineData("*a")]
    [InlineData("a**")]
    [InlineData("^*")]
    [InlineData("[]")]
    [InlineData("[a")]
    [InlineData("[a-]b]")]
    [InlineData("[a-z-[")]
    [InlineData("[\\1]")]
    [InlineData("\\1(a)")]
    [InlineData("(a\\1)")]
    [InlineData("(a")]
    [InlineData("a)")]
    [InlineData("\\p{Xx}")]
    [InlineData("[z-a]")]
    public void what_xpath_does_not_define_is_refused(string pattern) =>
        Assert.Equal("FORX0002", Refused(pattern));

    [Fact]
    public void an_unknown_flag_is_refused() => Assert.Equal("FORX0001", Refused("a", "g"));

    [Fact]
    public void an_unknown_block_is_refused_when_compiled() // §5.6.1.5
    {
        Assert.True(XPathRegex.TryTranslate("\\p{IsNoSuchBlock}", "", out XPathRegexTranslation translation, out _));
        Assert.False(XPathRegex.TryCompile(translation, TimeSpan.FromSeconds(1), out _, out XPathRegexError error));
        Assert.Equal("FORX0002", error.Code);
        Assert.True(Matches("a", "^\\p{IsBasicLatin}$"));
        Assert.False(Matches("é", "^\\p{IsBasicLatin}$"));
        Assert.True(Matches("\U0001F600", "^\\P{IsBasicLatin}$"));
        Assert.True(Matches("b", "^[\\p{IsBasicLatin}-[a]]$"));
    }

    [Theory]
    [InlineData("$1-$2", 2, "${1}-${2}")]
    [InlineData("\\$\\\\", 0, "$$\\")]
    [InlineData("$12", 1, "${1}2")]
    [InlineData("$3", 2, "")]
    public void replacement_strings(string replacement, int groups, string expected)
    {
        Assert.True(XPathRegex.TryTranslateReplacement(replacement, groups, out string? translated, out _));
        Assert.Equal(expected, translated);
    }

    [Theory]
    [InlineData("\\n")]
    [InlineData("$")]
    [InlineData("$x")]
    public void invalid_replacements_are_refused(string replacement)
    {
        Assert.False(XPathRegex.TryTranslateReplacement(replacement, 1, out _, out XPathRegexError error));
        Assert.Equal("FORX0004", error.Code);
    }

    [Fact]
    public void a_pathological_pattern_times_out()
    {
        Assert.True(XPathRegex.TryTranslate("^(a+)+$", "", out XPathRegexTranslation translation, out _));
        Assert.True(XPathRegex.TryCompile(translation, TimeSpan.FromMilliseconds(50), out Regex? regex, out _));
        Assert.Throws<RegexMatchTimeoutException>(() => regex.IsMatch(new string('a', 40) + "!"));
    }
}
